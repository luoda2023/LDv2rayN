using System.Security.Cryptography;
using System.Text;

// Usage: decrypt_log <logfile> <keyfile> [tail-lines]
var logFile = args.Length > 0 ? args[0] : "";
var keyFile = args.Length > 1 ? args[1] : "";
var tail = args.Length > 2 ? int.Parse(args[2]) : 100;

if (string.IsNullOrEmpty(logFile) || string.IsNullOrEmpty(keyFile))
{
    Console.WriteLine("Usage: decrypt_log <logfile> <keyfile> [tail-lines]");
    return;
}

var key = Convert.FromBase64String(File.ReadAllText(keyFile).Trim());
var bytes = File.ReadAllBytes(logFile);
var records = new List<string>();
var offset = 0;
while (offset + 4 <= bytes.Length)
{
    var len = BitConverter.ToInt32(bytes, offset);
    offset += 4;
    var total = 12 + 16 + len;
    if (offset + total > bytes.Length || len < 0) break;
    var nonce = bytes.AsSpan(offset, 12).ToArray();
    var tag = bytes.AsSpan(offset + 12, 16).ToArray();
    var cipher = bytes.AsSpan(offset + 12 + 16, len).ToArray();
    offset += total;
    try
    {
        var plain = new byte[len];
        using var aes = new AesGcm(key, 16);
        aes.Decrypt(nonce, cipher, tag, plain);
        records.Add(Encoding.UTF8.GetString(plain));
    }
    catch (Exception ex)
    {
        records.Add($"[DECRYPT-FAIL: {ex.Message}]");
    }
}

var start = Math.Max(0, records.Count - tail);
for (var i = start; i < records.Count; i++)
{
    Console.WriteLine(records[i]);
}