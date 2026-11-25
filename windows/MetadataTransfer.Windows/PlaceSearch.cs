using System.Net.Http;
using System.Text.Json;

namespace MetadataTransfer.Windows;

internal static class PlaceSearch
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(12) };

    internal sealed record Result(string Title, double Latitude, double Longitude);

    public static async Task<Result[]> FindAsync(string query, CancellationToken cancellation)
    {
        using var response = await Client.GetAsync(
            "https://photon.komoot.io/api/?limit=6&q=" + Uri.EscapeDataString(query), cancellation);
        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync(cancellation);
        using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellation);
        return Parse(document.RootElement);
    }

    internal static Result[] Parse(JsonElement response)
    {
        var results = new List<Result>();
        foreach (var feature in response.GetProperty("features").EnumerateArray())
        {
            var properties = feature.GetProperty("properties");
            var coordinates = feature.GetProperty("geometry").GetProperty("coordinates");
            if (coordinates.GetArrayLength() < 2) continue;
            var longitude = coordinates[0].GetDouble();
            var latitude = coordinates[1].GetDouble();
            if (!(latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180)) continue;

            string? Text(string key) => properties.TryGetProperty(key, out var value) &&
                value.ValueKind == JsonValueKind.String ? value.GetString() : null;

            var address = string.Join(" ", new[] { Text("housenumber"), Text("street") }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
            var title = string.Join(", ", new[] { Text("name"), address, Text("city"), Text("state"), Text("country") }
                .Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase));
            if (title.Length > 0) results.Add(new(title, latitude, longitude));
        }
        return results.Distinct().ToArray();
    }
}
