using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MundoBrowser.Helpers;

namespace MundoBrowser.Services.Extensions;

internal static class ExtensionRuntime
{
    public static string ExtensionsPath { get; } = Path.Combine(
        AppRuntime.LocalDataDirectory,
        "Extensions");

    public static IReadOnlyList<string> GetInstalledDirectories()
    {
        Directory.CreateDirectory(ExtensionsPath);

        return Directory.EnumerateDirectories(ExtensionsPath)
            .Where(path => IsSourceId(Path.GetFileName(path))
                           && File.Exists(Path.Combine(path, "manifest.json")))
            .Select(Path.GetFullPath)
            .ToArray();
    }

    public static bool IsSourceId(string id) =>
        id.Length == 32 && id.All(character => character is >= 'a' and <= 'p');

    public static bool IsInstalled(string sourceId) =>
        IsSourceId(sourceId)
        && File.Exists(Path.Combine(ExtensionsPath, sourceId, "manifest.json"));

    public static string GetRuntimeId(string extensionDirectory, JsonElement manifest)
    {
        if (manifest.TryGetProperty("key", out var keyProperty)
            && keyProperty.ValueKind == JsonValueKind.String
            && TryDecodeKey(keyProperty.GetString(), out var key))
            return GenerateId(key);

        string normalizedPath = Path.GetFullPath(extensionDirectory);
        if (normalizedPath.Length >= 2
            && normalizedPath[0] is >= 'a' and <= 'z'
            && normalizedPath[1] == ':')
            normalizedPath = char.ToUpperInvariant(normalizedPath[0]) + normalizedPath[1..];

        return GenerateId(Encoding.Unicode.GetBytes(normalizedPath));
    }

    private static bool TryDecodeKey(string? encodedKey, out byte[] key)
    {
        try
        {
            key = Convert.FromBase64String(encodedKey ?? "");
            return key.Length > 0;
        }
        catch (FormatException)
        {
            key = [];
            return false;
        }
    }

    private static string GenerateId(byte[] input)
    {
        byte[] hash = SHA256.HashData(input);
        var id = new StringBuilder(32);
        for (int i = 0; i < 16; i++)
        {
            id.Append((char)('a' + (hash[i] >> 4)));
            id.Append((char)('a' + (hash[i] & 0x0F)));
        }

        return id.ToString();
    }
}
