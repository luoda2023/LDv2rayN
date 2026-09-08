using System.Security.Cryptography;

namespace ServiceLib.Common;

/// <summary>
/// Encrypts log records at rest using AES-256-GCM.
///
/// File layout: a sequence of self-describing records, each written as
///   [4-byte little-endian ciphertext length][12-byte nonce][16-byte tag][ciphertext]
/// A fresh random nonce is used for every record, and nothing is ever written
/// to the log file in plaintext. The 256-bit key is generated on first run and
/// kept in a separate file outside the logs directory so the log directory
/// contains ciphertext only.
/// </summary>
public static class LogCrypto
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const string KeyFileName = ".ldv2rayn_logkey";
    private static readonly object Sync = new();
    private static byte[]? _key;

    /// <summary>
    /// Ensures the encryption key is loaded or created. Returns true on success.
    /// </summary>
    public static bool Init()
    {
        lock (Sync)
        {
            if (_key != null)
            {
                return true;
            }
            try
            {
                _key = LoadOrCreateKey();
                return _key != null;
            }
            catch
            {
                _key = null;
                return false;
            }
        }
    }

    /// <summary>
    /// Encrypts and appends one record to <paramref name="filePath"/>.
    /// Returns true on success. Never falls back to plaintext.
    /// </summary>
    public static bool AppendRecord(string filePath, string content)
    {
        if (content.IsNullOrEmpty() || !Init())
        {
            return false;
        }

        try
        {
            var nonce = RandomNumberGenerator.GetBytes(NonceSize);
            var tag = new byte[TagSize];
            var plainBytes = System.Text.Encoding.UTF8.GetBytes(content);
            var cipher = new byte[plainBytes.Length];

            using (var aes = new AesGcm(_key!, TagSize))
            {
                aes.Encrypt(nonce, plainBytes, cipher, tag);
            }

            var header = BitConverter.GetBytes(cipher.Length);
            var record = new byte[header.Length + nonce.Length + tag.Length + cipher.Length];
            Buffer.BlockCopy(header, 0, record, 0, header.Length);
            Buffer.BlockCopy(nonce, 0, record, header.Length, nonce.Length);
            Buffer.BlockCopy(tag, 0, record, header.Length + nonce.Length, tag.Length);
            Buffer.BlockCopy(cipher, 0, record, header.Length + nonce.Length + tag.Length, cipher.Length);

            Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? ".");
            using var fs = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.Read);
            fs.Write(record, 0, record.Length);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Reads and decrypts every record in <paramref name="filePath"/>.
    /// Returns an empty list when the file is missing or undecryptable.
    /// </summary>
    public static List<string> DecryptFile(string filePath)
    {
        var results = new List<string>();
        if (!Init() || !File.Exists(filePath))
        {
            return results;
        }

        try
        {
            var bytes = File.ReadAllBytes(filePath);
            var offset = 0;
            while (offset + 4 <= bytes.Length)
            {
                var len = BitConverter.ToInt32(bytes, offset);
                offset += 4;
                var total = NonceSize + TagSize + len;
                if (offset + total > bytes.Length || len < 0)
                {
                    break;
                }
                var nonce = bytes.AsSpan(offset, NonceSize).ToArray();
                var tag = bytes.AsSpan(offset + NonceSize, TagSize).ToArray();
                var cipher = bytes.AsSpan(offset + NonceSize + TagSize, len).ToArray();
                offset += total;

                var plain = new byte[len];
                using (var aes = new AesGcm(_key!, TagSize))
                {
                    aes.Decrypt(nonce, cipher, tag, plain);
                }
                results.Add(System.Text.Encoding.UTF8.GetString(plain));
            }
        }
        catch
        {
            // File is corrupt or key mismatch — do not expose partial plaintext.
            results.Clear();
        }
        return results;
    }

    private static byte[]? LoadOrCreateKey()
    {
        var keyPath = GetKeyPath();
        if (File.Exists(keyPath))
        {
            try
            {
                var base64 = File.ReadAllText(keyPath).Trim();
                if (base64.IsNotEmpty())
                {
                    return Convert.FromBase64String(base64);
                }
            }
            catch
            {
                // Corrupt key file — regenerate below.
            }
        }

        try
        {
            var newKey = RandomNumberGenerator.GetBytes(32);
            File.WriteAllText(keyPath, Convert.ToBase64String(newKey));
            RestrictFileAccess(keyPath);
            return newKey;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Best-effort restriction of key file access to the current user.
    /// Windows relies on the per-user profile ACLs; on Unix the mode is set to 0600.
    /// </summary>
    private static void RestrictFileAccess(string filePath)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(filePath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        catch
        {
            // Non-fatal — the key is still outside the (ciphertext-only) log directory.
        }
    }

    /// <summary>
    /// Key lives next to the config (outside the logs directory).
    /// </summary>
    private static string GetKeyPath()
    {
        var dir = Utils.StartupPath();
        return Path.Combine(dir, KeyFileName);
    }
}
