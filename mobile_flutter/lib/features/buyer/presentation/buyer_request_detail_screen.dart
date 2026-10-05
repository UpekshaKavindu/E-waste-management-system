import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';

import '../../../core/network/api_error.dart';
import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_theme.dart';
import '../../../core/utils/format.dart';
import '../../../core/widgets/app_button.dart';
import '../../../core/widgets/feedback.dart';
import '../../../core/widgets/glass_card.dart';
import '../../../core/widgets/layout.dart';
import '../../notifications/notification_bell.dart';
import '../application/buyer_providers.dart';
import '../data/buyer_models.dart';
import 'buyer_shell.dart';
import 'widgets/request_status.dart';

/// One material request in full: how far it has got, the plan and order it produced,
/// the note the matcher left, and the one action the buyer can take (cancel).
class BuyerRequestDetailScreen extends ConsumerWidget {
  const BuyerRequestDetailScreen({super.key, required this.requestId});

  final String requestId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final data = ref.watch(buyerRequestProvider(requestId));

    return BuyerPage(
      onRefresh: () => ref.refresh(buyerRequestProvider(requestId).future),
      children: [
        Align(
          alignment: Alignment.centerLeft,
          child: TextButton.icon(
            onPressed: () => context.canPop() ? context.pop() : context.go('/buyer'),
            icon: const Icon(LucideIcons.arrowLeft, size: 14, color: AppColors.mint700),
            label: const Text('Requests', style: TextStyle(color: AppColors.mint700, fontWeight: FontWeight.w600)),
          ),
        ),
        const SizedBox(height: 4),
        switch (data) {
          AsyncData(:final value) => _RequestBody(request: value),
          AsyncError(:final error) => ErrorMessage(
              message: error is RequestNotFound
                  ? 'This request is no longer available. It may have been removed since you opened it.'
                  : apiErrorMessage(error, 'Failed to load this request.'),
              onRetry: () => ref.invalidate(buyerRequestProvider(requestId)),
            ),
          _ => const GlassCard(child: LoadingState(label: 'Loading request…')),
        },
      ],
    );
  }
}

class _RequestBody extends ConsumerWidget {
  const _RequestBody({required this.request});

  final MaterialRequest request;

  Future<void> _cancel(BuildContext context, WidgetRef ref) async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (dialogContext) => AlertDialog(
        title: const Text('Cancel this request?'),
        content: Text(
          'We will stop looking for ${Format.kg(request.quantityKg)} of ${request.materialType}. '
          'You can always raise a new request later.',
        ),
        actions: [
          TextButton(onPressed: () => Navigator.of(dialogContext).pop(false), child: const Text('Keep it')),
          FilledButton(
            onPressed: () => Navigator.of(dialogContext).pop(true),
            style: FilledButton.styleFrom(backgroundColor: AppColors.red600),
            child: const Text('Cancel request'),
          ),
        ],
      ),
    );
    if (confirmed != true || !context.mounted) return;

    try {
      await cancelBuyerRequest(ref, request.id);
      if (!context.mounted) return;
      ScaffoldMessenger.of(context)
        ..hideCurrentSnackBar()
        ..showSnackBar(const SnackBar(content: Text('Request cancelled.')));
    } catch (error) {
      if (!context.mounted) return;
      ScaffoldMessenger.of(context)
        ..hideCurrentSnackBar()
        ..showSnackBar(SnackBar(content: Text(apiErrorMessage(error, 'Could not cancel this request.'))));
    }
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final stage = request.stage;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        PageHeader(
          title: request.materialType,
          subtitle: 'Requested ${Format.dateOf(request.createdAt)}',
          icon: LucideIcons.package,
          actions: const [NotificationBell(), SizedBox(width: 10)],
        ),
        GlassCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Row(
                children: [
                  Text('Progress', style: AppText.label),
                  const Spacer(),
                  RequestStatusPill(stage),
                ],
              ),
              const SizedBox(height: 14),
              RequestStepper(stage),
            ],
          ),
        ),
        const SizedBox(height: 12),
        _StageExplanation(stage: stage),
        const SizedBox(height: 12),
        _PlanAndOrderCard(request: request),
        if (request.lastMatchingNote != null && request.lastMatchingNote!.isNotEmpty) ...[
          const SizedBox(height: 12),
          Notice(
            tone: stage.needsAttention ? NoticeTone.warning : NoticeTone.info,
            title: stage.needsAttention ? 'Why this stalled' : 'From the matching desk',
            message: request.lastMatchingNote,
          ),
        ],
        const SizedBox(height: 12),
        _RequestFacts(request: request),
        if (request.canCancel) ...[
          const SizedBox(height: 14),
          AppButton.danger(
            label: 'Cancel request',
            icon: LucideIcons.ban,
            expand: true,
            onPressed: () => _cancel(context, ref),
          ),
        ],
      ],
    );
  }
}


/// Plain-language "what is happening / what happens next" banner.
class _StageExplanation extends StatelessWidget {
  const _StageExplanation({required this.stage});

  final RequestStage stage;

  @override
  Widget build(BuildContext context) {
    final (tone, title) = switch (stage) {
      RequestStage.planGenerationFailed => (NoticeTone.error, 'Stopped'),
      RequestStage.fulfilled => (NoticeTone.success, 'All done'),
      RequestStage.cancelled => (NoticeTone.warning, 'Cancelled'),
      RequestStage.planGenerated => (NoticeTone.info, 'Waiting on staff'),
      _ => (NoticeTone.info, 'What is happening'),
    };
    return Notice(tone: tone, title: title, message: stage.explanation);
  }
}

/// The plan and order this request produced. Buyers may not read the commercial plan's
/// commercial detail (that is staff-only on the API), so this shows the linkage, the
/// stage, and who to talk to — not the pricing rationale.
class _PlanAndOrderCard extends StatelessWidget {
  const _PlanAndOrderCard({required this.request});

  final MaterialRequest request;

  @override
  Widget build(BuildContext context) {
    final stage = request.stage;
    return GlassCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const SectionTitle('Plan & order', icon: LucideIcons.fileCheck),
          _LinkRow(
            icon: LucideIcons.fileCheck,
            title: request.hasPlan ? 'Commercial plan' : 'No plan yet',
            subtitle: request.hasPlan
                ? 'Plan ${Format.shortId(request.commercialPlanId)} · ${stage.label}'
                : switch (stage) {
                    RequestStage.waitingForPrice => 'A plan needs an approved price first.',
                    RequestStage.generatingPlan => 'Being prepared now.',
                    RequestStage.cancelled => 'Not created — this request was cancelled.',
                    _ => 'Created once matching finds priced stock.',
                  },
            ready: request.hasPlan,
          ),
          const SizedBox(height: 12),
          _LinkRow(
            icon: LucideIcons.packageCheck,
            title: request.hasOrder ? 'Sales order' : 'No order yet',
            subtitle: request.hasOrder
                ? 'Order ${Format.shortId(request.salesOrderId)}'
                : switch (stage) {
                    RequestStage.planGenerated => 'Created the moment staff approve the plan.',
                    RequestStage.fulfilled => 'This request has been fulfilled.',
                    _ => 'Not created yet.',
                  },
            ready: request.hasOrder,
          ),
          if (request.hasPlan || request.hasOrder) ...[
            const SizedBox(height: 14),
            Text(
              'Pricing, margin and routing decisions are made by the sales desk. '
              'You will be notified here the moment your plan is approved or rejected.',
              style: AppText.small,
            ),
          ],
        ],
      ),
    );
  }
}


/// One row in the plan & order card: icon, title, subtitle and a ready/pending marker.
class _LinkRow extends StatelessWidget {
  const _LinkRow({required this.icon, required this.title, required this.subtitle, required this.ready});

  final IconData icon;
  final String title;
  final String subtitle;
  final bool ready;

  @override
  Widget build(BuildContext context) => Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Container(
            width: 34,
            height: 34,
            alignment: Alignment.center,
            decoration: BoxDecoration(
              color: ready ? AppColors.mint100 : AppColors.ink50,
              borderRadius: BorderRadius.circular(AppRadius.input),
            ),
            child: Icon(icon, size: 16, color: ready ? AppColors.mint700 : AppColors.ink600),
          ),
          const SizedBox(width: 12),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    Expanded(child: Text(title, style: AppText.strong)),
                    if (ready) const Icon(LucideIcons.circleCheck, size: 15, color: AppColors.mint600),
                  ],
                ),
                const SizedBox(height: 2),
                Text(subtitle, style: AppText.small),
              ],
            ),
          ),
        ],
      );
}

/// The request's own record: what was asked for, when, and the identifiers the sales
/// desk quotes back.
class _RequestFacts extends StatelessWidget {
  const _RequestFacts({required this.request});

  final MaterialRequest request;

  @override
  Widget build(BuildContext context) => GlassCard(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            const SectionTitle('Request details', icon: LucideIcons.clipboardCheck),
            _Fact(label: 'Material', value: request.materialType),
            _Fact(label: 'Quantity', value: Format.kg(request.quantityKg)),
            _Fact(label: 'Requested', value: '${Format.dateTimeOf(request.createdAt)} (${Format.relative(request.createdAt)})'),
            _Fact(
              label: 'Last update',
              value: request.updatedAt == null
                  ? 'Not changed since it was raised'
                  : '${Format.dateTimeOf(request.updatedAt!)} (${Format.relative(request.updatedAt!)})',
            ),
            _Fact(label: 'Current status', value: request.stage.label),
            _Fact(label: 'Request ID', value: Format.shortId(request.id)),
            if (request.commercialPlanId != null) _Fact(label: 'Plan ID', value: Format.shortId(request.commercialPlanId)),
            if (request.salesOrderId != null) _Fact(label: 'Order ID', value: Format.shortId(request.salesOrderId)),
          ],
        ),
      );
}

class _Fact extends StatelessWidget {
  const _Fact({required this.label, required this.value});

  final String label;
  final String value;

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.symmetric(vertical: 5),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            SizedBox(width: 108, child: Text(label.toUpperCase(), style: AppText.label)),
            Expanded(
              child:
                  Text(value, textAlign: TextAlign.right, style: const TextStyle(fontSize: 13, color: AppColors.ink800)),
            ),
          ],
        ),
      );
}
