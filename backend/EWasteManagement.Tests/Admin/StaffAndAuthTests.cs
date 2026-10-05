using EWasteManagement.API.Features.Admin.DTOs;
using EWasteManagement.API.Features.Admin.Services;
using EWasteManagement.API.Features.Auth.DTOs;
using EWasteManagement.API.Features.Auth.Entities;
using EWasteManagement.API.Features.Auth.Services;
using EWasteManagement.API.Features.Processing.Entities;
using EWasteManagement.API.Features.Processing.Events;
using EWasteManagement.API.Infrastructure.BackgroundTasks;
using EWasteManagement.API.Infrastructure.Persistence;
using EWasteManagement.Tests.TestHelpers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EWasteManagement.Tests.Admin;

public class StaffAndAuthTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private ApplicationDbContext _db = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();
        _db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options,
            new NoOpDomainEventDispatcher());
        await _db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() { await _db.DisposeAsync(); await _connection.DisposeAsync(); }

    private sealed class FakeJwtService : IJwtService
    {
        public string GenerateToken(User user) => $"token-for-{user.GetAccessRole()}";
    }

    private sealed class RecordingRestockQueue : IMaterialRestockQueue
    {
        public List<Guid> Enqueued { get; } = new();
        public void Enqueue(Guid inventoryItemId) => Enqueued.Add(inventoryItemId);
        public IAsyncEnumerable<Guid> DequeueAllAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private AuthService Auth() => new(_db, new FakeJwtService());

    // ---------------------------------------------------------------- registration and roles

    [Theory]
    [InlineData("Staff")]
    [InlineData("admin")]
    [InlineData("3")]
    [InlineData("Worker")]
    public async Task Register_CannotCreateStaffOrAdminAccounts(string role)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Auth().RegisterAsync(new RegisterRequest
        {
            FullName = "Mallory", Email = $"{Guid.NewGuid()}@test.com", Password = "secret1", Role = role
        }));

        Assert.Equal(0, await _db.Users.CountAsync());
    }

    [Theory]
    [InlineData("Household")]
    [InlineData("Corporate")]
    [InlineData("Collector")]
    public async Task Register_PublicRolesStillWork(string role)
    {
        var result = await Auth().RegisterAsync(new RegisterRequest
        {
            FullName = "Pat", Email = $"{Guid.NewGuid()}@test.com", Password = "secret1", Role = role
        });

        Assert.Equal(role, result.Role);
        Assert.Null(result.StaffType);
    }

    [Fact]
    public async Task Login_WorkerStaffGetTheWorkerRole_ManagementStaffKeepStaff()
    {
        var staff = new StaffService(_db);
        await staff.CreateAsync(new CreateStaffRequest { FullName = "Wendy", Email = "w@test.com", Password = "secret1", StaffType = StaffType.Worker });
        await staff.CreateAsync(new CreateStaffRequest { FullName = "Mark", Email = "m@test.com", Password = "secret1", StaffType = StaffType.Management });

        var worker = await Auth().LoginAsync(new LoginRequest { Email = "w@test.com", Password = "secret1" });
        var manager = await Auth().LoginAsync(new LoginRequest { Email = "m@test.com", Password = "secret1" });

        Assert.Equal("Worker", worker.Role);
        Assert.Equal("token-for-Worker", worker.Token);
        Assert.Equal("Worker", worker.StaffType);
        Assert.Equal("Staff", manager.Role);
        Assert.Equal("Management", manager.StaffType);
    }

    // ---------------------------------------------------------------- admin staff management

    [Fact]
    public async Task CreateStaff_DuplicateEmail_IsRejected_EvenForADeletedAccount()
    {
        var staff = new StaffService(_db);
        var created = await staff.CreateAsync(new CreateStaffRequest { FullName = "A", Email = "dup@test.com", Password = "secret1", StaffType = StaffType.Worker });
        await staff.DeleteAsync(created.UserId, Guid.NewGuid());

        await Assert.ThrowsAsync<InvalidOperationException>(() => staff.CreateAsync(
            new CreateStaffRequest { FullName = "B", Email = "dup@test.com", Password = "secret1", StaffType = StaffType.Management }));
    }

    [Fact]
    public async Task DeleteStaff_IsSoft_BlocksLogin_AndKeepsTheirWorkReviewable()
    {
        var staff = new StaffService(_db);
        var created = await staff.CreateAsync(new CreateStaffRequest { FullName = "Gone", Email = "gone@test.com", Password = "secret1", StaffType = StaffType.Worker });
        var location = await _db.WarehouseLocations.FirstAsync();
        var item = new InventoryItem { OriginType = OriginType.ExtraWaste, ItemType = "Laptop", VerifiedWeightKg = 2m, CurrentLocationId = location.Id };
        _db.InventoryItems.Add(item);
        _db.ProcessingLogs.Add(new ProcessingLog { InventoryItemId = item.Id, Action = "Received", PerformedByStaffId = created.UserId });
        await _db.SaveChangesAsync();

        await staff.DeleteAsync(created.UserId, Guid.NewGuid());

        Assert.Empty(await staff.ListAsync());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            Auth().LoginAsync(new LoginRequest { Email = "gone@test.com", Password = "secret1" }));

        var summary = Assert.Single(await staff.GetActivitySummaryAsync());
        Assert.True(summary.IsDeleted);
        Assert.Equal(1, summary.ItemsReceived);
        Assert.Equal("Laptop", Assert.Single(await staff.GetRecentActivityAsync(created.UserId, 10)).ItemType);
    }

    [Fact]
    public async Task DeleteStaff_CannotDeleteYourselfOrNonStaff()
    {
        var staff = new StaffService(_db);
        var admin = new User { Email = "admin@test.com", PasswordHash = "x", FullName = "Admin", Role = UserRole.Admin };
        _db.Users.Add(admin);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => staff.DeleteAsync(admin.UserId, admin.UserId));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => staff.DeleteAsync(admin.UserId, Guid.NewGuid()));
    }

    // ---------------------------------------------------------------- admin account management

    [Fact]
    public async Task CreateAdmin_CanSignInAsAdmin_AndIsListedWithoutStaff()
    {
        var admins = new AdminAccountService(_db);
        await new StaffService(_db).CreateAsync(new CreateStaffRequest { FullName = "Mark", Email = "m@test.com", Password = "secret1", StaffType = StaffType.Management });
        await admins.CreateAsync(new CreateAdminRequest { FullName = "Ada", Email = "ada@test.com", Password = "secret1" });

        var login = await Auth().LoginAsync(new LoginRequest { Email = "ada@test.com", Password = "secret1" });

        Assert.Equal("Admin", login.Role);
        Assert.Equal("Ada", Assert.Single(await admins.ListAsync()).FullName);
    }

    [Fact]
    public async Task UpdateAdmin_ChangesOwnDetails_KeepsPasswordWhenBlank_AndRejectsTakenEmailOrOtherAdmin()
    {
        var admins = new AdminAccountService(_db);
        var ada = await admins.CreateAsync(new CreateAdminRequest { FullName = "Ada", Email = "ada@test.com", Password = "secret1" });
        var bob = await admins.CreateAsync(new CreateAdminRequest { FullName = "Bob", Email = "bob@test.com", Password = "secret1" });

        var updated = await admins.UpdateAsync(ada.UserId, ada.UserId, new UpdateAdminRequest { FullName = "Ada L", Email = "ada@test.com", Phone = "0771234567" });
        Assert.Equal("Ada L", updated.FullName);
        Assert.Equal("0771234567", updated.Phone);
        await Auth().LoginAsync(new LoginRequest { Email = "ada@test.com", Password = "secret1" });

        await admins.UpdateAsync(ada.UserId, ada.UserId, new UpdateAdminRequest { FullName = "Ada L", Email = "ada@test.com", Password = "newpass1" });
        await Auth().LoginAsync(new LoginRequest { Email = "ada@test.com", Password = "newpass1" });

        await Assert.ThrowsAsync<InvalidOperationException>(() => admins.UpdateAsync(
            ada.UserId, ada.UserId, new UpdateAdminRequest { FullName = "Ada L", Email = "bob@test.com" }));

        // Ada cannot change Bob's details or password.
        await Assert.ThrowsAsync<InvalidOperationException>(() => admins.UpdateAsync(
            bob.UserId, ada.UserId, new UpdateAdminRequest { FullName = "Hacked", Email = "bob@test.com", Password = "takeover1" }));
        await Auth().LoginAsync(new LoginRequest { Email = "bob@test.com", Password = "secret1" });
    }

    [Fact]
    public async Task DeleteAdmin_CannotDeleteYourselfTheLastAdminOrStaff()
    {
        var admins = new AdminAccountService(_db);
        var ada = await admins.CreateAsync(new CreateAdminRequest { FullName = "Ada", Email = "ada@test.com", Password = "secret1" });
        var worker = await new StaffService(_db).CreateAsync(new CreateStaffRequest { FullName = "W", Email = "w@test.com", Password = "secret1", StaffType = StaffType.Worker });

        await Assert.ThrowsAsync<InvalidOperationException>(() => admins.DeleteAsync(ada.UserId, ada.UserId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => admins.DeleteAsync(ada.UserId, Guid.NewGuid()));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => admins.DeleteAsync(worker.UserId, ada.UserId));

        var bob = await admins.CreateAsync(new CreateAdminRequest { FullName = "Bob", Email = "bob@test.com", Password = "secret1" });
        await admins.DeleteAsync(bob.UserId, ada.UserId);

        Assert.Equal(ada.UserId, Assert.Single(await admins.ListAsync()).UserId);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            Auth().LoginAsync(new LoginRequest { Email = "bob@test.com", Password = "secret1" }));
    }

    // ---------------------------------------------------------------- Sales is told about new materials

    [Fact]
    public async Task MaterialBornReadyForSale_IsQueuedForSalesMatching()
    {
        var queue = new RecordingRestockQueue();
        var handler = new InventoryStatusChangedEventHandler(_db, queue);
        var location = await _db.WarehouseLocations.FirstAsync();
        var material = new InventoryItem { OriginType = OriginType.ExtraWaste, ItemType = "Copper", Kind = ItemKind.Material, VerifiedWeightKg = 1m, CurrentLocationId = location.Id };
        material.MarkCreatedByDismantling(InventoryStatus.ReadyForSale, Guid.NewGuid(), "Recovered");
        var entryEvent = (InventoryStatusChangedEvent)material.DomainEvents.Single();
        _db.InventoryItems.Add(material);
        await _db.SaveChangesAsync();

        await handler.Handle(entryEvent);

        Assert.Equal(new[] { material.Id }, queue.Enqueued);
    }

    [Fact]
    public async Task ReceivedItem_IsNotQueuedForSalesMatching()
    {
        var queue = new RecordingRestockQueue();
        var handler = new InventoryStatusChangedEventHandler(_db, queue);
        var location = await _db.WarehouseLocations.FirstAsync();
        var item = new InventoryItem { OriginType = OriginType.ExtraWaste, ItemType = "Laptop", VerifiedWeightKg = 1m, CurrentLocationId = location.Id };
        item.MarkReceived(Guid.NewGuid());
        var entryEvent = (InventoryStatusChangedEvent)item.DomainEvents.Single();
        _db.InventoryItems.Add(item);
        await _db.SaveChangesAsync();

        await handler.Handle(entryEvent);

        Assert.Empty(queue.Enqueued);
    }
}
