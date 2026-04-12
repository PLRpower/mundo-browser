using System.IO;
using System.IO.Compression;
using System.Net.Http;

namespace MundoBrowser.Services
{
    public class ExtensionDownloader
    {
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
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _extensionsPath = Path.Combine(appDataPath, "MundoBrowser", "Extensions");
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
                var crxFilePath = Path.Combine(_extensionsPath, $"{extensionId}.crx");
                byte[]? crxBytes = null;
                
                foreach (var crxUrl in urlVariants)
                {
                    int maxRetries = 1;
                    for (int i = 0; i <= maxRetries; i++)
                    {
                        try
                        {
                            var response = await _httpClient.GetAsync(crxUrl);
                            
                            // If we get 204 (NoContent) or 404, this URL variant doesn't work for this extension, try next variant
                            if (response.StatusCode == System.Net.HttpStatusCode.NoContent || response.StatusCode == System.Net.HttpStatusCode.NotFound)
                                break; 
                                
                            if (response.IsSuccessStatusCode)
                            {
                                crxBytes = await response.Content.ReadAsByteArrayAsync();
                                if (crxBytes != null && crxBytes.Length > 0)
                                    goto DownloadFinished;
                            }
                        }
                        catch (Exception) when (i < maxRetries)
                        {
                            await Task.Delay(300);
                        }
                    }
                }

                DownloadFinished:
                if (crxBytes == null || crxBytes.Length == 0)
                {
                    throw new Exception($"Could not download extension {extensionId}. All server attempts returned 'No Content' (204). This usually means the extension is restricted, requires a specific region, or the ID is invalid for direct download.");
                }

                await File.WriteAllBytesAsync(crxFilePath, crxBytes);

                // Extract the CRX file
                var extractPath = Path.Combine(_extensionsPath, extensionId);
                if (Directory.Exists(extractPath))
                {
                    Directory.Delete(extractPath, true);
                }
                Directory.CreateDirectory(extractPath);

                // CRX files are essentially ZIP files with a header
                // We need to skip the CRX header and extract the ZIP content
                await ExtractCrxFile(crxFilePath, extractPath);

                // Clean up the CRX file
                File.Delete(crxFilePath);

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
        private async Task ExtractCrxFile(string crxPath, string extractPath)
        {
            try
            {
                var crxBytes = await File.ReadAllBytesAsync(crxPath);
                
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
                    await ExtractZipBytes(crxBytes, extractPath);
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

                int zipStartOffset = 0;

                // Read version
                var version = BitConverter.ToInt32(crxBytes, 4);

                if (version == 2)
                {
                    // CRX2 format: 4 + 4 + 4 + publicKeyLength + 4 + signatureLength
                    var publicKeyLength = BitConverter.ToInt32(crxBytes, 8);
                    var signatureLength = BitConverter.ToInt32(crxBytes, 12);
                    zipStartOffset = 16 + publicKeyLength + signatureLength;
                }
                else if (version == 3)
                {
                    // CRX3 format: read header size and skip it
                    var headerSize = BitConverter.ToInt32(crxBytes, 8);
                    zipStartOffset = 12 + headerSize;
                }
                else
                {
                    throw new Exception($"Unsupported CRX version: {version}");
                }

                // Extract the ZIP portion
                var zipBytes = new byte[crxBytes.Length - zipStartOffset];
                Array.Copy(crxBytes, zipStartOffset, zipBytes, 0, zipBytes.Length);

                await ExtractZipBytes(zipBytes, extractPath);
            }
            catch (Exception ex)
            {
                throw new Exception($"Error extracting CRX file: {ex.Message}", ex);
            }
        }

        private async Task ExtractZipBytes(byte[] zipBytes, string extractPath)
        {
            // Write to a temporary ZIP file and extract
            var tempZipPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.zip");
            await File.WriteAllBytesAsync(tempZipPath, zipBytes);

            try
            {
                // Ensure the directory is clean
                if (Directory.Exists(extractPath))
                {
                    try { Directory.Delete(extractPath, true); } catch { }
                }
                Directory.CreateDirectory(extractPath);
                
                ZipFile.ExtractToDirectory(tempZipPath, extractPath);
            }
            finally
            {
                if (File.Exists(tempZipPath))
                    File.Delete(tempZipPath);
            }
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
    }
}
