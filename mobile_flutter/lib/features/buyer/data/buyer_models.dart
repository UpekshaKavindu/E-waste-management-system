import '../../../core/utils/format.dart';

class BuyerRegistration {
  const BuyerRegistration({required this.status});

  final String status;

  factory BuyerRegistration.fromJson(Map<String, dynamic> json) => BuyerRegistration(status: json['status'] as String? ?? 'Pending');
}

/// Where one material request sits in the lifecycle the buyer sees
/// (`MaterialRequestStatus` on the backend). An unknown api name falls back to
/// [RequestStage.waiting] so a new server-side status still renders a usable screen.
enum RequestStage {
  waiting('Waiting', 'Submitted'),
  waitingForPrice('WaitingForPrice', 'Matching stock'),
  generatingPlan('GeneratingPlan', 'Building plan'),
  planGenerated('PlanGenerated', 'Plan ready'),
  planGenerationFailed('PlanGenerationFailed', 'Needs attention'),
  orderPlaced('OrderPlaced', 'Order placed'),
  fulfilled('Fulfilled', 'Fulfilled'),
  cancelled('Cancelled', 'Cancelled');

  const RequestStage(this.apiName, this.label);

  final String apiName;
  final String label;

  static RequestStage parse(String? apiName) => values.firstWhere(
        (stage) => stage.apiName == apiName,
        orElse: () => RequestStage.waiting,
      );

  /// Index of the milestone this stage sits on for the stepper:
  /// Submitted → Matching → Plan → Order → Fulfilled.
  /// Failed and cancelled requests have stopped, so they report -1.
  int get step => switch (this) {
        RequestStage.waiting => 0,
        RequestStage.waitingForPrice => 1,
        RequestStage.generatingPlan => 2,
        RequestStage.planGenerated => 2,
        RequestStage.orderPlaced => 3,
        RequestStage.fulfilled => 4,
        RequestStage.planGenerationFailed || RequestStage.cancelled => -1,
      };

  /// Still moving: the sales desk may progress it, and the buyer may still cancel it.
  bool get isOpen =>
      const {waiting, waitingForPrice, generatingPlan, planGenerated, planGenerationFailed}.contains(this);

  /// Stopped early, so the stepper's last milestone never lights up.
  bool get isStopped => step < 0;

  /// Needs the buyer or staff to do something before it can move again.
  bool get needsAttention => this == RequestStage.planGenerationFailed;

  /// Plain-language explanation shown on the detail screen, so the buyer always knows
  /// what the system is doing with their request and what happens next.
  String get explanation => switch (this) {
        RequestStage.waiting =>
          'Received. The sales desk is looking for recovered stock that matches what you asked for.',
        RequestStage.waitingForPrice =>
          'Matching stock was found, but no approved price is live for this material yet. Matching retries '
              'as soon as the price is approved.',
        RequestStage.generatingPlan => 'A commercial plan is being prepared for this request.',
        RequestStage.planGenerated =>
          'A plan is ready and waiting for staff approval. You will be notified the moment it is approved or rejected.',
        RequestStage.planGenerationFailed =>
          'Plan generation did not finish. Staff will retry — see the note below for the reason.',
        RequestStage.orderPlaced => 'Approved. Your materials are now an order and are being prepared for you.',
        RequestStage.fulfilled => 'Complete. This request has been fulfilled.',
        RequestStage.cancelled => 'Cancelled. No further action will be taken on this request.',
      };
}

class MaterialRequest {
  const MaterialRequest({
    required this.id,
    required this.materialType,
    required this.quantityKg,
    required this.status,
    required this.createdAt,
    this.commercialPlanId,
    this.salesOrderId,
    this.lastMatchingNote,
    this.updatedAt,
  });

  final String id;
  final String materialType;
  final double quantityKg;
  final String status;
  final DateTime createdAt;
  final String? commercialPlanId;
  final String? salesOrderId;
  final String? lastMatchingNote;
  final DateTime? updatedAt;

  RequestStage get stage => RequestStage.parse(status);

  bool get canCancel => const {'Waiting', 'WaitingForPrice', 'PlanGenerationFailed'}.contains(status);

  /// A commercial plan has been generated against this request.
  bool get hasPlan => commercialPlanId != null && commercialPlanId!.isNotEmpty;

  /// An order exists — i.e. the plan was approved and stock committed.
  bool get hasOrder => salesOrderId != null && salesOrderId!.isNotEmpty;

  /// A plan or order has been produced for this request.
  bool get isPlanned => hasPlan || hasOrder;

  factory MaterialRequest.fromJson(Map<String, dynamic> json) => MaterialRequest(
        id: json['materialRequestId'] as String,
        materialType: json['materialType'] as String? ?? 'Material',
        quantityKg: (json['quantityKg'] as num? ?? 0).toDouble(),
        status: json['status'] as String? ?? 'Waiting',
        // Zone-less ASP.NET timestamps are treated as UTC by Format.parseApiDate.
        createdAt: Format.parseApiDate(json['createdAt'] as String?) ?? DateTime.now(),
        commercialPlanId: json['commercialPlanId'] as String?,
        salesOrderId: json['salesOrderId'] as String?,
        lastMatchingNote: json['lastMatchingNote'] as String?,
        updatedAt: Format.parseApiDate(json['updatedAt'] as String?),
      );
}
