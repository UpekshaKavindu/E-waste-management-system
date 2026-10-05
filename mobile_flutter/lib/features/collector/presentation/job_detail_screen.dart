import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';
import 'package:url_launcher/url_launcher.dart';

import '../../../core/network/api_error.dart';
import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_theme.dart';
import '../../../core/utils/format.dart';
import '../../../core/widgets/app_background.dart';
import '../../../core/widgets/app_button.dart';
import '../../../core/widgets/feedback.dart';
import '../../../core/widgets/glass_card.dart';
import '../application/collector_providers.dart';
import '../data/collector_models.dart';
import 'complete_job_sheet.dart';
import 'job_route_map.dart';
import 'job_list_screen.dart' show jobStatusLabel;

final _jobDetailProvider = FutureProvider.autoDispose.family<CollectionJob, String>(
  (ref, jobId) => ref.watch(collectorApiProvider).getById(jobId),
);

/// Accept/reject a new job, navigate then start an accepted one, or complete an in-progress one.
class JobDetailScreen extends ConsumerStatefulWidget {
  const JobDetailScreen({super.key, required this.jobId});

  final String jobId;

  @override
  ConsumerState<JobDetailScreen> createState() => _JobDetailScreenState();
}

class _JobDetailScreenState extends ConsumerState<JobDetailScreen> {
  bool _busy = false;

  void _refresh() {
    ref.invalidate(_jobDetailProvider(widget.jobId));
    ref.invalidate(myActiveJobsProvider);
    ref.invalidate(myCompletedJobsProvider);
    ref.invalidate(jobRouteProvider(widget.jobId));
    ref.invalidate(jobInfoProvider(widget.jobId));
  }

  Future<void> _run(Future<void> Function() action) async {
    setState(() => _busy = true);
    try {
      await action();
      _refresh();
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(apiErrorMessage(e, 'That didn\'t work. Please try again.'))));
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _accept() => _run(() => ref.read(collectorApiProvider).accept(widget.jobId));

  Future<void> _reject() async {
    final reason = await showDialog<String>(
      context: context,
      builder: (context) => _RejectDialog(),
    );
    if (reason == null) return; // dialog cancelled
    await _run(() => ref.read(collectorApiProvider).reject(widget.jobId, reason: reason.isEmpty ? null : reason));
  }

  Future<void> _navigateAndStart(CollectionJob job) async {
    if (job.pickupLatitude != null && job.pickupLongitude != null) {
      final uri = Uri.parse(
        'https://www.google.com/maps/dir/?api=1&destination=${job.pickupLatitude},${job.pickupLongitude}&travelmode=driving',
      );
      await launchUrl(uri, mode: LaunchMode.externalApplication);
    }
    await _run(() => ref.read(collectorApiProvider).start(widget.jobId));
  }

  Future<void> _openCompleteSheet() async {
    final done = await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      useSafeArea: true,
      backgroundColor: Colors.transparent,
      builder: (context) => CompleteJobSheet(jobId: widget.jobId),
    );
    if (done == true) {
      _refresh();
      if (mounted) context.pop();
    }
  }

  @override
  Widget build(BuildContext context) {
    final jobAsync = ref.watch(_jobDetailProvider(widget.jobId));

    return Scaffold(
      appBar: AppBar(backgroundColor: Colors.transparent, elevation: 0, title: const Text('Job details')),
      body: Stack(
        children: [
          const AppBackground(),
          SafeArea(
            child: switch (jobAsync) {
              AsyncData(:final value) => _JobBody(job: value, busy: _busy, onAccept: _accept, onReject: _reject, onNavigate: _navigateAndStart, onComplete: _openCompleteSheet),
              AsyncError(:final error) => Padding(
                  padding: const EdgeInsets.all(16),
                  child: ErrorMessage(message: apiErrorMessage(error, 'Failed to load this job.'), onRetry: _refresh),
                ),
              _ => const LoadingState(),
            },
          ),
        ],
      ),
    );
  }
}

class _JobBody extends ConsumerWidget {
  const _JobBody({required this.job, required this.busy, required this.onAccept, required this.onReject, required this.onNavigate, required this.onComplete});

  final CollectionJob job;
  final bool busy;
  final VoidCallback onAccept;
  final VoidCallback onReject;
  final void Function(CollectionJob) onNavigate;
  final VoidCallback onComplete;

  // Jobs still ahead of the collector get the route map; finished ones don't need it.
  static const _routed = {JobStatus.assigned, JobStatus.accepted, JobStatus.inProgress};

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final showRoute = _routed.contains(job.status);
    final routeAsync = showRoute ? ref.watch(jobRouteProvider(job.jobId)) : null;
    final route = routeAsync?.value;
    // The live route is from where the collector is now; the job's own figures were taken at matching time.
    final distanceKm = route?.distanceKm ?? job.estimatedDistanceKm;
    final etaMinutes = route?.durationMinutes ?? job.estimatedEtaMinutes;

    return ListView(
      padding: const EdgeInsets.fromLTRB(16, 16, 16, 110), // room for the shell's bottom bar
      children: [
        if (showRoute) ...[
          JobRouteMap(job: job, route: route, loading: routeAsync?.isLoading ?? false),
          const SizedBox(height: 16),
        ],
        GlassCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              if (showRoute) ...[
                Text('Route to pickup', style: AppText.display(18)),
                if (etaMinutes != null) ...[
                  const SizedBox(height: 2),
                  Text('About $etaMinutes min away by road', style: AppText.small),
                ],
                const SizedBox(height: 14),
                _FromTo(
                  from: route?.originLatitude == null ? 'Your location (unknown)' : 'Your location',
                  to: job.pickupAddress,
                ),
              ] else
                Row(
                  children: [
                    const Icon(LucideIcons.mapPin, size: 16, color: AppColors.mint600),
                    const SizedBox(width: 8),
                    Expanded(child: Text(job.pickupAddress, style: AppText.display(16))),
                  ],
                ),
              const SizedBox(height: 12),
              Wrap(
                spacing: 16,
                runSpacing: 6,
                children: [
                  if (job.requiredCapacityKg != null) _Fact(LucideIcons.weight, '${job.requiredCapacityKg} kg'),
                  if (distanceKm != null) _Fact(LucideIcons.route, '${distanceKm.toStringAsFixed(1)} km'),
                  if (etaMinutes != null) _Fact(LucideIcons.clock, '~$etaMinutes min'),
                  if (job.measuredWeightKg != null) _Fact(LucideIcons.packageCheck, 'Collected ${job.measuredWeightKg} kg'),
                ],
              ),
              const SizedBox(height: 6),
              Text(
                job.status == JobStatus.completed
                    ? 'Status: ${job.receivedAtWarehouse ? 'Delivered to warehouse' : 'Completed — still in your vehicle'}'
                    : 'Status: ${jobStatusLabel(job.status)}',
                style: AppText.small,
              ),
            ],
          ),
        ),
        ...switch (ref.watch(jobInfoProvider(job.jobId))) {
          AsyncData(:final value) => [
              if (showRoute) ...[const SizedBox(height: 12), _CustomerCard(job: job, info: value)],
              if (value.paymentAmount != null) ...[const SizedBox(height: 12), _PaymentCard(info: value)],
            ],
          // Contact and payment are extras — the job itself still works if they fail to load.
          _ => const <Widget>[],
        },
        const SizedBox(height: 20),
        switch (job.status) {
          JobStatus.assigned => Row(
              children: [
                Expanded(child: AppButton(label: 'Accept', icon: LucideIcons.check, loading: busy, onPressed: onAccept)),
                const SizedBox(width: 12),
                Expanded(child: AppButton.danger(label: 'Reject', icon: LucideIcons.x, loading: busy, onPressed: onReject)),
              ],
            ),
          JobStatus.accepted => AppButton(
              label: 'Navigate',
              icon: LucideIcons.navigation,
              expand: true,
              loading: busy,
              onPressed: () => onNavigate(job),
            ),
          JobStatus.inProgress => AppButton(
              label: 'Complete job',
              icon: LucideIcons.packageCheck,
              expand: true,
              loading: busy,
              onPressed: onComplete,
            ),
          _ => const SizedBox.shrink(),
        },
      ],
    );
  }
}

/// Who to meet at the pickup. The phone number is only released once the job is accepted.
class _CustomerCard extends StatelessWidget {
  const _CustomerCard({required this.job, required this.info});

  final CollectionJob job;
  final CollectorJobInfo info;

  @override
  Widget build(BuildContext context) {
    final phone = info.customerPhone;
    final name = info.customerName ?? 'Customer';
    return GlassCard(
      padding: const EdgeInsets.all(16),
      child: !info.contactAvailable
          ? const Row(
              children: [
                Icon(LucideIcons.lock, size: 16, color: AppColors.ink600),
                SizedBox(width: 10),
                Expanded(
                  child: Text("The customer's name and phone number appear here once you accept the job.",
                      style: AppText.small),
                ),
              ],
            )
          : Row(
              children: [
                Container(
                  width: 44,
                  height: 44,
                  alignment: Alignment.center,
                  decoration: const BoxDecoration(color: AppColors.mint100, shape: BoxShape.circle),
                  child: Text(Format.initials(name), style: AppText.display(15, color: AppColors.mint800)),
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(name, style: AppText.strong),
                      Text(phone == null || phone.isEmpty ? 'No phone number given' : phone, style: AppText.small),
                    ],
                  ),
                ),
                if (phone != null && phone.isNotEmpty)
                  Material(
                    color: AppColors.mint600,
                    shape: const CircleBorder(),
                    child: IconButton(
                      tooltip: 'Call $name',
                      onPressed: () => launchUrl(Uri(scheme: 'tel', path: phone.replaceAll(RegExp(r'\s'), ''))),
                      icon: const Icon(LucideIcons.phone, size: 18, color: Colors.white),
                    ),
                  ),
              ],
            ),
    );
  }
}

/// What the company pays for the job: the real payment once received, otherwise an estimate using
/// the warehouse formula, with its parts so the number can be checked.
class _PaymentCard extends StatelessWidget {
  const _PaymentCard({required this.info});

  final CollectorJobInfo info;

  @override
  Widget build(BuildContext context) {
    final estimate = info.paymentIsEstimate;
    final paid = info.paymentStatus == 'Paid';
    return GlassCard(
      padding: const EdgeInsets.all(16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              const Icon(LucideIcons.wallet, size: 16, color: AppColors.mint600),
              const SizedBox(width: 8),
              Expanded(child: Text(estimate ? "You'll earn about" : 'Your payment', style: AppText.small)),
              if (!estimate)
                Container(
                  padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
                  decoration: BoxDecoration(
                    color: paid ? AppColors.mint50 : AppColors.amber50,
                    borderRadius: BorderRadius.circular(999),
                  ),
                  child: Text(
                    paid ? 'Paid' : 'Pending',
                    style: TextStyle(
                      color: paid ? AppColors.mint700 : AppColors.amber900,
                      fontSize: 11,
                      fontWeight: FontWeight.w700,
                    ),
                  ),
                ),
            ],
          ),
          const SizedBox(height: 4),
          Text(Format.money(info.paymentAmount!), style: AppText.display(24)),
          if (estimate) ...[
            const SizedBox(height: 10),
            if (info.baseFee != null) _MoneyRow('Collection fee', info.baseFee!),
            if (info.weightAmount != null)
              _MoneyRow(
                info.estimateWeightKg != null && info.ratePerKg != null
                    ? 'Weight · ${Format.kg(info.estimateWeightKg!)} × ${Format.money(info.ratePerKg!)}'
                    : 'Weight',
                info.weightAmount!,
              ),
            if (info.distanceAmount != null)
              _MoneyRow(
                info.distanceKm != null ? 'Distance · ${info.distanceKm!.toStringAsFixed(1)} km' : 'Distance',
                info.distanceAmount!,
              ),
            const SizedBox(height: 6),
            const Text('Final amount uses the weight measured at the warehouse.', style: AppText.small),
          ],
        ],
      ),
    );
  }
}

class _MoneyRow extends StatelessWidget {
  const _MoneyRow(this.label, this.amount);

  final String label;
  final double amount;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 2),
      child: Row(
        children: [
          Expanded(child: Text(label, style: AppText.small)),
          Text(Format.money(amount), style: const TextStyle(fontSize: 12, color: AppColors.ink800, fontWeight: FontWeight.w600)),
        ],
      ),
    );
  }
}

/// "Your location" → pickup address, joined by a dashed line like a trip summary.
class _FromTo extends StatelessWidget {
  const _FromTo({required this.from, required this.to});

  final String from;
  final String to;

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Row(
          children: [
            Container(
              width: 18,
              height: 18,
              decoration: BoxDecoration(
                shape: BoxShape.circle,
                border: Border.all(color: AppColors.mint600, width: 2),
              ),
              alignment: Alignment.center,
              child: Container(
                width: 8,
                height: 8,
                decoration: const BoxDecoration(color: AppColors.mint600, shape: BoxShape.circle),
              ),
            ),
            const SizedBox(width: 12),
            Expanded(child: Text(from, style: AppText.body)),
          ],
        ),
        Padding(
          padding: const EdgeInsets.only(left: 8),
          child: Column(
            children: [
              for (var i = 0; i < 3; i++)
                Container(width: 2, height: 4, margin: const EdgeInsets.symmetric(vertical: 2), color: AppColors.ink100),
            ],
          ),
        ),
        Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const SizedBox(width: 18, child: Icon(LucideIcons.mapPin, size: 18, color: AppColors.ink900)),
            const SizedBox(width: 12),
            Expanded(child: Text(to, style: AppText.strong)),
          ],
        ),
      ],
    );
  }
}

class _Fact extends StatelessWidget {
  const _Fact(this.icon, this.text);

  final IconData icon;
  final String text;

  @override
  Widget build(BuildContext context) {
    return Row(
      mainAxisSize: MainAxisSize.min,
      children: [Icon(icon, size: 13, color: AppColors.ink600), const SizedBox(width: 4), Text(text, style: AppText.small)],
    );
  }
}

class _RejectDialog extends StatefulWidget {
  @override
  State<_RejectDialog> createState() => _RejectDialogState();
}

class _RejectDialogState extends State<_RejectDialog> {
  final _reason = TextEditingController();

  @override
  void dispose() {
    _reason.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return AlertDialog(
      title: const Text('Reject this job?'),
      content: TextField(
        controller: _reason,
        decoration: const InputDecoration(hintText: 'Reason (optional)'),
        maxLines: 2,
      ),
      actions: [
        TextButton(onPressed: () => Navigator.of(context).pop(), child: const Text('Cancel')),
        FilledButton(
          onPressed: () => Navigator.of(context).pop(_reason.text.trim()),
          style: FilledButton.styleFrom(backgroundColor: AppColors.red600),
          child: const Text('Reject'),
        ),
      ],
    );
  }
}
