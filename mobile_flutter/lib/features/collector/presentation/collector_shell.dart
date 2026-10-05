import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';

import '../../../core/widgets/app_shell.dart';

const _destinations = [
  ShellDestination('My Jobs', LucideIcons.truck),
  ShellDestination('Profile', LucideIcons.circleUser),
];

/// The collector app frame (see [AppShell]) — same side rail / bottom bar as the warehouse app.
class CollectorShell extends StatelessWidget {
  const CollectorShell({super.key, required this.navigationShell});

  final StatefulNavigationShell navigationShell;

  @override
  Widget build(BuildContext context) => AppShell(
        navigationShell: navigationShell,
        destinations: _destinations,
        sectionLabel: 'COLLECTION',
      );
}
