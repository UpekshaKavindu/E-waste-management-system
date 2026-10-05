using EWasteManagement.API.Features.Collection.DTOs;
using EWasteManagement.API.Features.Collection.Entities;
using EWasteManagement.API.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EWasteManagement.API.Features.Collection.Services;

public interface ICollectorService
{
    Task<CollectorResponseDto> CreateProfileAsync(Guid userId, CreateCollectorProfileDto dto);
    Task<CollectorResponseDto?> GetByUserIdAsync(Guid userId);
    Task<CollectorResponseDto> UpdateMyProfileAsync(Guid userId, UpdateCollectorProfileDto dto);
    Task<CollectorResponseDto?> GetByIdAsync(Guid collectorId);
    Task<List<CollectorResponseDto>> GetAllAsync(bool? isAvailable);
    Task<CollectorResponseDto> UpdateAvailabilityAsync(Guid collectorId, Guid requestingUserId, UpdateAvailabilityDto dto);
    Task<CollectorResponseDto> UpdateLocationAsync(Guid collectorId, Guid requestingUserId, UpdateLocationDto dto);
}

public class CollectorService : ICollectorService
{
    private readonly ApplicationDbContext _db;

    public CollectorService(ApplicationDbContext db) => _db = db;

    public async Task<CollectorResponseDto> CreateProfileAsync(Guid userId, CreateCollectorProfileDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.VehicleType))
            throw new ArgumentException("VehicleType is required.");

        if (dto.CapacityKg <= 0)
            throw new ArgumentException("CapacityKg must be greater than zero.");

        var existing = await _db.Collectors.FirstOrDefaultAsync(c => c.UserId == userId);
        if (existing is not null)
            throw new InvalidOperationException("A collector profile already exists for this user.");

        var collector = new Collector
        {
            UserId = userId,
            VehicleType = dto.VehicleType,
            CapacityKg = dto.CapacityKg
        };

        _db.Collectors.Add(collector);
        await _db.SaveChangesAsync();

        return await ToDtoAsync(collector);
    }

    public async Task<CollectorResponseDto?> GetByUserIdAsync(Guid userId)
    {
        var collector = await _db.Collectors.FirstOrDefaultAsync(c => c.UserId == userId);
        return collector is null ? null : await ToDtoAsync(collector);
    }

    // Name and phone live on the User row, vehicle and capacity on the Collector row; saved together.
    public async Task<CollectorResponseDto> UpdateMyProfileAsync(Guid userId, UpdateCollectorProfileDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.FullName))
            throw new ArgumentException("FullName is required.");

        if (string.IsNullOrWhiteSpace(dto.VehicleType))
            throw new ArgumentException("VehicleType is required.");

        if (dto.CapacityKg <= 0)
            throw new ArgumentException("CapacityKg must be greater than zero.");

        var collector = await _db.Collectors.FirstOrDefaultAsync(c => c.UserId == userId)
            ?? throw new KeyNotFoundException("No collector profile exists for this user.");
        var user = await _db.Users.FirstOrDefaultAsync(u => u.UserId == userId)
            ?? throw new KeyNotFoundException("User not found.");

        user.FullName = dto.FullName.Trim();
        user.Phone = string.IsNullOrWhiteSpace(dto.Phone) ? null : dto.Phone.Trim();
        user.UpdatedAt = DateTime.UtcNow;

        collector.VehicleType = dto.VehicleType.Trim();
        collector.CapacityKg = dto.CapacityKg;
        collector.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return await ToDtoAsync(collector);
    }

    public async Task<CollectorResponseDto?> GetByIdAsync(Guid collectorId)
    {
        var collector = await _db.Collectors.FindAsync(collectorId);
        return collector is null ? null : await ToDtoAsync(collector);
    }

    public async Task<CollectorResponseDto> UpdateAvailabilityAsync(Guid collectorId, Guid requestingUserId, UpdateAvailabilityDto dto)
    {
        var collector = await GetOwnedCollectorAsync(collectorId, requestingUserId);

        collector.IsAvailable = dto.IsAvailable;
        collector.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return await ToDtoAsync(collector);
    }

    public async Task<CollectorResponseDto> UpdateLocationAsync(Guid collectorId, Guid requestingUserId, UpdateLocationDto dto)
    {
        var collector = await GetOwnedCollectorAsync(collectorId, requestingUserId);

        collector.CurrentLatitude = dto.Latitude;
        collector.CurrentLongitude = dto.Longitude;
        collector.LocationUpdatedAt = DateTime.UtcNow;
        collector.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return await ToDtoAsync(collector);
    }

    public async Task<List<CollectorResponseDto>> GetAllAsync(bool? isAvailable)
    {
        var query = _db.Collectors.AsQueryable();
        if (isAvailable is not null)
            query = query.Where(c => c.IsAvailable == isAvailable);

        var collectors = await query.OrderByDescending(c => c.IsAvailable)
            .ThenBy(c => c.CreatedAt)
            .ToListAsync();

        return await ToDtosAsync(collectors);
    }

    // Loads the collector and checks that the caller owns this profile.
    // Every write endpoint goes through this — a collector should only ever
    // be able to modify their own availability/location, never someone else's,
    // even if they guess another collector's id.
    private async Task<Collector> GetOwnedCollectorAsync(Guid collectorId, Guid requestingUserId)
    {
        var collector = await _db.Collectors.FindAsync(collectorId)
            ?? throw new KeyNotFoundException("Collector not found.");

        if (collector.UserId != requestingUserId)
            throw new UnauthorizedAccessException("You do not have permission to modify this collector profile.");

        return collector;
    }

    private async Task<CollectorResponseDto> ToDtoAsync(Collector c) =>
        (await ToDtosAsync(new List<Collector> { c }))[0];

    // Batch version: two extra queries total (users, active job counts),
    // not two per collector, so the staff list stays cheap as it grows.
    private async Task<List<CollectorResponseDto>> ToDtosAsync(List<Collector> collectors)
    {
        var userIds = collectors.Select(c => c.UserId).ToList();
        var collectorIds = collectors.Select(c => c.CollectorId).ToList();

        var users = await _db.Users
            .Where(u => userIds.Contains(u.UserId))
            .ToDictionaryAsync(u => u.UserId);

        var activeCounts = await _db.Jobs
            .Where(j => j.CollectorId != null && collectorIds.Contains(j.CollectorId.Value))
            .Where(j => MatchingRules.ActiveStatuses.Contains(j.Status))
            .GroupBy(j => j.CollectorId!.Value)
            .Select(g => new { CollectorId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CollectorId, x => x.Count);

        return collectors.Select(c =>
        {
            users.TryGetValue(c.UserId, out var user);
            return new CollectorResponseDto
            {
                CollectorId = c.CollectorId,
                UserId = c.UserId,
                FullName = user?.FullName ?? string.Empty,
                Email = user?.Email ?? string.Empty,
                Phone = user?.Phone,
                VehicleType = c.VehicleType,
                CapacityKg = c.CapacityKg,
                IsAvailable = c.IsAvailable,
                Rating = c.Rating,
                CurrentLatitude = c.CurrentLatitude,
                CurrentLongitude = c.CurrentLongitude,
                LocationUpdatedAt = c.LocationUpdatedAt,
                CreatedAt = c.CreatedAt,
                ActiveJobCount = activeCounts.GetValueOrDefault(c.CollectorId, 0)
            };
        }).ToList();
    }
}
