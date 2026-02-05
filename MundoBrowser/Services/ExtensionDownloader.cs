using System.IO;
using System.IO.Compression;
using System.Net.Http;

namespace MundoBrowser.Services
{
    public class ExtensionDownloader
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private readonly string _extensionsPath;

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
                // Chrome Web Store CRX download URL format
                // This uses the Chrome Web Store API to download the CRX file
                var crxUrl = $"https://clients2.google.com/service/update2/crx?response=redirect&acceptformat=crx2,crx3&prodversion=119.0.0.0&x=id%3D{extensionId}%26installsource%3Dondemand%26uc";

                // Download the CRX file
                var crxFilePath = Path.Combine(_extensionsPath, $"{extensionId}.crx");
                var response = await _httpClient.GetAsync(crxUrl);
                
                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"Failed to download extension. Status code: {response.StatusCode}");
                }

                var crxBytes = await response.Content.ReadAsByteArrayAsync();
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

                // Check for CRX magic number
                if (crxBytes.Length < 4 || 
                    crxBytes[0] != 'C' || crxBytes[1] != 'r' || 
                    crxBytes[2] != '2' || crxBytes[3] != '4')
                {
                    throw new Exception("Invalid CRX file format");
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

                // Write to a temporary ZIP file and extract
                var tempZipPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.zip");
                await File.WriteAllBytesAsync(tempZipPath, zipBytes);

                try
                {
                    ZipFile.ExtractToDirectory(tempZipPath, extractPath);
                }
                finally
                {
                    File.Delete(tempZipPath);
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Error extracting CRX file: {ex.Message}", ex);
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
