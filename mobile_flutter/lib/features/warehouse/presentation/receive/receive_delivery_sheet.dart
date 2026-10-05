import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';

import '../../../../core/network/api_error.dart';
import '../../../../core/theme/app_colors.dart';
import '../../../../core/theme/app_theme.dart';
import '../../../../core/utils/format.dart';
import '../../../../core/widgets/app_button.dart';
import '../../../../core/widgets/app_sheet.dart';
import '../../../../core/widgets/feedback.dart';
import '../../../../core/widgets/form_fields.dart';
import '../../../../core/widgets/glass_card.dart';
import '../../../../core/widgets/layout.dart';
import '../../application/warehouse_providers.dart';
import '../../data/warehouse_models.dart';

/// A collector's delivery (the web JobReceiveForm): tick the jobs they brought, then for each job go
/// through its items — how many came, what each is, what it weighs. Every item brought becomes its own
/// inventory item (a lot for several units); each job still gets one payment. Saved together. Pops with
/// an inventory item id when the worker taps one on the success screen, or null.
class ReceiveDeliverySheet extends ConsumerStatefulWidget {
  const ReceiveDeliverySheet({super.key, required this.collectorName, required this.jobs});

  final String collectorName;

  /// The collector's completed jobs that are waiting to be received (all the same collector).
  final List<ReceivableJob> jobs;

  @override
  ConsumerState<ReceiveDeliverySheet> createState() => _ReceiveDeliverySheetState();
}

/// One submission item / CSV row: units brought (0 = not brought), its type, the whole row's weight.
class _ItemEntry {
  _ItemEntry(ReceivableJobItem item, {String? weight})
      : received = item.quantity,
        itemType = item.suggestedItemType,
        weight = TextEditingController(text: weight ?? '');

  int received;
  String? itemType;
  final TextEditingController weight;
}

class _JobEntry {
  _JobEntry(ReceivableJob job, {required this.selected})
      : items = {
          for (final item in job.items)
            item.submissionItemId: _ItemEntry(
              item,
              // A one-item job weighs what the collector reported; the worker only corrects it.
              weight: job.items.length == 1 ? job.reportedWeightKg?.toString() : null,
            ),
        },
        wholeWeight = TextEditingController(text: job.reportedWeightKg?.toString() ?? ''),
        wholeType = job.suggestedItemType;

  bool selected;
  final Map<String, _ItemEntry> items;

  // Only for a job whose submission has no items: weighed and typed as one.
  final TextEditingController wholeWeight;
  String? wholeType;

  void dispose() {
    wholeWeight.dispose();
    for (final i in items.values) {
      i.weight.dispose();
    }
  }
}

class _ReceiveDeliverySheetState extends ConsumerState<ReceiveDeliverySheet> {
  // A collector with a single job almost always brought it, so it starts ticked.
  late final Map<String, _JobEntry> _entries = {
    for (final j in widget.jobs) j.jobId: _JobEntry(j, selected: widget.jobs.length == 1),
  };
  String? _locationId;
  bool _submitting = false;
  bool _submitted = false;
  String? _error;
  DeliveryResult? _result;

  @override
  void dispose() {
    for (final e in _entries.values) {
      e.dispose();
    }
    super.dispose();
  }

  List<ReceivableJob> get _selected => widget.jobs.where((j) => _entries[j.jobId]!.selected).toList();

  List<String> get _problems {
    final selected = _selected;
    final problems = <String>[if (selected.isEmpty) 'Tick at least one job the collector brought.'];
    for (final (i, job) in selected.indexed) {
      final entry = _entries[job.jobId]!;
      final label = 'Job ${i + 1}';
      if (job.items.isEmpty) {
        if (entry.wholeType == null) problems.add('$label: choose what it is.');
        if (!((parseDecimal(entry.wholeWeight.text) ?? 0) > 0)) problems.add('$label: enter the weight.');
        continue;
      }
      if (entry.items.values.every((e) => e.received == 0)) {
        problems.add('$label: nothing is marked as brought — untick the job instead.');
      }
      for (final item in job.items) {
        final e = entry.items[item.submissionItemId]!;
        if (e.received == 0) continue;
        if (e.itemType == null) problems.add('$label · ${item.itemName}: choose what it is.');
        if (!((parseDecimal(e.weight.text) ?? 0) > 0)) problems.add('$label · ${item.itemName}: enter the weight.');
      }
    }
    if (_locationId == null) problems.add('Choose where to put it.');
    return problems;
  }

  DeliveryJobInput _lineFor(ReceivableJob job) {
    final entry = _entries[job.jobId]!;
    if (job.items.isEmpty) {
      return DeliveryJobInput.whole(
        jobId: job.jobId,
        verifiedWeightKg: parseDecimal(entry.wholeWeight.text)!,
        itemType: entry.wholeType!,
      );
    }
    return DeliveryJobInput.items(jobId: job.jobId, items: [
      for (final item in job.items)
        DeliveryItemInput(
          submissionItemId: item.submissionItemId,
          receivedQuantity: entry.items[item.submissionItemId]!.received,
          itemType: entry.items[item.submissionItemId]!.itemType,
          verifiedWeightKg: parseDecimal(entry.items[item.submissionItemId]!.weight.text) ?? 0,
        ),
    ]);
  }

  Future<void> _submit() async {
    setState(() => _submitted = true);
    if (_problems.isNotEmpty) return;
    setState(() {
      _submitting = true;
      _error = null;
    });
    try {
      final result = await ref.read(warehouseApiProvider).receiveDelivery(
            collectorId: widget.jobs.first.collectorId,
            warehouseLocationId: _locationId!,
            jobs: [for (final j in _selected) _lineFor(j)],
          );
      if (mounted) setState(() => _result = result);
    } catch (e) {
      if (mounted) setState(() => _error = apiErrorMessage(e, 'The delivery could not be saved. Nothing was received.'));
    } finally {
      if (mounted) setState(() => _submitting = false);
    }
  }

  void _setAll(bool selected) => setState(() {
        for (final e in _entries.values) {
          e.selected = selected;
        }
      });

  @override
  Widget build(BuildContext context) {
    final result = _result;
    if (result != null) return _success(result);

    final locations = ref.watch(warehouseLocationsProvider);
    final itemTypes = ref.watch(itemTypesProvider);

    // Default the destination to the receiving bay once the locations arrive.
    if (_locationId == null && locations.value != null && locations.value!.isNotEmpty) {
      final all = locations.value!;
      _locationId = (all.where((l) => l.name.toLowerCase().contains('receiv')).firstOrNull ?? all.first).id;
    }

    final count = _selected.length;
    final allTicked = count == widget.jobs.length;

    return AppSheet(
      title: 'Receive from ${widget.collectorName}',
      subtitle: widget.jobs.length == 1
          ? 'Check each item, then save.'
          : 'Tick the jobs they brought, then check each item.',
      busy: _submitting,
      footer: [
        AppButton.secondary(label: 'Cancel', onPressed: _submitting ? null : () => Navigator.pop(context)),
        AppButton(
          label: _submitting
              ? 'Saving…'
              : count <= 1
                  ? 'Receive job'
                  : 'Receive $count jobs',
          icon: LucideIcons.check,
          loading: _submitting,
          onPressed: count == 0 ? null : _submit,
        ),
      ],
      body: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          if (widget.jobs.length > 1)
            Row(
              children: [
                Expanded(child: _StepLabel(number: 1, text: 'Which jobs did they bring?')),
                TextButton(
                  onPressed: () => _setAll(!allTicked),
                  child: Text(allTicked ? 'Untick all' : 'Tick all',
                      style: const TextStyle(color: AppColors.mint700, fontWeight: FontWeight.w600)),
                ),
              ],
            )
          else
            const _StepLabel(number: 1, text: 'Check the job'),
          const SizedBox(height: 8),
          for (final job in widget.jobs)
            _JobEntryCard(
              job: job,
              entry: _entries[job.jobId]!,
              itemTypes: itemTypes,
              onChanged: () => setState(() {}),
              onRetryItemTypes: () => ref.invalidate(itemTypesProvider),
            ),
          const SizedBox(height: 12),
          const _StepLabel(number: 2, text: 'Where are you putting it?'),
          const SizedBox(height: 8),
          switch (locations) {
            AsyncError(:final error) => ErrorMessage(
                message: apiErrorMessage(error, 'Failed to load locations.'),
                onRetry: () => ref.invalidate(warehouseLocationsProvider),
              ),
            _ => AppDropdown<String>(
                value: _locationId,
                hint: 'Loading…',
                enabled: locations.hasValue,
                items: [
                  for (final l in locations.value ?? const <WarehouseLocation>[]) DropdownMenuItem(value: l.id, child: Text(l.name)),
                ],
                onChanged: (v) => setState(() => _locationId = v),
              ),
          },
          if (_submitted && _problems.isNotEmpty) ...[const SizedBox(height: 16), ProblemList(problems: _problems)],
          if (_error != null) ...[const SizedBox(height: 16), ErrorMessage(message: _error!)],
        ],
      ),
    );
  }

  Widget _success(DeliveryResult result) {
    final n = result.jobs.length;
    return AppSheet(
      title: 'Received',
      footer: [AppButton(label: 'Done', expand: true, onPressed: () => Navigator.pop(context))],
      body: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          const SizedBox(height: 4),
          Center(
            child: Container(
              width: 64,
              height: 64,
              decoration: const BoxDecoration(color: AppColors.mint100, shape: BoxShape.circle),
              child: const Icon(LucideIcons.check, size: 32, color: AppColors.mint700),
            ),
          ),
          const SizedBox(height: 12),
          Text(
            '$n job${n == 1 ? '' : 's'} received from ${widget.collectorName}',
            textAlign: TextAlign.center,
            style: AppText.display(17),
          ),
          const SizedBox(height: 4),
          const Text('Every item brought is now in inventory. Tap one to open it.',
              textAlign: TextAlign.center, style: AppText.small),
          const SizedBox(height: 16),
          for (final j in result.jobs) ...[
            Padding(
              padding: const EdgeInsets.only(bottom: 6, top: 4),
              child: Text(
                'Job ${Format.shortId(j.jobId)} · ${j.receivedQuantity} of ${j.expectedQuantity} item'
                '${j.expectedQuantity == 1 ? '' : 's'} · ${Format.kg(j.verifiedWeightKg)}',
                style: AppText.label,
              ),
            ),
            for (final item in j.items)
              Padding(
                padding: const EdgeInsets.only(bottom: 8),
                child: Tile(
                  onTap: item.inventoryItemId == null ? null : () => Navigator.pop(context, item.inventoryItemId),
                  child: Row(
                    children: [
                      Icon(
                        item.inventoryItemId == null ? LucideIcons.packageX : LucideIcons.package,
                        size: 18,
                        color: item.inventoryItemId == null ? AppColors.amber700 : AppColors.mint700,
                      ),
                      const SizedBox(width: 10),
                      Expanded(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Text(
                              item.receivedQuantity > 1 ? '${item.itemName} × ${item.receivedQuantity}' : item.itemName,
                              style: AppText.strong,
                            ),
                            Text(
                              item.inventoryItemId == null
                                  ? 'Not brought'
                                  : [
                                      item.itemType ?? '',
                                      if (item.receivedQuantity < item.expectedQuantity)
                                        '${item.receivedQuantity} of ${item.expectedQuantity}',
                                    ].join(' · '),
                              style: AppText.small,
                            ),
                          ],
                        ),
                      ),
                      if (item.inventoryItemId != null) ...[
                        Text(Format.kg(item.verifiedWeightKg), style: AppText.strong),
                        const SizedBox(width: 6),
                        const Icon(LucideIcons.chevronRight, size: 16, color: AppColors.ink600),
                      ],
                    ],
                  ),
                ),
              ),
          ],
          const SizedBox(height: 8),
          Tile(
            color: AppColors.amber50,
            child: Row(
              children: [
                const Icon(LucideIcons.wallet, size: 18, color: AppColors.amber800),
                const SizedBox(width: 10),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text('Payment ${Format.money(result.totalPendingAmount)}',
                          style: const TextStyle(fontSize: 14, fontWeight: FontWeight.w700, color: AppColors.amber900)),
                      const SizedBox(height: 2),
                      const Text('The office pays the collector. You don\'t need to do anything.',
                          style: TextStyle(fontSize: 12, color: AppColors.amber800)),
                    ],
                  ),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

/// "1  Which jobs did they bring?" — numbered steps so the form reads top to bottom.
class _StepLabel extends StatelessWidget {
  const _StepLabel({required this.number, required this.text});

  final int number;
  final String text;

  @override
  Widget build(BuildContext context) {
    return Row(
      children: [
        Container(
          width: 24,
          height: 24,
          alignment: Alignment.center,
          decoration: const BoxDecoration(color: AppColors.mint600, shape: BoxShape.circle),
          child: Text('$number', style: const TextStyle(fontSize: 12, fontWeight: FontWeight.w700, color: Colors.white)),
        ),
        const SizedBox(width: 10),
        Flexible(child: Text(text, style: AppText.display(15))),
      ],
    );
  }
}

/// One job: tap to tick it; once ticked, its items (or, without items, one type and weight) appear underneath.
class _JobEntryCard extends StatelessWidget {
  const _JobEntryCard({
    required this.job,
    required this.entry,
    required this.itemTypes,
    required this.onChanged,
    required this.onRetryItemTypes,
  });

  final ReceivableJob job;
  final _JobEntry entry;
  final AsyncValue<List<String>> itemTypes;
  final VoidCallback onChanged;
  final VoidCallback onRetryItemTypes;

  @override
  Widget build(BuildContext context) {
    final selected = entry.selected;
    final reported = job.reportedWeightKg;

    return Padding(
      padding: const EdgeInsets.only(bottom: 10),
      child: AnimatedContainer(
        duration: const Duration(milliseconds: 180),
        decoration: BoxDecoration(
          color: selected ? Colors.white : AppColors.tileFill,
          borderRadius: BorderRadius.circular(AppRadius.tile),
          border: Border.all(color: selected ? AppColors.mint400 : AppColors.mint100, width: selected ? 1.5 : 1),
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Material(
              type: MaterialType.transparency,
              child: InkWell(
                borderRadius: BorderRadius.circular(AppRadius.tile),
                onTap: () {
                  entry.selected = !selected;
                  onChanged();
                },
                child: Padding(
                  padding: const EdgeInsets.all(14),
                  child: Row(
                    children: [
                      _Tick(selected: selected),
                      const SizedBox(width: 12),
                      Expanded(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Text(
                              job.pickupAddress.isEmpty ? 'No address recorded' : job.pickupAddress,
                              style: AppText.strong,
                              maxLines: 2,
                              overflow: TextOverflow.ellipsis,
                            ),
                            const SizedBox(height: 2),
                            Text(
                              [
                                if (job.items.isNotEmpty)
                                  '${job.items.length} item${job.items.length == 1 ? '' : 's'}'
                                      '${job.items.any((i) => i.quantity > 1) ? ' (${job.expectedUnits} units)' : ''}',
                                if (job.submissionCategory != null) job.submissionCategory!,
                                'Collector said ${reported == null ? 'no weight' : Format.kg(reported)}',
                              ].join(' · '),
                              style: AppText.small,
                            ),
                          ],
                        ),
                      ),
                    ],
                  ),
                ),
              ),
            ),
            AnimatedSize(
              duration: const Duration(milliseconds: 180),
              alignment: Alignment.topCenter,
              child: !selected
                  ? const SizedBox(width: double.infinity)
                  : Padding(
                      padding: const EdgeInsets.fromLTRB(14, 0, 14, 14),
                      child: job.items.isEmpty
                          ? _WholeJobFields(job: job, entry: entry, itemTypes: itemTypes, onChanged: onChanged, onRetryItemTypes: onRetryItemTypes)
                          : Column(
                              crossAxisAlignment: CrossAxisAlignment.stretch,
                              children: [
                                for (final item in job.items) ...[
                                  _ItemFields(
                                    item: item,
                                    entry: entry.items[item.submissionItemId]!,
                                    itemTypes: itemTypes,
                                    onChanged: onChanged,
                                    onRetryItemTypes: onRetryItemTypes,
                                  ),
                                  const SizedBox(height: 10),
                                ],
                                _JobTotal(job: job, entry: entry),
                              ],
                            ),
                    ),
            ),
          ],
        ),
      ),
    );
  }
}

const _sourceLabel = {
  'name': 'from the item name',
  'category': 'from the CSV category',
  'ai': "from the AI's classification",
  'submission': "from the submission's category",
};

Widget _typeDropdown({
  required AsyncValue<List<String>> itemTypes,
  required String? value,
  required bool enabled,
  required ValueChanged<String?> onChanged,
  required VoidCallback onRetry,
}) =>
    switch (itemTypes) {
      AsyncError(:final error) => ErrorMessage(message: apiErrorMessage(error, 'Failed to load item types.'), onRetry: onRetry),
      _ => AppDropdown<String>(
          value: value,
          hint: itemTypes.isLoading ? 'Loading…' : 'Choose…',
          enabled: enabled && itemTypes.hasValue,
          items: [for (final t in itemTypes.value ?? const <String>[]) DropdownMenuItem(value: t, child: Text(t))],
          onChanged: onChanged,
        ),
    };

/// One item of a ticked job: units brought, what it is (pre-filled when it matched), the row's weight.
class _ItemFields extends StatelessWidget {
  const _ItemFields({
    required this.item,
    required this.entry,
    required this.itemTypes,
    required this.onChanged,
    required this.onRetryItemTypes,
  });

  final ReceivableJobItem item;
  final _ItemEntry entry;
  final AsyncValue<List<String>> itemTypes;
  final VoidCallback onChanged;
  final VoidCallback onRetryItemTypes;

  @override
  Widget build(BuildContext context) {
    final brought = entry.received > 0;
    final suggested = item.suggestedItemType != null && entry.itemType == item.suggestedItemType && item.suggestionSource != null;

    return Tile(
      color: brought ? Colors.white : AppColors.ink50,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(item.itemName, style: AppText.strong),
                    if (item.description != null && item.description!.isNotEmpty)
                      Text(item.description!, style: AppText.small, maxLines: 2, overflow: TextOverflow.ellipsis),
                    if (!brought)
                      const Text('Not brought', style: TextStyle(fontSize: 12, fontWeight: FontWeight.w600, color: AppColors.amber700))
                    else if (entry.received < item.quantity)
                      Text('${item.quantity - entry.received} short',
                          style: const TextStyle(fontSize: 12, fontWeight: FontWeight.w600, color: AppColors.amber700)),
                  ],
                ),
              ),
              const SizedBox(width: 8),
              _Stepper(
                value: entry.received,
                max: item.quantity,
                onChanged: (v) {
                  entry.received = v;
                  onChanged();
                },
              ),
            ],
          ),
          if (brought) ...[
            const SizedBox(height: 10),
            LabeledField(
              label: 'What is it?',
              help: suggested
                  ? 'Suggested ${_sourceLabel[item.suggestionSource] ?? ''}'
                  : entry.itemType == null
                      ? 'No match — choose the closest type.'
                      : null,
              helpColor: suggested ? AppColors.mint700 : AppColors.amber700,
              child: _typeDropdown(
                itemTypes: itemTypes,
                value: entry.itemType,
                enabled: true,
                onChanged: (v) {
                  entry.itemType = v;
                  onChanged();
                },
                onRetry: onRetryItemTypes,
              ),
            ),
            const SizedBox(height: 10),
            LabeledField(
              label: entry.received > 1 ? 'Weight of all ${entry.received} together' : 'Weight on the scale',
              child: DecimalField(
                controller: entry.weight,
                hint: item.expectedWeightKg != null ? 'About ${Format.kg(item.expectedWeightKg!)}' : 'Weigh it here',
                onChanged: (_) => onChanged(),
              ),
            ),
          ],
        ],
      ),
    );
  }
}

/// − 3 / 5 + : units brought out of the units expected.
class _Stepper extends StatelessWidget {
  const _Stepper({required this.value, required this.max, required this.onChanged});

  final int value;
  final int max;
  final ValueChanged<int> onChanged;

  @override
  Widget build(BuildContext context) {
    return Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        IconButton(
          tooltip: 'One fewer',
          visualDensity: VisualDensity.compact,
          onPressed: value > 0 ? () => onChanged(value - 1) : null,
          icon: const Icon(LucideIcons.minus, size: 16),
        ),
        Text('$value / $max', style: AppText.strong),
        IconButton(
          tooltip: 'One more',
          visualDensity: VisualDensity.compact,
          onPressed: value < max ? () => onChanged(value + 1) : null,
          icon: const Icon(LucideIcons.plus, size: 16),
        ),
      ],
    );
  }
}

/// The job's verified total against what the collector reported.
class _JobTotal extends StatelessWidget {
  const _JobTotal({required this.job, required this.entry});

  final ReceivableJob job;
  final _JobEntry entry;

  @override
  Widget build(BuildContext context) {
    final brought = entry.items.values.where((e) => e.received > 0);
    final total = brought.fold<double>(0, (sum, e) => sum + (parseDecimal(e.weight.text) ?? 0));
    final units = brought.fold<int>(0, (sum, e) => sum + e.received);
    final reported = job.reportedWeightKg;
    return Wrap(
      spacing: 8,
      runSpacing: 6,
      crossAxisAlignment: WrapCrossAlignment.center,
      children: [
        Text('$units of ${job.expectedUnits} brought · ${Format.kg(total)} total', style: AppText.small),
        if (reported != null && total > 0) _DiffChip(diff: total - reported),
      ],
    );
  }
}

/// A job whose submission has no items: one type and one weight, as before.
class _WholeJobFields extends StatelessWidget {
  const _WholeJobFields({
    required this.job,
    required this.entry,
    required this.itemTypes,
    required this.onChanged,
    required this.onRetryItemTypes,
  });

  final ReceivableJob job;
  final _JobEntry entry;
  final AsyncValue<List<String>> itemTypes;
  final VoidCallback onChanged;
  final VoidCallback onRetryItemTypes;

  @override
  Widget build(BuildContext context) {
    final verified = parseDecimal(entry.wholeWeight.text);
    final reported = job.reportedWeightKg;
    final diff = verified != null && reported != null ? verified - reported : null;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        LabeledField(
          label: 'What is it?',
          help: job.submissionCategory != null && job.suggestedItemType == null
              ? 'The customer wrote “${job.submissionCategory}”. Pick the closest match.'
              : null,
          helpColor: AppColors.amber700,
          child: _typeDropdown(
            itemTypes: itemTypes,
            value: entry.wholeType,
            enabled: true,
            onChanged: (v) {
              entry.wholeType = v;
              onChanged();
            },
            onRetry: onRetryItemTypes,
          ),
        ),
        const SizedBox(height: 12),
        LabeledField(
          label: 'Weight on the scale',
          child: DecimalField(controller: entry.wholeWeight, hint: 'Weigh it here', onChanged: (_) => onChanged()),
        ),
        if (diff != null) ...[
          const SizedBox(height: 8),
          Align(alignment: Alignment.centerLeft, child: _DiffChip(diff: diff)),
        ],
      ],
    );
  }
}

class _Tick extends StatelessWidget {
  const _Tick({required this.selected});

  final bool selected;

  @override
  Widget build(BuildContext context) {
    return AnimatedContainer(
      duration: const Duration(milliseconds: 150),
      width: 28,
      height: 28,
      decoration: BoxDecoration(
        color: selected ? AppColors.mint600 : Colors.white,
        shape: BoxShape.circle,
        border: Border.all(color: selected ? AppColors.mint600 : AppColors.ink100, width: 2),
      ),
      child: selected ? const Icon(LucideIcons.check, size: 16, color: Colors.white) : null,
    );
  }
}

class _DiffChip extends StatelessWidget {
  const _DiffChip({required this.diff});

  final double diff;

  @override
  Widget build(BuildContext context) {
    final matches = diff.abs() < 0.005;
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
      decoration: BoxDecoration(
        color: matches ? AppColors.mint50 : AppColors.amber50,
        borderRadius: BorderRadius.circular(999),
      ),
      child: Text(
        matches ? 'Same as the collector said' : '${Format.signedKg(diff)} from what the collector said',
        style: TextStyle(fontSize: 12, fontWeight: FontWeight.w600, color: matches ? AppColors.mint800 : AppColors.amber800),
      ),
    );
  }
}
