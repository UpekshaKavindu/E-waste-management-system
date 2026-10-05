import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';

import '../../../core/auth/auth_controller.dart';
import '../../../core/network/api_error.dart';
import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_theme.dart';
import '../../../core/utils/format.dart';
import '../../../core/widgets/app_button.dart';
import '../../../core/widgets/feedback.dart';
import '../../../core/widgets/glass_card.dart';
import '../../../core/widgets/layout.dart';
import '../../../core/widgets/pill_tabs.dart';
import '../../notifications/notification_bell.dart';
import '../application/buyer_providers.dart';
import '../data/buyer_models.dart';
import 'buyer_shell.dart';
import 'request_material_sheet.dart';
import 'widgets/request_status.dart';

/// Which slice of the portfolio the list is showing.
enum RequestFilter {
  all('All'),
  open('In progress'),
  plans('Plan ready'),
  fulfilled('Fulfilled'),
  closed('Cancelled');

  const RequestFilter(this.label);

  final String label;

  bool matches(MaterialRequest request) => switch (this) {
        RequestFilter.all => true,
        RequestFilter.open => request.stage.isOpen && !request.hasPlan,
        RequestFilter.plans => request.hasPlan || request.stage == RequestStage.orderPlaced,
        RequestFilter.fulfilled => request.stage == RequestStage.fulfilled,
        RequestFilter.closed => request.stage == RequestStage.cancelled,
      };
}

/// The buyer's material-request portfolio: what's open, what's been planned, and a way into
/// the full view of any single request. Pull to refresh; the unread bell is in the header.
class BuyerRequestsScreen extends ConsumerStatefulWidget {
  const BuyerRequestsScreen({super.key});

  @override
  ConsumerState<BuyerRequestsScreen> createState() => _BuyerRequestsScreenState();
}

class _BuyerRequestsScreenState extends ConsumerState<BuyerRequestsScreen> {
  RequestFilter _filter = RequestFilter.all;

  @override
  Widget build(BuildContext context) {
    final requests = ref.watch(buyerRequestsProvider);
    final stats = ref.watch(buyerRequestStatsProvider);
    final user = ref.watch(authControllerProvider).user;

    final visible = requests.value?.where(_filter.matches).toList() ?? const <MaterialRequest>[];

    return BuyerPage(
      onRefresh: () => ref.refresh(buyerRequestsProvider.future),
      children: [
        PageHeader(
          title: 'Buyer portal',
          subtitle: user == null ? null : 'Signed in as ${user.fullName}',
          icon: LucideIcons.building2,
          actions: const [NotificationBell(), SizedBox(width: 10)],
        ),
        AppButton(
          label: 'Request material',
          icon: LucideIcons.packagePlus,
          expand: true,
          onPressed: () async {
            if (await showRequestMaterialSheet(context) && mounted) {
              setState(() => _filter = RequestFilter.all);
            }
          },
        ),
        const SizedBox(height: 18),
        _StatsRow(stats: stats),
        const SizedBox(height: 20),
        PillTabs<RequestFilter>(
          value: _filter,
          options: {for (final f in RequestFilter.values) f: f.label},
          onChanged: (value) => setState(() => _filter = value),
        ),
        const SizedBox(height: 16),
        switch (requests) {
          AsyncData(:final value) when value.isEmpty => GlassCard(
              child: EmptyState(
                icon: LucideIcons.packagePlus,
                title: 'No material requests yet',
                description: 'Ask for a recovered material and follow it here from matching through to fulfilment.',
                action: AppButton(
                  label: 'Request material',
                  icon: LucideIcons.packagePlus,
                  small: true,
                  onPressed: () => showRequestMaterialSheet(context),
                ),
              ),
            ),
          AsyncData() when visible.isEmpty => GlassCard(
              child: EmptyState(
                icon: LucideIcons.inbox,
                title: 'Nothing in "${_filter.label}"',
                description: 'Try another filter to see the rest of your requests.',
              ),
            ),
          AsyncData() => Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                SectionTitle('${_filter.label} requests', trailing: Text('${visible.length}', style: AppText.small)),
                for (final request in visible) _RequestCard(request: request),
              ],
            ),
          AsyncError(:final error) => ErrorMessage(
              message: apiErrorMessage(error, 'Could not load your material requests.'),
              onRetry: () => ref.invalidate(buyerRequestsProvider),
            ),
          _ => const GlassCard(child: LoadingState(label: 'Loading your requestsâ€¦')),
        },
      ],
    );
  }
}
/// Portfolio summary: three counters above the list, so a buyer can see at a glance how much
/// is still in flight before scrolling.
class _StatsRow extends StatelessWidget {
  const _StatsRow({required this.stats});

  final BuyerRequestStats stats;

  @override
  Widget build(BuildContext context) => Row(
        children: [
          Expanded(
            child: _StatTile(
              icon: LucideIcons.loader,
              label: 'In progress',
              value: '${stats.open}',
              hint: Format.kg(stats.openKg),
              color: AppColors.amber700,
            ),
          ),
          const SizedBox(width: 10),
          Expanded(
            child: _StatTile(
              icon: LucideIcons.fileCheck,
              label: 'Plan ready',
              value: '${stats.planned}',
              color: AppColors.violet800,
            ),
          ),
          const SizedBox(width: 10),
          Expanded(
            child: _StatTile(
              icon: LucideIcons.circleCheck,
              label: 'Fulfilled',
              value: '${stats.fulfilled}',
              color: AppColors.mint700,
            ),
          ),
        ],
      );
}

class _StatTile extends StatelessWidget {
  const _StatTile({required this.icon, required this.label, required this.value, required this.color, this.hint});

  final IconData icon;
  final String label;
  final String value;
  final Color color;
  final String? hint;

  @override
  Widget build(BuildContext context) => Tile(
        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 14),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Icon(icon, size: 16, color: color),
            const SizedBox(height: 8),
            Text(value, style: AppText.display(22, color: color)),
            Text(label, style: AppText.label.copyWith(fontSize: 10)),
            if (hint != null) ...[
              const SizedBox(height: 2),
              Text(hint!, style: AppText.small.copyWith(fontSize: 11)),
            ],
          ],
        ),
      );
}

/// One request in the list. Tapping opens the full view.
class _RequestCard extends StatelessWidget {
  const _RequestCard({required this.request});

  final MaterialRequest request;

  @override
  Widget build(BuildContext context) {
    final stage = request.stage;
    return GlassCard(
      margin: const EdgeInsets.only(bottom: 10),
      padding: const EdgeInsets.all(16),
      onTap: () => context.go('/buyer/requests/${request.id}'),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(request.materialType, style: AppText.display(16)),
                    const SizedBox(height: 3),
                    Text(
                      '${Format.kg(request.quantityKg)} · raised ${Format.dateOf(request.createdAt)}',
                      style: AppText.small,
                    ),
                  ],
                ),
              ),
              const SizedBox(width: 10),
              RequestStatusPill(stage, compact: true),
            ],
          ),
          const SizedBox(height: 12),
          Text(
            stage.explanation,
            maxLines: 2,
            overflow: TextOverflow.ellipsis,
            style: AppText.body,
          ),
          if (request.lastMatchingNote != null && request.lastMatchingNote!.isNotEmpty) ...[
            const SizedBox(height: 8),
            Text(request.lastMatchingNote!, style: AppText.small.copyWith(fontStyle: FontStyle.italic)),
          ],
          const SizedBox(height: 10),
          Row(
            children: [
              if (request.hasPlan) const _LinkBadge(label: 'Plan', icon: LucideIcons.fileCheck),
              if (request.hasOrder) const _LinkBadge(label: 'Order', icon: LucideIcons.packageCheck),
              const Spacer(),
              Text('View details', style: AppText.small.copyWith(color: AppColors.mint700, fontWeight: FontWeight.w600)),
              const Icon(LucideIcons.chevronRight, size: 14, color: AppColors.mint700),
            ],
          ),
        ],
      ),
    );
  }
}

/// Small "this request has a linked record" marker (plan / order).
class _LinkBadge extends StatelessWidget {
  const _LinkBadge({required this.label, required this.icon});

  final String label;
  final IconData icon;

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.only(right: 8),
        child: Container(
          padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
          decoration: BoxDecoration(color: AppColors.mint50, borderRadius: BorderRadius.circular(999)),
          child: Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              Icon(icon, size: 11, color: AppColors.mint700),
              const SizedBox(width: 4),
              Text(label, style: const TextStyle(color: AppColors.mint800, fontSize: 11, fontWeight: FontWeight.w600)),
            ],
          ),
        ),
      );
}

