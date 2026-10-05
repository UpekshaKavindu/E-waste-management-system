using EWasteManagement.API.Features.Sales.DTOs;
using EWasteManagement.API.Features.Sales.Validators;

namespace EWasteManagement.Tests.Sales;

/// <summary>
/// Pure input-shape tests for the Sales FluentValidation validators — the rules the
/// API pipeline runs before a request ever reaches a service. No database here.
/// </summary>
public class SalesValidatorsTests
{
    // ---------- Sales orders ----------

    [Fact]
    public void CreateSalesOrder_TakesAWellFormedOrder()
    {
        var result = new CreateSalesOrderValidator().Validate(new CreateSalesOrderRequest
        {
            BuyerId = Guid.NewGuid(),
            Items = { new CreateSalesOrderItemRequest { RecoveredMaterialId = Guid.NewGuid(), QuantityKg = 12.5m } }
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void CreateSalesOrder_RejectsAnEmptyBasket()
        => Assert.False(new CreateSalesOrderValidator().Validate(
            new CreateSalesOrderRequest { BuyerId = Guid.NewGuid() }).IsValid);

    [Fact]
    public void CreateSalesOrder_RejectsMoreThanFiftyLines()
    {
        var request = new CreateSalesOrderRequest { BuyerId = Guid.NewGuid() };
        for (var i = 0; i < 51; i++)
            request.Items.Add(new CreateSalesOrderItemRequest { RecoveredMaterialId = Guid.NewGuid(), QuantityKg = 1m });

        Assert.False(new CreateSalesOrderValidator().Validate(request).IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(100_001)]
    public void CreateSalesOrder_RejectsUnrealisticQuantities(decimal quantityKg)
    {
        var request = new CreateSalesOrderRequest
        {
            BuyerId = Guid.NewGuid(),
            Items = { new CreateSalesOrderItemRequest { RecoveredMaterialId = Guid.NewGuid(), QuantityKg = quantityKg } }
        };

        Assert.False(new CreateSalesOrderValidator().Validate(request).IsValid);
    }

    [Theory]
    [InlineData("Draft")]
    [InlineData("Confirmed")]
    [InlineData("Completed")]
    [InlineData("Cancelled")]
    public void UpdateSalesOrderStatus_AcceptsTheClientSettableStatuses(string status)
        => Assert.True(new UpdateSalesOrderStatusValidator().Validate(
            new UpdateSalesOrderStatusRequest { Status = status }).IsValid);

    [Theory]
    [InlineData("WaitingForStock")]   // internal states are not client-settable
    [InlineData("Nonsense")]
    public void UpdateSalesOrderStatus_RejectsAnythingElse(string status)
        => Assert.False(new UpdateSalesOrderStatusValidator().Validate(
            new UpdateSalesOrderStatusRequest { Status = status }).IsValid);

    // ---------- Export orders ----------

    [Fact]
    public void CreateExportOrder_TakesAValidShipment()
        => Assert.True(new CreateExportOrderValidator().Validate(new CreateExportOrderRequest
        {
            BuyerId = Guid.NewGuid(),
            DestinationCountry = "IN",
            ShipmentDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10),
            Items = { new CreateExportOrderItemRequest { RecoveredMaterialId = Guid.NewGuid(), QuantityKg = 40m } }
        }).IsValid);

    [Fact]
    public void CreateExportOrder_RejectsAShipmentBelowTheMinimumWeight()
        => Assert.False(new CreateExportOrderValidator().Validate(new CreateExportOrderRequest
        {
            BuyerId = Guid.NewGuid(),
            DestinationCountry = "IN",
            ShipmentDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10),
            Items = { new CreateExportOrderItemRequest { RecoveredMaterialId = Guid.NewGuid(), QuantityKg = 10m } }
        }).IsValid);

    [Fact]
    public void CreateExportOrder_RejectsAShipmentDateInThePast()
        => Assert.False(new CreateExportOrderValidator().Validate(new CreateExportOrderRequest
        {
            BuyerId = Guid.NewGuid(),
            DestinationCountry = "IN",
            ShipmentDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1),
            Items = { new CreateExportOrderItemRequest { RecoveredMaterialId = Guid.NewGuid(), QuantityKg = 40m } }
        }).IsValid);

    [Fact]
    public void CreateExportOrder_RejectsABlankDestinationCountry()
        => Assert.False(new CreateExportOrderValidator().Validate(new CreateExportOrderRequest
        {
            BuyerId = Guid.NewGuid(),
            DestinationCountry = "   ",
            ShipmentDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10),
            Items = { new CreateExportOrderItemRequest { RecoveredMaterialId = Guid.NewGuid(), QuantityKg = 40m } }
        }).IsValid);

    [Fact]
    public void UpdateExportOrderStatus_RejectsAnUnknownStatus()
        => Assert.False(new UpdateExportOrderStatusValidator().Validate(
            new UpdateExportOrderStatusRequest { Status = "Delivered" }).IsValid);

    // ---------- Buyers ----------

    [Fact]
    public void CreateBuyer_TakesAValidProfile()
        => Assert.True(new CreateBuyerValidator().Validate(new CreateBuyerRequest
        {
            UserId = Guid.NewGuid(),
            CompanyName = "Colombo Metals Ltd",
            ContactPerson = "Nuwan",
            Email = "nuwan@colombometals.lk",
            BuyerType = "Local"
        }).IsValid);

    [Theory]
    [InlineData("not-an-email", "Local")]
    [InlineData("a@b.co", "Wholesale")]     // BuyerType must be Local or Export
    public void CreateBuyer_RejectsBadEmailOrBuyerType(string email, string buyerType)
        => Assert.False(new CreateBuyerValidator().Validate(new CreateBuyerRequest
        {
            UserId = Guid.NewGuid(),
            CompanyName = "Colombo Metals Ltd",
            ContactPerson = "Nuwan",
            Email = email,
            BuyerType = buyerType
        }).IsValid);

    [Fact]
    public void UpdateBuyer_RejectsAnUnknownStatus()
        => Assert.False(new UpdateBuyerValidator().Validate(new UpdateBuyerRequest
        {
            CompanyName = "Colombo Metals Ltd",
            ContactPerson = "Nuwan",
            Email = "nuwan@colombometals.lk",
            BuyerType = "Local",
            Status = "Deleted"
        }).IsValid);

    [Fact]
    public void RegisterBuyer_RejectsAShortPassword()
        => Assert.False(new RegisterBuyerValidator().Validate(new RegisterBuyerRequest
        {
            FullName = "Nimal Perera",
            Email = "nimal@recycle.test",
            Password = "123",              // minimum is 6
            CompanyName = "Nimal Recyclers",
            ContactPerson = "Nimal Perera",
            BuyerType = "Export"
        }).IsValid);

    // ---------- Material requests ----------

    [Fact]
    public void CreateMaterialRequest_RejectsANonPositiveQuantity()
        => Assert.False(new CreateMaterialRequestValidator().Validate(
            new CreateMaterialRequestRequest { MaterialType = "Copper", QuantityKg = 0m }).IsValid);

    [Fact]
    public void CreateMaterialRequest_RejectsABlankMaterialType()
        => Assert.False(new CreateMaterialRequestValidator().Validate(
            new CreateMaterialRequestRequest { MaterialType = " ", QuantityKg = 10m }).IsValid);

    // ---------- Material pricing ----------

    [Fact]
    public void CreateMaterialPricing_RejectsANonPositivePrice()
        => Assert.False(new CreateMaterialPricingValidator().Validate(new CreateMaterialPricingRequest
        {
            MaterialType = "Copper",
            PricePerKg = 0m,
            EffectiveDate = DateOnly.FromDateTime(DateTime.UtcNow)
        }).IsValid);

    [Fact]
    public void CreateMaterialPricing_RejectsAnExpiryBeforeTheEffectiveDate()
        => Assert.False(new CreateMaterialPricingValidator().Validate(new CreateMaterialPricingRequest
        {
            MaterialType = "Copper",
            PricePerKg = 950m,
            EffectiveDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1)
        }).IsValid);

    // ---------- Commercial plans & approval decisions ----------

    [Fact]
    public void CreateCommercialPlan_RejectsAMissingReasoningSummary()
        => Assert.False(new CreateCommercialPlanValidator().Validate(new CreateCommercialPlanRequest
        {
            WorkflowId = Guid.NewGuid(),
            RecommendedRoute = "LocalSale",
            MaterialsJson = "[]",
            ReasoningSummary = " "
        }).IsValid);

    [Fact]
    public void CreateCommercialPlan_RejectsMaterialsJsonThatIsNotAnArray()
        => Assert.False(new CreateCommercialPlanValidator().Validate(new CreateCommercialPlanRequest
        {
            WorkflowId = Guid.NewGuid(),
            RecommendedRoute = "LocalSale",
            MaterialsJson = "{\"materialType\":\"Copper\"}",   // object, not array
            ReasoningSummary = "Sell locally."
        }).IsValid);

    [Fact]
    public void CreateCommercialPlan_RequiresADestinationCountryForExport()
        => Assert.False(new CreateCommercialPlanValidator().Validate(new CreateCommercialPlanRequest
        {
            WorkflowId = Guid.NewGuid(),
            RecommendedRoute = "Export",
            DestinationCountry = null,
            MaterialsJson = "[]",
            ReasoningSummary = "Export this batch."
        }).IsValid);

    [Fact]
    public void ApprovalDecision_RequiresCommentsWhenRejecting()
        => Assert.False(new ApprovalDecisionValidator().Validate(
            new ApprovalDecisionRequest { Decision = "Rejected" }).IsValid);

    [Fact]
    public void ApprovalDecision_AllowsAnApprovalWithoutComments()
        => Assert.True(new ApprovalDecisionValidator().Validate(
            new ApprovalDecisionRequest { Decision = "Approved" }).IsValid);
}
