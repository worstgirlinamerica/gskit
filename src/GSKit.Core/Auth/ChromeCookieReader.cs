using Microsoft.Data.Sqlite;
using SQLitePCL;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace GSKit.Core.Auth;

/// <summary>
/// Reads gamestop.com cookies from Chrome/Chromium on macOS.
/// Uses macOS system libsqlite3 (/usr/lib/libsqlite3.dylib) — no bundled native lib needed.
/// </summary>
public static class ChromeCookieReader
{
    private static readonly string[] ChromePaths =
    [
        "Library/Application Support/Google/Chrome/Default/Cookies",
        "Library/Application Support/Google/Chrome/Profile 1/Cookies",
        "Library/Application Support/Chromium/Default/Cookies",
        "Library/Application Support/Microsoft Edge/Default/Cookies",
    ];

    private static readonly string[] TargetCookies =
        ["cf_clearance", "dwsid", "dwanonymous_", "sid", "__cflb"];

    public static async Task<Dictionary<string, string>> GetGameStopCookiesAsync()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        string? cookiePath = null;
        foreach (var rel in ChromePaths)
        {
            var full = Path.Combine(home, rel);
            if (File.Exists(full)) { cookiePath = full; break; }
        }

        if (cookiePath == null)
            throw new FileNotFoundException(
                "Chrome cookie store not found. Checked: " + string.Join(", ", ChromePaths));

        var tmp = Path.GetTempFileName() + ".db";
        File.Copy(cookiePath, tmp, overwrite: true);

        try   { return await ReadCookiesAsync(tmp); }
        finally { try { File.Delete(tmp); } catch { } }
    }

    private static async Task<Dictionary<string, string>> ReadCookiesAsync(string dbPath)
    {
        // Use macOS system libsqlite3 — works in self-contained single-file publish
        raw.SetProvider(new SQLite3Provider_sqlite3());

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var encryptionKey = await GetChromeKeyAsync();

        var connStr = $"Data Source={dbPath};Mode=ReadOnly;Cache=Shared";
        await using var db = new SqliteConnection(connStr);
        await db.OpenAsync();

        var cmd = db.CreateCommand();
        cmd.CommandText = @"
            SELECT name, value, encrypted_value
            FROM cookies
            WHERE (host_key = '.gamestop.com' OR host_key = 'www.gamestop.com')
              AND name IN ('" + string.Join("','", TargetCookies) + @"')
            ORDER BY last_access_utc DESC";

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var name    = reader.GetString(0);
            var plain   = reader.IsDBNull(1) ? "" : reader.GetString(1);
            var encBlob = reader.IsDBNull(2) ? null : (byte[])reader["encrypted_value"];

            if (result.ContainsKey(name)) continue;

            string? value = null;

            if (!string.IsNullOrEmpty(plain))
                value = plain;
            else if (encBlob?.Length > 0 && encryptionKey != null)
                value = DecryptChromeValue(encBlob, encryptionKey);

            if (value != null)
                result[name] = value;
        }

        return result;
    }

    private static string? DecryptChromeValue(byte[] encrypted, byte[] key)
    {
        try
        {
            if (encrypted.Length < 3) return null;
            var prefix = Encoding.ASCII.GetString(encrypted, 0, 3);

            if (prefix is "v10" or "v20")
            {
                var nonce      = encrypted[3..15];
                var ciphertext = encrypted[15..(encrypted.Length - 16)];
                var tag        = encrypted[(encrypted.Length - 16)..];

                using var aes = new AesGcm(key, 16);
                var plaintext = new byte[ciphertext.Length];
                aes.Decrypt(nonce, ciphertext, tag, plaintext);
                return Encoding.UTF8.GetString(plaintext);
            }

            var iv = new byte[16];
            Array.Fill(iv, (byte)' ');
            using var aesOld = Aes.Create();
            aesOld.Key  = key;
            aesOld.IV   = iv;
            aesOld.Mode = CipherMode.CBC;
            using var dec = aesOld.CreateDecryptor();
            var result = dec.TransformFinalBlock(encrypted, 0, encrypted.Length);
            return Encoding.UTF8.GetString(result).TrimEnd('\0');
        }
        catch { return null; }
    }

    private static async Task<byte[]?> GetChromeKeyAsync()
    {
        try
        {
            var psi = new ProcessStartInfo("security",
                "find-generic-password -wa 'Chrome Safe Storage'")
            {
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
            };

            using var proc = Process.Start(psi)!;
            var password = (await proc.StandardOutput.ReadToEndAsync()).Trim();
            await proc.WaitForExitAsync();

            if (string.IsNullOrEmpty(password)) return null;

            return Rfc2898DeriveBytes.Pbkdf2(
                password:       Encoding.UTF8.GetBytes(password),
                salt:           Encoding.UTF8.GetBytes("saltysalt"),
                iterations:     1003,
                hashAlgorithm:  HashAlgorithmName.SHA1,
                outputLength:   16
            );
        }
        catch { return null; }
    }
}
