namespace VPS_Monitor_Desktop_App.Application.DTOs;

public enum SshAuthMethod
{
    Password,
    PrivateKey
}

/// <summary>
/// Konfigurasi koneksi SSH ke VPS. Disimpan via <see cref="VPS_Monitor_Desktop_App.Application.Interfaces.ICredentialStore"/>.
/// Mutable agar bisa di-bind langsung ke <c>InputText</c>/<c>InputNumber</c> di Razor form.
/// </summary>
public sealed class SshConnectionConfig
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 22;
    public string Username { get; set; } = "root";
    public SshAuthMethod AuthMethod { get; set; } = SshAuthMethod.Password;
    public string? Password { get; set; }
    public string? PrivateKey { get; set; }

    public SshConnectionConfig() { }

    public SshConnectionConfig(
        string host,
        int port,
        string username,
        SshAuthMethod authMethod,
        string? password,
        string? privateKey)
    {
        Host = host;
        Port = port;
        Username = username;
        AuthMethod = authMethod;
        Password = password;
        PrivateKey = privateKey;
    }

    public SshConnectionConfig Clone() =>
        new(Host, Port, Username, AuthMethod, Password, PrivateKey);

    public static SshConnectionConfig Empty() => new();
}
