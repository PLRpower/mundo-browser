using System.IO;
using System.IO.Compression;
using System.Net.Http;
using MundoBrowser.Helpers;

namespace MundoBrowser.Services;

public class ExtensionDownloader
{
    private const int MaxExtensionDownloadBytes = 256 * 1024 * 1024;
    private const long MaxExtractedBytes = 1024L * 1024 * 1024;
    private const int MaxArchiveEntries = 100_000;
    private static readonly HttpClient HttpClient = new();
    private readonly string _extensionsPath;

    static ExtensionDownloader()
    {
        HttpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/123.0.0.0 Safari/537.36");
        HttpClient.DefaultRequestHeaders.Add("Referer", "https://chromewebstore.google.com/");
        HttpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public ExtensionDownloader()
    {
        _extensionsPath = Path.Combine(AppRuntime.LocalDataDirectory, "Extensions");
        Directory.CreateDirectory(_extensionsPath);
    }

    /// <summary>
    /// Downloads and installs an extension from the Chrome Web Store
    /// </summary>
    public async Task<string> DownloadAndExtractExtension(string extensionId)
    {
        try
        {
            extensionId = extensionId.Trim().ToLowerInvariant();
            if (!IsValidExtensionId(extensionId))
                throw new ArgumentException("Invalid Chrome Web Store extension ID.", nameof(extensionId));

            string[] urlVariants =
            [
                // Variant 1: Modern Google APIs endpoint (MV3 compatible)
                $"https://update.googleapis.com/service/update2/crx?response=redirect&acceptformat=crx3&x=id%3D{extensionId}%26uc",
                // Variant 2: Classic endpoint with precise version
                $"https://clients2.google.com/service/update2/crx?response=redirect&os=win&arch=x64&os_arch=x86-64&nacl_arch=x86-64&prod=chromebrowser&prodchannel=stable&prodversion=123.0.6312.122&acceptformat=crx3&x=id%3D{extensionId}%26installsource%3Dondemand%26uc",
                // Variant 3: Ultra-minimalist
                $"https://clients2.google.com/service/update2/crx?response=redirect&x=id%3D{extensionId}%26uc"
            ];

            byte[]? crxBytes = null;
            
            foreach (var crxUrl in urlVariants)
            {
                const int maxRetries = 1;
                for (int i = 0; i <= maxRetries; i++)
                {
                    try
                    {
                        using var response = await HttpClient.GetAsync(
                            crxUrl,
                            HttpCompletionOption.ResponseHeadersRead);
                        
                        if (response.StatusCode == System.Net.HttpStatusCode.NoContent || response.StatusCode == System.Net.HttpStatusCode.NotFound)
                            break; 
                            
                        if (response.IsSuccessStatusCode)
                        {
                            if (response.Content.Headers.ContentLength > MaxExtensionDownloadBytes)
                                break;

                            crxBytes = await ReadLimitedBytesAsync(response.Content);

                            if (crxBytes is { Length: > 0 })
                                goto DownloadFinished;
                        }
                    }
                    catch
                    {
                        if (i < maxRetries)
                            await Task.Delay(300);
                        else
                            break;
                    }
                }
            }

            DownloadFinished:
            if (crxBytes == null || crxBytes.Length == 0)
            {
                throw new InvalidOperationException($"Could not download extension {extensionId}. All server attempts returned 'No Content' (204) or failed. This usually means the extension is restricted or unavailable for direct download.");
            }

            var extractPath = Path.Combine(_extensionsPath, extensionId);
            await ExtractCrxBytes(crxBytes, extractPath);

            return extractPath;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Error downloading/extracting extension: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Extracts a CRX file by skipping the CRX header and extracting the ZIP content
    /// </summary>
    private static async Task ExtractCrxBytes(byte[] crxBytes, string extractPath)
    {
        // Check if it's already a standard ZIP file (starts with 'PK' magic number)
        if (crxBytes.Length >= 4 && crxBytes[0] == 0x50 && crxBytes[1] == 0x4B)
        {
            await Task.Run(() => ExtractZipBytes(crxBytes, extractPath, 0));
            return;
        }

        // Check for CRX magic number "Cr24"
        if (crxBytes.Length < 4 || 
            crxBytes[0] != 'C' || crxBytes[1] != 'r' || 
            crxBytes[2] != '2' || crxBytes[3] != '4')
        {
            var startSnippet = System.Text.Encoding.UTF8.GetString(crxBytes, 0, Math.Min(crxBytes.Length, 100));
            
            if (startSnippet.Contains("<?xml") || startSnippet.Contains("<g:updateresponse"))
                throw new InvalidDataException("The server returned an XML error response instead of the extension file.");
            
            if (startSnippet.Contains("<!DOCTYPE html") || startSnippet.Contains("<html"))
                throw new InvalidDataException("The server returned an HTML page instead of the extension file.");

            throw new InvalidDataException($"Invalid file format (Not CRX or ZIP). Header: {startSnippet}");
        }

        if (crxBytes.Length < 12)
            throw new InvalidDataException("Invalid CRX header.");

        int zipStartOffset;
        var version = BitConverter.ToInt32(crxBytes, 4);

        if (version == 2)
        {
            if (crxBytes.Length < 16)
                throw new InvalidDataException("Invalid CRX2 header.");

            var publicKeyLength = BitConverter.ToInt32(crxBytes, 8);
            var signatureLength = BitConverter.ToInt32(crxBytes, 12);
            if (publicKeyLength < 0 || signatureLength < 0)
                throw new InvalidDataException("Invalid CRX2 header lengths.");

            zipStartOffset = checked(16 + publicKeyLength + signatureLength);
        }
        else if (version == 3)
        {
            var headerSize = BitConverter.ToInt32(crxBytes, 8);
            if (headerSize < 0)
                throw new InvalidDataException("Invalid CRX3 header length.");

            zipStartOffset = checked(12 + headerSize);
        }
        else
        {
            throw new NotSupportedException($"Unsupported CRX version: {version}");
        }

        if (zipStartOffset <= 0 || zipStartOffset >= crxBytes.Length)
            throw new InvalidDataException("Invalid CRX header: ZIP payload is missing.");

        await Task.Run(() => ExtractZipBytes(crxBytes, extractPath, zipStartOffset));
    }

    private static void ExtractZipBytes(byte[] zipBytes, string extractPath, int zipStartOffset)
    {
        var temporaryExtractPath = extractPath + ".tmp";
        var backupExtractPath = extractPath + ".bak";
        if (Directory.Exists(temporaryExtractPath))
            Directory.Delete(temporaryExtractPath, true);
        if (Directory.Exists(backupExtractPath))
            Directory.Delete(backupExtractPath, true);

        Directory.CreateDirectory(temporaryExtractPath);
        try
        {
            using var zipStream = new MemoryStream(
                zipBytes,
                zipStartOffset,
                zipBytes.Length - zipStartOffset,
                writable: false);
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
            ExtractZipArchiveSafely(archive, temporaryExtractPath);

            if (Directory.Exists(extractPath))
                Directory.Move(extractPath, backupExtractPath);

            Directory.Move(temporaryExtractPath, extractPath);

            if (Directory.Exists(backupExtractPath))
            {
                try { Directory.Delete(backupExtractPath, true); } catch { }
            }
        }
        catch
        {
            if (Directory.Exists(temporaryExtractPath))
            {
                try { Directory.Delete(temporaryExtractPath, true); } catch { }
            }

            if (!Directory.Exists(extractPath) && Directory.Exists(backupExtractPath))
                Directory.Move(backupExtractPath, extractPath);
            throw;
        }
    }

    private static async Task<byte[]?> ReadLimitedBytesAsync(HttpContent content)
    {
        await using var input = await content.ReadAsStreamAsync();
        using var output = new MemoryStream(
            content.Headers.ContentLength is > 0
                ? (int)Math.Min(content.Headers.ContentLength.Value, MaxExtensionDownloadBytes)
                : 0);
        var buffer = new byte[81920];
        int total = 0;

        while (true)
        {
            int read = await input.ReadAsync(buffer);
            if (read == 0)
                break;

            total = checked(total + read);
            if (total > MaxExtensionDownloadBytes)
                return null;

            await output.WriteAsync(buffer.AsMemory(0, read));
        }

        return total == 0 ? null : output.ToArray();
    }

    private static void ExtractZipArchiveSafely(ZipArchive archive, string extractPath)
    {
        string destinationRoot = EnsureTrailingDirectorySeparator(Path.GetFullPath(extractPath));
        long extractedBytes = 0;
        int processedEntries = 0;

        foreach (var entry in archive.Entries)
        {
            if (++processedEntries > MaxArchiveEntries)
                throw new InvalidDataException("Archive contains too many entries.");

            extractedBytes = checked(extractedBytes + entry.Length);
            if (extractedBytes > MaxExtractedBytes)
                throw new InvalidDataException("Archive expands beyond the allowed size.");

            if (string.IsNullOrWhiteSpace(entry.FullName))
                continue;

            var entryPath = entry.FullName
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar);

            var destinationPath = Path.GetFullPath(Path.Combine(destinationRoot, entryPath));
            if (!destinationPath.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Archive entry escapes the extraction directory: {entry.FullName}");

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            var destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(destinationDirectory))
                Directory.CreateDirectory(destinationDirectory);

            entry.ExtractToFile(destinationPath, overwrite: true);
        }
    }

    private static string EnsureTrailingDirectorySeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    /// <summary>
    /// Extracts extension ID from a Chrome Web Store URL
    /// </summary>
    public static string? ExtractExtensionIdFromUrl(string url)
    {
        try
        {
            if (!url.Contains("chrome.google.com/webstore") && !url.Contains("chromewebstore.google.com"))
            {
                return null;
            }

            var uri = new Uri(url);
            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            
            for (int i = segments.Length - 1; i >= 0; i--)
            {
                var segment = segments[i];
                if (IsValidExtensionId(segment))
                {
                    return segment;
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsValidExtensionId(string extensionId)
    {
        return extensionId.Length == 32 && extensionId.All(c => c is >= 'a' and <= 'p');
    }
}
