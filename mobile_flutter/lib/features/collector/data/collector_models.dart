import '../../../core/utils/format.dart';

/// CollectorResponseDto.
class CollectorProfile {
  const CollectorProfile({
    required this.collectorId,
    this.fullName = '',
    this.email = '',
    this.phone,
    required this.vehicleType,
    required this.capacityKg,
    required this.isAvailable,
    required this.rating,
    this.currentLatitude,
    this.currentLongitude,
    required this.activeJobCount,
    required this.maxActiveJobs,
  });

  final String collectorId;
  final String fullName;
  final String email;
  final String? phone;
  final String vehicleType;
  final double capacityKg;
  final bool isAvailable;
  final double rating;
  final double? currentLatitude;
  final double? currentLongitude;
  final int activeJobCount;
  final int maxActiveJobs;

  CollectorProfile copyWith({bool? isAvailable}) => CollectorProfile(
        collectorId: collectorId,
        fullName: fullName,
        email: email,
        phone: phone,
        vehicleType: vehicleType,
        capacityKg: capacityKg,
        isAvailable: isAvailable ?? this.isAvailable,
        rating: rating,
        currentLatitude: currentLatitude,
        currentLongitude: currentLongitude,
        activeJobCount: activeJobCount,
        maxActiveJobs: maxActiveJobs,
      );

  factory CollectorProfile.fromJson(Map<String, dynamic> json) => CollectorProfile(
        collectorId: json['collectorId'] as String,
        fullName: json['fullName'] as String? ?? '',
        email: json['email'] as String? ?? '',
        phone: json['phone'] as String?,
        vehicleType: json['vehicleType'] as String? ?? '',
        capacityKg: (json['capacityKg'] as num?)?.toDouble() ?? 0,
        isAvailable: json['isAvailable'] as bool? ?? false,
        rating: (json['rating'] as num?)?.toDouble() ?? 0,
        currentLatitude: (json['currentLatitude'] as num?)?.toDouble(),
        currentLongitude: (json['currentLongitude'] as num?)?.toDouble(),
        activeJobCount: json['activeJobCount'] as int? ?? 0,
        maxActiveJobs: json['maxActiveJobs'] as int? ?? 3,
      );
}

/// JobStatus on the backend — Job.cs / Features/Collection/Entities/JobStatus.cs. Serialized
/// as the exact C# member name (job.Status.ToString()), so these strings must match exactly.
enum JobStatus {
  assigned('Assigned'),
  accepted('Accepted'),
  rejected('Rejected'),
  inProgress('InProgress'),
  completed('Completed'),
  cancelled('Cancelled'),
  noCollectorAvailable('NoCollectorAvailable'),
  pickupLocationUnresolved('PickupLocationUnresolved');

  const JobStatus(this.apiValue);
  final String apiValue;

  static JobStatus? fromApi(String? value) {
    for (final status in JobStatus.values) {
      if (status.apiValue == value) return status;
    }
    return null;
  }
}

/// JobResponseDto.
class CollectionJob {
  const CollectionJob({
    required this.jobId,
    required this.status,
    required this.pickupAddress,
    this.pickupLatitude,
    this.pickupLongitude,
    this.requiredCapacityKg,
    this.estimatedEtaMinutes,
    this.estimatedDistanceKm,
    this.photoUrl,
    this.measuredWeightKg,
    this.notes,
    this.rejectionReason,
    required this.createdAt,
    this.completedAt,
    this.receivedAtWarehouse = false,
  });

  final String jobId;
  final JobStatus status;
  final String pickupAddress;
  final double? pickupLatitude;
  final double? pickupLongitude;
  final double? requiredCapacityKg;
  final int? estimatedEtaMinutes;
  final double? estimatedDistanceKm;
  final String? photoUrl;
  final double? measuredWeightKg;
  final String? notes;
  final String? rejectionReason;
  final DateTime createdAt;
  final DateTime? completedAt;

  /// Completed and weighed into inventory. A completed job not yet received is still in the vehicle.
  final bool receivedAtWarehouse;

  factory CollectionJob.fromJson(Map<String, dynamic> json) => CollectionJob(
        jobId: json['jobId'] as String,
        status: JobStatus.fromApi(json['status'] as String?) ?? JobStatus.assigned,
        pickupAddress: json['pickupAddress'] as String? ?? '',
        pickupLatitude: (json['pickupLatitude'] as num?)?.toDouble(),
        pickupLongitude: (json['pickupLongitude'] as num?)?.toDouble(),
        requiredCapacityKg: (json['requiredCapacityKg'] as num?)?.toDouble(),
        estimatedEtaMinutes: json['estimatedEtaMinutes'] as int?,
        estimatedDistanceKm: (json['estimatedDistanceKm'] as num?)?.toDouble(),
        photoUrl: json['photoUrl'] as String?,
        measuredWeightKg: (json['measuredWeightKg'] as num?)?.toDouble(),
        notes: json['notes'] as String?,
        rejectionReason: json['rejectionReason'] as String?,
        createdAt: DateTime.parse(json['createdAt'] as String),
        completedAt: Format.parseApiDate(json['completedAt'] as String?),
        receivedAtWarehouse: json['receivedAtWarehouse'] as bool? ?? false,
      );
}

/// JobRouteDto — the road route from the collector to a job's pickup, for the job map.
class JobRoute {
  const JobRoute({
    this.originLatitude,
    this.originLongitude,
    this.pickupLatitude,
    this.pickupLongitude,
    this.distanceKm,
    this.durationMinutes,
    this.points = const [],
  });

  final double? originLatitude;
  final double? originLongitude;
  final double? pickupLatitude;
  final double? pickupLongitude;
  final double? distanceKm;
  final int? durationMinutes;

  /// [lat, lng] pairs, origin first. Empty when no route could be found.
  final List<(double, double)> points;

  factory JobRoute.fromJson(Map<String, dynamic> json) => JobRoute(
        originLatitude: (json['originLatitude'] as num?)?.toDouble(),
        originLongitude: (json['originLongitude'] as num?)?.toDouble(),
        pickupLatitude: (json['pickupLatitude'] as num?)?.toDouble(),
        pickupLongitude: (json['pickupLongitude'] as num?)?.toDouble(),
        distanceKm: (json['distanceKm'] as num?)?.toDouble(),
        durationMinutes: json['durationMinutes'] as int?,
        points: [
          for (final p in (json['points'] as List<dynamic>? ?? const []).cast<List<dynamic>>())
            ((p[0] as num).toDouble(), (p[1] as num).toDouble()),
        ],
      );
}

/// CollectorJobInfoDto — customer contact (only once the job is accepted) and the payment for a job.
class CollectorJobInfo {
  const CollectorJobInfo({
    required this.contactAvailable,
    this.customerName,
    this.customerPhone,
    this.paymentAmount,
    required this.paymentIsEstimate,
    this.paymentStatus,
    this.estimateWeightKg,
    this.baseFee,
    this.ratePerKg,
    this.weightAmount,
    this.distanceKm,
    this.distanceAmount,
  });

  final bool contactAvailable;
  final String? customerName;
  final String? customerPhone;

  /// The real payment once the warehouse received the job, otherwise an estimate (null if unknown).
  final double? paymentAmount;
  final bool paymentIsEstimate;

  /// "Pending" | "Paid" for a real payment.
  final String? paymentStatus;

  final double? estimateWeightKg;
  final double? baseFee;
  final double? ratePerKg;
  final double? weightAmount;
  final double? distanceKm;
  final double? distanceAmount;

  factory CollectorJobInfo.fromJson(Map<String, dynamic> json) {
    double? d(String key) => (json[key] as num?)?.toDouble();
    return CollectorJobInfo(
      contactAvailable: json['contactAvailable'] as bool? ?? false,
      customerName: json['customerName'] as String?,
      customerPhone: json['customerPhone'] as String?,
      paymentAmount: d('paymentAmount'),
      paymentIsEstimate: json['paymentIsEstimate'] as bool? ?? false,
      paymentStatus: json['paymentStatus'] as String?,
      estimateWeightKg: d('estimateWeightKg'),
      baseFee: d('baseFee'),
      ratePerKg: d('ratePerKg'),
      weightAmount: d('weightAmount'),
      distanceKm: d('distanceKm'),
      distanceAmount: d('distanceAmount'),
    );
  }
}
