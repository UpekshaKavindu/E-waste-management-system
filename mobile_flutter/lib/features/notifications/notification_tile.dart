import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';

import '../../core/theme/app_colors.dart';
import '../../core/theme/app_theme.dart';
import 'notifications_api.dart';

/// One notification row: type icon, title, message, timestamp and an unread dot.
/// Shared by the header bell's sheet and the full notifications page so both look
/// and behave the same.
class NotificationTile extends StatelessWidget {
  const NotificationTile(this.notification, {super.key, required this.when, required this.onTap});

  final AppNotification notification;
  final String? when;
  final VoidCallback onTap;

  /// Icon + colour for a notification type, matching the web NotificationBell.
  static (IconData, Color) iconFor(String type) => switch (type) {
        'success' => (LucideIcons.circleCheck, AppColors.mint600),
        'warning' => (LucideIcons.triangleAlert, AppColors.amber700),
        'error' => (LucideIcons.circleAlert, const Color(0xFFDC2626)),
        _ => (LucideIcons.info, AppColors.sky500),
      };

  @override
  Widget build(BuildContext context) {
    final (icon, color) = iconFor(notification.type);
    return InkWell(
      onTap: onTap,
      borderRadius: BorderRadius.circular(AppRadius.input),
      child: Container(
        margin: const EdgeInsets.only(bottom: 6),
        padding: const EdgeInsets.all(12),
        decoration: BoxDecoration(
          color: notification.isRead ? Colors.transparent : AppColors.mint50,
          borderRadius: BorderRadius.circular(AppRadius.input),
          border: notification.isRead ? null : Border.all(color: AppColors.mint100),
        ),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Icon(icon, size: 18, color: color),
            const SizedBox(width: 10),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(notification.title, style: AppText.strong),
                  const SizedBox(height: 2),
                  Text(notification.message, style: AppText.body),
                  if (when != null) ...[
                    const SizedBox(height: 4),
                    Text(when!, style: AppText.small),
                  ],
                ],
              ),
            ),
            if (!notification.isRead)
              Container(
                width: 8,
                height: 8,
                margin: const EdgeInsets.only(top: 6, left: 6),
                decoration: const BoxDecoration(color: AppColors.mint600, shape: BoxShape.circle),
              ),
          ],
        ),
      ),
    );
  }
}

/// Marks a notification read (best effort) and refreshes the list and badge afterwards.
/// Kept separate from routing so both the bell's sheet and the full page can use it.
Future<void> markNotificationRead(WidgetRef ref, AppNotification notification) async {
  if (notification.isRead) return;
  try {
    await ref.read(notificationsApiProvider).markRead(notification.id);
  } catch (_) {
    // reading still works if marking fails; the badge just stays until the next try
  }
  ref.invalidate(notificationsListProvider);
  ref.invalidate(unreadNotificationsProvider);
}

/// The in-app screen a notification link points at, or null when this app has no page for
/// it. The web app and the API share the same `link` field, so links aimed at the desktop
/// portal are mapped here rather than followed blindly.
String? resolveNotificationLink(String? link) {
  if (link == null) return null;
  if (link.startsWith('/collector')) return link;
  // /material-requests is the link the Sales backend attaches to buyer notifications.
  if (link.startsWith('/material-requests') || link.startsWith('/buyer')) return '/buyer';
  return null;
}

