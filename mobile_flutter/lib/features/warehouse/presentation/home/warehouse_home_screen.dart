import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';

import '../../../../core/auth/auth_controller.dart';
import '../../../../core/network/api_error.dart';
import '../../../../core/theme/app_colors.dart';
import '../../../../core/theme/app_theme.dart';
import '../../../../core/widgets/feedback.dart';
import '../../../../core/widgets/glass_card.dart';
import '../../../../core/widgets/greeting_header.dart';
import '../../../../core/widgets/layout.dart';
import '../../../notifications/notification_bell.dart';
import '../../application/warehouse_providers.dart';
import '../../data/processing_enums.dart';
import '../warehouse_shell.dart';
import '../widgets/badges.dart';
import '../widgets/inventory_tile.dart';

/// Warehouse home — the phone version of the web Processing dashboard: quick actions first
/// (what a worker on the floor does), then what needs attention, counts and recent items.
class WarehouseHomeScreen extends ConsumerWidget {
  const WarehouseHomeScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final user = ref.watch(authControllerProvider).user;
    final summary = ref.watch(warehouseSummaryProvider);

    return WarehousePage(
      onRefresh: () => ref.refresh(warehouseSummaryProvider.future),
      children: [
        GreetingHeader(
          name: user?.fullName ?? '',
          actions: [
            const NotificationBell(),
            IconButton(
              tooltip: 'Sign out',
              onPressed: () => _confirmSignOut(context, ref),
              icon: const Icon(LucideIcons.logOut, size: 20, color: AppColors.ink800),
            ),
          ],
        ),
        _QuickActions(receivableJobs: summary.value?.receivableJobs),
        const SizedBox(height: 16),
        switch (summary) {
          AsyncData(:final value) => _SummaryBody(summary: value),
          AsyncError(:final error) => ErrorMessage(
              message: apiErrorMessage(error, 'Failed to load the warehouse overview.'),
              onRetry: () => ref.invalidate(warehouseSummaryProvider),
            ),
          _ => const GlassCard(child: LoadingState(label: 'Loading overview…')),
        },
      ],
    );
  }

  Future<void> _confirmSignOut(BuildContext context, WidgetRef ref) async {
    final ok = await showDialog<bool>(
      context: context,
      builder: (c) => AlertDialog(
        backgroundColor: Colors.white,
        title: Text('Sign out?', style: AppText.display(18)),
        content: const Text('You will need to sign in again to use the warehouse app.'),
        actions: [
          TextButton(onPressed: () => Navigator.pop(c, false), child: const Text('Cancel')),
          TextButton(onPressed: () => Navigator.pop(c, true), child: const Text('Sign out')),
        ],
      ),
    );
    if (ok == true) await ref.read(authControllerProvider.notifier).signOut();
  }
}

class _QuickActions extends StatelessWidget {
  const _QuickActions({required this.receivableJobs});

  final int? receivableJobs;

  @override
  Widget build(BuildContext context) {
    final actions = [
      // Scan has its own tab in the bottom bar, so the fourth card is material stock instead.
      _ActionData('Receive jobs', "Weigh in a collector's delivery", LucideIcons.truck, '/warehouse/receive', receivableJobs),
      const _ActionData('Extra waste', 'Record a drop-off', LucideIcons.packagePlus, '/warehouse/receive?tab=extra', null),
      const _ActionData('Inventory', 'Sort, dismantle, classify', LucideIcons.boxes, '/warehouse/inventory', null),
      const _ActionData('Material stock', 'What is ready to sell', LucideIcons.packageOpen, '/warehouse/materials', null),
    ];
    return LayoutBuilder(
      builder: (context, constraints) {
        final columns = constraints.maxWidth >= 600 ? 4 : 2;
        const gap = 10.0;
        final width = (constraints.maxWidth - gap * (columns - 1)) / columns;
        return Wrap(
          spacing: gap,
          runSpacing: gap,
          children: [for (final a in actions) SizedBox(width: width, child: _ActionCard(data: a))],
        );
      },
    );
  }
}

class _ActionData {
  const _ActionData(this.title, this.subtitle, this.icon, this.route, this.badge);

  final String title;
  final String subtitle;
  final IconData icon;
  final String route;
  final int? badge;
}

class _ActionCard extends StatelessWidget {
  const _ActionCard({required this.data});

  final _ActionData data;

  @override
  Widget build(BuildContext context) {
    return GlassCard(
      padding: const EdgeInsets.all(14),
      onTap: () => context.go(data.route),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Container(
                width: 38,
                height: 38,
                decoration: BoxDecoration(gradient: AppColors.brandGradient, borderRadius: BorderRadius.circular(AppRadius.input)),
                child: Icon(data.icon, size: 18, color: Colors.white),
              ),
              const Spacer(),
              if ((data.badge ?? 0) > 0)
                Pill(label: '${data.badge}', background: AppColors.amber100, foreground: AppColors.amber800),
            ],
          ),
          const SizedBox(height: 10),
          Text(data.title, style: AppText.display(14)),
          const SizedBox(height: 2),
          Text(data.subtitle, style: AppText.small, maxLines: 2, overflow: TextOverflow.ellipsis),
        ],
      ),
    );
  }
}

class _SummaryBody extends StatelessWidget {
  const _SummaryBody({required this.summary});

  final WarehouseSummary summary;

  @override
  Widget build(BuildContext context) {
    final counts = summary.statusCounts;
    final received = counts[InventoryStatus.received] ?? 0;
    final onHold = counts[InventoryStatus.onHold] ?? 0;

    final attention = <Widget>[
      if (summary.receivableJobs > 0)
        _LinkNotice(
          tone: NoticeTone.info,
          text: '${_plural(summary.receivableJobs, 'completed job')} waiting to be received.',
          route: '/warehouse/receive',
        ),
      if (received > 0)
        _LinkNotice(
          tone: NoticeTone.info,
          text: '${_plural(received, 'item')} received and waiting to be sorted.',
          route: '/warehouse/inventory?status=${InventoryStatus.received.apiName}',
        ),
      if (onHold > 0)
        _LinkNotice(
          tone: NoticeTone.warning,
          text: '${_plural(onHold, 'item')} on hold (quarantined).',
          route: '/warehouse/inventory?status=${InventoryStatus.onHold.apiName}',
        ),
    ];

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        for (final n in attention) Padding(padding: const EdgeInsets.only(bottom: 8), child: n),
        if (attention.isNotEmpty) const SizedBox(height: 8),
        Row(
          children: [
            Expanded(child: _StatCard(label: 'Total items', value: summary.total, icon: LucideIcons.boxes)),
            const SizedBox(width: 10),
            Expanded(child: _StatCard(label: 'In progress', value: summary.inProgress, icon: LucideIcons.layers)),
            const SizedBox(width: 10),
            Expanded(child: _StatCard(label: 'Ready to hand off', value: summary.readyToHandOff, icon: LucideIcons.shoppingCart)),
          ],
        ),
        const SizedBox(height: 16),
        GlassCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              const SectionTitle('Inventory by status'),
              LayoutBuilder(
                builder: (context, constraints) {
                  final columns = constraints.maxWidth >= 560 ? 4 : 2;
                  const gap = 8.0;
                  final width = (constraints.maxWidth - gap * (columns - 1)) / columns;
                  return Wrap(
                    spacing: gap,
                    runSpacing: gap,
                    children: [
                      for (final status in InventoryStatus.values)
                        SizedBox(
                          width: width,
                          child: Tile(
                            padding: const EdgeInsets.all(12),
                            onTap: () => context.go('/warehouse/inventory?status=${status.apiName}'),
                            child: Column(
                              crossAxisAlignment: CrossAxisAlignment.start,
                              children: [
                                StatusBadge(status),
                                const SizedBox(height: 8),
                                Text('${counts[status] ?? 0}', style: AppText.display(22)),
                              ],
                            ),
                          ),
                        ),
                    ],
                  );
                },
              ),
            ],
          ),
        ),
        const SizedBox(height: 16),
        GlassCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              SectionTitle(
                'Recently received',
                trailing: TextButton(
                  onPressed: () => context.go('/warehouse/inventory'),
                  child: const Text('All inventory', style: TextStyle(color: AppColors.mint700, fontWeight: FontWeight.w600)),
                ),
              ),
              if (summary.recentItems.isEmpty)
                const EmptyState(
                  icon: LucideIcons.boxes,
                  title: 'No inventory yet',
                  description: 'Receive a job or an extra-waste drop-off to get started.',
                )
              else
                for (final item in summary.recentItems) InventoryTile(item: item),
            ],
          ),
        ),
      ],
    );
  }

  static String _plural(int n, String noun) => '$n $noun${n == 1 ? '' : 's'}';
}

class _LinkNotice extends StatelessWidget {
  const _LinkNotice({required this.tone, required this.text, required this.route});

  final NoticeTone tone;
  final String text;
  final String route;

  @override
  Widget build(BuildContext context) {
    return InkWell(
      onTap: () => context.go(route),
      borderRadius: BorderRadius.circular(AppRadius.tile),
      child: Notice(
        tone: tone,
        child: Row(
          children: [
            Expanded(child: Text(text, style: const TextStyle(fontWeight: FontWeight.w600))),
            const Icon(LucideIcons.chevronRight, size: 16),
          ],
        ),
      ),
    );
  }
}

class _StatCard extends StatelessWidget {
  const _StatCard({required this.label, required this.value, required this.icon});

  final String label;
  final int value;
  final IconData icon;

  @override
  Widget build(BuildContext context) {
    return GlassCard(
      padding: const EdgeInsets.all(14),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(icon, size: 18, color: AppColors.mint600),
          const SizedBox(height: 8),
          Text('$value', style: AppText.display(22)),
          const SizedBox(height: 2),
          Text(label.toUpperCase(), style: AppText.label.copyWith(fontSize: 9.5), maxLines: 2),
        ],
      ),
    );
  }
}
