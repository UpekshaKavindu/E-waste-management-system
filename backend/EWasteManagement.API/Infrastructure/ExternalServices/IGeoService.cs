namespace EWasteManagement.API.Infrastructure.ExternalServices;

// Renamed from IGoogleMapsService now that the implementation is OSM-based —
// nothing else in the codebase (MatchingService, DI registration) needs to
// know which provider is behind this.
public interface IGeoService
{
    /// <summary>
    /// Turns a free-text address into coordinates. Returns null if the
    /// address couldn't be resolved (ambiguous, incomplete, or a network/API
    /// failure) — callers should treat null as "flag for staff", not throw.
    /// </summary>
    Task<(decimal Latitude, decimal Longitude)?> GeocodeAsync(string address);

    /// <summary>
    /// Driving distance and duration between two points, used both for
    /// ranking collectors during matching and for showing an ETA on a job.
    /// </summary>
    Task<(decimal DistanceKm, int DurationMinutes)?> GetDistanceAsync(
        decimal originLat, decimal originLng,
        decimal destLat, decimal destLng);

    /// <summary>
    /// The driving route itself (road geometry), for drawing on a map. Null on
    /// no route or a network/API failure.
    /// </summary>
    Task<GeoRoute?> GetRouteAsync(
        decimal originLat, decimal originLng,
        decimal destLat, decimal destLng);
}

/// <param name="Points">The road path as (lat, lng), origin first.</param>
public record GeoRoute(decimal DistanceKm, int DurationMinutes, IReadOnlyList<(double Lat, double Lng)> Points);
