import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';

import '../../../core/widgets/app_shell.dart';

/// Scroll container every warehouse page uses — the shared [ShellPage].
typedef WarehousePage = ShellPage;

const _destinations = [
  ShellDestination('Home', LucideIcons.house),
  ShellDestination('Receive', LucideIcons.packagePlus),
  ShellDestination('Inventory', LucideIcons.boxes),
  ShellDestination('Scan', LucideIcons.scanQrCode),
];

/// The warehouse app frame (see [AppShell]).
class WarehouseShell extends StatelessWidget {
  const WarehouseShell({super.key, required this.navigationShell});

  final StatefulNavigationShell navigationShell;

  @override
  Widget build(BuildContext context) => AppShell(
        navigationShell: navigationShell,
        destinations: _destinations,
        sectionLabel: 'PROCESSING & INVENTORY',
      );
}
