using System.IO;
using System.IO.Compression;
using System.Net.Http;
using MundoBrowser.Helpers;

namespace MundoBrowser.Services
{
    public class ExtensionDownloader
    {
        private const int MaxExtensionDownloadBytes = 256 * 1024 * 1024;
        private const long MaxExtractedBytes = 1024L * 1024 * 1024;
        private const int MaxArchiveEntries = 100_000;
        private static readonly HttpClient _httpClient = new HttpClient();
        private readonly string _extensionsPath;

        static ExtensionDownloader()
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/123.0.0.0 Safari/537.36");
            _httpClient.DefaultRequestHeaders.Add("Referer", "https://chromewebstore.google.com/");
        }

        public ExtensionDownloader()
        {
            // Create a folder for downloaded extensions
            _extensionsPath = Path.Combine(AppRuntime.LocalDataDirectory, "Extensions");
            Directory.CreateDirectory(_extensionsPath);
        }

        /// <summary>
        /// Downloads and installs an extension from the Chrome Web Store
        /// </summary>
        /// <param name="extensionId">The Chrome Web Store extension ID (e.g., "nngceckbapebfimnlniiiahkandclblb" for Bitwarden)</param>
        /// <returns>Path to the extracted extension folder</returns>
        public async Task<string> DownloadAndExtractExtension(string extensionId)
        {
            try
            {
                extensionId = extensionId.Trim().ToLowerInvariant();
                if (!IsValidExtensionId(extensionId))
                    throw new ArgumentException("Invalid Chrome Web Store extension ID.", nameof(extensionId));

                // We try multiple URL variants
                // AdBlock and other modern extensions often require the more modern googleapis endpoint or precise prodversion
                var urlVariants = new[]
                {
                    // Variant 1: Modern Google APIs endpoint (MV3 compatible)
                    $"https://update.googleapis.com/service/update2/crx?response=redirect&acceptformat=crx3&x=id%3D{extensionId}%26uc",
                    // Variant 2: Classic endpoint with precise version
                    $"https://clients2.google.com/service/update2/crx?response=redirect&os=win&arch=x64&os_arch=x86-64&nacl_arch=x86-64&prod=chromebrowser&prodchannel=stable&prodversion=123.0.6312.122&acceptformat=crx3&x=id%3D{extensionId}%26installsource%3Dondemand%26uc",
                    // Variant 3: Ultra-minimalist
                    $"https://clients2.google.com/service/update2/crx?response=redirect&x=id%3D{extensionId}%26uc"
                };

                // Download the CRX file with retry and fallback logic
                byte[]? crxBytes = null;
                
                foreach (var crxUrl in urlVariants)
                {
                    int maxRetries = 1;
                    for (int i = 0; i <= maxRetries; i++)
                    {
                        try
                        {
                            using var response = await _httpClient.GetAsync(
                                crxUrl,
                                HttpCompletionOption.ResponseHeadersRead);
                            
                            // If we get 204 (NoContent) or 404, this URL variant doesn't work for this extension, try next variant
                            if (response.StatusCode == System.Net.HttpStatusCode.NoContent || response.StatusCode == System.Net.HttpStatusCode.NotFound)
                                break; 
                                
                            if (response.IsSuccessStatusCode)
                            {
                                if (response.Content.Headers.ContentLength is > MaxExtensionDownloadBytes)
                                    break;

                                crxBytes = await ReadLimitedBytesAsync(response.Content);

                                if (crxBytes is { Length: > 0 })
                                    goto DownloadFinished;
                            }
                        }
                        catch (Exception)
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
                    throw new Exception($"Could not download extension {extensionId}. All server attempts returned 'No Content' (204). This usually means the extension is restricted, requires a specific region, or the ID is invalid for direct download.");
                }

                // Extract the CRX file
                var extractPath = Path.Combine(_extensionsPath, extensionId);

                // CRX files are essentially ZIP files with a header
                // We need to skip the CRX header and extract the ZIP content
                await ExtractCrxBytes(crxBytes, extractPath);

                // MundoBrowser Hack: Patch the extension to support chrome.tabs and chrome.scripting APIs via C# backend
                // Disabled for now as it needs more refinement
                // ExtensionPatcher.PatchExtension(extractPath);

                return extractPath;
            }
            catch (Exception ex)
            {
                throw new Exception($"Error downloading/extracting extension: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Extracts a CRX file by skipping the CRX header and extracting the ZIP content
        /// </summary>
        private async Task ExtractCrxBytes(byte[] crxBytes, string extractPath)
        {
            try
            {
                // CRX3 format:
                // - Magic number: "Cr24" (4 bytes)
                // - Version: 3 (4 bytes)
                // - Header size (4 bytes)
                // - Header data (variable)
                // - ZIP archive

                // CRX files are usually ZIP files with a header (Cr24)
                // However, some download sources might provide the ZIP directly

                // Check if it's already a ZIP file (starts with 'PK' magic number)
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
                    // Try to provide a more helpful error message
                    var startSnippet = System.Text.Encoding.UTF8.GetString(crxBytes, 0, Math.Min(crxBytes.Length, 100));
                    
                    if (startSnippet.Contains("<?xml") || startSnippet.Contains("<g:updateresponse"))
                        throw new Exception("The server returned an XML error response instead of the extension file. This can happen if the extension ID is invalid or the extension is not available for download.");
                    
                    if (startSnippet.Contains("<!DOCTYPE html") || startSnippet.Contains("<html"))
                        throw new Exception("The server returned an HTML page instead of the extension file. This might be a login page or CAPTCHA.");

                    throw new Exception($"Invalid file format (Not CRX or ZIP). Header: {startSnippet}");
                }

                if (crxBytes.Length < 12)
                    throw new InvalidDataException("Invalid CRX header.");

                int zipStartOffset = 0;

                // Read version
                var version = BitConverter.ToInt32(crxBytes, 4);

                if (version == 2)
                {
                    // CRX2 format: 4 + 4 + 4 + publicKeyLength + 4 + signatureLength
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
                    // CRX3 format: read header size and skip it
                    var headerSize = BitConverter.ToInt32(crxBytes, 8);
                    if (headerSize < 0)
                        throw new InvalidDataException("Invalid CRX3 header length.");

                    zipStartOffset = checked(12 + headerSize);
                }
                else
                {
                    throw new Exception($"Unsupported CRX version: {version}");
                }

                if (zipStartOffset <= 0 || zipStartOffset >= crxBytes.Length)
                    throw new InvalidDataException("Invalid CRX header: ZIP payload is missing.");

                await Task.Run(() => ExtractZipBytes(crxBytes, extractPath, zipStartOffset));
            }
            catch (Exception ex)
            {
                throw new Exception($"Error extracting CRX file: {ex.Message}", ex);
            }
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
        /// <param name="url">Chrome Web Store URL (e.g., https://chrome.google.com/webstore/detail/bitwarden/.../nngceckbapebfimnlniiiahkandclblb)</param>
        /// <returns>Extension ID or null if not found</returns>
        public static string? ExtractExtensionIdFromUrl(string url)
        {
            try
            {
                // Chrome Web Store URLs format:
                // https://chrome.google.com/webstore/detail/[name]/[ID]
                // or https://chromewebstore.google.com/detail/[name]/[ID]
                
                if (!url.Contains("chrome.google.com/webstore") && !url.Contains("chromewebstore.google.com"))
                {
                    return null;
                }

                var uri = new Uri(url);
                var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                
                // The extension ID is typically the last segment and is 32 characters long
                for (int i = segments.Length - 1; i >= 0; i--)
                {
                    var segment = segments[i];
                    // Extension IDs are 32 lowercase letters (a-p)
                    if (segment.Length == 32 && segment.All(c => c >= 'a' && c <= 'p'))
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
            return extensionId.Length == 32 && extensionId.All(c => c >= 'a' && c <= 'p');
        }
    }
}
