import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:intl/intl.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';

import '../../core/network/api_error.dart';
import '../../core/theme/app_colors.dart';
import '../../core/widgets/app_sheet.dart';
import '../../core/widgets/feedback.dart';
import 'notification_tile.dart';
import 'notifications_api.dart';

/// Header bell with an unread badge. Opens the notification list as a bottom sheet.
class NotificationBell extends ConsumerWidget {
  const NotificationBell({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final unread = ref.watch(unreadNotificationsProvider).value ?? 0;
    return Semantics(
      button: true,
      label: unread == 0 ? 'Notifications' : 'Notifications, $unread unread',
      child: InkWell(
        customBorder: const CircleBorder(),
        onTap: () async {
          await showAppSheet<void>(context, builder: (_) => const _NotificationSheet());
          ref.invalidate(unreadNotificationsProvider);
        },
        child: Container(
          width: 44,
          height: 44,
          decoration: BoxDecoration(
            color: Colors.white.withValues(alpha: 0.7),
            shape: BoxShape.circle,
            border: Border.all(color: AppColors.mint100),
          ),
          child: Stack(
            alignment: Alignment.center,
            clipBehavior: Clip.none,
            children: [
              const Icon(LucideIcons.bell, size: 20, color: AppColors.ink800),
              if (unread > 0)
                Positioned(
                  top: 6,
                  right: 5,
                  child: Container(
                    constraints: const BoxConstraints(minWidth: 16),
                    height: 16,
                    padding: const EdgeInsets.symmetric(horizontal: 4),
                    alignment: Alignment.center,
                    decoration: BoxDecoration(
                      color: AppColors.mint600,
                      borderRadius: BorderRadius.circular(999),
                      border: Border.all(color: Colors.white, width: 1.5),
                    ),
                    child: Text(
                      unread > 9 ? '9+' : '$unread',
                      style: const TextStyle(color: Colors.white, fontSize: 9, fontWeight: FontWeight.w700, height: 1),
                    ),
                  ),
                ),
            ],
          ),
        ),
      ),
    );
  }
}

class _NotificationSheet extends ConsumerWidget {
  const _NotificationSheet();

  static final _when = DateFormat('MMM d, h:mm a');

  Future<void> _open(BuildContext context, WidgetRef ref, AppNotification n) async {
    await markNotificationRead(ref, n);
    final target = resolveNotificationLink(n.link);
    if (!context.mounted || target == null) return;
    // Grab the router first â€” this sheet's context is gone once it closes.
    final router = GoRouter.of(context);
    Navigator.of(context).pop();
    router.go(target);
  }

  Future<void> _markAll(WidgetRef ref) async {
    try {
      await ref.read(notificationsApiProvider).markAllRead();
    } finally {
      ref.invalidate(notificationsListProvider);
      ref.invalidate(unreadNotificationsProvider);
    }
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final listAsync = ref.watch(notificationsListProvider);
    final hasUnread = listAsync.value?.any((n) => !n.isRead) ?? false;

    return AppSheet(
      title: 'Notifications',
      body: switch (listAsync) {
        AsyncData(:final value) when value.isEmpty => const EmptyState(
            icon: LucideIcons.bellOff,
            title: 'No notifications yet',
            description: 'Updates about your work will show up here.',
          ),
        AsyncData(:final value) => Column(
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
            message: apiErrorMessage(error, 'Failed to load notifications.'),
            onRetry: () => ref.invalidate(notificationsListProvider),
          ),
        _ => const LoadingState(),
      },
      footer: [
        if (hasUnread)
          TextButton.icon(
            onPressed: () => _markAll(ref),
            icon: const Icon(LucideIcons.checkCheck, size: 16),
            label: const Text('Mark all as read'),
          ),
      ],
    );
  }
}

