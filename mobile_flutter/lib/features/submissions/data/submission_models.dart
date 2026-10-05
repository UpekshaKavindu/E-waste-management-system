/// Must match backend/EWasteManagement.API/Features/Submissions/DTOs/SubmissionCategories.cs
/// — kept in sync by hand, same as the web app's SUBMISSION_CATEGORIES.
const submissionCategories = [
  'Household Electronics',
  'IT Equipment',
  'Batteries',
  'Heavy Appliances',
  'Other',
];

/// One item being drafted in the submit form, before it's sent. Not the backend's shape —
/// see SubmissionsApi.create for that.
class SubmissionItemDraft {
  SubmissionItemDraft({String? itemName, String? description, this.imageUrl})
      : itemName = itemName ?? '',
        description = description ?? '';

  String itemName;
  String description;
  String? imageUrl;
}

/// One item on a submission already sent to the backend (SubmissionItemResponseDto).
class SubmissionItem {
  const SubmissionItem({required this.id, required this.itemName, this.description, required this.imageUrl});

  final String id;
  final String itemName;
  final String? description;
  final String imageUrl;

  factory SubmissionItem.fromJson(Map<String, dynamic> json) => SubmissionItem(
        id: json['id'] as String,
        itemName: json['itemName'] as String? ?? '',
        description: json['description'] as String?,
        imageUrl: json['imageUrl'] as String? ?? '',
      );
}

/// SubmissionAnalysisDto — only present once the Analyzer agent has run. The top-level fields are
/// the whole submission (worst hazard, totals, lowest confidence); [items] is each item's own result.
class SubmissionAnalysis {
  const SubmissionAnalysis({
    required this.wasteCategory,
    required this.hazardLevel,
    required this.estimatedVolumeKg,
    required this.estimatedValueLkr,
    required this.confidenceScore,
    this.items = const [],
  });

  final String wasteCategory;
  final String hazardLevel;
  final double estimatedVolumeKg;
  final double estimatedValueLkr;
  final double confidenceScore;

  /// Empty for submissions analysed before per-item analysis.
  final List<AnalyzedItem> items;

  factory SubmissionAnalysis.fromJson(Map<String, dynamic> json) => SubmissionAnalysis(
        wasteCategory: json['wasteCategory'] as String? ?? '',
        hazardLevel: json['hazardLevel'] as String? ?? '',
        estimatedVolumeKg: (json['estimatedVolumeKg'] as num?)?.toDouble() ?? 0,
        estimatedValueLkr: (json['estimatedValueLkr'] as num?)?.toDouble() ?? 0,
        confidenceScore: (json['confidenceScore'] as num?)?.toDouble() ?? 0,
        items: [
          for (final item in (json['items'] as List<dynamic>? ?? const []))
            AnalyzedItem.fromJson(item as Map<String, dynamic>),
        ],
      );
}

/// One item's own classification inside a [SubmissionAnalysis].
class AnalyzedItem {
  const AnalyzedItem({
    required this.itemName,
    required this.wasteCategory,
    required this.hazardLevel,
    required this.estimatedVolumeKg,
    required this.estimatedValueLkr,
    required this.confidenceScore,
  });

  final String itemName;
  final String wasteCategory;
  final String hazardLevel;
  final double estimatedVolumeKg;
  final double estimatedValueLkr;
  final double confidenceScore;

  factory AnalyzedItem.fromJson(Map<String, dynamic> json) => AnalyzedItem(
        itemName: json['itemName'] as String? ?? '',
        wasteCategory: json['wasteCategory'] as String? ?? '',
        hazardLevel: json['hazardLevel'] as String? ?? '',
        estimatedVolumeKg: (json['estimatedVolumeKg'] as num?)?.toDouble() ?? 0,
        estimatedValueLkr: (json['estimatedValueLkr'] as num?)?.toDouble() ?? 0,
        confidenceScore: (json['confidenceScore'] as num?)?.toDouble() ?? 0,
      );
}

/// SubmissionWorkflowDto.
class SubmissionWorkflow {
  const SubmissionWorkflow({required this.workflowId, required this.status, required this.approvalRequired, this.analysis});

  final String workflowId;
  final String status;
  final bool approvalRequired;
  final SubmissionAnalysis? analysis;

  factory SubmissionWorkflow.fromJson(Map<String, dynamic> json) => SubmissionWorkflow(
        workflowId: json['workflowId'] as String,
        status: json['status'] as String? ?? '',
        approvalRequired: json['approvalRequired'] as bool? ?? false,
        analysis: json['analysis'] == null ? null : SubmissionAnalysis.fromJson(json['analysis'] as Map<String, dynamic>),
      );
}

/// SubmissionResponseDto — status/statusLabel/statusReason are derived on the backend from the
/// job (once one exists) or the workflow; never set directly. See SubmissionStatusResolver.cs.
class SubmissionResponse {
  const SubmissionResponse({
    required this.id,
    required this.category,
    required this.estimatedWeight,
    required this.pickupAddress,
    required this.phoneNumber,
    required this.createdAt,
    required this.items,
    required this.status,
    required this.statusLabel,
    this.statusReason,
    this.workflow,
    this.jobId,
    this.jobStatus,
  });

  final String id;
  final String category;
  final double estimatedWeight;
  final String pickupAddress;
  final String phoneNumber;
  final DateTime createdAt;
  final List<SubmissionItem> items;

  /// Machine-readable, e.g. "Analyzing", "CollectorAssigned".
  final String status;

  /// Human-readable, e.g. "Collector assigned".
  final String statusLabel;

  final String? statusReason;
  final SubmissionWorkflow? workflow;
  final String? jobId;
  final String? jobStatus;

  factory SubmissionResponse.fromJson(Map<String, dynamic> json) => SubmissionResponse(
        id: json['id'] as String,
        category: json['category'] as String? ?? '',
        estimatedWeight: (json['estimatedWeight'] as num?)?.toDouble() ?? 0,
        pickupAddress: json['pickupAddress'] as String? ?? '',
        phoneNumber: json['phoneNumber'] as String? ?? '',
        createdAt: DateTime.parse(json['createdAt'] as String),
        items: (json['items'] as List<dynamic>? ?? [])
            .map((e) => SubmissionItem.fromJson(e as Map<String, dynamic>))
            .toList(),
        status: json['status'] as String? ?? '',
        statusLabel: json['statusLabel'] as String? ?? '',
        statusReason: json['statusReason'] as String?,
        workflow: json['workflow'] == null ? null : SubmissionWorkflow.fromJson(json['workflow'] as Map<String, dynamic>),
        jobId: json['jobId'] as String?,
        jobStatus: json['jobStatus'] as String?,
      );
}

/// Statuses where the background agent chain is still working on its own — same list as the
/// web app's IN_PROGRESS_STATUSES.
const inProgressStatuses = {'Analyzing', 'Scheduling'};
