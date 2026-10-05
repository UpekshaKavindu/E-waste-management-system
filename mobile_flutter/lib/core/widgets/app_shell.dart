import 'dart:ui';

import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../theme/app_colors.dart';
import '../theme/app_theme.dart';
import 'app_background.dart';
import 'layout.dart';

class ShellDestination {
  const ShellDestination(this.label, this.icon);

  final String label;
  final IconData icon;
}

/// App frame shared by the warehouse and collector apps: the web app's background everywhere, a
/// glass bottom bar on phones and a glass side rail (like the web sidebar) on tablets and wide screens.
class AppShell extends StatelessWidget {
  const AppShell({super.key, required this.navigationShell, required this.destinations, required this.sectionLabel});

  final StatefulNavigationShell navigationShell;
  final List<ShellDestination> destinations;

  /// Heading above the side-rail links, e.g. "PROCESSING & INVENTORY".
  final String sectionLabel;

  void _go(int index) => navigationShell.goBranch(index, initialLocation: index == navigationShell.currentIndex);

  @override
  Widget build(BuildContext context) {
    final wide = MediaQuery.sizeOf(context).width >= 900;
    return Scaffold(
      extendBody: true,
      body: Stack(
        children: [
          const AppBackground(),
          SafeArea(
            bottom: wide,
            child: wide
                ? Row(
                    children: [
                      _SideRail(
                        destinations: destinations,
                        sectionLabel: sectionLabel,
                        current: navigationShell.currentIndex,
                        onSelect: _go,
                      ),
                      Expanded(child: navigationShell),
                    ],
                  )
                : navigationShell,
          ),
        ],
      ),
      bottomNavigationBar:
          wide ? null : _BottomBar(destinations: destinations, current: navigationShell.currentIndex, onSelect: _go),
    );
  }
}

class _BottomBar extends StatelessWidget {
  const _BottomBar({required this.destinations, required this.current, required this.onSelect});

  final List<ShellDestination> destinations;
  final int current;
  final ValueChanged<int> onSelect;

  @override
  Widget build(BuildContext context) {
    return SafeArea(
      minimum: const EdgeInsets.fromLTRB(12, 0, 12, 12),
      child: ClipRRect(
        borderRadius: BorderRadius.circular(AppRadius.card),
        child: BackdropFilter(
          filter: ImageFilter.blur(sigmaX: 18, sigmaY: 18),
          child: Container(
            height: 68,
            decoration: BoxDecoration(
              color: Colors.white.withValues(alpha: 0.72),
              borderRadius: BorderRadius.circular(AppRadius.card),
              border: Border.all(color: AppColors.glassBorder),
            ),
            child: Row(
              children: [
                for (var i = 0; i < destinations.length; i++)
                  Expanded(
                    child: _NavItem(
                      destination: destinations[i],
                      selected: i == current,
                      onTap: () => onSelect(i),
                      vertical: true,
                    ),
                  ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

class _SideRail extends StatelessWidget {
  const _SideRail(
      {required this.destinations, required this.sectionLabel, required this.current, required this.onSelect});

  final List<ShellDestination> destinations;
  final String sectionLabel;
  final int current;
  final ValueChanged<int> onSelect;

  @override
  Widget build(BuildContext context) {
    return Container(
      width: 232,
      margin: const EdgeInsets.all(12),
      child: ClipRRect(
        borderRadius: BorderRadius.circular(AppRadius.card),
        child: BackdropFilter(
          filter: ImageFilter.blur(sigmaX: 18, sigmaY: 18),
          child: Container(
            padding: const EdgeInsets.all(16),
            decoration: BoxDecoration(
              color: AppColors.glassFill,
              borderRadius: BorderRadius.circular(AppRadius.card),
              border: Border.all(color: AppColors.glassBorder),
            ),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                const BrandMark(showTagline: true),
                const SizedBox(height: 20),
                Text(sectionLabel, style: AppText.label.copyWith(fontSize: 10, fontWeight: FontWeight.w700)),
                const SizedBox(height: 8),
                for (var i = 0; i < destinations.length; i++)
                  Padding(
                    padding: const EdgeInsets.only(bottom: 4),
                    child: _NavItem(destination: destinations[i], selected: i == current, onTap: () => onSelect(i)),
                  ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

/// Nav entry: the selected one gets the green gradient, like the web sidebar's active link.
class _NavItem extends StatelessWidget {
  const _NavItem({required this.destination, required this.selected, required this.onTap, this.vertical = false});

  final ShellDestination destination;
  final bool selected;
  final VoidCallback onTap;
  final bool vertical;

  @override
  Widget build(BuildContext context) {
    final color = selected ? (vertical ? AppColors.mint700 : Colors.white) : AppColors.ink800;
    final icon = Icon(destination.icon, size: vertical ? 22 : 16, color: color);
    final label = Text(
      destination.label,
      style: TextStyle(
          fontSize: vertical ? 11 : 14, fontWeight: selected ? FontWeight.w700 : FontWeight.w500, color: color),
    );

    return Semantics(
      selected: selected,
      button: true,
      label: destination.label,
      child: InkWell(
        onTap: onTap,
        borderRadius: BorderRadius.circular(AppRadius.input),
        child: vertical
            ? Column(
                mainAxisAlignment: MainAxisAlignment.center,
                children: [
                  AnimatedContainer(
                    duration: const Duration(milliseconds: 180),
                    padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 4),
                    decoration: BoxDecoration(
                      color: selected ? AppColors.mint100 : Colors.transparent,
                      borderRadius: BorderRadius.circular(999),
                    ),
                    child: icon,
                  ),
                  const SizedBox(height: 4),
                  label,
                ],
              )
            : Container(
                padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
                decoration: BoxDecoration(
                  gradient: selected ? AppColors.primaryGradient : null,
                  borderRadius: BorderRadius.circular(AppRadius.input),
                ),
                child: Row(children: [icon, const SizedBox(width: 10), label]),
              ),
      ),
    );
  }
}

/// Scroll container every shell page uses: padding, pull-to-refresh, room for the bottom bar.
class ShellPage extends StatelessWidget {
  const ShellPage({super.key, required this.children, this.onRefresh});

  final List<Widget> children;
  final Future<void> Function()? onRefresh;

  @override
  Widget build(BuildContext context) {
    final list = ListView(
      physics: const AlwaysScrollableScrollPhysics(),
      padding: const EdgeInsets.fromLTRB(16, 16, 16, 110),
      children: [ResponsiveCenter(child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: children))],
    );
    return onRefresh == null ? list : RefreshIndicator(onRefresh: onRefresh!, color: AppColors.mint600, child: list);
  }
}
