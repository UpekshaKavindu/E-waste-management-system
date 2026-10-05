using EWasteManagement.API.Features.Processing.Entities;

namespace EWasteManagement.API.Features.Processing.DTOs;

public enum InventorySortField
{
    CreatedAt,
    ItemType,
    WeightKg,
    Status
}

public class InventoryListQuery
{
    public string? Search { get; set; }
    public InventoryStatus? Status { get; set; }
    public ClassificationCategory? Category { get; set; }
    public OriginType? OriginType { get; set; }
    public ItemKind? Kind { get; set; }
    public Guid? LocationId { get; set; }
    public Guid? ParentId { get; set; }
    public InventorySortField SortBy { get; set; } = InventorySortField.CreatedAt;
    public bool Descending { get; set; } = true;
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class PagedResponse<T>
{
    public List<T> Items { get; set; } = new();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
}

public class InventoryItemListItemResponse
{
    public Guid Id { get; set; }
    public string ItemType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string OriginType { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public decimal VerifiedWeightKg { get; set; }
    /// <summary>Units in this item — above 1 for a received lot ("Laptop × 50").</summary>
    public int Quantity { get; set; } = 1;
    public Guid CurrentLocationId { get; set; }
    public string CurrentLocationName { get; set; } = string.Empty;
    public Guid? ParentInventoryItemId { get; set; }
    public string? Category { get; set; }
    public DateTime ReceivedAt { get; set; }
}

public class InventoryClassificationSummary
{
    public string Category { get; set; } = string.Empty;
    public string? SubCategory { get; set; }
    public string Source { get; set; } = string.Empty;
    public decimal? ConfidenceScore { get; set; }
    public bool IsFinal { get; set; }
    public Guid? ClassifiedByStaffId { get; set; }
    public DateTime ClassifiedAt { get; set; }
}

public class InventoryChildSummary
{
    public Guid Id { get; set; }
    public string ItemType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public decimal VerifiedWeightKg { get; set; }
}

public class InventoryItemDetailResponse
{
    public Guid Id { get; set; }
    public string ItemType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string OriginType { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public decimal VerifiedWeightKg { get; set; }
    /// <summary>Units in this item — above 1 for a received lot ("Laptop × 50").</summary>
    public int Quantity { get; set; } = 1;
    public Guid CurrentLocationId { get; set; }
    public string CurrentLocationName { get; set; } = string.Empty;
    public Guid? JobId { get; set; }
    public Guid? SubmissionId { get; set; }
    public Guid? SubmissionItemId { get; set; }
    public Guid? ExtraWasteReceiptId { get; set; }
    public Guid? ParentInventoryItemId { get; set; }
    public DateTime ReceivedAt { get; set; }
    public InventoryClassificationSummary? Classification { get; set; }
    public List<InventoryChildSummary> Children { get; set; } = new();
}

public class PendingPaymentsQuery
{
    public Guid? CollectorId { get; set; }
    public PaymentSourceType? SourceType { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class PendingPaymentsResponse : PagedResponse<CollectorPaymentResponse>
{
    public decimal TotalPendingAmount { get; set; }
}
