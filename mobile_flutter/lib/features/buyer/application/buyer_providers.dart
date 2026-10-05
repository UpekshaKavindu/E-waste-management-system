import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/auth/auth_controller.dart';
import '../../../core/network/api_client.dart';
import '../data/buyer_api.dart';
import '../data/buyer_models.dart';

final buyerApiProvider = Provider<BuyerApi>((ref) => BuyerApi(ref.watch(dioProvider)));

final buyerRequestsProvider = FutureProvider<List<MaterialRequest>>((ref) {
  ref.watch(authControllerProvider.select((state) => state.user?.userId));
  return ref.watch(buyerApiProvider).myRequests();
});

/// One request for the full-view detail screen. There is no per-request endpoint a buyer
/// may call, so this reuses the "mine" list and picks the row out — the family key keeps
/// each request's state separate, so opening one detail screen doesn't reload the list.
final buyerRequestProvider = FutureProvider.autoDispose.family<MaterialRequest, String>((ref, requestId) async {
  final requests = await ref.watch(buyerApiProvider).myRequests();
  final match = requests.where((request) => request.id == requestId);
  if (match.isEmpty) throw RequestNotFound(requestId);
  return match.first;
});

/// Thrown when a request id no longer appears in the buyer's own list.
class RequestNotFound implements Exception {
  const RequestNotFound(this.requestId);

  final String requestId;

  @override
  String toString() => 'RequestNotFound($requestId)';
}

/// Counts the portfolio at a glance: how much is still moving, how much is planned,
/// and how much has been fulfilled. Derived here so the list screen stays presentational.
class BuyerRequestStats {
  const BuyerRequestStats({
    required this.total,
    required this.open,
    required this.planned,
    required this.fulfilled,
    required this.openKg,
  });

  const BuyerRequestStats.empty()
      : total = 0,
        open = 0,
        planned = 0,
        fulfilled = 0,
        openKg = 0;

  final int total;
  final int open;
  final int planned;
  final int fulfilled;

  /// Kilograms still being matched, priced or planned.
  final double openKg;

  static BuyerRequestStats of(List<MaterialRequest> requests) {
    final open = requests.where((r) => r.stage.isOpen).toList();
    return BuyerRequestStats(
      total: requests.length,
      open: open.length,
      planned: requests.where((r) => r.hasOrder || r.stage == RequestStage.planGenerated).length,
      fulfilled: requests.where((r) => r.stage == RequestStage.fulfilled).length,
      openKg: open.fold(0, (sum, r) => sum + r.quantityKg),
    );
  }
}

final buyerRequestStatsProvider = Provider<BuyerRequestStats>((ref) {
  final requests = ref.watch(buyerRequestsProvider).value;
  return requests == null ? const BuyerRequestStats.empty() : BuyerRequestStats.of(requests);
});

/// Cancels a request and refreshes the list plus any open detail screen for it.
Future<void> cancelBuyerRequest(WidgetRef ref, String requestId) async {
  await ref.read(buyerApiProvider).cancelRequest(requestId);
  ref.invalidate(buyerRequestsProvider);
  ref.invalidate(buyerRequestProvider(requestId));
}
