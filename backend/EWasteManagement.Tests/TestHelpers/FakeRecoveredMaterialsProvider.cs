using EWasteManagement.API.Features.Sales.DTOs;
using EWasteManagement.API.Infrastructure.ExternalServices;

namespace EWasteManagement.Tests.TestHelpers;

/// <summary>
/// Deterministic stand-in for Component C's recovered-materials boundary.
/// The Sales tests build a small catalogue of batches and then let
/// SalesOrderService / ExportOrderService / MaterialRestockMatcher price and
/// sell them, without needing Component C's storage at all.
/// </summary>
public class FakeRecoveredMaterialsProvider : IRecoveredMaterialsProvider
{
    private readonly List<RecoveredMaterialResponse> _materials;

    public FakeRecoveredMaterialsProvider(params RecoveredMaterialResponse[] materials)
        => _materials = materials.ToList();

    /// <summary>Adds a batch after construction — handy for a test that needs a bad batch too.</summary>
    public void Add(RecoveredMaterialResponse material) => _materials.Add(material);

    public Task<IReadOnlyList<RecoveredMaterialResponse>> GetAllAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<RecoveredMaterialResponse>>(_materials);

    // Only "Ready" + safety-validated batches are sellable — mirrors EfRecoveredMaterialsProvider.
    public Task<IReadOnlyList<RecoveredMaterialResponse>> GetAvailableAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<RecoveredMaterialResponse>>(
            _materials.Where(m => m.ProcessingStatus == "Ready" && m.SafetyValidated).ToList());

    public Task<RecoveredMaterialResponse?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(_materials.FirstOrDefault(m => m.RecoveredMaterialId == id));
}
