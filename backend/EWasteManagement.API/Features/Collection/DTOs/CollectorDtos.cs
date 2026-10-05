using EWasteManagement.API.Features.Collection.Services;

namespace EWasteManagement.API.Features.Collection.DTOs;

public class CreateCollectorProfileDto
{
    public string VehicleType { get; set; } = string.Empty;
    public decimal CapacityKg { get; set; }
}

// PUT /api/v1/collectors/me — the collector's own details. Email is the login and stays as is.
public class UpdateCollectorProfileDto
{
    public string FullName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string VehicleType { get; set; } = string.Empty;
    public decimal CapacityKg { get; set; }
}

public class UpdateAvailabilityDto
{
    public bool IsAvailable { get; set; }
}

public class UpdateLocationDto
{
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
}

public class CollectorResponseDto
{
    public Guid CollectorId { get; set; }
    public Guid UserId { get; set; }

    // From the linked User row, so staff screens show a person, not a GUID.
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }

    public string VehicleType { get; set; } = string.Empty;
    public decimal CapacityKg { get; set; }
    public bool IsAvailable { get; set; }
    public decimal Rating { get; set; }
    public decimal? CurrentLatitude { get; set; }
    public decimal? CurrentLongitude { get; set; }
    public DateTime? LocationUpdatedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    // Live count of Assigned/Accepted/InProgress jobs, and the cap matching enforces.
    public int ActiveJobCount { get; set; }
    public int MaxActiveJobs { get; set; } = MatchingRules.MaxActiveJobsPerCollector;
}
