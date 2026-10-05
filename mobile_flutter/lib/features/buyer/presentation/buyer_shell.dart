import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';

import '../../../core/widgets/app_shell.dart';

/// Scroll container every buyer page uses — the shared [ShellPage].
typedef BuyerPage = ShellPage;

const _destinations = [
  ShellDestination('Requests', LucideIcons.clipboardList),
  ShellDestination('Updates', LucideIcons.bell),
  ShellDestination('Account', LucideIcons.circleUser),
];

/// The buyer portal app frame (see [AppShell]) — same glass bottom bar on phones and side
/// rail on wide screens as the warehouse and collector apps.
class BuyerShell extends StatelessWidget {
  const BuyerShell({super.key, required this.navigationShell});

  final StatefulNavigationShell navigationShell;

  @override
  Widget build(BuildContext context) => AppShell(
        navigationShell: navigationShell,
        destinations: _destinations,
        sectionLabel: 'BUYER PORTAL',
      );
}
