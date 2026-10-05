using EWasteManagement.API.Features.Auth.Entities;
using EWasteManagement.API.Features.Sales.DTOs;
using EWasteManagement.API.Features.Sales.Entities;
using EWasteManagement.API.Features.Sales.Services;
using EWasteManagement.API.Infrastructure.Persistence;
using EWasteManagement.Tests.TestHelpers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EWasteManagement.Tests.Sales;

/// <summary>
/// Shared harness for the Sales (Component D) tests: one in-memory SQLite database
/// per test with the real schema (EnsureCreated) plus the staff user and the
/// local/export buyers every service under test expects to find. Same approach as
/// the Collection tests' CollectionTestBase.
/// </summary>
public abstract class SalesTestBase : IAsyncLifetime
{
    private SqliteConnection _connection = null!;

    protected ApplicationDbContext Db = null!;

    /// <summary>Management staff — the user who "performs" every write in these tests.</summary>
    protected User StaffUser = null!;

    /// <summary>The Corporate user behind <see cref="LocalBuyer"/>.</summary>
    protected User BuyerUser = null!;

    /// <summary>The Corporate user behind <see cref="ExportBuyer"/>.</summary>
    protected User ExportUser = null!;

    protected Buyer LocalBuyer = null!;
    protected Buyer ExportBuyer = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options;
        Db = new ApplicationDbContext(options, new NoOpDomainEventDispatcher());
        await Db.Database.EnsureCreatedAsync();

        StaffUser = await SeedUserAsync("sales.staff@example.test", UserRole.Staff, StaffType.Management);
        BuyerUser = await SeedUserAsync("local.buyer@example.test", UserRole.Corporate);
        ExportUser = await SeedUserAsync("export.buyer@example.test", UserRole.Corporate);

        LocalBuyer = await SeedBuyerAsync(BuyerUser, BuyerType.Local, "Colombo Metals Ltd");
        ExportBuyer = await SeedBuyerAsync(ExportUser, BuyerType.Export, "Shenzhen Reclaim Ltd");

        await SetUpAsync();
    }

    public async Task DisposeAsync()
    {
        await Db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    /// <summary>
    /// Hook for a derived test class to build the service under test once the
    /// shared fixtures above exist. (xUnit's IAsyncLifetime.InitializeAsync is not virtual.)
    /// </summary>
    protected virtual Task SetUpAsync() => Task.CompletedTask;

    // --- seeding ------------------------------------------------------

    protected async Task<User> SeedUserAsync(
        string email, UserRole role = UserRole.Staff, StaffType? staffType = null)
    {
        var user = new User
        {
            Email = email,
            FullName = "Sales Test User",
            PasswordHash = "not-a-real-hash",
            Role = role,
            StaffType = role == UserRole.Staff ? staffType ?? StaffType.Management : null
        };
        Db.Users.Add(user);
        await Db.SaveChangesAsync();
        return user;
    }

    protected async Task<Buyer> SeedBuyerAsync(
        User user,
        BuyerType type = BuyerType.Local,
        string company = "Test Buyer Ltd",
        BuyerStatus status = BuyerStatus.Active)
    {
        var buyer = new Buyer
        {
            UserId = user.UserId,
            CompanyName = company,
            ContactPerson = "Test Contact",
            Email = user.Email,
            BuyerType = type,
            Status = status
        };
        Db.Buyers.Add(buyer);
        await Db.SaveChangesAsync();
        return buyer;
    }

    /// <summary>
    /// Seeds an Approved price. Defaults to effective today with no expiry, i.e. the
    /// "live" row most tests want; pass an expiry in the past to model a price that is
    /// still stored as Approved but whose window has closed.
    /// </summary>
    protected async Task<MaterialPricing> SeedApprovedPriceAsync(
        string materialType, decimal pricePerKg, DateOnly? effectiveDate = null, DateOnly? expiryDate = null)
    {
        var row = new MaterialPricing
        {
            MaterialType = materialType,
            PricePerKg = pricePerKg,
            EffectiveDate = effectiveDate ?? MaterialPricingPolicy.Today,
            ExpiryDate = expiryDate,
            Status = PricingStatus.Approved,
            CreatedByUserId = StaffUser.UserId
        };
        Db.MaterialPricings.Add(row);
        await Db.SaveChangesAsync();
        return row;
    }

    /// <summary>Seeds a price in an arbitrary status (e.g. a Draft nobody approved yet).</summary>
    protected async Task<MaterialPricing> SeedPriceAsync(
        string materialType, decimal pricePerKg, DateOnly effectiveDate, PricingStatus status)
    {
        var row = new MaterialPricing
        {
            MaterialType = materialType,
            PricePerKg = pricePerKg,
            EffectiveDate = effectiveDate,
            Status = status,
            CreatedByUserId = StaffUser.UserId
        };
        Db.MaterialPricings.Add(row);
        await Db.SaveChangesAsync();
        return row;
    }

    /// <summary>A sellable recovered-material batch owned by Component C.</summary>
    protected static RecoveredMaterialResponse SellableMaterial(
        string materialType, decimal quantityKg, Guid? id = null)
        => new()
        {
            RecoveredMaterialId = id ?? Guid.NewGuid(),
            MaterialType = materialType,
            QuantityKg = quantityKg,
            QualityGrade = "A",
            ProcessingStatus = "Ready",
            SafetyValidated = true
        };
}
