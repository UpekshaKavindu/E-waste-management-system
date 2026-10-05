import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';
import 'package:qr_flutter/qr_flutter.dart';

import '../../../../core/auth/auth_controller.dart';
import '../../../../core/network/api_error.dart';
import '../../../../core/theme/app_colors.dart';
import '../../../../core/theme/app_theme.dart';
import '../../../../core/utils/format.dart';
import '../../../../core/widgets/app_button.dart';
import '../../../../core/widgets/app_sheet.dart';
import '../../../../core/widgets/feedback.dart';
import '../../../../core/widgets/glass_card.dart';
import '../../../../core/widgets/layout.dart';
import '../../application/warehouse_providers.dart';
import '../../data/processing_enums.dart';
import '../../data/warehouse_models.dart';
import '../warehouse_shell.dart';
import '../widgets/badges.dart';
import 'item_actions.dart';

/// One inventory item: where it is in the flow, the actions allowed right now, its QR label and
/// its full history. Actions open as bottom sheets; the item reloads after each one.
class ItemDetailScreen extends ConsumerWidget {
  const ItemDetailScreen({super.key, required this.itemId});

  final String itemId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final data = ref.watch(itemDetailProvider(itemId));

    return WarehousePage(
      onRefresh: () => ref.refresh(itemDetailProvider(itemId).future),
      children: [
        Align(
          alignment: Alignment.centerLeft,
          child: TextButton.icon(
            onPressed: () => context.canPop() ? context.pop() : context.go('/warehouse/inventory'),
            icon: const Icon(LucideIcons.arrowLeft, size: 14, color: AppColors.mint700),
            label: const Text('Inventory', style: TextStyle(color: AppColors.mint700, fontWeight: FontWeight.w600)),
          ),
        ),
        const SizedBox(height: 4),
        switch (data) {
          AsyncData(:final value) => _ItemBody(data: value),
          AsyncError(:final error) => ErrorMessage(
              message: apiErrorStatus(error) == 404
                  ? 'This inventory item does not exist. If you scanned a label, check that it belongs to this warehouse.'
                  : apiErrorMessage(error, 'Failed to load this inventory item.'),
              onRetry: () => ref.invalidate(itemDetailProvider(itemId)),
            ),
          _ => const GlassCard(child: LoadingState(label: 'Loading item…')),
        },
      ],
    );
  }
}

class _ItemBody extends ConsumerWidget {
  const _ItemBody({required this.data});

  final ItemDetailData data;

  Future<void> _open(BuildContext context, WidgetRef ref, Widget sheet) async {
    final message = await showAppSheet<String>(context, builder: (_) => sheet);
    if (message == null) return;
    ref.invalidate(itemDetailProvider(data.item.id));
    ref.invalidate(warehouseSummaryProvider);
    if (context.mounted) {
      ScaffoldMessenger.of(context)
        ..hideCurrentSnackBar()
        ..showSnackBar(SnackBar(content: Text(message)));
    }
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final item = data.item;
    final status = item.status;
    final classification = item.classification;
    final manual = status.manualTransitions(classification?.category);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        // ---- header ------------------------------------------------------------
        GlassCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Expanded(
                    child: Text(item.quantity > 1 ? '${item.itemType} × ${item.quantity}' : item.itemType,
                        style: AppText.display(22)),
                  ),
                  Text(Format.kg(item.verifiedWeightKg), style: AppText.display(18, color: AppColors.mint700)),
                ],
              ),
              const SizedBox(height: 4),
              SelectableText(item.id, style: AppText.mono.copyWith(fontSize: 11, color: AppColors.ink600)),
              const SizedBox(height: 10),
              Wrap(
                spacing: 6,
                runSpacing: 6,
                children: [
                  StatusBadge(status),
                  CategoryBadge(classification?.category),
                  if (item.originType != null)
                    Pill(label: item.originType!.label, background: AppColors.ink100, foreground: AppColors.ink800),
                  if (item.kind != ItemKind.unit)
                    Pill(label: item.kind.label, background: AppColors.violet100, foreground: AppColors.violet800),
                ],
              ),
              const SizedBox(height: 18),
              StatusStepper(status),
            ],
          ),
        ),
        const SizedBox(height: 14),

        // ---- actions -------------------------------------------------------------
        GlassCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              const SectionTitle('Actions', icon: LucideIcons.listChecks),
              if (status.isTerminal) ...[
                Notice(
                  tone: status == InventoryStatus.onHold ? NoticeTone.error : NoticeTone.success,
                  message: status == InventoryStatus.onHold
                      ? 'This item is on hold (quarantined). Its status is final and cannot be released from here.'
                      : 'This item is ${status.label.toLowerCase()}. Its status is final.',
                ),
                const SizedBox(height: 12),
              ],
              if (manual.contains(InventoryStatus.sorting))
                _actionButton(
                  AppButton(
                    label: transitionButtonLabel(InventoryStatus.sorting),
                    icon: LucideIcons.listChecks,
                    expand: true,
                    onPressed: () => _open(context, ref, TransitionSheet(item: item, next: InventoryStatus.sorting)),
                  ),
                ),
              if (status.canDismantle)
                _actionButton(
                  AppButton.secondary(
                    label: 'Add dismantle step',
                    icon: LucideIcons.wrench,
                    expand: true,
                    onPressed: () => _open(context, ref, DismantleSheet(item: item)),
                  ),
                ),
              if (status.canClassify)
                _actionButton(
                  AppButton(
                    label: 'Classify item',
                    icon: LucideIcons.tag,
                    expand: true,
                    onPressed: () => _open(context, ref, ClassifySheet(item: item)),
                  ),
                ),
              if (status == InventoryStatus.classified)
                for (final next in manual)
                  _actionButton(
                    AppButton(
                      label: transitionButtonLabel(next),
                      icon: switch (next) {
                        InventoryStatus.readyForSale => LucideIcons.shoppingCart,
                        InventoryStatus.exportOnly => LucideIcons.ship,
                        _ => LucideIcons.shieldAlert,
                      },
                      variant: switch (next) {
                        InventoryStatus.onHold => AppButtonVariant.danger,
                        InventoryStatus.readyForSale => AppButtonVariant.primary,
                        _ => AppButtonVariant.secondary,
                      },
                      expand: true,
                      onPressed: () => _open(context, ref, TransitionSheet(item: item, next: next)),
                    ),
                  ),
              _actionButton(
                AppButton.secondary(
                  label: 'Move location',
                  icon: LucideIcons.mapPin,
                  expand: true,
                  onPressed: () => _open(context, ref, MoveLocationSheet(item: item)),
                ),
              ),
              if (status == InventoryStatus.sorting)
                const Text(
                  'Dismantle first if the item has parts to track separately, or classify it straight away.',
                  style: AppText.small,
                ),
            ],
          ),
        ),
        const SizedBox(height: 14),

        // ---- details -------------------------------------------------------------
        GlassCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              const SectionTitle('Details', icon: LucideIcons.info),
              _Fact('Location', item.currentLocationName),
              _Fact('Received', Format.dateTime(item.receivedAt)),
              _Fact('Origin', item.originType?.label ?? '—'),
              if (item.jobId != null) _Fact('Job', Format.shortId(item.jobId)),
              if (item.extraWasteReceiptId != null) _Fact('Receipt', Format.shortId(item.extraWasteReceiptId)),
              if (item.parentInventoryItemId != null)
                _Fact(
                  'Parent item',
                  Format.shortId(item.parentInventoryItemId),
                  onTap: () => context.go('/warehouse/inventory/${item.parentInventoryItemId}'),
                ),
            ],
          ),
        ),
        const SizedBox(height: 14),

        if (classification != null) ...[
          GlassCard(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                const SectionTitle('Classification', icon: LucideIcons.tag),
                _Fact('Category', classification.category?.label ?? '—'),
                if (classification.subCategory != null) _Fact('Sub-category', classification.subCategory!),
                _Fact('Source', classification.source?.label ?? '—'),
                if (classification.confidenceScore != null)
                  _Fact('Confidence', '${(classification.confidenceScore! * 100).round()}%'),
                _Fact('Final', classification.isFinal ? 'Yes' : 'No (provisional)'),
                _Fact('Classified', Format.dateTime(classification.classifiedAt)),
              ],
            ),
          ),
          const SizedBox(height: 14),
        ],

        for (final (title, icon, outputs) in [
          ('Dismantled components', LucideIcons.gitFork, item.children.where((c) => c.kind != ItemKind.material).toList()),
          ('Recovered materials', LucideIcons.packageOpen, item.children.where((c) => c.kind == ItemKind.material).toList()),
        ])
          if (outputs.isNotEmpty) ...[
            GlassCard(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  SectionTitle('$title (${outputs.length})', icon: icon),
                  for (final child in outputs)
                    Padding(
                      padding: const EdgeInsets.only(bottom: 8),
                      child: Tile(
                        onTap: () => context.go('/warehouse/inventory/${child.id}'),
                        child: Row(
                          children: [
                            Expanded(
                              child: Column(
                                crossAxisAlignment: CrossAxisAlignment.start,
                                children: [
                                  Text(child.itemType, style: AppText.strong),
                                  const SizedBox(height: 4),
                                  Text(Format.kg(child.verifiedWeightKg), style: AppText.small),
                                ],
                              ),
                            ),
                            StatusBadge(child.status),
                            const SizedBox(width: 4),
                            const Icon(LucideIcons.chevronRight, size: 16, color: AppColors.ink600),
                          ],
                        ),
                      ),
                    ),
                ],
              ),
            ),
            const SizedBox(height: 14),
          ],

        _QrCard(itemId: item.id, itemType: item.itemType),
        const SizedBox(height: 14),

        _HistoryCard(history: data.history),
      ],
    );
  }

  Widget _actionButton(Widget button) => Padding(padding: const EdgeInsets.only(bottom: 8), child: button);
}

class _Fact extends StatelessWidget {
  const _Fact(this.label, this.value, {this.onTap});

  final String label;
  final String value;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final row = Padding(
      padding: const EdgeInsets.symmetric(vertical: 6),
      child: Row(
        children: [
          SizedBox(width: 118, child: Text(label.toUpperCase(), style: AppText.label)),
          Expanded(
            child: Text(
              value,
              style: TextStyle(
                fontSize: 14,
                fontWeight: FontWeight.w600,
                color: onTap == null ? AppColors.ink900 : AppColors.mint700,
                decoration: onTap == null ? null : TextDecoration.underline,
              ),
            ),
          ),
        ],
      ),
    );
    return onTap == null ? row : InkWell(onTap: onTap, child: row);
  }
}

/// The item's QR label. It holds `EWI:{item id}`, the same value the Scan tab reads.
class _QrCard extends StatelessWidget {
  const _QrCard({required this.itemId, required this.itemType});

  final String itemId;
  final String itemType;

  void _showLarge(BuildContext context) {
    showDialog<void>(
      context: context,
      builder: (c) => Dialog(
        backgroundColor: Colors.white,
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(AppRadius.card)),
        child: Padding(
          padding: const EdgeInsets.all(24),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Text(itemType, style: AppText.display(18), textAlign: TextAlign.center),
              const SizedBox(height: 12),
              _qr(260),
              const SizedBox(height: 8),
              Text(itemId, style: AppText.mono.copyWith(fontSize: 11, color: AppColors.ink600), textAlign: TextAlign.center),
              const SizedBox(height: 16),
              AppButton.secondary(label: 'Close', onPressed: () => Navigator.pop(c)),
            ],
          ),
        ),
      ),
    );
  }

  Widget _qr(double size) => QrImageView(
        data: inventoryQrData(itemId),
        size: size,
        backgroundColor: Colors.white,
        eyeStyle: const QrEyeStyle(eyeShape: QrEyeShape.square, color: AppColors.ink900),
        dataModuleStyle: const QrDataModuleStyle(dataModuleShape: QrDataModuleShape.square, color: AppColors.ink900),
      );

  @override
  Widget build(BuildContext context) {
    return GlassCard(
      child: Row(
        children: [
          InkWell(
            onTap: () => _showLarge(context),
            child: Container(
              padding: const EdgeInsets.all(8),
              decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(AppRadius.tile)),
              child: _qr(96),
            ),
          ),
          const SizedBox(width: 16),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text('QR label', style: AppText.display(16)),
                const SizedBox(height: 4),
                const Text('Scan it from the Scan tab to open this item. Tap the code to show it large.', style: AppText.small),
                const SizedBox(height: 8),
                TextButton.icon(
                  style: TextButton.styleFrom(padding: EdgeInsets.zero),
                  onPressed: () {
                    Clipboard.setData(ClipboardData(text: itemId));
                    ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('Item ID copied.')));
                  },
                  icon: const Icon(LucideIcons.copy, size: 14, color: AppColors.mint700),
                  label: const Text('Copy item ID', style: TextStyle(color: AppColors.mint700, fontWeight: FontWeight.w600)),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

class _HistoryCard extends ConsumerWidget {
  const _HistoryCard({required this.history});

  final List<ProcessingLogEntry> history;

  static const _icons = {
    'Received': LucideIcons.inbox,
    'Sorting': LucideIcons.listChecks,
    'Dismantling': LucideIcons.wrench,
    'DismantleStep': LucideIcons.wrench,
    'Classified': LucideIcons.tag,
    'ReadyForSale': LucideIcons.shoppingCart,
    'ExportOnly': LucideIcons.ship,
    'OnHold': LucideIcons.shieldAlert,
    'LocationMoved': LucideIcons.mapPin,
  };

  static String _label(String action) {
    if (action == 'DismantleStep') return 'Dismantle step';
    if (action == 'LocationMoved') return 'Location moved';
    final status = InventoryStatus.values.where((s) => s.apiName == action).firstOrNull;
    return status == null ? action : 'Status → ${status.label}';
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final me = ref.watch(authControllerProvider).user?.userId;
    final newestFirst = history.reversed.toList();

    return GlassCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          const SectionTitle('History', icon: LucideIcons.route),
          if (newestFirst.isEmpty) const Text('No history recorded.', style: AppText.small),
          // The connector line is a layer behind the row rather than an IntrinsicHeight column:
          // intrinsic sizing measures wrapped text slightly differently and overflowed by a pixel.
          for (var i = 0; i < newestFirst.length; i++)
            Stack(
              children: [
                if (i < newestFirst.length - 1)
                  Positioned(left: 12, top: 26, bottom: 0, child: Container(width: 2, color: AppColors.mint100)),
                Row(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Container(
                      width: 26,
                      height: 26,
                      decoration: BoxDecoration(
                        color: AppColors.mint600,
                        shape: BoxShape.circle,
                        border: Border.all(color: Colors.white.withValues(alpha: 0.7), width: 3),
                      ),
                      child: Icon(_icons[newestFirst[i].action] ?? LucideIcons.circleCheck, size: 12, color: Colors.white),
                    ),
                    const SizedBox(width: 12),
                    Expanded(
                      child: Padding(
                        padding: const EdgeInsets.only(bottom: 16, top: 2),
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Text(_label(newestFirst[i].action), style: AppText.strong),
                            if (newestFirst[i].notes != null && newestFirst[i].notes!.isNotEmpty) ...[
                              const SizedBox(height: 2),
                              Text(newestFirst[i].notes!, style: const TextStyle(fontSize: 12, color: AppColors.ink800)),
                            ],
                            const SizedBox(height: 2),
                            Text(
                              '${Format.dateTime(newestFirst[i].performedAt)} · by '
                              '${newestFirst[i].performedByStaffId == me ? 'you' : 'staff ${Format.shortId(newestFirst[i].performedByStaffId)}'}',
                              style: const TextStyle(fontSize: 11, color: AppColors.ink600),
                            ),
                          ],
                        ),
                      ),
                    ),
                  ],
                ),
              ],
            ),
        ],
      ),
    );
  }
}
