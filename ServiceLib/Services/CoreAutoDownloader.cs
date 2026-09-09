using System.IO.Compression;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;

namespace ServiceLib.Services;

/// <summary>
/// Detects missing Xray-core binaries at startup and downloads the latest
/// release from XTLS/Xray-core, extracting into bin/xray/. Never blocks the
/// UI — runs on a background thread, logs success/failure to Logging.SaveLog.
/// Direct github.com download often stalls mid-file on CN networks, so the
/// download is retried through GitHub mirror prefixes; every attempt verifies
/// the received byte count against Content-Length before extraction.
/// </summary>
public static class CoreAutoDownloader
{
    private static readonly string _tag = "CoreAutoDownloader";

    // Try direct first, then mirror prefixes. "" = direct github.com.
    private static readonly string[] _mirrorPrefixes =
    [
        "",
        "https://ghfast.top/",
        "https://gh-proxy.com/",
        "https://ghproxy.net/",
        "https://mirror.ghproxy.com/",
    ];

    private static readonly Lazy<HttpClient> _client = new(() => new HttpClient
    {
        Timeout = TimeSpan.FromMinutes(5),
        DefaultRequestHeaders = { { "User-Agent", "LDv2rayN" } },
    });

    /// <summary>
    /// Kick off the check-and-download in a background task. Safe to call on
    /// every launch — the check is idempotent (only downloads when missing).
    /// </summary>
    public static void CheckAndDownloadXray()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await EnsureXrayAsync();
            }
            catch (Exception ex)
            {
                Logging.SaveLog(_tag, ex);
            }
        });
    }

    /// <summary>
    /// Synchronous entry point for tests / explicit calls. Returns true if
    /// xray was downloaded or already present, false on failure.
    /// </summary>
    public static async Task<bool> EnsureXrayAsync()
    {
        // Skip if a non-Windows target doesn't apply (still try; failure is fine)
        var targetDir = Utils.GetBinPath("", ECoreType.Xray.ToString());
        if (!Directory.Exists(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        var existing = FindXrayBinary(targetDir);
        if (existing != null)
        {
            Logging.SaveLog($"{_tag}: xray present at {existing}, skipping download");
            return true; // Already there
        }

        Logging.SaveLog($"{_tag}: xray missing in {targetDir}, starting download...");

        var version = await GetLatestVersionAsync();
        if (version == null)
        {
            Logging.SaveLog($"{_tag}: could not resolve latest version from GitHub");
            return false;
        }

        var downloadUrl = BuildDownloadUrl(version);
        foreach (var mirror in _mirrorPrefixes)
        {
            var url = $"{mirror}{downloadUrl}";
            var zipPath = Path.Combine(Path.GetTempPath(), $"LDv2rayN_xray_{Guid.NewGuid():N}.zip");
            var start = DateTime.UtcNow;
            try
            {
                Logging.SaveLog($"{_tag}: downloading {url}");
                var bytes = await DownloadToFileAsync(url, zipPath);
                Logging.SaveLog($"{_tag}: finished {bytes / 1024} KB in {DateTime.UtcNow - start} via '{mirror}'");

                await ExtractAsync(zipPath, targetDir);

                var installed = FindXrayBinary(targetDir);
                if (installed == null)
                {
                    Logging.SaveLog($"{_tag}: extracted but xray binary not found in {targetDir}");
                    return false;
                }

                Logging.SaveLog($"{_tag}: xray installed at {installed} (v{version}) via '{mirror}'");
                return true;
            }
            catch (Exception ex)
            {
                Logging.SaveLog($"{_tag}: download via '{mirror}' failed after {DateTime.UtcNow - start}: {ex.Message}");
            }
            finally
            {
                try
                {
                    if (File.Exists(zipPath)) File.Delete(zipPath);
                }
                catch { /* ignore cleanup */ }
            }
        }

        Logging.SaveLog($"{_tag}: all {_mirrorPrefixes.Length} source(s) failed for v{version}");
        return false;
    }

    private static async Task<long> DownloadToFileAsync(string url, string zipPath)
    {
        using (var response = await _client.Value.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new IOException($"HTTP {(int)response.StatusCode} from {url}");
            }

            var contentLength = response.Content.Headers.ContentLength;
            await using (var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
            await using (var stream = response.Content.ReadAsStream())
            {
                var buffer = new byte[81920];
                long bytes = 0;
                int n;
                while ((n = await stream.ReadAsync(buffer, CancellationToken.None)) > 0)
                {
                    await fs.WriteAsync(buffer.AsMemory(0, n), CancellationToken.None);
                    bytes += n;
                }

                // A truncated stream (stalled/reset CN connection) would otherwise
                // be silently accepted and fail only at extraction. Reject early so
                // the next mirror is tried with the full payload.
                if (contentLength.HasValue && bytes != contentLength.Value)
                {
                    throw new IOException($"incomplete download: {bytes} of {contentLength} bytes from {url}");
                }
                return bytes;
            }
        }
    }

    private static async Task<string?> GetLatestVersionAsync()
    {
        // Direct api.github.com first; gh-proxy.com proxies the API when direct fails.
        var apiUrls = new[]
        {
            "https://api.github.com/repos/XTLS/Xray-core/releases/latest",
            "https://gh-proxy.com/https://api.github.com/repos/XTLS/Xray-core/releases/latest",
        };

        foreach (var url in apiUrls)
        {
            try
            {
                var json = await _client.Value.GetStringAsync(url);
                // Naive tag_name extraction (avoid adding a JSON library dependency).
                var idx = json.IndexOf("\"tag_name\"", StringComparison.Ordinal);
                if (idx < 0) continue;
                var quoteOpen = json.IndexOf('"', idx + 10);
                if (quoteOpen < 0) continue;
                var quoteClose = json.IndexOf('"', quoteOpen + 1);
                if (quoteClose < 0) continue;
                var tag = json.Substring(quoteOpen + 1, quoteClose - quoteOpen - 1);
                return tag.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? tag[1..] : tag;
            }
            catch (Exception ex)
            {
                Logging.SaveLog($"{_tag}: GetLatestVersion via {url} failed: {ex.Message}");
            }
        }
        return null;
    }

    private static string BuildDownloadUrl(string version)
    {
        var repo = Global.CoreUrls[ECoreType.Xray]; // "XTLS/Xray-core"
        var prefix = $"{Global.GithubUrl}/{repo}/releases/download/v{version}";

        if (Utils.IsWindows())
        {
            var is64 = Environment.Is64BitProcess;
            return is64 ? $"{prefix}/Xray-windows-64.zip" : $"{prefix}/Xray-windows-32.zip";
        }
        if (Utils.IsMacOS())
        {
            // Prefer arm64 when running on arm64, else amd64
            var isArm64 = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) &&
                          RuntimeInformation.OSArchitecture == Architecture.Arm64;
            return isArm64 ? $"{prefix}/Xray-darwin-arm64.zip" : $"{prefix}/Xray-darwin-amd64.zip";
        }
        if (Utils.IsLinux())
        {
            var arch = RuntimeInformation.OSArchitecture;
            return arch switch
            {
                Architecture.Arm64 => $"{prefix}/Xray-linux-arm64-v8a.zip",
                Architecture.Arm => $"{prefix}/Xray-linux-32.zip",
                _ => $"{prefix}/Xray-linux-64.zip",
            };
        }

        // Unknown platform — default to linux-x64
        return $"{prefix}/Xray-linux-64.zip";
    }

    private static async Task ExtractAsync(string zipPath, string targetDir)
    {
        // .NET 5+ supports ZipFile.OpenRead + async entry copy
        await using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name) || entry.Name.Contains('/', StringComparison.Ordinal)
                || entry.Name.Contains('\\', StringComparison.Ordinal))
            {
                continue; // Skip directory entries and any nested paths
            }
            // Only extract the files we care about
            var lower = entry.Name.ToLowerInvariant();
            if (!lower.EndsWith(".exe") && !lower.EndsWith(".dll")
                && !lower.EndsWith(".so") && !lower.EndsWith(".dylib")
                && !lower.EndsWith(".json") && !lower.EndsWith(".md")
                && !lower.Contains("xray") && !lower.Contains("sing-box")
                && !lower.Contains("libgeodata") && !lower.Contains("libgeomap")
                && !lower.Contains("geoip") && !lower.Contains("geosite"))
            {
                continue;
            }

            var outPath = Path.Combine(targetDir, entry.Name);
            await using (var fs = new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.None))
            await using (var es = entry.Open())
            {
                await es.CopyToAsync(fs);
            }

            // Set executable bit on non-Windows
            if (Utils.IsNonWindows() && (lower.EndsWith(".exe") || lower.Contains("xray")
                                          || lower.Contains("sing-box") || lower.EndsWith(".so")))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "chmod",
                        Arguments = $"+x {outPath.AppendQuotes()}",
                        UseShellExecute = false,
                    });
                }
                catch { /* best effort */ }
            }
        }
    }

    private static string? FindXrayBinary(string dir)
    {
        var candidates = Utils.IsWindows()
            ? new[] { "xray.exe" }
            : new[] { "xray", "xray-client", "sing-box-client", "sing-box" };

        foreach (var name in candidates)
        {
            var path = Path.Combine(dir, name);
            if (File.Exists(path)) return path;
        }
        return null;
    }
}