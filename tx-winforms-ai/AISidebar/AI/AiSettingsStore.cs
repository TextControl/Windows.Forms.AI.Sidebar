using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TXTextControl.AISidebar.AI;

/// <summary>Persists validated settings and encrypts remembered API keys with current-user Windows DPAPI.</summary>
public sealed class AiSettingsStore
{
    private readonly string _path;
    /// <summary>Creates a settings store. Supply an absolute path to isolate settings for a consuming application.</summary>
    public AiSettingsStore(string? path = null) => _path = Path.GetFullPath(path ?? DefaultPath);

    /// <summary>Gets an application-specific path that keeps independent host applications' settings separate.</summary>
    public static string DefaultPath
    {
        get
        {
            string name = string.Join("_", (Application.ProductName ?? "AISidebar").Split(Path.GetInvalidFileNameChars()));
            if (string.IsNullOrWhiteSpace(name)) name = "AISidebar";
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), name, "ai-connection.json");
        }
    }

    /// <summary>Reads settings and decrypts a remembered key; missing files return default settings.</summary>
    public AiConnectionSettings Load()
    {
        if (!File.Exists(_path)) return new();
        var stored = JsonSerializer.Deserialize<StoredConnection>(File.ReadAllText(_path))
            ?? throw new InvalidDataException("The AI settings file is empty.");
        var key = string.IsNullOrEmpty(stored.ProtectedKey) ? "" : Encoding.UTF8.GetString(
            ProtectedData.Unprotect(Convert.FromBase64String(stored.ProtectedKey), null, DataProtectionScope.CurrentUser));
        return stored.Settings with { ApiKey = key };
    }

    /// <summary>Validates settings and replaces the JSON file after encrypting any remembered key.</summary>
    public void Save(AiConnectionSettings settings)
    {
        settings.Validate();
        string? protectedKey = settings.RememberKey ? Convert.ToBase64String(ProtectedData.Protect(
            Encoding.UTF8.GetBytes(settings.ApiKey), null, DataProtectionScope.CurrentUser)) : null;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var tempPath = _path + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(new StoredConnection(settings, protectedKey),
            new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tempPath, _path, overwrite: true);
    }

    /// <summary>Only the DPAPI ciphertext is persisted; Settings.ApiKey is excluded by JsonIgnore.</summary>
    private sealed record StoredConnection(AiConnectionSettings Settings, string? ProtectedKey);
}
