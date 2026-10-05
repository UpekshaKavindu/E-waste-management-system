import 'package:dio/dio.dart';

import 'collector_models.dart';

/// The Collector-role endpoints on CollectorsController and JobsController. Every write here
/// (availability, location, accept/reject/start/complete) checks server-side that the caller
/// owns the collector profile / job — this class just calls the URLs.
class CollectorApi {
  CollectorApi(this._dio);

  final Dio _dio;

  /// null means "no profile yet" (backend returns 404) rather than an error — the caller (the
  /// router's profile gate) decides what to do with that.
  Future<CollectorProfile?> me() async {
    try {
      final response = await _dio.get<Map<String, dynamic>>('/api/v1/collectors/me');
      return CollectorProfile.fromJson(response.data!);
    } on DioException catch (e) {
      if (e.response?.statusCode == 404) return null;
      rethrow;
    }
  }

  Future<CollectorProfile> createProfile({required String vehicleType, required double capacityKg}) async {
    final response = await _dio.post<Map<String, dynamic>>('/api/v1/collectors', data: {
      'vehicleType': vehicleType,
      'capacityKg': capacityKg,
    });
    return CollectorProfile.fromJson(response.data!);
  }

  /// PUT /me — the collector's own name, phone, vehicle and capacity. Email is the login and can't change here.
  Future<CollectorProfile> updateMyProfile({
    required String fullName,
    String? phone,
    required String vehicleType,
    required double capacityKg,
  }) async {
    final response = await _dio.put<Map<String, dynamic>>('/api/v1/collectors/me', data: {
      'fullName': fullName,
      'phone': phone,
      'vehicleType': vehicleType,
      'capacityKg': capacityKg,
    });
    return CollectorProfile.fromJson(response.data!);
  }

  Future<CollectorProfile> updateAvailability(String collectorId, bool isAvailable) async {
    final response = await _dio.put<Map<String, dynamic>>(
      '/api/v1/collectors/$collectorId/availability',
      data: {'isAvailable': isAvailable},
    );
    return CollectorProfile.fromJson(response.data!);
  }

  Future<void> updateLocation(String collectorId, double latitude, double longitude) => _dio.put<void>(
        '/api/v1/collectors/$collectorId/location',
        data: {'latitude': latitude, 'longitude': longitude},
      );

  // ---- jobs -------------------------------------------------------------------

  Future<CollectionJob> getById(String jobId) async {
    final response = await _dio.get<Map<String, dynamic>>('/api/v1/jobs/$jobId');
    return CollectionJob.fromJson(response.data!);
  }

  Future<List<CollectionJob>> myJobs(JobStatus status) async {
    final response = await _dio.get<List<dynamic>>('/api/v1/jobs/my', queryParameters: {'status': status.apiValue});
    return response.data!.map((e) => CollectionJob.fromJson(e as Map<String, dynamic>)).toList();
  }

  /// The three statuses that belong on the active job list — Assigned (offered, awaiting a
  /// decision), Accepted (on the way to pick up) and InProgress (collector has set off).
  Future<List<CollectionJob>> myActiveJobs() async {
    final lists = await Future.wait([myJobs(JobStatus.assigned), myJobs(JobStatus.accepted), myJobs(JobStatus.inProgress)]);
    final jobs = lists.expand((l) => l).toList()..sort((a, b) => b.createdAt.compareTo(a.createdAt));
    return jobs;
  }

  /// Road route to the job's pickup. Without a position the server uses the last one this collector reported.
  Future<JobRoute> route(String jobId, {double? fromLat, double? fromLng}) async {
    final response = await _dio.get<Map<String, dynamic>>('/api/v1/jobs/$jobId/route', queryParameters: {
      if (fromLat != null && fromLng != null) ...{'fromLat': fromLat, 'fromLng': fromLng},
    });
    return JobRoute.fromJson(response.data!);
  }

  /// Customer contact and payment for one of this collector's jobs.
  Future<CollectorJobInfo> jobInfo(String jobId) async {
    final response = await _dio.get<Map<String, dynamic>>('/api/v1/jobs/$jobId/collector-info');
    return CollectorJobInfo.fromJson(response.data!);
  }

  /// Completed jobs, most recently completed first.
  Future<List<CollectionJob>> myCompletedJobs() async {
    final jobs = await myJobs(JobStatus.completed);
    return jobs..sort((a, b) => (b.completedAt ?? b.createdAt).compareTo(a.completedAt ?? a.createdAt));
  }

  Future<CollectionJob> accept(String jobId) async {
    final response = await _dio.put<Map<String, dynamic>>('/api/v1/jobs/$jobId/accept');
    return CollectionJob.fromJson(response.data!);
  }

  Future<CollectionJob> reject(String jobId, {String? reason}) async {
    final response = await _dio.put<Map<String, dynamic>>('/api/v1/jobs/$jobId/reject', data: {'reason': reason});
    return CollectionJob.fromJson(response.data!);
  }

  Future<CollectionJob> start(String jobId) async {
    final response = await _dio.put<Map<String, dynamic>>('/api/v1/jobs/$jobId/start');
    return CollectionJob.fromJson(response.data!);
  }

  Future<CollectionJob> complete(
    String jobId, {
    required String photoUrl,
    required double measuredWeightKg,
    String? notes,
  }) async {
    final response = await _dio.post<Map<String, dynamic>>('/api/v1/jobs/$jobId/complete', data: {
      'photoUrl': photoUrl,
      'measuredWeightKg': measuredWeightKg,
      'notes': notes,
    });
    return CollectionJob.fromJson(response.data!);
  }
}
