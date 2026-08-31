using System.IO;
using System.Text;
using System.Text.Json;

namespace MundoBrowser.Helpers;

/// <summary>
/// Provides atomic file write and robust read operations to prevent file corruption.
/// </summary>
public static class AtomicFileHelper
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    /// <summary>
    /// Writes text to a file atomically by writing to a temporary file and moving it into place.
    /// </summary>
    public static async Task WriteAllTextAtomicAsync(string destinationPath, string content, CancellationToken cancellationToken = default)
    {
        string? directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        string tempPath = destinationPath + $".tmp.{Guid.NewGuid():N}";

        try
        {
            await using (var stream = new FileStream(
                tempPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true))
            await using (var writer = new StreamWriter(stream, Utf8NoBom))
            {
                await writer.WriteAsync(content.AsMemory(), cancellationToken);
                await writer.FlushAsync(cancellationToken);
            }

            // Retry moving up to 3 times to handle transient locks by indexing/antivirus
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    File.Move(tempPath, destinationPath, overwrite: true);
                    return;
                }
                catch (IOException) when (attempt < 3)
                {
                    await Task.Delay(50 * attempt, cancellationToken);
                }
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { /* best effort */ }
            }
        }
    }

    /// <summary>
    /// Writes an object serialized to JSON atomically.
    /// </summary>
    public static async Task WriteJsonAtomicAsync<T>(string destinationPath, T data, JsonSerializerOptions? options = null, CancellationToken cancellationToken = default)
    {
        string? directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        string tempPath = destinationPath + $".tmp.{Guid.NewGuid():N}";

        try
        {
            await using (var stream = new FileStream(
                tempPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, data, options, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    File.Move(tempPath, destinationPath, overwrite: true);
                    return;
                }
                catch (IOException) when (attempt < 3)
                {
                    await Task.Delay(50 * attempt, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { /* best effort */ }
            }
        }
    }

    /// <summary>
    /// Reads all text safely with shared read access. Returns null if file doesn't exist.
    /// </summary>
    public static async Task<string?> ReadAllTextSafeAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                bufferSize: 4096,
                useAsync: true);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return await reader.ReadToEndAsync(cancellationToken);
        }
        catch
        {
            return null;
        }
    }
}
