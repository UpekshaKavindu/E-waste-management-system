using System.Text.Json.Serialization;

namespace EWasteManagement.API.Infrastructure.ExternalServices;

// Mirrors Photon's and OSRM's response shapes exactly (not our own domain
// model) — kept internal so nothing outside this folder depends on their JSON.

// Photon (Komoot's OSM-based geocoder) returns a GeoJSON FeatureCollection.
// Coordinates come back as [longitude, latitude] — GeoJSON's standard order,
// the OPPOSITE of how we store/pass lat/lng everywhere else in this codebase.
internal class PhotonResponse
{
    [JsonPropertyName("features")]
    public List<PhotonFeature> Features { get; set; } = new();
}

internal class PhotonFeature
{
    [JsonPropertyName("geometry")]
    public PhotonGeometry Geometry { get; set; } = new();
}

internal class PhotonGeometry
{
    [JsonPropertyName("coordinates")]
    public List<double> Coordinates { get; set; } = new();
}

internal class OsrmRouteResponse
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("routes")]
    public List<OsrmRoute> Routes { get; set; } = new();
}

internal class OsrmRoute
{
    // OSRM returns distance in metres and duration in seconds, as numbers
    // (unlike Nominatim's coordinates, these are NOT strings).
    [JsonPropertyName("distance")]
    public double Distance { get; set; }

    [JsonPropertyName("duration")]
    public double Duration { get; set; }

    // Only present when requested with geometries=geojson.
    [JsonPropertyName("geometry")]
    public OsrmGeometry? Geometry { get; set; }
}

internal class OsrmGeometry
{
    // GeoJSON LineString: [lon, lat] pairs.
    [JsonPropertyName("coordinates")]
    public List<List<double>> Coordinates { get; set; } = new();
}
