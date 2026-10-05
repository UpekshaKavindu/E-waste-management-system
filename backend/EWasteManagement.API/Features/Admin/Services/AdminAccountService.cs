using EWasteManagement.API.Features.Admin.DTOs;
using EWasteManagement.API.Features.Auth.Entities;
using EWasteManagement.API.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EWasteManagement.API.Features.Admin.Services;

public interface IAdminAccountService
{
    Task<IReadOnlyList<AdminAccountResponse>> ListAsync(CancellationToken cancellationToken = default);
    Task<AdminAccountResponse> CreateAsync(CreateAdminRequest request, CancellationToken cancellationToken = default);
    Task<AdminAccountResponse> UpdateAsync(Guid userId, Guid actingAdminId, UpdateAdminRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid userId, Guid actingAdminId, CancellationToken cancellationToken = default);
}

// Admin accounts are never self-registered (see AuthService); an existing admin adds and removes them.
// Each admin can edit only their own details and password, never another admin's.
public class AdminAccountService : IAdminAccountService
{
    private readonly ApplicationDbContext _db;

    public AdminAccountService(ApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<AdminAccountResponse>> ListAsync(CancellationToken cancellationToken = default)
    {
        var admins = await _db.Users.AsNoTracking()
            .Where(u => u.Role == UserRole.Admin)
            .OrderBy(u => u.FullName)
            .ToListAsync(cancellationToken);

        return admins.Select(Map).ToList();
    }

    public async Task<AdminAccountResponse> CreateAsync(CreateAdminRequest request, CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim();
        await EnsureEmailFreeAsync(email, null, cancellationToken);

        var user = new User
        {
            FullName = request.FullName.Trim(),
            Email = email,
            Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Role = UserRole.Admin
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync(cancellationToken);
        return Map(user);
    }

    public async Task<AdminAccountResponse> UpdateAsync(Guid userId, Guid actingAdminId, UpdateAdminRequest request, CancellationToken cancellationToken = default)
    {
        if (userId != actingAdminId)
            throw new InvalidOperationException("You can only edit your own admin account.");

        var user = await FindAdminAsync(userId, cancellationToken);
        var email = request.Email.Trim();
        await EnsureEmailFreeAsync(email, userId, cancellationToken);

        user.FullName = request.FullName.Trim();
        user.Email = email;
        user.Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();
        if (!string.IsNullOrEmpty(request.Password))
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);
        user.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        return Map(user);
    }

    // Soft delete, same as staff: approvals and other records keep pointing at this admin's id.
    public async Task DeleteAsync(Guid userId, Guid actingAdminId, CancellationToken cancellationToken = default)
    {
        if (userId == actingAdminId)
            throw new InvalidOperationException("You cannot delete your own account.");

        var user = await FindAdminAsync(userId, cancellationToken);

        if (await _db.Users.CountAsync(u => u.Role == UserRole.Admin, cancellationToken) <= 1)
            throw new InvalidOperationException("The last admin account cannot be deleted.");

        user.IsDeleted = true;
        user.IsActive = false;
        user.DeletedAt = DateTime.UtcNow;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<User> FindAdminAsync(Guid userId, CancellationToken cancellationToken)
        => await _db.Users.FirstOrDefaultAsync(u => u.UserId == userId && u.Role == UserRole.Admin, cancellationToken)
            ?? throw new KeyNotFoundException($"Admin '{userId}' was not found.");

    // Deleted accounts still hold their email (unique index), so check them too.
    private async Task EnsureEmailFreeAsync(string email, Guid? exceptUserId, CancellationToken cancellationToken)
    {
        if (await _db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == email && u.UserId != exceptUserId, cancellationToken))
            throw new InvalidOperationException("Email is already registered.");
    }

    private static AdminAccountResponse Map(User u) => new()
    {
        UserId = u.UserId,
        FullName = u.FullName,
        Email = u.Email,
        Phone = u.Phone,
        IsActive = u.IsActive,
        CreatedAt = u.CreatedAt,
        UpdatedAt = u.UpdatedAt
    };
}
