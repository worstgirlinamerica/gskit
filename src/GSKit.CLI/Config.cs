using System.Text.Json;

namespace GSKit.CLI;

/// <summary>
/// Loads ~/.config/gskit/config.json if it exists.
/// Fields are null / zero if the file is absent or the key is not set.
/// </summary>
public sealed class GsConfig
{
    public string? DefaultZip    { get; init; }
    public double? DefaultRadius { get; init; }

    private static readonly string ConfigPath =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config", "gskit", "config.json");

    public static GsConfig Load()
    {
        if (!File.Exists(ConfigPath))
            return new GsConfig();

        try
        {
            var json = File.ReadAllText(ConfigPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string? zip    = null;
            double? radius = null;

            if (root.TryGetProperty("defaultZip", out var zp) && zp.ValueKind == JsonValueKind.String)
                zip = zp.GetString();

            if (root.TryGetProperty("defaultRadius", out var rp) && rp.ValueKind == JsonValueKind.Number)
                radius = rp.GetDouble();

            return new GsConfig { DefaultZip = zip, DefaultRadius = radius };
        }
        catch
        {
            // Malformed config — silently ignore and use defaults
            return new GsConfig();
        }
    }
}
