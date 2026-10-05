import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:intl/intl.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';

import '../../../core/network/api_error.dart';
import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_theme.dart';
import '../../../core/widgets/app_button.dart';
import '../../../core/widgets/feedback.dart';
import '../../../core/widgets/glass_card.dart';
import '../../../core/widgets/layout.dart';
import '../../notifications/notifications_api.dart';
import '../../notifications/notification_tile.dart';
import 'buyer_shell.dart';

/// Everything the sales desk and the AI have told this buyer, newest first. The Sales
/// backend notifies buyers when a commercial plan is approved, rejected or sent back for
/// revision, so this is where a buyer learns the outcome of a request.
class BuyerNotificationsScreen extends ConsumerWidget {
  const BuyerNotificationsScreen({super.key});

  static final _when = DateFormat('MMM d, y, h:mm a');

  Future<void> _open(BuildContext context, WidgetRef ref, AppNotification n) async {
    await markNotificationRead(ref, n);
    final target = resolveNotificationLink(n.link);
    if (target == null || !context.mounted) return;
    context.go(target);
  }

  Future<void> _markAll(WidgetRef ref) async {
    try {
      await ref.read(notificationsApiProvider).markAllRead();
      ref.invalidate(notificationsListProvider);
      ref.invalidate(unreadNotificationsProvider);
    } catch (_) {
      // advisory: the badge clears on the next poll if the call fails
    }
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final listAsync = ref.watch(notificationsListProvider);
    final unread = ref.watch(unreadNotificationsProvider).value ?? 0;

    return BuyerPage(
      onRefresh: () => ref.refresh(notificationsListProvider.future),
      children: [
        PageHeader(
          title: 'Updates',
          subtitle: unread == 0
              ? 'You are all caught up.'
              : '$unread unread ${unread == 1 ? 'update' : 'updates'}',
          icon: LucideIcons.bell,
          actions: [
            if (unread > 0)
              AppButton.secondary(
                label: 'Mark all read',
                icon: LucideIcons.checkCheck,
                small: true,
                onPressed: () => _markAll(ref),
              ),
          ],
        ),
        switch (listAsync) {
          AsyncData(:final value) when value.isEmpty => const GlassCard(
              child: EmptyState(
                icon: LucideIcons.bellOff,
                title: 'No updates yet',
                description: 'Plan approvals, rejections and revision requests on your material requests will appear here.',
              ),
            ),
          AsyncData(:final value) => Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                for (final n in value)
                  NotificationTile(
                    n,
                    when: n.createdAt == null ? null : _when.format(n.createdAt!),
                    onTap: () => _open(context, ref, n),
                  ),
              ],
            ),
          AsyncError(:final error) => ErrorMessage(
              message: apiErrorMessage(error, 'Could not load your updates.'),
              onRetry: () => ref.invalidate(notificationsListProvider),
            ),
          _ => const GlassCard(child: LoadingState(label: 'Loading updates…')),
        },
        const SizedBox(height: 10),
        Row(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            const Icon(LucideIcons.clock, size: 12, color: AppColors.ink600),
            const SizedBox(width: 6),
            Text('Updates refresh automatically every 30 seconds.', style: AppText.small),
          ],
        ),
      ],
    );
  }
}
