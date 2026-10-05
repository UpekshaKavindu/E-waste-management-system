import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:geolocator/geolocator.dart';

import '../../../core/auth/auth_controller.dart';
import '../../../core/network/api_client.dart';
import '../data/collector_api.dart';
import '../data/collector_models.dart';

final collectorApiProvider = Provider<CollectorApi>((ref) => CollectorApi(ref.watch(dioProvider)));

void _resetOnUserChange(Ref ref) => ref.watch(authControllerProvider.select((s) => s.user?.userId));

/// null means the signed-in collector has no profile yet (GET /me returned 404) — the router's
/// profile gate uses this to decide whether to show the setup screen.
final collectorProfileProvider = FutureProvider<CollectorProfile?>((ref) {
  _resetOnUserChange(ref);
  return ref.watch(collectorApiProvider).me();
});

/// Assigned + Accepted + InProgress jobs, re-fetched every 30 seconds while this provider has a
/// listener (i.e. while the job list screen is on screen) — autoDispose stops the polling loop
/// as soon as it doesn't.
final myActiveJobsProvider = StreamProvider.autoDispose<List<CollectionJob>>((ref) async* {
  final api = ref.watch(collectorApiProvider);
  while (true) {
    yield await api.myActiveJobs();
    await Future<void>.delayed(const Duration(seconds: 30));
  }
});

/// Completed jobs, polled like [myActiveJobsProvider] so the vehicle load updates once the
/// warehouse receives a job.
final myCompletedJobsProvider = StreamProvider.autoDispose<List<CollectionJob>>((ref) async* {
  final api = ref.watch(collectorApiProvider);
  while (true) {
    yield await api.myCompletedJobs();
    await Future<void>.delayed(const Duration(seconds: 30));
  }
});

/// Route from where the phone is now to a job's pickup. The position is best-effort: without
/// location permission (or if it times out) the server falls back to the last reported position.
final jobRouteProvider = FutureProvider.autoDispose.family<JobRoute, String>((ref, jobId) async {
  final position = await _currentPosition();
  return ref.watch(collectorApiProvider).route(jobId, fromLat: position?.latitude, fromLng: position?.longitude);
});

Future<Position?> _currentPosition() async {
  try {
    if (!await Geolocator.isLocationServiceEnabled()) return null;
    var permission = await Geolocator.checkPermission();
    if (permission == LocationPermission.denied) permission = await Geolocator.requestPermission();
    if (permission != LocationPermission.always && permission != LocationPermission.whileInUse) return null;
    return await Geolocator.getCurrentPosition(
      locationSettings: const LocationSettings(accuracy: LocationAccuracy.high, timeLimit: Duration(seconds: 10)),
    );
  } catch (_) {
    return null;
  }
}

final jobInfoProvider = FutureProvider.autoDispose.family<CollectorJobInfo, String>(
  (ref, jobId) => ref.watch(collectorApiProvider).jobInfo(jobId),
);
