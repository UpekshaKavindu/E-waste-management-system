import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';

import '../../../../core/network/api_error.dart';
import '../../../../core/theme/app_colors.dart';
import '../../../../core/theme/app_theme.dart';
import '../../../../core/widgets/app_sheet.dart';
import '../../../../core/widgets/feedback.dart';
import '../../../../core/widgets/glass_card.dart';
import '../../../../core/widgets/layout.dart';
import '../../application/warehouse_providers.dart';
import '../../data/warehouse_models.dart';
import '../warehouse_shell.dart';
import '../../../../core/widgets/pill_tabs.dart';
import 'extra_waste_form.dart';
import 'receive_delivery_sheet.dart';

enum ReceiveTab { job, extra }

/// Receive waste at the dock: a collector's delivery of completed jobs, or an extra-waste
/// drop-off — each with its own receipt and payment, exactly like the web Receive page.
class ReceiveScreen extends StatefulWidget {
  const ReceiveScreen({super.key, this.initialTab = ReceiveTab.job});

  final ReceiveTab initialTab;

  @override
  State<ReceiveScreen> createState() => _ReceiveScreenState();
}

class _ReceiveScreenState extends State<ReceiveScreen> {
  late ReceiveTab _tab = widget.initialTab;

  @override
  void didUpdateWidget(covariant ReceiveScreen oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.initialTab != widget.initialTab) _tab = widget.initialTab;
  }

  @override
  Widget build(BuildContext context) {
    return switch (_tab) {
      ReceiveTab.job => _JobCollectionTab(header: _header()),
      ReceiveTab.extra => WarehousePage(children: [_header(), const ExtraWasteForm()]),
    };
  }

  Widget _header() => Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          const PageHeader(
            title: 'Receive waste',
            subtitle: 'Weigh in what collectors bring to the warehouse.',
            icon: LucideIcons.packagePlus,
          ),
          PillTabs<ReceiveTab>(
            value: _tab,
            options: const {ReceiveTab.job: 'Jobs', ReceiveTab.extra: 'Extra waste'},
            onChanged: (t) => setState(() => _tab = t),
          ),
          const SizedBox(height: 16),
        ],
      );
}

/// Completed jobs waiting at the dock, grouped by the collector who brought them: the worker
/// picks the collector standing in front of them, then ticks the jobs in one sheet.
class _JobCollectionTab extends ConsumerWidget {
  const _JobCollectionTab({required this.header});

  final Widget header;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final jobs = ref.watch(receivableJobsProvider);

    return WarehousePage(
      onRefresh: () => ref.refresh(receivableJobsProvider.future),
      children: [
        header,
        switch (jobs) {
          AsyncData(:final value) when value.isEmpty => const GlassCard(
              child: EmptyState(
                icon: LucideIcons.truck,
                title: 'Nothing to receive right now',
                description: 'When a collector finishes a pickup, they will show up here.',
              ),
            ),
          AsyncData(:final value) => Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                const Padding(
                  padding: EdgeInsets.only(left: 4, bottom: 10),
                  child: Text('Who is delivering? Tap their name.', style: AppText.strong),
                ),
                for (final group in _byCollector(value)) _CollectorCard(jobs: group),
              ],
            ),
          AsyncError(:final error) => ErrorMessage(
              message: apiErrorMessage(error, 'Failed to load the jobs waiting to be received.'),
              onRetry: () => ref.invalidate(receivableJobsProvider),
            ),
          _ => const GlassCard(child: LoadingState(label: 'Loading…')),
        },
      ],
    );
  }

  static List<List<ReceivableJob>> _byCollector(List<ReceivableJob> jobs) {
    final groups = <String, List<ReceivableJob>>{};
    for (final j in jobs) {
      groups.putIfAbsent(j.collectorId, () => []).add(j);
    }
    return groups.values.toList()..sort((a, b) => _name(a.first).toLowerCase().compareTo(_name(b.first).toLowerCase()));
  }
}

String _name(ReceivableJob job) {
  final name = job.collectorName;
  return name == null || name.isEmpty ? 'Collector ${job.collectorId.substring(0, 8)}' : name;
}

class _CollectorCard extends ConsumerWidget {
  const _CollectorCard({required this.jobs});

  /// Jobs waiting from one collector.
  final List<ReceivableJob> jobs;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final first = jobs.first;
    final name = _name(first);
    final vehicle = first.collectorVehicleType;

    return Padding(
      padding: const EdgeInsets.only(bottom: 10),
      child: GlassCard(
        padding: const EdgeInsets.all(16),
        onTap: () async {
          final itemId = await showAppSheet<String>(
            context,
            builder: (_) => ReceiveDeliverySheet(collectorName: name, jobs: jobs),
          );
          ref.invalidate(receivableJobsProvider);
          ref.invalidate(warehouseSummaryProvider);
          if (itemId != null && context.mounted) context.go('/warehouse/inventory/$itemId');
        },
        child: Row(
          children: [
            Container(
              width: 46,
              height: 46,
              alignment: Alignment.center,
              decoration: const BoxDecoration(color: AppColors.mint100, shape: BoxShape.circle),
              child: Text(
                name.characters.first.toUpperCase(),
                style: AppText.display(18, color: AppColors.mint800),
              ),
            ),
            const SizedBox(width: 14),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(name, style: AppText.display(16)),
                  if (vehicle != null && vehicle.isNotEmpty) ...[
                    const SizedBox(height: 2),
                    Text(vehicle, style: AppText.small),
                  ],
                ],
              ),
            ),
            Container(
              padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 6),
              decoration: BoxDecoration(color: AppColors.amber100, borderRadius: BorderRadius.circular(999)),
              child: Text(
                '${jobs.length} job${jobs.length == 1 ? '' : 's'}',
                style: const TextStyle(fontSize: 13, fontWeight: FontWeight.w700, color: AppColors.amber800),
              ),
            ),
            const SizedBox(width: 6),
            const Icon(LucideIcons.chevronRight, size: 20, color: AppColors.ink600),
          ],
        ),
      ),
    );
  }
}
