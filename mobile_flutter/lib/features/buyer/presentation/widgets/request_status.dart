import 'package:flutter/material.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';

import '../../../../core/theme/app_colors.dart';
import '../../data/buyer_models.dart';

/// The five milestones a material request walks through, in order.
const requestMilestones = ['Submitted', 'Matching', 'Plan', 'Order', 'Fulfilled'];

/// Rounded status pill for a material request. Same colours as the web StatusBadge:
/// amber while still moving, mint once an order exists, green when fulfilled,
/// red when it stopped and needs attention, grey when cancelled.
class RequestStatusPill extends StatelessWidget {
  const RequestStatusPill(this.stage, {super.key, this.compact = false});

  final RequestStage stage;
  final bool compact;

  static (Color, Color, IconData) styleFor(RequestStage stage) => switch (stage) {
        RequestStage.waiting => (AppColors.sky100, AppColors.sky800, LucideIcons.inbox),
        RequestStage.waitingForPrice => (AppColors.amber100, AppColors.amber800, LucideIcons.tags),
        RequestStage.generatingPlan => (AppColors.amber100, AppColors.amber800, LucideIcons.loader),
        RequestStage.planGenerated => (AppColors.violet100, AppColors.violet800, LucideIcons.fileCheck),
        RequestStage.planGenerationFailed => (AppColors.red100, AppColors.red800, LucideIcons.triangleAlert),
        RequestStage.orderPlaced => (AppColors.mint100, AppColors.mint800, LucideIcons.packageCheck),
        RequestStage.fulfilled => (AppColors.mint600, Colors.white, LucideIcons.circleCheck),
        RequestStage.cancelled => (AppColors.ink100, AppColors.ink600, LucideIcons.ban),
      };

  @override
  Widget build(BuildContext context) {
    final (background, foreground, icon) = styleFor(stage);
    return Container(
      padding: EdgeInsets.symmetric(horizontal: compact ? 8 : 10, vertical: compact ? 3 : 5),
      decoration: BoxDecoration(color: background, borderRadius: BorderRadius.circular(999)),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(icon, size: compact ? 11 : 12, color: foreground),
          const SizedBox(width: 5),
          Text(
            stage.label,
            style: TextStyle(color: foreground, fontSize: compact ? 11 : 12, fontWeight: FontWeight.w600),
          ),
        ],
      ),
    );
  }
}

/// Read-only progress strip for a material request: Submitted → Matching → Plan → Order →
/// Fulfilled. A request that stopped early (cancelled, or plan generation failed) shows the
/// milestones it reached in mint and leaves the rest grey rather than pretending to be done.
class RequestStepper extends StatelessWidget {
  const RequestStepper(this.stage, {super.key});

  final RequestStage stage;

  @override
  Widget build(BuildContext context) {
    final reached = stage.step;
    return Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        for (var i = 0; i < requestMilestones.length; i++) ...[
          SizedBox(
            width: 56,
            child: Column(
              children: [
                _dot(i, reached),
                const SizedBox(height: 5),
                Text(
                  requestMilestones[i].toUpperCase(),
                  textAlign: TextAlign.center,
                  maxLines: 2,
                  style: TextStyle(
                    fontFamily: 'monospace',
                    fontSize: 9,
                    letterSpacing: 0.4,
                    fontWeight: i == reached ? FontWeight.w700 : FontWeight.w400,
                    color: i == reached ? AppColors.ink900 : AppColors.ink600,
                  ),
                ),
              ],
            ),
          ),
          if (i < requestMilestones.length - 1)
            Expanded(
              child: Container(
                margin: const EdgeInsets.only(top: 13),
                height: 2,
                decoration: BoxDecoration(
                  color: i < reached ? AppColors.mint600 : AppColors.ink100,
                  borderRadius: BorderRadius.circular(1),
                ),
              ),
            ),
        ],
      ],
    );
  }

  Widget _dot(int index, int reached) {
    final done = index < reached;
    final active = index == reached;
    // Once fulfilled every milestone is done, so the last one takes the highlighted ring.
    final bad = active && stage.needsAttention;
    final muted = stage.isStopped;

    return Container(
      width: 28,
      height: 28,
      alignment: Alignment.center,
      decoration: BoxDecoration(
        shape: BoxShape.circle,
        color: bad
            ? AppColors.red600
            : done || active
                ? (muted && done ? AppColors.ink600 : AppColors.mint600)
                : AppColors.ink100,
        border: active && !bad ? Border.all(color: AppColors.mint200, width: 4, strokeAlign: BorderSide.strokeAlignOutside) : null,
      ),
      child: done
          ? Icon(muted ? LucideIcons.x : LucideIcons.check, size: 13, color: Colors.white)
          : bad
              ? const Icon(LucideIcons.triangleAlert, size: 13, color: Colors.white)
              : Text(
                  '${index + 1}',
                  style: TextStyle(
                    fontSize: 11,
                    fontWeight: FontWeight.w700,
                    color: done || active ? Colors.white : AppColors.ink600,
                  ),
                ),
    );
  }
}
