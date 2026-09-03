using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Renci.SshNet;
using VPS_Monitor_Desktop_App.Application.DTOs;
using VPS_Monitor_Desktop_App.Application.Interfaces;
using VPS_Monitor_Desktop_App.Application.Services;

namespace VPS_Monitor_Desktop_App.Infrastructure.Ssh;

/// <summary>
/// Implementasi <see cref="IVpsMonitorService"/> pakai SSH.NET. Mengirim satu
/// shell script panjang yang mengeksekusi banyak command secara sequential di server,
/// parse output per section, lalu kembalikan <see cref="SystemSnapshot"/> lengkap.
/// Tidak ada caching &amp; tidak ada polling — murni on-demand sesuai brief.
/// </summary>
public sealed class SshMonitorService : IVpsMonitorService
{
    private const int CommandTimeoutSeconds = 20;

    private const string CombinedScript = """
        set +e
        echo "=== top ==="
        top -bn1 2>/dev/null | head -10
        echo "=== nproc ==="
        nproc 2>/dev/null
        echo "=== free ==="
        free -m 2>/dev/null
        echo "=== df ==="
        df -h 2>/dev/null
        echo "=== uptime ==="
        uptime 2>/dev/null
        echo "=== os-release ==="
        cat /etc/os-release 2>/dev/null
        echo "=== packages ==="
        dpkg-query -W -f='${Package}\t${Version}\t${Status}\n' 2>/dev/null
        if [ "$?" -ne 0 ]; then rpm -qa --queryformat '%{NAME}\t%{VERSION}\n' 2>/dev/null; fi
        echo "=== services ==="
        for svc in nginx apache2 mysql mariadb postgresql docker fail2ban ufw redis-server php8.1-fpm php8.2-fpm php8.3-fpm php-fpm caddy; do
          state=$(systemctl is-active $svc 2>/dev/null)
          echo "$svc=${state:-unknown}"
        done
        echo "=== nginx-config ==="
        # Cari konfigurasi nginx di beberapa lokasi umum:
        # 1. /etc/nginx/sites-enabled/ (Debian/Ubuntu default)
        # 2. /etc/nginx/conf.d/ (RHEL/CentOS / custom)
        # 3. /etc/nginx/nginx.conf (single-file setup)
        # 4. Fallback: semua .conf di /etc/nginx (catches snippets, included files, dll)
        # 5. Fallback terakhir: nginx -T untuk dump full parsed config
        {
          if [ -d /etc/nginx/sites-enabled ]; then
            grep -rhE '^[[:space:]]*(server_name|listen[[:space:]])' /etc/nginx/sites-enabled/ 2>/dev/null
          fi
          if [ -d /etc/nginx/conf.d ]; then
            grep -rhE '^[[:space:]]*(server_name|listen[[:space:]])' /etc/nginx/conf.d/ 2>/dev/null
          fi
          if [ -f /etc/nginx/nginx.conf ]; then
            grep -hE '^[[:space:]]*(server_name|listen[[:space:]])' /etc/nginx/nginx.conf 2>/dev/null
          fi
        } | sort -u
        # Fallback terakhir kalau belum ada hasil
        if [ -z "$(cat /tmp/nc_$$ 2>/dev/null)" ]; then
          {
            find /etc/nginx -type f -name "*.conf" 2>/dev/null | xargs grep -hE '^[[:space:]]*(server_name|listen[[:space:]])' 2>/dev/null
            nginx -T 2>/dev/null | grep -E '^[[:space:]]*(server_name|listen[[:space:]])' 2>/dev/null
          } | sort -u | head -50
        fi
        echo "=== nginx-status ==="
        # Multiple detection methods — systemctl sering restricted untuk non-root
        if systemctl is-active nginx 2>/dev/null | grep -q active 2>/dev/null; then
          echo "active"
        elif pgrep -x nginx > /dev/null 2>&1; then
          echo "active"
        elif [ -f /var/run/nginx.pid ] || [ -f /run/nginx.pid ]; then
          echo "active"
        else
          echo "unknown"
        fi
        echo "=== apache-config ==="
        apache2ctl -S 2>/dev/null || httpd -S 2>/dev/null
        echo "=== apache-status ==="
        if systemctl is-active apache2 2>/dev/null | grep -q active 2>/dev/null; then
          echo "active"
        elif systemctl is-active httpd 2>/dev/null | grep -q active 2>/dev/null; then
          echo "active"
        elif pgrep -x apache2 > /dev/null 2>&1 || pgrep -x httpd > /dev/null 2>&1; then
          echo "active"
        else
          echo "unknown"
        fi
        echo "=== docker-ps ==="
        docker ps --format '{{.Names}}|{{.Image}}|{{.Status}}|{{.Ports}}' 2>/dev/null
        echo "=== ufw ==="
        ufw status 2>/dev/null
        echo "=== ss ==="
        ss -tln 2>/dev/null
        echo "=== END ==="
        """;

    private static readonly string[] SectionMarkers =
    {
        "=== top ===", "=== nproc ===", "=== free ===", "=== df ===", "=== uptime ===",
        "=== os-release ===", "=== packages ===", "=== services ===", "=== nginx-config ===",
        "=== nginx-status ===", "=== apache-config ===", "=== apache-status ===",
        "=== docker-ps ===", "=== ufw ===", "=== ss ===", "=== END ==="
    };

    public async Task<SystemSnapshot> GetSnapshotAsync(
        SshConnectionConfig config, CancellationToken ct = default)
    {
        try
        {
            using var client = CreateClient(config);
            await Task.Run(() => client.Connect(), ct);

            if (!client.IsConnected)
            {
                return new SystemSnapshot
                {
                    IsOnline = false,
                    ErrorMessage = "Gagal terhubung ke server (koneksi tidak aktif).",
                };
            }

            var output = await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                using var cmd = client.RunCommand(CombinedScript);
                return cmd.Result ?? string.Empty;
            }, ct);

            try { client.Disconnect(); } catch { /* ignore */ }

            var sections = SplitSections(output);
            var listeningPorts = ParseListeningPorts(sections.GetValueOrDefault("ss"));

            return new SystemSnapshot
            {
                IsOnline = true,
                Cpu = ParseCpu(sections.GetValueOrDefault("top"), sections.GetValueOrDefault("nproc")),
                Memory = ParseMemory(sections.GetValueOrDefault("free")),
                Storage = ParseStorage(sections.GetValueOrDefault("df")),
                Uptime = ParseUptime(sections.GetValueOrDefault("uptime")),
                OsFamily = DetectOsFamily(sections.GetValueOrDefault("os-release")),
                Software = ParsePackages(sections.GetValueOrDefault("packages")),
                Services = ParseServices(sections.GetValueOrDefault("services")),
                Websites = ParseWebsites(
                    sections.GetValueOrDefault("nginx-status"),
                    sections.GetValueOrDefault("nginx-config"),
                    sections.GetValueOrDefault("apache-status"),
                    sections.GetValueOrDefault("apache-config"),
                    sections.GetValueOrDefault("docker-ps"),
                    listeningPorts),
                DockerContainers = ParseDockerContainers(sections.GetValueOrDefault("docker-ps")),
                Firewall = ParseFirewall(sections.GetValueOrDefault("ufw")),
            };
        }
        catch (OperationCanceledException)
        {
            return new SystemSnapshot
            {
                IsOnline = false,
                ErrorMessage = "Pengecekan dibatalkan atau timeout.",
            };
        }
        catch (Exception ex)
        {
            return new SystemSnapshot
            {
                IsOnline = false,
                ErrorMessage = $"Gagal mengambil snapshot: {ex.Message}",
            };
        }
    }

    // -------- Section splitter --------

    private static Dictionary<string, string> SplitSections(string output)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var lines = output.Split('\n');
        string? currentKey = null;
        var currentLines = new List<string>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            // Strip carriage return & shell prompt artifacts
            if (trimmed.EndsWith("=== END ===", StringComparison.Ordinal))
            {
                if (currentKey is not null)
                    result[currentKey] = string.Join("\n", currentLines);
                currentKey = null;
                continue;
            }
            var marker = Array.Find(SectionMarkers, m => trimmed == m);
            if (marker is not null)
            {
                if (currentKey is not null)
                    result[currentKey] = string.Join("\n", currentLines);
                currentKey = marker.Replace("=== ", "").Replace(" ===", "");
                currentLines.Clear();
            }
            else if (currentKey is not null)
            {
                currentLines.Add(line);
            }
        }
        // Flush last
        if (currentKey is not null)
            result[currentKey] = string.Join("\n", currentLines);

        return result;
    }

    // -------- System parsers --------

    private static CpuMetrics? ParseCpu(string? topOutput, string? nprocOutput)
    {
        if (string.IsNullOrEmpty(topOutput)) return null;

        var line = topOutput.Split('\n')
            .FirstOrDefault(l => l.Contains("Cpu(s)", StringComparison.Ordinal) ||
                                 l.Contains("%Cpu", StringComparison.Ordinal));
        if (line is null) return null;

        var idleMatch = Regex.Match(line, @"(\d+(?:\.\d+)?)\s+id");
        if (!idleMatch.Success) return null;

        var idle = double.Parse(idleMatch.Groups[1].Value, CultureInfo.InvariantCulture);
        var usage = Math.Clamp(100.0 - idle, 0.0, 100.0);

        int coreCount = 0;
        if (!string.IsNullOrWhiteSpace(nprocOutput) &&
            int.TryParse(nprocOutput.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var cores))
        {
            coreCount = cores;
        }

        return new CpuMetrics
        {
            UsagePercent = Math.Round(usage, 1),
            CoreCount = coreCount,
        };
    }

    private static MemoryMetrics? ParseMemory(string? output)
    {
        if (string.IsNullOrEmpty(output)) return null;

        var memLine = output.Split('\n')
            .FirstOrDefault(l => l.StartsWith("Mem:", StringComparison.Ordinal));
        if (memLine is null) return null;

        var parts = memLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4) return null;

        if (!TryParseDouble(parts[1], out var total) ||
            !TryParseDouble(parts[2], out var used))
            return null;

        var free = Math.Max(0, total - used);
        var usage = total > 0 ? (used / total * 100.0) : 0.0;

        return new MemoryMetrics
        {
            TotalGb = Math.Round(total / 1024.0, 2),
            UsedGb = Math.Round(used / 1024.0, 2),
            FreeGb = Math.Round(free / 1024.0, 2),
            UsagePercent = Math.Round(usage, 1),
        };
    }

    private static IReadOnlyList<StorageMetrics> ParseStorage(string? output)
    {
        if (string.IsNullOrEmpty(output)) return Array.Empty<StorageMetrics>();

        var skipPrefixes = new[] { "tmpfs", "devtmpfs", "overlay", "squashfs", "proc", "cgroup", "sysfs", "efivarfs" };

        var result = new List<StorageMetrics>();
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("Filesystem", StringComparison.Ordinal)) continue;

            var parts = Regex.Split(line, @"\s+");
            if (parts.Length < 6) continue;

            var fs = parts[0];
            if (skipPrefixes.Any(p => fs.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                continue;

            if (!TryParseDouble(parts[4].TrimEnd('%'), out var pct)) continue;

            var totalGb = ParseSizeToGb(parts[1]);
            var usedGb = ParseSizeToGb(parts[2]);

            result.Add(new StorageMetrics
            {
                Filesystem = fs,
                MountPoint = parts[5],
                UsagePercent = Math.Round(pct, 1),
                TotalGb = Math.Round(totalGb, 2),
                UsedGb = Math.Round(usedGb, 2),
                FreeGb = Math.Round(Math.Max(0, totalGb - usedGb), 2),
            });
        }

        return result;
    }

    private static UptimeMetrics? ParseUptime(string? output)
    {
        if (string.IsNullOrEmpty(output)) return null;

        var loadMatch = Regex.Match(
            output, @"load average:\s*(\d+(?:\.\d+)?),\s*(\d+(?:\.\d+)?),\s*(\d+(?:\.\d+)?)");

        var upText = string.Empty;
        var upIdx = output.IndexOf("up ", StringComparison.Ordinal);
        if (upIdx >= 0)
        {
            var endIdx = output.IndexOf(" user", upIdx, StringComparison.Ordinal);
            if (endIdx < 0) endIdx = output.IndexOf(",", upIdx, StringComparison.Ordinal);
            if (endIdx > upIdx)
                upText = output.Substring(upIdx + 3, endIdx - upIdx - 3).Trim().TrimEnd(',');
        }

        return new UptimeMetrics
        {
            HumanReadable = upText,
            LoadAverage1Min = loadMatch.Success ? double.Parse(loadMatch.Groups[1].Value, CultureInfo.InvariantCulture) : 0,
            LoadAverage5Min = loadMatch.Success ? double.Parse(loadMatch.Groups[2].Value, CultureInfo.InvariantCulture) : 0,
            LoadAverage15Min = loadMatch.Success ? double.Parse(loadMatch.Groups[3].Value, CultureInfo.InvariantCulture) : 0,
        };
    }

    // -------- New parsers --------

    private static OsFamily DetectOsFamily(string? osRelease)
    {
        if (string.IsNullOrEmpty(osRelease)) return OsFamily.Unknown;

        if (osRelease.Contains("ID=debian", StringComparison.OrdinalIgnoreCase) ||
            osRelease.Contains("ID_LIKE=debian", StringComparison.OrdinalIgnoreCase) ||
            osRelease.Contains("ID=ubuntu", StringComparison.OrdinalIgnoreCase) ||
            osRelease.Contains("ID=kali", StringComparison.OrdinalIgnoreCase) ||
            osRelease.Contains("ID=raspbian", StringComparison.OrdinalIgnoreCase))
            return OsFamily.Debian;

        if (osRelease.Contains("ID=rhel", StringComparison.OrdinalIgnoreCase) ||
            osRelease.Contains("ID=centos", StringComparison.OrdinalIgnoreCase) ||
            osRelease.Contains("ID=fedora", StringComparison.OrdinalIgnoreCase) ||
            osRelease.Contains("ID=rocky", StringComparison.OrdinalIgnoreCase) ||
            osRelease.Contains("ID=almalinux", StringComparison.OrdinalIgnoreCase) ||
            osRelease.Contains("ID_LIKE=rhel", StringComparison.OrdinalIgnoreCase) ||
            osRelease.Contains("ID_LIKE=fedora", StringComparison.OrdinalIgnoreCase))
            return OsFamily.Rhel;

        if (osRelease.Contains("ID=arch", StringComparison.OrdinalIgnoreCase) ||
            osRelease.Contains("ID=manjaro", StringComparison.OrdinalIgnoreCase))
            return OsFamily.Arch;

        return OsFamily.Unknown;
    }

    private static IReadOnlyList<InstalledPackage> ParsePackages(string? output)
    {
        if (string.IsNullOrEmpty(output)) return Array.Empty<InstalledPackage>();

        var result = new List<InstalledPackage>(capacity: 256);
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;

            var parts = line.Split('\t');
            if (parts.Length < 2) continue;

            // dpkg output: Package\tVersion\tStatus
            // rpm output: NAME\tVERSION
            // Skip "deinstall" / "purge" status dari dpkg
            if (parts.Length >= 3 && !parts[2].StartsWith("install", StringComparison.Ordinal))
                continue;

            var name = parts[0].Trim();
            var version = parts[1].Trim();
            if (string.IsNullOrEmpty(name)) continue;

            result.Add(new InstalledPackage
            {
                Name = name,
                Version = version,
                Category = PackageCategorizer.Categorize(name),
            });
        }

        return result;
    }

    private static IReadOnlyList<ServiceStatus> ParseServices(string? output)
    {
        if (string.IsNullOrEmpty(output)) return Array.Empty<ServiceStatus>();

        var result = new List<ServiceStatus>();
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            var sepIdx = line.IndexOf('=');
            if (sepIdx <= 0) continue;
            var name = line[..sepIdx].Trim();
            var state = line[(sepIdx + 1)..].Trim();
            if (string.IsNullOrEmpty(name)) continue;

            result.Add(new ServiceStatus
            {
                Name = name,
                State = state,
                IsActive = state == "active",
            });
        }
        return result;
    }

    private static IReadOnlyList<HostedWebsite> ParseWebsites(
        string? nginxStatus, string? nginxConfig,
        string? apacheStatus, string? apacheConfig,
        string? dockerOutput, HashSet<int> listeningPorts)
    {
        var result = new Dictionary<string, HostedWebsite>(StringComparer.OrdinalIgnoreCase);

        // Parse config regardless of active status — config existence is the real signal.
        // systemctl sering restricted untuk non-root; jangan skip kalau config ternyata ada.
        if (!string.IsNullOrEmpty(nginxConfig))
        {
            ParseNginxConfig(nginxConfig, listeningPorts, result);
        }

        if (!string.IsNullOrEmpty(apacheConfig))
        {
            ParseApacheConfig(apacheConfig, listeningPorts, result);
        }

        if (!string.IsNullOrEmpty(dockerOutput))
        {
            ParseDockerForWebsites(dockerOutput, listeningPorts, result);
        }

        return result.Values.OrderBy(w => w.Domain).ToList();
    }

    private static void ParseNginxConfig(string config, HashSet<int> listeningPorts, Dictionary<string, HostedWebsite> result)
    {
        // Mendukung 2 format output:
        // Format A (grep -r):  "/etc/nginx/sites-enabled/mysite:server_name example.com www.example.com;"
        // Format B (grep -h):  "    server_name example.com www.example.com;"
        // Plus: "listen 443 ssl;" untuk deteksi SSL
        var lines = config.Split('\n');
        string? currentFile = null;
        string? currentServerDomain = null;
        int currentServerPort = 80;
        bool currentServerSsl = false;

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            // Skip nginx -T warning/error lines
            if (line.StartsWith("nginx:", StringComparison.Ordinal)) continue;

            // Detect file path prefix (Format A only)
            string content = line;
            var colonIdx = line.IndexOf(':');
            if (colonIdx > 0 && line[..colonIdx].StartsWith("/etc/nginx", StringComparison.Ordinal))
            {
                currentFile = line[..colonIdx].TrimEnd(':');
                content = line[(colonIdx + 1)..].Trim();
            }
            else if (currentFile is null)
            {
                // No file context yet — pakai generic label
                currentFile = "nginx";
            }

            // Detect server block start (heuristic: "server {" or "server{")
            if (content.StartsWith("server", StringComparison.Ordinal) &&
                (content.EndsWith("{", StringComparison.Ordinal) || content.Contains(" {", StringComparison.Ordinal)))
            {
                // Reset per-server context saat masuk server block baru
                currentServerDomain = null;
                currentServerPort = 80;
                currentServerSsl = false;
                continue;
            }

            // Parse server_name
            if (content.StartsWith("server_name", StringComparison.Ordinal))
            {
                var after = content["server_name".Length..].TrimEnd('{', ';').Trim();
                // Strip inline comments
                var hashIdx = after.IndexOf('#');
                if (hashIdx >= 0) after = after[..hashIdx].Trim();

                foreach (var token in after.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (token.StartsWith("$", StringComparison.Ordinal)) continue; // skip $variable
                    if (string.IsNullOrWhiteSpace(token)) continue;
                    if (token == "_" || token == "localhost") continue; // skip default

                    var domain = token;
                    // Normalize wildcard: *.example.com → example.com
                    if (domain.StartsWith("*.", StringComparison.Ordinal))
                        domain = domain[2..];

                    currentServerDomain = domain;
                    var httpsPort = (currentServerSsl || listeningPorts.Contains(443)) ? 443 : (int?)null;
                    AddOrUpdate(result, domain, "nginx", currentFile, listeningPorts, currentServerSsl, httpsPort);
                }
            }
            // Parse listen untuk deteksi SSL
            else if (content.StartsWith("listen", StringComparison.Ordinal))
            {
                // Format: "listen 80;" / "listen 443 ssl;" / "listen 443 ssl http2;"
                var after = content["listen".Length..].TrimEnd('{', ';').Trim();
                var parts = after.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0 && int.TryParse(parts[0], out var port))
                {
                    currentServerPort = port;
                    if (port == 443 || after.Contains("ssl", StringComparison.OrdinalIgnoreCase) ||
                        after.Contains("quic", StringComparison.OrdinalIgnoreCase))
                    {
                        currentServerSsl = true;
                    }
                }
            }
        }
    }

    private static void ParseApacheConfig(string config, HashSet<int> listeningPorts, Dictionary<string, HostedWebsite> result)
    {
        // Output dari apache2ctl -S:
        // "port 80 namevhost example.com (/etc/apache2/sites-enabled/example.com.conf:1)"
        // "port 443 ssl namevhost secure.com (/etc/apache2/sites-enabled/secure.com.conf:1)"
        foreach (var raw in config.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;

            var portMatch = Regex.Match(line, @"port\s+(\d+)");
            var hostMatch = Regex.Match(line, @"namevhost\s+([\w\.\-\*]+)");
            var pathMatch = Regex.Match(line, @"\((/[^)]+)\)");

            if (!hostMatch.Success) continue;
            var domain = hostMatch.Groups[1].Value;
            if (domain == "_default_" || domain == "*") continue;

            int port = 80;
            int.TryParse(portMatch.Success ? portMatch.Groups[1].Value : "80", out port);

            var isHttps = line.Contains(" ssl", StringComparison.OrdinalIgnoreCase);
            var existing = result.GetValueOrDefault(domain);
            if (existing is null || !existing.HasSsl)
            {
                result[domain] = new HostedWebsite
                {
                    Domain = domain,
                    ServerType = "apache",
                    ConfigPath = pathMatch.Success ? pathMatch.Groups[1].Value : null,
                    HasSsl = isHttps || (port == 443) || (port == 80 && listeningPorts.Contains(443)),
                    HttpPort = port,
                    HttpsPort = (isHttps || port == 443) ? 443 : (int?)null,
                };
            }
        }
    }

    private static void ParseDockerForWebsites(string dockerOutput, HashSet<int> listeningPorts, Dictionary<string, HostedWebsite> result)
    {
        // Format: name|image|status|ports  →  "0.0.0.0:80->80/tcp, 0.0.0.0:443->443/tcp"
        foreach (var raw in dockerOutput.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;

            var parts = line.Split('|');
            if (parts.Length < 4) continue;
            var name = parts[0];
            var ports = parts[3];

            var exposesWebPort = false;
            var hasSsl = false;
            foreach (var portSpec in ports.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var spec = portSpec.Trim();
                if (!spec.Contains("->", StringComparison.Ordinal)) continue;
                var right = spec.Split("->", 2)[1];
                var portStr = right.Split('/')[0];
                if (!int.TryParse(portStr, out var port)) continue;

                if (port == 80 || port == 443)
                {
                    exposesWebPort = true;
                    if (port == 443) hasSsl = true;
                }
            }

            if (!exposesWebPort) continue;

            // Pakai container name sebagai domain identifier (user biasanya panggil by name)
            var domain = $"{name}.docker.local";
            if (!result.ContainsKey(domain))
            {
                result[domain] = new HostedWebsite
                {
                    Domain = name,
                    ServerType = "docker",
                    HasSsl = hasSsl || listeningPorts.Contains(443),
                    HttpPort = 80,
                    HttpsPort = hasSsl ? 443 : (listeningPorts.Contains(443) ? 443 : null),
                };
            }
        }
    }

    private static void AddOrUpdate(Dictionary<string, HostedWebsite> result, string domain, string serverType,
        string? configPath, HashSet<int> listeningPorts, bool? forceHasSsl = null, int? forceHttpsPort = null)
    {
        if (string.IsNullOrEmpty(domain)) return;
        if (result.ContainsKey(domain)) return;

        var hasSsl = forceHasSsl ?? listeningPorts.Contains(443);
        var httpsPort = forceHttpsPort ?? (hasSsl ? 443 : (int?)null);

        result[domain] = new HostedWebsite
        {
            Domain = domain,
            ServerType = serverType,
            ConfigPath = configPath,
            HasSsl = hasSsl,
            HttpPort = 80,
            HttpsPort = httpsPort,
        };
    }

    private static IReadOnlyList<DockerContainer> ParseDockerContainers(string? output)
    {
        if (string.IsNullOrEmpty(output)) return Array.Empty<DockerContainer>();

        var result = new List<DockerContainer>();
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            var parts = line.Split('|');
            if (parts.Length < 4) continue;

            var ports = parts[3].Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => p.Contains("->", StringComparison.Ordinal))
                .Select(p =>
                {
                    var right = p.Split("->", 2)[1];
                    var portStr = right.Split('/')[0];
                    return int.TryParse(portStr, out var port) ? (int?)port : null;
                })
                .Where(p => p.HasValue)
                .Select(p => p!.Value)
                .ToList();

            result.Add(new DockerContainer
            {
                Name = parts[0],
                Image = parts[1],
                Status = parts[2],
                ExposedPorts = ports,
            });
        }
        return result;
    }

    private static FirewallStatus? ParseFirewall(string? output)
    {
        if (string.IsNullOrEmpty(output)) return null;

        var statusMatch = Regex.Match(output, @"Status:\s*(\w+)");
        var isActive = statusMatch.Success && statusMatch.Groups[1].Value.Equals("active", StringComparison.OrdinalIgnoreCase);

        var openPorts = new List<FirewallRule>();
        if (isActive)
        {
            // Header line: "To                         Action      From"
            // Data lines: "22/tcp                     ALLOW IN    Anywhere"
            var lines = output.Split('\n');
            var startParsing = false;
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.StartsWith("--", StringComparison.Ordinal)) { startParsing = true; continue; }
                if (!startParsing) continue;
                if (line.Length == 0) continue;
                if (line.StartsWith("To ", StringComparison.Ordinal)) continue; // header

                var parts = Regex.Split(line, @"\s{2,}");
                if (parts.Length < 2) continue;

                openPorts.Add(new FirewallRule
                {
                    To = parts[0].Trim(),
                    Action = parts[1].Trim(),
                    From = parts.Length >= 3 ? parts[2].Trim() : string.Empty,
                });
            }
        }

        return new FirewallStatus
        {
            IsActive = isActive,
            Raw = output,
            OpenPorts = openPorts,
        };
    }

    private static HashSet<int> ParseListeningPorts(string? output)
    {
        var ports = new HashSet<int>();
        if (string.IsNullOrEmpty(output)) return ports;

        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith("LISTEN", StringComparison.Ordinal)) continue;
            // format: LISTEN 0 128 0.0.0.0:22 0.0.0.0:*
            // atau:   LISTEN 0 511 *:443 *:*
            var match = Regex.Match(line, @":(\d+)\s");
            if (match.Success && int.TryParse(match.Groups[1].Value, out var port))
                ports.Add(port);
        }
        return ports;
    }

    // -------- Helpers --------

    private static double ParseSizeToGb(string s)
    {
        s = s.Trim();
        if (s.Length == 0) return 0;

        var unit = char.ToUpperInvariant(s[^1]);
        var numStr = s[..^1];

        if (!double.TryParse(numStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var num))
            return 0;

        return unit switch
        {
            'K' => num / 1024.0 / 1024.0,
            'M' => num / 1024.0,
            'G' => num,
            'T' => num * 1024.0,
            _ => num / 1024.0 / 1024.0,
        };
    }

    private static bool TryParseDouble(string s, out double value) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static SshClient CreateClient(SshConnectionConfig config)
    {
        var authMethod = config.AuthMethod switch
        {
            SshAuthMethod.Password => (AuthenticationMethod)
                new PasswordAuthenticationMethod(config.Username, config.Password ?? string.Empty),
            SshAuthMethod.PrivateKey => new PrivateKeyAuthenticationMethod(
                config.Username,
                new PrivateKeyFile(ResolveKeyStream(config.PrivateKey!))),
            _ => throw new InvalidOperationException($"Metode autentikasi tidak dikenal: {config.AuthMethod}")
        };

        var connectionInfo = new ConnectionInfo(config.Host, config.Port, config.Username, authMethod)
        {
            Timeout = TimeSpan.FromSeconds(CommandTimeoutSeconds),
        };

        return new SshClient(connectionInfo);
    }

    private static Stream ResolveKeyStream(string privateKey)
    {
        if (File.Exists(privateKey))
            return new FileStream(privateKey, FileMode.Open, FileAccess.Read, FileShare.Read);
        return new MemoryStream(System.Text.Encoding.UTF8.GetBytes(privateKey));
    }
}
