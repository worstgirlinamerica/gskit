using System.Text.Json;

namespace GSKit.Core.Http;

/// <summary>
/// Geocodes a US zip code to lat/lon using OpenStreetMap Nominatim.
/// Free, no API key required. Results are cached in memory per run.
/// </summary>
public static class GeocodingHelper
{
    private static readonly HttpClient _http = new();
    private static readonly Dictionary<string, (double lat, double lon)> _cache = [];

    static GeocodingHelper()
    {
        // Nominatim requires a descriptive User-Agent
        _http.DefaultRequestHeaders.Add("User-Agent", "GSKit/1.0 (github.com/you/gskit)");
    }

    public static async Task<(double lat, double lon)> ZipToLatLonAsync(
        string postalCode,
        CancellationToken ct = default)
    {
        if (_cache.TryGetValue(postalCode, out var cached))
            return cached;

        var url = $"https://nominatim.openstreetmap.org/search" +
                  $"?postalcode={postalCode}&country=US&format=json&limit=1";

        var json = await _http.GetStringAsync(url, ct);
        using var doc = JsonDocument.Parse(json);
        var arr = doc.RootElement;

        if (arr.GetArrayLength() == 0)
            throw new ArgumentException($"Could not geocode zip code: {postalCode}");

        var first = arr[0];
        var result = (
            double.Parse(first.GetProperty("lat").GetString()!),
            double.Parse(first.GetProperty("lon").GetString()!)
        );

        _cache[postalCode] = result;
        return result;
    }
}
