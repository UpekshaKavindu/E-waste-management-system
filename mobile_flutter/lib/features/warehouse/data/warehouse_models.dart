import 'processing_enums.dart';

// Response shapes of the Processing API (camelCase JSON from ASP.NET). Numbers may arrive as int
// or double, so every decimal goes through [_d].

double _d(Object? v) => (v as num).toDouble();
double? _dOrNull(Object? v) => v == null ? null : (v as num).toDouble();

class PagedResponse<T> {
  const PagedResponse({required this.items, required this.page, required this.totalCount, required this.totalPages});

  final List<T> items;
  final int page;
  final int totalCount;
  final int totalPages;

  factory PagedResponse.fromJson(Map<String, dynamic> json, T Function(Map<String, dynamic>) item) => PagedResponse(
        items: (json['items'] as List).map((e) => item(e as Map<String, dynamic>)).toList(),
        page: json['page'] as int,
        totalCount: json['totalCount'] as int,
        totalPages: json['totalPages'] as int,
      );
}

// ---- lookups ------------------------------------------------------------------

class WarehouseLocation {
  const WarehouseLocation({required this.id, required this.name, this.description});

  final String id;
  final String name;
  final String? description;

  factory WarehouseLocation.fromJson(Map<String, dynamic> j) =>
      WarehouseLocation(id: j['id'] as String, name: j['name'] as String, description: j['description'] as String?);
}

class RatePolicy {
  const RatePolicy({required this.id, required this.itemType, required this.ratePerKg, required this.isActive});

  final String id;
  final String itemType;
  final double ratePerKg;
  final bool isActive;

  factory RatePolicy.fromJson(Map<String, dynamic> j) => RatePolicy(
        id: j['id'] as String,
        itemType: j['itemType'] as String,
        ratePerKg: _d(j['ratePerKg']),
        isActive: j['isActive'] as bool,
      );
}

class CollectorLookup {
  const CollectorLookup({required this.collectorId, required this.fullName, required this.vehicleType});

  final String collectorId;
  final String fullName;
  final String vehicleType;

  factory CollectorLookup.fromJson(Map<String, dynamic> j) => CollectorLookup(
        collectorId: j['collectorId'] as String,
        fullName: j['fullName'] as String,
        vehicleType: j['vehicleType'] as String? ?? '',
      );
}

// ---- inventory ----------------------------------------------------------------

class InventoryListItem {
  const InventoryListItem({
    required this.id,
    required this.itemType,
    required this.status,
    required this.originType,
    required this.kind,
    required this.verifiedWeightKg,
    required this.currentLocationName,
    required this.parentInventoryItemId,
    required this.category,
    required this.receivedAt,
    this.quantity = 1,
  });

  final String id;
  final String itemType;
  final InventoryStatus status;
  final OriginType? originType;
  final ItemKind kind;
  final double verifiedWeightKg;

  /// Above 1 for a received lot ("Laptop × 50").
  final int quantity;
  final String currentLocationName;
  final String? parentInventoryItemId;
  final ClassificationCategory? category;
  final String receivedAt;

  factory InventoryListItem.fromJson(Map<String, dynamic> j) => InventoryListItem(
        id: j['id'] as String,
        itemType: j['itemType'] as String,
        status: InventoryStatus.fromApi(j['status'] as String),
        originType: OriginType.tryFromApi(j['originType'] as String?),
        kind: ItemKind.fromApi(j['kind'] as String?),
        verifiedWeightKg: _d(j['verifiedWeightKg']),
        currentLocationName: j['currentLocationName'] as String? ?? '',
        parentInventoryItemId: j['parentInventoryItemId'] as String?,
        category: ClassificationCategory.tryFromApi(j['category'] as String?),
        receivedAt: j['receivedAt'] as String,
        quantity: (j['quantity'] as num?)?.toInt() ?? 1,
      );
}

class InventoryClassification {
  const InventoryClassification({
    required this.category,
    required this.subCategory,
    required this.source,
    required this.confidenceScore,
    required this.isFinal,
    required this.classifiedAt,
  });

  final ClassificationCategory? category;
  final String? subCategory;
  final ClassificationSource? source;
  final double? confidenceScore;
  final bool isFinal;
  final String classifiedAt;

  factory InventoryClassification.fromJson(Map<String, dynamic> j) => InventoryClassification(
        category: ClassificationCategory.tryFromApi(j['category'] as String?),
        subCategory: j['subCategory'] as String?,
        source: ClassificationSource.tryFromApi(j['source'] as String?),
        confidenceScore: _dOrNull(j['confidenceScore']),
        isFinal: j['isFinal'] as bool? ?? false,
        classifiedAt: j['classifiedAt'] as String,
      );
}

class InventoryChild {
  const InventoryChild({
    required this.id,
    required this.itemType,
    required this.status,
    required this.kind,
    required this.verifiedWeightKg,
  });

  final String id;
  final String itemType;
  final InventoryStatus status;
  final ItemKind kind;
  final double verifiedWeightKg;

  factory InventoryChild.fromJson(Map<String, dynamic> j) => InventoryChild(
        id: j['id'] as String,
        itemType: j['itemType'] as String,
        status: InventoryStatus.fromApi(j['status'] as String),
        kind: ItemKind.fromApi(j['kind'] as String?),
        verifiedWeightKg: _d(j['verifiedWeightKg']),
      );
}

class InventoryDetail {
  const InventoryDetail({
    required this.id,
    required this.itemType,
    required this.status,
    required this.originType,
    required this.kind,
    required this.verifiedWeightKg,
    required this.currentLocationId,
    required this.currentLocationName,
    required this.jobId,
    required this.extraWasteReceiptId,
    required this.parentInventoryItemId,
    required this.receivedAt,
    required this.classification,
    required this.children,
    this.quantity = 1,
  });

  final String id;
  final String itemType;

  /// Above 1 for a received lot ("Laptop × 50").
  final int quantity;
  final InventoryStatus status;
  final OriginType? originType;
  final ItemKind kind;
  final double verifiedWeightKg;
  final String currentLocationId;
  final String currentLocationName;
  final String? jobId;
  final String? extraWasteReceiptId;
  final String? parentInventoryItemId;
  final String receivedAt;
  final InventoryClassification? classification;
  final List<InventoryChild> children;

  factory InventoryDetail.fromJson(Map<String, dynamic> j) => InventoryDetail(
        id: j['id'] as String,
        itemType: j['itemType'] as String,
        status: InventoryStatus.fromApi(j['status'] as String),
        originType: OriginType.tryFromApi(j['originType'] as String?),
        kind: ItemKind.fromApi(j['kind'] as String?),
        verifiedWeightKg: _d(j['verifiedWeightKg']),
        currentLocationId: j['currentLocationId'] as String,
        currentLocationName: j['currentLocationName'] as String? ?? '',
        jobId: j['jobId'] as String?,
        extraWasteReceiptId: j['extraWasteReceiptId'] as String?,
        parentInventoryItemId: j['parentInventoryItemId'] as String?,
        receivedAt: j['receivedAt'] as String,
        classification: j['classification'] == null
            ? null
            : InventoryClassification.fromJson(j['classification'] as Map<String, dynamic>),
        children: ((j['children'] as List?) ?? const [])
            .map((c) => InventoryChild.fromJson(c as Map<String, dynamic>))
            .toList(),
        quantity: (j['quantity'] as num?)?.toInt() ?? 1,
      );
}

class ProcessingLogEntry {
  const ProcessingLogEntry({required this.action, required this.performedByStaffId, required this.performedAt, this.notes});

  final String action;
  final String performedByStaffId;
  final String performedAt;
  final String? notes;

  factory ProcessingLogEntry.fromJson(Map<String, dynamic> j) => ProcessingLogEntry(
        action: j['action'] as String,
        performedByStaffId: j['performedByStaffId'] as String,
        performedAt: j['performedAt'] as String,
        notes: j['notes'] as String?,
      );
}

// ---- action results -------------------------------------------------------------

class DismantleResult {
  const DismantleResult({
    required this.updatedWeightKg,
    required this.lossKg,
    required this.childIds,
    required this.materialIds,
  });

  final double? updatedWeightKg;
  final double lossKg;
  final List<String> childIds;
  final List<String> materialIds;

  factory DismantleResult.fromJson(Map<String, dynamic> j) => DismantleResult(
        updatedWeightKg: _dOrNull(j['updatedWeightKg']),
        lossKg: _dOrNull(j['lossKg']) ?? 0,
        childIds: (j['childInventoryItemIds'] as List).cast<String>(),
        materialIds: ((j['materialInventoryItemIds'] as List?) ?? const []).cast<String>(),
      );
}

class ClassificationValidation {
  const ClassificationValidation({required this.approved, required this.requiresHumanReview, required this.reasons});

  final bool approved;
  final bool requiresHumanReview;
  final List<String> reasons;

  factory ClassificationValidation.fromJson(Map<String, dynamic> j) => ClassificationValidation(
        approved: j['approved'] as bool,
        requiresHumanReview: j['requiresHumanReview'] as bool,
        reasons: ((j['reasons'] as List?) ?? const []).cast<String>(),
      );
}

class ClassificationResult {
  const ClassificationResult({required this.category, required this.status});

  final ClassificationCategory? category;

  /// OnHold when the backend quarantined a Hazardous item automatically.
  final InventoryStatus status;

  factory ClassificationResult.fromJson(Map<String, dynamic> j) => ClassificationResult(
        category: ClassificationCategory.tryFromApi(j['category'] as String?),
        status: InventoryStatus.fromApi(j['status'] as String),
      );
}

// ---- receiving ----------------------------------------------------------------

/// A completed job with a collector that has not been received yet (server-side filter).
class ReceivableJob {
  const ReceivableJob({
    required this.jobId,
    required this.collectorId,
    required this.collectorName,
    required this.collectorVehicleType,
    required this.pickupAddress,
    required this.reportedWeightKg,
    required this.estimatedDistanceKm,
    required this.completedAt,
    required this.submissionCategory,
    required this.suggestedItemType,
    this.items = const [],
  });

  final String jobId;
  final String collectorId;
  final String? collectorName;
  final String? collectorVehicleType;
  final String pickupAddress;
  final double? reportedWeightKg;
  final double? estimatedDistanceKm;
  final String? completedAt;
  final String? submissionCategory;

  /// The submission category matched to the item-type list; null when staff must choose.
  final String? suggestedItemType;

  /// What the customer submitted, one row per item / CSV row. Empty → the job is received as a whole.
  final List<ReceivableJobItem> items;

  int get expectedUnits => items.fold(0, (sum, i) => sum + i.quantity);

  String get collectorLabel {
    final name = collectorName;
    if (name == null || name.isEmpty) return 'Collector ${collectorId.substring(0, 8)}…';
    final vehicle = collectorVehicleType;
    return vehicle == null || vehicle.isEmpty ? name : '$name · $vehicle';
  }

  factory ReceivableJob.fromJson(Map<String, dynamic> j) => ReceivableJob(
        jobId: j['jobId'] as String,
        collectorId: j['collectorId'] as String,
        collectorName: j['collectorName'] as String?,
        collectorVehicleType: j['collectorVehicleType'] as String?,
        pickupAddress: j['pickupAddress'] as String? ?? '',
        reportedWeightKg: _dOrNull(j['reportedWeightKg']),
        estimatedDistanceKm: _dOrNull(j['estimatedDistanceKm']),
        completedAt: j['completedAt'] as String?,
        submissionCategory: j['submissionCategory'] as String?,
        suggestedItemType: j['suggestedItemType'] as String?,
        items: [
          for (final i in (j['items'] as List? ?? const [])) ReceivableJobItem.fromJson(i as Map<String, dynamic>),
        ],
      );
}

/// One submission item / CSV row of a job, as the warehouse receives it.
class ReceivableJobItem {
  const ReceivableJobItem({
    required this.submissionItemId,
    required this.itemName,
    this.description,
    required this.quantity,
    this.expectedWeightKg,
    this.suggestedItemType,
    this.suggestionSource,
  });

  final String submissionItemId;
  final String itemName;
  final String? description;

  /// Units expected (CSV quantity; 1 for manual items).
  final int quantity;

  /// Best guess for the whole row (CSV per-unit × quantity, else the AI's estimate).
  final double? expectedWeightKg;
  final String? suggestedItemType;

  /// "name" | "category" | "ai" | "submission".
  final String? suggestionSource;

  factory ReceivableJobItem.fromJson(Map<String, dynamic> j) => ReceivableJobItem(
        submissionItemId: j['submissionItemId'] as String,
        itemName: j['itemName'] as String? ?? '',
        description: j['description'] as String?,
        quantity: (j['quantity'] as num?)?.toInt() ?? 1,
        expectedWeightKg: _dOrNull(j['expectedWeightKg']),
        suggestedItemType: j['suggestedItemType'] as String?,
        suggestionSource: j['suggestionSource'] as String?,
      );
}

/// One item line of a job, as sent to receive-delivery. [receivedQuantity] 0 = not brought.
class DeliveryItemInput {
  const DeliveryItemInput({
    required this.submissionItemId,
    required this.receivedQuantity,
    required this.itemType,
    required this.verifiedWeightKg,
  });

  final String submissionItemId;
  final int receivedQuantity;
  final String? itemType;

  /// The whole row on the scale.
  final double verifiedWeightKg;

  Map<String, dynamic> toJson() => {
        'submissionItemId': submissionItemId,
        'receivedQuantity': receivedQuantity,
        'itemType': receivedQuantity > 0 ? itemType : null,
        'verifiedWeightKg': receivedQuantity > 0 ? verifiedWeightKg : 0,
      };
}

/// One job of a collector's delivery, as sent to receive-delivery: item by item, or — for a job whose
/// submission has no items — as one whole weight and type.
class DeliveryJobInput {
  const DeliveryJobInput.items({required this.jobId, required this.items})
      : wholeWeightKg = null,
        wholeItemType = null;

  const DeliveryJobInput.whole({required this.jobId, required double verifiedWeightKg, required String itemType})
      : items = const [],
        wholeWeightKg = verifiedWeightKg,
        wholeItemType = itemType;

  final String jobId;
  final List<DeliveryItemInput> items;
  final double? wholeWeightKg;
  final String? wholeItemType;

  Map<String, dynamic> toJson() => {
        'jobId': jobId,
        'items': [for (final i in items) i.toJson()],
        if (wholeWeightKg != null) 'verifiedWeightKg': wholeWeightKg,
        if (wholeItemType != null) 'itemType': wholeItemType,
      };
}

class DeliveryItemResult {
  const DeliveryItemResult({
    required this.itemName,
    required this.expectedQuantity,
    required this.receivedQuantity,
    required this.inventoryItemId,
    required this.itemType,
    required this.verifiedWeightKg,
  });

  final String itemName;
  final int expectedQuantity;
  final int receivedQuantity;

  /// Null when nothing was brought.
  final String? inventoryItemId;
  final String? itemType;
  final double verifiedWeightKg;

  factory DeliveryItemResult.fromJson(Map<String, dynamic> j) => DeliveryItemResult(
        itemName: j['itemName'] as String? ?? '',
        expectedQuantity: (j['expectedQuantity'] as num?)?.toInt() ?? 1,
        receivedQuantity: (j['receivedQuantity'] as num?)?.toInt() ?? 0,
        inventoryItemId: j['inventoryItemId'] as String?,
        itemType: j['itemType'] as String?,
        verifiedWeightKg: _d(j['verifiedWeightKg']),
      );
}

class DeliveryJobResult {
  const DeliveryJobResult({
    required this.jobId,
    required this.verifiedWeightKg,
    required this.reportedWeightKg,
    required this.discrepancyKg,
    required this.expectedQuantity,
    required this.receivedQuantity,
    required this.items,
    required this.paymentAmount,
  });

  final String jobId;

  /// Sum of the received rows — what the payment uses.
  final double verifiedWeightKg;
  final double? reportedWeightKg;
  final double? discrepancyKg;
  final int expectedQuantity;
  final int receivedQuantity;
  final List<DeliveryItemResult> items;
  final double paymentAmount;

  factory DeliveryJobResult.fromJson(Map<String, dynamic> j) => DeliveryJobResult(
        jobId: j['jobId'] as String,
        verifiedWeightKg: _d(j['verifiedWeightKg']),
        reportedWeightKg: _dOrNull(j['reportedWeightKg']),
        discrepancyKg: _dOrNull(j['discrepancyKg']),
        expectedQuantity: (j['expectedQuantity'] as num?)?.toInt() ?? 1,
        receivedQuantity: (j['receivedQuantity'] as num?)?.toInt() ?? 1,
        items: [
          for (final i in (j['items'] as List? ?? const [])) DeliveryItemResult.fromJson(i as Map<String, dynamic>),
        ],
        paymentAmount: _d(j['paymentAmount']),
      );
}

/// A collector's visit: several completed jobs received together, each its own inventory item and
/// payment, shown with one pending total (ReceiveDeliveryResponse on the backend).
class DeliveryResult {
  const DeliveryResult({required this.deliveryId, required this.jobs, required this.totalPendingAmount});

  final String deliveryId;
  final List<DeliveryJobResult> jobs;
  final double totalPendingAmount;

  factory DeliveryResult.fromJson(Map<String, dynamic> j) => DeliveryResult(
        deliveryId: j['deliveryId'] as String,
        jobs: (j['jobs'] as List).map((e) => DeliveryJobResult.fromJson(e as Map<String, dynamic>)).toList(),
        totalPendingAmount: _d(j['totalPendingAmount']),
      );
}

class ExtraWasteLineInput {
  const ExtraWasteLineInput({required this.itemType, required this.weightKg, required this.accepted, this.rejectionReason});

  final String itemType;
  final double weightKg;
  final bool accepted;
  final String? rejectionReason;
}

class ExtraWasteLineResult {
  const ExtraWasteLineResult({required this.itemType, required this.accepted, this.rejectionReason, this.inventoryItemId});

  final String itemType;
  final bool accepted;
  final String? rejectionReason;
  final String? inventoryItemId;

  factory ExtraWasteLineResult.fromJson(Map<String, dynamic> j) => ExtraWasteLineResult(
        itemType: j['itemType'] as String,
        accepted: j['accepted'] as bool,
        rejectionReason: j['rejectionReason'] as String?,
        inventoryItemId: j['inventoryItemId'] as String?,
      );
}

class ExtraWasteReceiptResult {
  const ExtraWasteReceiptResult({required this.receiptId, required this.items});

  final String receiptId;
  final List<ExtraWasteLineResult> items;

  factory ExtraWasteReceiptResult.fromJson(Map<String, dynamic> j) => ExtraWasteReceiptResult(
        receiptId: j['extraWasteReceiptId'] as String,
        items: (j['items'] as List).map((e) => ExtraWasteLineResult.fromJson(e as Map<String, dynamic>)).toList(),
      );
}

// ---- receipt history (extra waste) -------------------------------------------------

class ExtraWasteReceiptSummary {
  const ExtraWasteReceiptSummary({
    required this.receiptId,
    required this.receivedAt,
    required this.collectorName,
    required this.acceptedCount,
    required this.rejectedCount,
    required this.acceptedWeightKg,
    required this.paymentStatus,
  });

  final String receiptId;
  final String receivedAt;
  final String? collectorName;
  final int acceptedCount;
  final int rejectedCount;
  final double acceptedWeightKg;

  /// "Pending" | "Paid", or null when everything was rejected (no payment is raised).
  final String? paymentStatus;

  factory ExtraWasteReceiptSummary.fromJson(Map<String, dynamic> j) => ExtraWasteReceiptSummary(
        receiptId: j['receiptId'] as String,
        receivedAt: j['receivedAt'] as String,
        collectorName: j['collectorName'] as String?,
        acceptedCount: j['acceptedCount'] as int,
        rejectedCount: j['rejectedCount'] as int,
        acceptedWeightKg: _d(j['acceptedWeightKg']),
        paymentStatus: j['paymentStatus'] as String?,
      );
}

class ExtraWasteReceiptLine {
  const ExtraWasteReceiptLine({
    required this.itemType,
    required this.weightKg,
    required this.accepted,
    required this.rejectionReason,
    required this.inventoryItemId,
  });

  final String itemType;
  final double weightKg;
  final bool accepted;
  final String? rejectionReason;
  final String? inventoryItemId;

  factory ExtraWasteReceiptLine.fromJson(Map<String, dynamic> j) => ExtraWasteReceiptLine(
        itemType: j['itemType'] as String? ?? '',
        weightKg: _d(j['weightKg']),
        accepted: j['accepted'] as bool,
        rejectionReason: j['rejectionReason'] as String?,
        inventoryItemId: j['inventoryItemId'] as String?,
      );
}

class ExtraWasteReceiptDetail {
  const ExtraWasteReceiptDetail({
    required this.receiptId,
    required this.receivedAt,
    required this.notes,
    required this.collectorName,
    required this.receivedByName,
    required this.items,
  });

  final String receiptId;
  final String receivedAt;
  final String? notes;
  final String? collectorName;
  final String? receivedByName;
  final List<ExtraWasteReceiptLine> items;

  factory ExtraWasteReceiptDetail.fromJson(Map<String, dynamic> j) => ExtraWasteReceiptDetail(
        receiptId: j['receiptId'] as String,
        receivedAt: j['receivedAt'] as String,
        notes: j['notes'] as String?,
        collectorName: j['collectorName'] as String?,
        receivedByName: j['receivedByName'] as String?,
        items: (j['items'] as List).map((e) => ExtraWasteReceiptLine.fromJson(e as Map<String, dynamic>)).toList(),
      );
}

// ---- material stock ------------------------------------------------------------------

/// One recovered-material item that is ready for sale, and where it is stored.
class MaterialStockItem {
  const MaterialStockItem({
    required this.inventoryItemId,
    required this.weightKg,
    required this.locationName,
    required this.recordedAt,
    required this.parentItemType,
  });

  final String inventoryItemId;
  final double weightKg;
  final String locationName;
  final String recordedAt;

  /// What it was dismantled from, e.g. "Laptop".
  final String? parentItemType;

  factory MaterialStockItem.fromJson(Map<String, dynamic> j) => MaterialStockItem(
        inventoryItemId: j['inventoryItemId'] as String,
        weightKg: _d(j['weightKg']),
        locationName: j['locationName'] as String? ?? '',
        recordedAt: j['recordedAt'] as String,
        parentItemType: j['parentItemType'] as String?,
      );
}

/// All sellable stock of one material, totalled (GET /api/v1/inventory/recovered-materials).
class MaterialStockGroup {
  const MaterialStockGroup({
    required this.materialType,
    required this.totalWeightKg,
    required this.availableWeightKg,
    required this.items,
  });

  final String materialType;
  final double totalWeightKg;

  /// Total minus what is already reserved on sales or export orders.
  final double availableWeightKg;
  final List<MaterialStockItem> items;

  double get reservedWeightKg => totalWeightKg - availableWeightKg;

  factory MaterialStockGroup.fromJson(Map<String, dynamic> j) => MaterialStockGroup(
        materialType: j['materialType'] as String,
        totalWeightKg: _d(j['totalWeightKg']),
        availableWeightKg: _d(j['availableWeightKg']),
        items: (j['items'] as List).map((e) => MaterialStockItem.fromJson(e as Map<String, dynamic>)).toList(),
      );
}
