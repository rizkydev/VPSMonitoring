using VPS_Monitor_Desktop_App.Application.DTOs;

namespace VPS_Monitor_Desktop_App.Application.Services;

/// <summary>
/// Heuristik untuk mengelompokkan package ke kategori umum. Berbasis prefix/keyword matching
/// — pragmatic untuk portofolio. Paket yang tidak match masuk <see cref="PackageCategory.System"/>.
/// </summary>
public static class PackageCategorizer
{
    public static PackageCategory Categorize(string packageName)
    {
        if (string.IsNullOrWhiteSpace(packageName)) return PackageCategory.Other;

        var name = packageName.ToLowerInvariant();

        // Web server
        if (name is "nginx" or "nginx-core" or "nginx-full" or "nginx-extras" or "lighttpd"
            || name.StartsWith("nginx-", StringComparison.Ordinal)
            || name is "apache2" or "apache2-bin" or "apache2-utils" or "httpd")
        {
            return PackageCategory.WebServer;
        }

        // Database
        if (name.StartsWith("mysql", StringComparison.Ordinal)
            || name.StartsWith("mariadb", StringComparison.Ordinal)
            || name.StartsWith("postgres", StringComparison.Ordinal)
            || name.StartsWith("redis", StringComparison.Ordinal)
            || name.StartsWith("mongodb", StringComparison.Ordinal)
            || name.StartsWith("mongo-", StringComparison.Ordinal)
            || name is "sqlite3" or "mariadb-server")
        {
            return PackageCategory.Database;
        }

        // Runtime / bahasa
        if (name.StartsWith("dotnet-", StringComparison.Ordinal)
            || name.StartsWith("aspnetcore-", StringComparison.Ordinal)
            || name is "nodejs" or "node" or "npm" or "yarn" or "pnpm"
            || name.StartsWith("python3", StringComparison.Ordinal)
            || name is "python3" or "python" or "python3-minimal"
            || name.StartsWith("openjdk-", StringComparison.Ordinal)
            || name.StartsWith("golang-", StringComparison.Ordinal)
            || name is "go" or "ruby" or "ruby-full" or "rust" or "cargo"
            || name.StartsWith("php", StringComparison.Ordinal) && !name.StartsWith("php-", StringComparison.Ordinal))
        {
            return PackageCategory.Runtime;
        }

        // Container
        if (name is "docker.io" or "docker-ce" or "docker-ce-cli" or "docker-compose" or "docker-compose-plugin"
            || name.StartsWith("docker-", StringComparison.Ordinal)
            || name is "containerd" or "containerd.io" or "runc"
            || name is "podman" or "buildah" or "skopeo"
            || name.StartsWith("kubernetes", StringComparison.Ordinal)
            || name is "kubelet" or "kubeadm" or "kubectl")
        {
            return PackageCategory.Container;
        }

        // Security
        if (name is "ufw" or "fail2ban" or "iptables" or "nftables"
            || name is "openssh-server" or "openssh-client" or "sshpass"
            || name is "sudo" or "polkit" or "apparmor" or "selinux"
            || name is "openssl" or "ca-certificates" or "gnupg"
            || name.StartsWith("libnss", StringComparison.Ordinal)
            || name.StartsWith("libpam", StringComparison.Ordinal))
        {
            return PackageCategory.Security;
        }

        // Network
        if (name is "curl" or "wget" or "net-tools" or "netcat" or "traceroute"
            || name is "nmap" or "tcpdump" or "mtr" or "iperf3"
            || name is "bind9" or "bind9-host" or "dnsutils" or "iptraf-ng" or "vnstat"
            || name.StartsWith("nginx-", StringComparison.Ordinal) == false && name.StartsWith("open", StringComparison.Ordinal) && name.EndsWith("-tools", StringComparison.Ordinal))
        {
            return PackageCategory.Network;
        }

        // System (kernel, init, base libs, libc, base OS packages)
        if (name.StartsWith("lib", StringComparison.Ordinal)
            || name.StartsWith("linux-", StringComparison.Ordinal)
            || name.StartsWith("systemd", StringComparison.Ordinal)
            || name.StartsWith("bash", StringComparison.Ordinal)
            || name is "dpkg" or "apt" or "apt-utils" or "cron" or "cron-daemon-common"
            || name.StartsWith("grub", StringComparison.Ordinal)
            || name.StartsWith("init", StringComparison.Ordinal)
            || name.StartsWith("udev", StringComparison.Ordinal)
            || name.StartsWith("coreutils", StringComparison.Ordinal)
            || name.StartsWith("base-files", StringComparison.Ordinal))
        {
            return PackageCategory.System;
        }

        return PackageCategory.Other;
    }
}
