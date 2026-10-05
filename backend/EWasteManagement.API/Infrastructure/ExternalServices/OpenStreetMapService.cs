using System.Text.Json;

namespace EWasteManagement.API.Infrastructure.ExternalServices;

public class OpenStreetMapService : IGeoService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenStreetMapService> _logger;
    private readonly string _photonBaseUrl;
    private readonly string _osrmBaseUrl;
    private readonly string _userAgent;

    public OpenStreetMapService(HttpClient httpClient, IConfiguration configuration, ILogger<OpenStreetMapService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        // Public demo servers by default — no API key, no billing.
        // Photon (not Nominatim) for geocoding: Nominatim's public server
        // increasingly 403s non-browser HTTP clients via edge bot-mitigation,
        // even with a compliant User-Agent — Photon doesn't have this problem.
        _photonBaseUrl = configuration["OpenStreetMap:PhotonBaseUrl"] ?? "https://photon.komoot.io/api";
        _osrmBaseUrl = configuration["OpenStreetMap:OsrmBaseUrl"] ?? "https://router.project-osrm.org";

        var contactEmail = configuration["OpenStreetMap:ContactEmail"] ?? "student-project@example.com";
        _userAgent = $"EWasteManagementSystem-StudentProject/1.0 ({contactEmail})";
    }

    public async Task<(decimal Latitude, decimal Longitude)?> GeocodeAsync(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            _logger.LogWarning("GeocodeAsync called with an empty address.");
            return null;
        }

        var url = $"{_photonBaseUrl}/?q={Uri.EscapeDataString(address)}&limit=1";

        try
        {
            using var requestMessage = new HttpRequestMessage(HttpMethod.Get, url);
            requestMessage.Headers.UserAgent.ParseAdd(_userAgent);

            var response = await _httpClient.SendAsync(requestMessage);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Photon call failed with status {StatusCode} for address '{Address}'.",
                    response.StatusCode, address);
                return null;
            }

            var body = await response.Content.ReadAsStringAsync();
            var parsed = JsonSerializer.Deserialize<PhotonResponse>(body);

            if (parsed is null || parsed.Features.Count == 0)
            {
                _logger.LogInformation("Photon found no match for address '{Address}'.", address);
                return null;
            }

            var coordinates = parsed.Features[0].Geometry.Coordinates;
            if (coordinates.Count < 2)
            {
                _logger.LogWarning("Photon returned an unexpected geometry for address '{Address}'.", address);
                return null;
            }

            // GeoJSON order is [lon, lat] — flipped to our (lat, lng) contract here.
            return ((decimal)coordinates[1], (decimal)coordinates[0]);
        }
        catch (Exception ex)
        {
            // Network failure, timeout, malformed JSON — none of these should
            // bubble up and break submission/job creation. Log and let the
            // caller fall back to "flag for staff".
            _logger.LogError(ex, "Geocoding threw an exception for address '{Address}'.", address);
            return null;
        }
    }

    public async Task<(decimal DistanceKm, int DurationMinutes)?> GetDistanceAsync(
        decimal originLat, decimal originLng,
        decimal destLat, decimal destLng)
    {
        // OSRM wants coordinates as longitude,latitude — the OPPOSITE order
        // from how they're stored/passed everywhere else in this codebase
        // (Google-style lat,lng). Getting this backwards silently returns a
        // route between the wrong points rather than an obvious error.
        var url = $"{_osrmBaseUrl}/route/v1/driving/{originLng},{originLat};{destLng},{destLat}?overview=false";

        try
        {
            using var requestMessage = new HttpRequestMessage(HttpMethod.Get, url);
            requestMessage.Headers.UserAgent.ParseAdd(_userAgent);

            var response = await _httpClient.SendAsync(requestMessage);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("OSRM call failed with status {StatusCode} ({OriginLat},{OriginLng} -> {DestLat},{DestLng}).",
                    response.StatusCode, originLat, originLng, destLat, destLng);
                return null;
            }

            var body = await response.Content.ReadAsStringAsync();
            var parsed = JsonSerializer.Deserialize<OsrmRouteResponse>(body);

            if (parsed is null || parsed.Code != "Ok" || parsed.Routes.Count == 0)
            {
                _logger.LogInformation("OSRM found no route ({OriginLat},{OriginLng} -> {DestLat},{DestLng}), code: {Code}.",
                    originLat, originLng, destLat, destLng, parsed?.Code);
                return null;
            }

            var route = parsed.Routes[0];
            var distanceKm = Math.Round((decimal)route.Distance / 1000m, 2);
            var durationMinutes = (int)Math.Ceiling(route.Duration / 60.0);

            return (distanceKm, durationMinutes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OSRM threw an exception ({OriginLat},{OriginLng} -> {DestLat},{DestLng}).",
                originLat, originLng, destLat, destLng);
            return null;
        }
    }

    public async Task<GeoRoute?> GetRouteAsync(
        decimal originLat, decimal originLng,
        decimal destLat, decimal destLng)
    {
        // lng,lat order again (see GetDistanceAsync); full GeoJSON geometry for the map line.
        var url = $"{_osrmBaseUrl}/route/v1/driving/{originLng},{originLat};{destLng},{destLat}?overview=full&geometries=geojson";

        try
        {
            using var requestMessage = new HttpRequestMessage(HttpMethod.Get, url);
            requestMessage.Headers.UserAgent.ParseAdd(_userAgent);

            var response = await _httpClient.SendAsync(requestMessage);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("OSRM route call failed with status {StatusCode} ({OriginLat},{OriginLng} -> {DestLat},{DestLng}).",
                    response.StatusCode, originLat, originLng, destLat, destLng);
                return null;
            }

            var body = await response.Content.ReadAsStringAsync();
            var parsed = JsonSerializer.Deserialize<OsrmRouteResponse>(body);
            if (parsed is null || parsed.Code != "Ok" || parsed.Routes.Count == 0)
                return null;

            var route = parsed.Routes[0];
            var points = (route.Geometry?.Coordinates ?? new())
                .Where(c => c.Count >= 2)
                .Select(c => (Lat: c[1], Lng: c[0]))
                .ToList();

            return new GeoRoute(
                Math.Round((decimal)route.Distance / 1000m, 2),
                (int)Math.Ceiling(route.Duration / 60.0),
                points);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OSRM route threw an exception ({OriginLat},{OriginLng} -> {DestLat},{DestLng}).",
                originLat, originLng, destLat, destLng);
            return null;
        }
    }
}
