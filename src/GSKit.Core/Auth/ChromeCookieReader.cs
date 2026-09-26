using Microsoft.Data.Sqlite;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace GSKit.Core.Auth;

/// <summary>
/// Reads gamestop.com cookies from Chrome/Chromium on macOS.
/// Chrome stores cookies in an SQLite file at:
///   ~/Library/Application Support/Google/Chrome/Default/Cookies
///
/// Cookie values are either:
///   - Plain text (older cookies, some session cookies)
///   - v10 encrypted: AES-128-CBC, key derived from macOS Keychain
///   - v20 encrypted: same but newer Chrome versions
///
/// We handle both. The Keychain key extraction uses `security` CLI.
/// </summary>
public static class ChromeCookieReader
{
    // Chrome profiles to check in order
    private static readonly string[] ChromePaths =
    [
        "Library/Application Support/Google/Chrome/Default/Cookies",
        "Library/Application Support/Google/Chrome/Profile 1/Cookies",
        "Library/Application Support/Chromium/Default/Cookies",
        "Library/Application Support/Microsoft Edge/Default/Cookies",  // Edge uses same format
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
                "Chrome cookie store not found. " +
                "Checked: " + string.Join(", ", ChromePaths));

        // Chrome holds a lock on the file while running — copy to temp first
        var tmp = Path.GetTempFileName() + ".db";
        File.Copy(cookiePath, tmp, overwrite: true);

        try
        {
            return await ReadCookiesAsync(tmp);
        }
        finally
        {
            try { File.Delete(tmp); } catch { /* ignore */ }
        }
    }

    private static async Task<Dictionary<string, string>> ReadCookiesAsync(string dbPath)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var encryptionKey = await GetChromeKeyAsync(); // null = skip decryption

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

            if (result.ContainsKey(name)) continue; // take most recent

            string? value = null;

            if (!string.IsNullOrEmpty(plain))
            {
                value = plain;
            }
            else if (encBlob?.Length > 0 && encryptionKey != null)
            {
                value = DecryptChromeValue(encBlob, encryptionKey);
            }

            if (value != null)
                result[name] = value;
        }

        return result;
    }

    // ── Chrome v10/v20 decryption (macOS) ────────────────────────────────────
    // Format: b"v10" or b"v20" + 12-byte nonce + ciphertext + 16-byte tag (AES-GCM)
    // Key: PBKDF2-SHA1(password, b"saltysalt", 1003 iterations, 16 bytes)
    // Password: from macOS Keychain item "Chrome Safe Storage"

    private static string? DecryptChromeValue(byte[] encrypted, byte[] key)
    {
        try
        {
            // Check for v10/v20 prefix
            if (encrypted.Length < 3) return null;
            var prefix = Encoding.ASCII.GetString(encrypted, 0, 3);

            if (prefix is "v10" or "v20")
            {
                // AES-GCM: 3 prefix + 12 nonce + ciphertext + 16 tag
                var nonce      = encrypted[3..15];
                var ciphertext = encrypted[15..(encrypted.Length - 16)];
                var tag        = encrypted[(encrypted.Length - 16)..];

                using var aes = new AesGcm(key, 16);
                var plaintext = new byte[ciphertext.Length];
                aes.Decrypt(nonce, ciphertext, tag, plaintext);
                return Encoding.UTF8.GetString(plaintext);
            }

            // Older format: AES-CBC with IV = space * 16
            // (rare on modern Chrome but still in some profiles)
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
        catch
        {
            return null; // decryption failed — cookie not accessible
        }
    }

    // Gets the AES key from macOS Keychain via `security` CLI
    private static async Task<byte[]?> GetChromeKeyAsync()
    {
        try
        {
            // `security find-generic-password -wa 'Chrome Safe Storage'`
            // Returns the Keychain password Chrome uses to derive the AES key
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

            // PBKDF2-SHA1, salt = "saltysalt", 1003 iterations, 16 bytes
            return Rfc2898DeriveBytes.Pbkdf2(
                password: Encoding.UTF8.GetBytes(password),
                salt:      Encoding.UTF8.GetBytes("saltysalt"),
                iterations: 1003,
                hashAlgorithm: HashAlgorithmName.SHA1,
                outputLength: 16
            );
        }
        catch
        {
            return null;
        }
    }
}
