import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../features/buyer/application/buyer_providers.dart';
import '../../features/buyer/presentation/buyer_account_screen.dart';
import '../../features/buyer/presentation/buyer_notifications_screen.dart';
import '../../features/buyer/presentation/buyer_registration_screen.dart';
import '../../features/buyer/presentation/buyer_request_detail_screen.dart';
import '../../features/buyer/presentation/buyer_requests_screen.dart';
import '../../features/buyer/presentation/buyer_shell.dart';
import '../../features/auth/presentation/login_screen.dart';
import '../../features/auth/presentation/register_screen.dart';
import '../../features/auth/presentation/session_screens.dart';
import '../../features/auth/presentation/welcome_screen.dart';
import '../../features/collector/application/collector_providers.dart';
import '../../features/collector/presentation/collector_profile_screen.dart';
import '../../features/collector/presentation/collector_profile_setup_screen.dart';
import '../../features/collector/presentation/collector_shell.dart';
import '../../features/collector/presentation/job_detail_screen.dart';
import '../../features/collector/presentation/job_list_screen.dart';
import '../../features/submissions/presentation/submission_shell.dart';
import '../../features/warehouse/data/processing_enums.dart';
import '../../features/warehouse/presentation/home/warehouse_home_screen.dart';
import '../../features/warehouse/presentation/inventory/inventory_list_screen.dart';
import '../../features/warehouse/presentation/inventory/item_detail_screen.dart';
import '../../features/warehouse/presentation/inventory/material_stock_screen.dart';
import '../../features/warehouse/presentation/receive/receive_screen.dart';
import '../../features/warehouse/presentation/scan/scan_screen.dart';
import '../../features/warehouse/presentation/warehouse_shell.dart';
import '../auth/auth_controller.dart';
import '../auth/auth_models.dart';
import '../network/api_error.dart';

/// Where each role lands after signing in. Worker staff get the warehouse (receiving & inventory);
/// Household/Corporate get the submission flow; Collector gets the job list (once their profile
/// check, below, lets them past it). Management staff and admins use the web portal.
String homeFor(AuthUser user) {
  if (user.isWorker) return '/warehouse';
  if (user.isGenerator) return '/submissions';
  if (user.isCollector) return '/collector';
  return '/unavailable';
}

final routerProvider = Provider<GoRouter>((ref) {
  // Re-run the redirect whenever the session changes (sign in, sign out, token expiry).
  final refresh = ValueNotifier<int>(0);
  ref.listen(authControllerProvider, (_, __) => refresh.value++);
  ref.onDispose(refresh.dispose);

  return GoRouter(
    initialLocation: '/splash',
    refreshListenable: refresh,
    debugLogDiagnostics: kDebugMode,
    redirect: (context, state) async {
      final auth = ref.read(authControllerProvider);
      final path = state.matchedLocation;

      if (auth.status == AuthStatus.restoring) return path == '/splash' ? null : '/splash';

      final user = auth.user;
      const publicPaths = {'/welcome', '/login', '/register', '/register/buyer'};
      if (user == null) {
        if (publicPaths.contains(path)) return null;
        // A forced sign-out (e.g. expired session) explains itself on the login screen; otherwise
        // signed-out users start at the welcome page.
        return auth.signedOutReason != null ? '/login' : '/welcome';
      }

      if (publicPaths.contains(path) || path == '/splash') {
        if (user.role.toLowerCase() == 'corporate') return _corporateHome(ref);
        return homeFor(user);
      }

      // Each role's API is scoped server-side too — don't show a screen that would only ever
      // come back 403.
      if (path.startsWith('/warehouse') && !user.isWorker) return '/unavailable';
      if (path.startsWith('/submissions') && !user.isGenerator) return '/unavailable';
      if (path.startsWith('/collector') && !user.isCollector) return '/unavailable';
      if (path.startsWith('/buyer')) {
        if (user.role.toLowerCase() != 'corporate') return '/unavailable';
        try {
          await ref.read(buyerRequestsProvider.future);
        } catch (error) {
          return apiErrorStatus(error) == 404 ? '/submissions' : '/unavailable';
        }
      }

      // A Collector with no profile yet can reach nothing except the setup screen; a Collector
      // who already has one skips straight past it. Cached by collectorProfileProvider, so this
      // only makes a network call the first time (or after CreateProfile invalidates it) —
      // not on every navigation.
      if (user.isCollector && path.startsWith('/collector')) {
        final hasProfile = await ref.read(collectorProfileProvider.future) != null;
        if (!hasProfile && path != '/collector/setup-profile') return '/collector/setup-profile';
        if (hasProfile && path == '/collector/setup-profile') return '/collector';
      }

      return null;
    },
    routes: [
      GoRoute(path: '/splash', builder: (_, __) => const SplashScreen()),
      GoRoute(path: '/welcome', builder: (_, __) => const WelcomeScreen()),
      GoRoute(path: '/login', builder: (_, __) => const LoginScreen()),
      GoRoute(path: '/register', builder: (_, __) => const RegisterScreen()),
      GoRoute(path: '/register/buyer', builder: (_, __) => const BuyerRegistrationScreen()),
      GoRoute(path: '/unavailable', builder: (_, __) => const RoleNotAvailableScreen()),
      // The buyer portal is a three-tab app like the warehouse and collector apps: the
      // request portfolio, the updates feed, and the account. A request's full view
      // nests under the portfolio tab so it keeps the bottom bar.
      StatefulShellRoute.indexedStack(
        builder: (_, __, shell) => BuyerShell(navigationShell: shell),
        branches: [
          StatefulShellBranch(routes: [
            GoRoute(
              path: '/buyer',
              builder: (_, __) => const BuyerRequestsScreen(),
              routes: [
                GoRoute(
                  path: 'requests/:id',
                  builder: (_, state) => BuyerRequestDetailScreen(
                    key: ValueKey(state.pathParameters['id']),
                    requestId: state.pathParameters['id']!,
                  ),
                ),
              ],
            ),
          ]),
          StatefulShellBranch(routes: [
            GoRoute(path: '/buyer/updates', builder: (_, __) => const BuyerNotificationsScreen()),
          ]),
          StatefulShellBranch(routes: [
            GoRoute(path: '/buyer/account', builder: (_, __) => const BuyerAccountScreen()),
          ]),
        ],
      ),
      GoRoute(path: '/submissions', builder: (_, __) => const SubmissionShell()),
      GoRoute(path: '/collector/setup-profile', builder: (_, __) => const CollectorProfileSetupScreen()),
      StatefulShellRoute.indexedStack(
        builder: (_, __, shell) => CollectorShell(navigationShell: shell),
        branches: [
          StatefulShellBranch(routes: [
            GoRoute(
              path: '/collector',
              builder: (_, __) => const JobListScreen(),
              routes: [
                GoRoute(
                  path: 'jobs/:id',
                  builder: (_, state) =>
                      JobDetailScreen(key: ValueKey(state.pathParameters['id']), jobId: state.pathParameters['id']!),
                ),
              ],
            ),
          ]),
          StatefulShellBranch(routes: [
            GoRoute(path: '/collector/profile', builder: (_, __) => const CollectorProfileScreen()),
          ]),
        ],
      ),
      StatefulShellRoute.indexedStack(
        builder: (_, __, shell) => WarehouseShell(navigationShell: shell),
        branches: [
          StatefulShellBranch(routes: [
            GoRoute(path: '/warehouse', builder: (_, __) => const WarehouseHomeScreen()),
          ]),
          StatefulShellBranch(routes: [
            GoRoute(
              path: '/warehouse/receive',
              builder: (_, state) => ReceiveScreen(
                initialTab: switch (state.uri.queryParameters['tab']) {
                  'extra' => ReceiveTab.extra,
                  _ => ReceiveTab.job,
                },
              ),
            ),
          ]),
          StatefulShellBranch(routes: [
            GoRoute(
              path: '/warehouse/inventory',
              builder: (_, state) {
                final name = state.uri.queryParameters['status'];
                final status = InventoryStatus.values.where((s) => s.apiName == name).firstOrNull;
                return InventoryListScreen(initialStatus: status);
              },
              routes: [
                GoRoute(
                  path: ':id',
                  builder: (_, state) => ItemDetailScreen(
                    key: ValueKey(state.pathParameters['id']),
                    itemId: state.pathParameters['id']!,
                  ),
                ),
              ],
            ),
            GoRoute(path: '/warehouse/materials', builder: (_, __) => const MaterialStockScreen()),
          ]),
          StatefulShellBranch(routes: [
            GoRoute(path: '/warehouse/scan', builder: (_, __) => const ScanScreen()),
          ]),
        ],
      ),
    ],
  );
});

Future<String> _corporateHome(Ref ref) async {
  try {
    await ref.read(buyerRequestsProvider.future);
    return '/buyer';
  } catch (error) {
    return apiErrorStatus(error) == 404 ? '/submissions' : '/unavailable';
  }
}
