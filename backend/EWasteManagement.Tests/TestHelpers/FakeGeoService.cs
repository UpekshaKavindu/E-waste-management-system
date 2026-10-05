using EWasteManagement.API.Infrastructure.ExternalServices;

namespace EWasteManagement.Tests.TestHelpers;

// Stands in for the OpenStreetMap-backed GeoService so Collection tests never
// touch the network. Geocoding returns a configurable fixed point (or null to
// simulate an unresolvable address). Distances are looked up by the
// collector's (origin) coordinates, so each test can decide exactly how far
// each seeded collector is from the pickup.
public class FakeGeoService : IGeoService
{
    private readonly Dictionary<(decimal Lat, decimal Lng), (decimal DistanceKm, int DurationMinutes)?> _distances = new();

    public (decimal Latitude, decimal Longitude)? GeocodeResult { get; set; } = (6.9271m, 79.8612m);

    public (decimal DistanceKm, int DurationMinutes)? DefaultDistance { get; set; } = (5m, 12);

    public int GeocodeCalls { get; private set; }

    public void SetDistanceFrom(decimal originLat, decimal originLng, (decimal DistanceKm, int DurationMinutes)? distance)
        => _distances[(originLat, originLng)] = distance;

    public Task<(decimal Latitude, decimal Longitude)?> GeocodeAsync(string address)
    {
        GeocodeCalls++;
        return Task.FromResult(GeocodeResult);
    }

    public Task<(decimal DistanceKm, int DurationMinutes)?> GetDistanceAsync(
        decimal originLat, decimal originLng, decimal destLat, decimal destLng)
    {
        var result = _distances.TryGetValue((originLat, originLng), out var configured)
            ? configured
            : DefaultDistance;
        return Task.FromResult(result);
    }

    // A straight line between the two points, with the configured distance.
    public Task<GeoRoute?> GetRouteAsync(decimal originLat, decimal originLng, decimal destLat, decimal destLng)
    {
        var distance = _distances.TryGetValue((originLat, originLng), out var configured) ? configured : DefaultDistance;
        return Task.FromResult(distance is null
            ? null
            : new GeoRoute(distance.Value.DistanceKm, distance.Value.DurationMinutes,
                new[] { ((double)originLat, (double)originLng), ((double)destLat, (double)destLng) }));
    }
}
