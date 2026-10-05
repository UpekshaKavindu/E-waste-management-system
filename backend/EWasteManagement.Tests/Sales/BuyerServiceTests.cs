using EWasteManagement.API.Features.Auth.Entities;
using EWasteManagement.API.Features.Sales.DTOs;
using EWasteManagement.API.Features.Sales.Entities;
using EWasteManagement.API.Features.Sales.Services;
using Microsoft.EntityFrameworkCore;

namespace EWasteManagement.Tests.Sales;

/// <summary>
/// Buyer onboarding and lifecycle: a buyer profile is always attached to a Corporate
/// user, starts Pending until staff activate it, and is soft-deleted rather than removed
/// so order history keeps its foreign key.
/// </summary>
public class BuyerServiceTests : SalesTestBase
{
    private BuyerService _service = null!;

    protected override Task SetUpAsync()
    {
        _service = new BuyerService(Db);
        return Task.CompletedTask;
    }

    // ---------- Create ----------

    [Fact]
    public async Task Create_LinksTheCorporateUserAndStartsPending_TrimmingAndLowercasing()
    {
        var corporate = await SeedUserAsync("new.corp@example.test", UserRole.Corporate);

        var created = await _service.CreateAsync(new CreateBuyerRequest
        {
            UserId = corporate.UserId,
            CompanyName = "  Lanka E-Metals  ",
            ContactPerson = "  Nuwan  ",
            Email = "  Sales@LankaMetals.LK  ",
            BuyerType = "Local"
        });

        Assert.Equal(corporate.UserId, created.UserId);
        Assert.Equal("Lanka E-Metals", created.CompanyName);
        Assert.Equal("Nuwan", created.ContactPerson);
        Assert.Equal("sales@lankametals.lk", created.Email);
        Assert.Equal(BuyerStatus.Pending.ToString(), created.Status);   // staff must activate
    }

    [Fact]
    public async Task Create_ForAnUnknownUser_IsRejected()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CreateAsync(
            new CreateBuyerRequest { UserId = Guid.NewGuid(), CompanyName = "X", ContactPerson = "Y", Email = "a@b.co" }));
        Assert.Contains("Linked user does not exist", ex.Message);
    }

    [Fact]
    public async Task Create_ForANonCorporateUser_IsRejected()
    {
        var staff = await SeedUserAsync("some.staff@example.test", UserRole.Staff, StaffType.Management);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CreateAsync(
            new CreateBuyerRequest { UserId = staff.UserId, CompanyName = "X", ContactPerson = "Y", Email = "a@b.co" }));
        Assert.Contains("Corporate", ex.Message);
    }

    [Fact]
    public async Task Create_WhenTheUserAlreadyHasABuyerProfile_IsRejected()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CreateAsync(
            new CreateBuyerRequest { UserId = BuyerUser.UserId, CompanyName = "Dup", ContactPerson = "Y", Email = "a@b.co" }));
        Assert.Contains("already exists", ex.Message);
    }

    // ---------- Update / Delete ----------

    [Fact]
    public async Task Update_ChangesTheDetailsAndTheStatus()
    {
        var updated = await _service.UpdateAsync(LocalBuyer.BuyerId, new UpdateBuyerRequest
        {
            CompanyName = "Colombo Metals (Pvt) Ltd",
            ContactPerson = "Kamal",
            Email = "kamal@colombometals.lk",
            BuyerType = "Local",
            Status = "Suspended"
        });

        Assert.Equal("Colombo Metals (Pvt) Ltd", updated.CompanyName);
        Assert.Equal(BuyerStatus.Suspended.ToString(), updated.Status);
        Assert.NotNull(updated.UpdatedAt);
    }

    [Fact]
    public async Task Update_ForAnUnknownBuyer_IsRejected()
        => await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.UpdateAsync(
            Guid.NewGuid(), new UpdateBuyerRequest
            {
                CompanyName = "X", ContactPerson = "Y", Email = "a@b.co", BuyerType = "Local", Status = "Active"
            }));

    [Fact]
    public async Task Delete_SoftDeletesSoTheBuyerDisappearsFromReads()
    {
        await _service.DeleteAsync(LocalBuyer.BuyerId);

        Assert.DoesNotContain(await _service.GetAllAsync(), b => b.BuyerId == LocalBuyer.BuyerId);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.GetByIdAsync(LocalBuyer.BuyerId));

        // The row is still there, just flagged (the order history's FK stays valid).
        Assert.True(await Db.Buyers.IgnoreQueryFilters()
            .AnyAsync(b => b.BuyerId == LocalBuyer.BuyerId && b.IsDeleted));
    }

    [Fact]
    public async Task GetById_ForAnUnknownBuyer_IsRejected()
        => await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.GetByIdAsync(Guid.NewGuid()));

    // ---------- Onboarding helpers ----------

    [Fact]
    public async Task GetAvailableUsers_ListsOnlyCorporateUsersWithoutABuyerProfile()
    {
        var freeCorporate = await SeedUserAsync("free.corp@example.test", UserRole.Corporate);
        await SeedUserAsync("plain.staff@example.test", UserRole.Staff, StaffType.Management);

        var available = await _service.GetAvailableUsersAsync();

        // BuyerUser and ExportUser already have profiles; the staff user is the wrong role.
        var only = Assert.Single(available);
        Assert.Equal(freeCorporate.UserId, only.UserId);
    }

    [Fact]
    public async Task Register_CreatesACorporateUserWithAHashedPasswordAndAPendingBuyer()
    {
        var buyer = await _service.RegisterBuyerAsync(new RegisterBuyerRequest
        {
            FullName = "  Nimal Perera ",
            Email = " Nimal@Recycle.test ",
            Password = "secret123",
            CompanyName = "Nimal Recyclers",
            ContactPerson = "Nimal Perera",
            BuyerType = "Export"
        });

        var user = await Db.Users.AsNoTracking().FirstAsync(u => u.UserId == buyer.UserId);
        Assert.Equal(UserRole.Corporate, user.Role);
        Assert.Equal("nimal@recycle.test", user.Email);
        Assert.NotEqual("secret123", user.PasswordHash);                 // never stored in the clear
        Assert.True(BCrypt.Net.BCrypt.Verify("secret123", user.PasswordHash));

        Assert.Equal(BuyerStatus.Pending.ToString(), buyer.Status);
        Assert.Equal(BuyerType.Export.ToString(), buyer.BuyerType);
    }

    [Fact]
    public async Task Register_WithAnEmailThatIsAlreadyRegistered_IsRejected()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.RegisterBuyerAsync(
            new RegisterBuyerRequest
            {
                FullName = "Someone", Email = BuyerUser.Email, Password = "secret123",
                CompanyName = "X", ContactPerson = "Y", BuyerType = "Local"
            }));
        Assert.Contains("already registered", ex.Message);
    }
}
