namespace ServiceLib.Common;

/// <summary>
/// Application logging funnel. Every persisted record is written through
/// <see cref="LogCrypto"/> as an AES-256-GCM ciphertext record — the log files
/// under guiLogs never contain plaintext. If encryption is unavailable the log
/// entry is dropped rather than written in the clear.
/// </summary>
public class Logging
{
    private static readonly object Lock = new();
    private static bool _enabled = true;
    private static string _currentFile = "";
    private static DateTime _currentDay = DateTime.MinValue;

    public static void Setup()
    {
        _ = LogCrypto.Init();
        RefreshFile();
    }

    public static void LoggingEnabled(bool enable)
    {
        lock (Lock)
        {
            _enabled = enable;
        }
    }

    public static void SaveLog(string strContent)
    {
        if (strContent.IsNullOrEmpty())
        {
            return;
        }
        Write("INFO", strContent);
    }

    public static void SaveLog(string strTitle, Exception ex)
    {
        Write("DEBUG", $"{strTitle},{ex.Message}");
        if (ex.StackTrace.IsNotEmpty())
        {
            Write("DEBUG", ex.StackTrace);
        }
        if (ex.InnerException != null)
        {
            Write("ERROR", $"{strTitle} Inner: {ex.InnerException.Message}");
        }
    }

    private static void Write(string level, string text)
    {
        lock (Lock)
        {
            if (!_enabled)
            {
                return;
            }
            try
            {
                RefreshFile();
                if (_currentFile.IsNullOrEmpty())
                {
                    return;
                }
                var entry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}-{level} {text}";
                _ = LogCrypto.AppendRecord(_currentFile, entry);
            }
            catch
            {
                // Never write plaintext on failure — silently drop instead.
            }
        }
    }

    private static void RefreshFile()
    {
        var now = DateTime.Now;
        if (_currentDay.Date == now.Date)
        {
            return;
        }
        _currentDay = now;
        _currentFile = Utils.GetLogPath($"{now:yyyy-MM-dd}.txt");
    }
}
