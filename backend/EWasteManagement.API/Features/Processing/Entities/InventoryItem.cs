using EWasteManagement.API.Features.Processing.Events;
using EWasteManagement.API.Shared.Common;

namespace EWasteManagement.API.Features.Processing.Entities;

// The core table everything else hangs off. Read-only references into Component B (JobId,
// SubmissionId) are stored as plain Guids, not navigation properties — we never write to
// those tables from here.
public class InventoryItem : BaseEntity
{
    public OriginType OriginType { get; set; }

    public Guid? JobId { get; set; }
    public Guid? SubmissionId { get; set; }

    // The submission item / CSV row this was received as. Null for items received as a whole job
    // (before per-item receiving, or a job whose submission has no items) and for non-job items.
    public Guid? SubmissionItemId { get; set; }

    // A lot of identical units ("Laptop × 50") is one inventory item; everything else is 1.
    public int Quantity { get; set; } = 1;

    public Guid? ExtraWasteReceiptId { get; set; }
    public ExtraWasteReceipt? ExtraWasteReceipt { get; set; }

    public Guid? ParentInventoryItemId { get; set; }
    public InventoryItem? ParentInventoryItem { get; set; }

    public string ItemType { get; set; } = string.Empty;

    public ItemKind Kind { get; set; } = ItemKind.Unit;

    // Private setter — TransitionTo() below is now the ONLY legal way to change this.
    public InventoryStatus Status { get; private set; } = InventoryStatus.Received;

    public decimal VerifiedWeightKg { get; set; }

    public Guid CurrentLocationId { get; set; }
    public WarehouseLocation? CurrentLocation { get; set; }

    // Deliberately simplified vs. the original flow doc: no Quarantined/Rejected/Closed states.
    // OnHold is the single terminal exception state for now — documented decision, revisit only
    // if there's time left after M0–M4. ReadyForSale/ExportOnly are also terminal from Component
    // C's side; Component D owns what happens after handoff.
    private static readonly Dictionary<InventoryStatus, InventoryStatus[]> AllowedTransitions = new()
    {
        [InventoryStatus.Received]     = new[] { InventoryStatus.Sorting },
        [InventoryStatus.Recovered]    = new[] { InventoryStatus.Sorting },
        [InventoryStatus.Sorting]      = new[] { InventoryStatus.Dismantling, InventoryStatus.Classified },
        [InventoryStatus.Dismantling]  = new[] { InventoryStatus.Classified },
        [InventoryStatus.Classified]   = new[] { InventoryStatus.ReadyForSale, InventoryStatus.ExportOnly, InventoryStatus.OnHold },
        [InventoryStatus.ReadyForSale] = Array.Empty<InventoryStatus>(),
        [InventoryStatus.ExportOnly]   = Array.Empty<InventoryStatus>(),
        [InventoryStatus.OnHold]       = Array.Empty<InventoryStatus>(),
    };

    /// <summary>
    /// The only legal way to change Status. Throws InvalidStatusTransitionException on any
    /// illegal jump and leaves Status unchanged. On success, raises InventoryStatusChangedEvent,
    /// which the registered handler turns into a ProcessingLog row automatically.
    /// </summary>
    public void TransitionTo(InventoryStatus next, Guid staffId, string? notes = null)
    {
        if (!AllowedTransitions.TryGetValue(Status, out var allowed) || !allowed.Contains(next))
            throw new InvalidStatusTransitionException(Status.ToString(), next.ToString());

        var previous = Status;
        Status = next;
        RaiseDomainEvent(new InventoryStatusChangedEvent(Id, previous, next, staffId, notes));
    }

    /// <summary>
    /// Raises the very first audit-log entry when an item enters the warehouse. Not a real
    /// transition (no previous state to move from), so it bypasses AllowedTransitions — but
    /// it guarantees a ProcessingLog row exists from the moment the item exists, closing the
    /// gap Day 1 deliberately left open.
    /// </summary>
    public void MarkReceived(Guid staffId, string? notes = null)
        => RaiseDomainEvent(new InventoryStatusChangedEvent(Id, Status, Status, staffId, notes ?? "Received at warehouse", isEntry: true));

    /// <summary>
    /// Starts a brand-new item produced by dismantling: a component enters as Recovered, a material
    /// enters directly as ReadyForSale (or OnHold when hazardous). Like MarkReceived this is an entry,
    /// not a transition, so it only works on an item that has not been given a status yet.
    /// </summary>
    public void MarkCreatedByDismantling(InventoryStatus initialStatus, Guid staffId, string notes)
    {
        if (initialStatus is not (InventoryStatus.Recovered or InventoryStatus.ReadyForSale or InventoryStatus.OnHold))
            throw new InvalidStatusTransitionException("(new)", initialStatus.ToString());
        if (Status != InventoryStatus.Received || DomainEvents.Count > 0)
            throw new InvalidOperationException("Only a new, unsaved item can be started from dismantling.");

        Status = initialStatus;
        RaiseDomainEvent(new InventoryStatusChangedEvent(Id, initialStatus, initialStatus, staffId, notes, isEntry: true));
    }
}