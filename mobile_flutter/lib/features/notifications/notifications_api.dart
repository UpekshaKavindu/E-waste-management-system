import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/network/api_client.dart';
import '../../core/utils/format.dart';

/// NotificationResponse — the in-app notifications behind the header bell (NotificationsController).
class AppNotification {
  const AppNotification({
    required this.id,
    required this.title,
    required this.message,
    required this.type,
    this.link,
    required this.isRead,
    this.createdAt,
  });

  final String id;
  final String title;
  final String message;

  /// "info" | "success" | "warning" | "error".
  final String type;

  /// Route to open when tapped, e.g. "/collector/jobs/{id}".
  final String? link;
  final bool isRead;
  final DateTime? createdAt;

  factory AppNotification.fromJson(Map<String, dynamic> json) => AppNotification(
        id: json['id'] as String,
        title: json['title'] as String? ?? '',
        message: json['message'] as String? ?? '',
        type: json['type'] as String? ?? 'info',
        link: json['link'] as String?,
        isRead: json['isRead'] as bool? ?? false,
        createdAt: Format.parseApiDate(json['createdAt'] as String?),
      );
}

/// Every call is scoped to the signed-in user by the token.
class NotificationsApi {
  NotificationsApi(this._dio);

  final Dio _dio;

  Future<List<AppNotification>> list({int limit = 30}) async {
    final response = await _dio.get<List<dynamic>>('/api/notifications', queryParameters: {'limit': limit});
    return response.data!.map((e) => AppNotification.fromJson(e as Map<String, dynamic>)).toList();
  }

  Future<int> unreadCount() async {
    final response = await _dio.get<Map<String, dynamic>>('/api/notifications/unread-count');
    return response.data!['count'] as int? ?? 0;
  }

  Future<void> markRead(String id) => _dio.post<void>('/api/notifications/$id/read');

  Future<void> markAllRead() => _dio.post<void>('/api/notifications/read-all');
}

final notificationsApiProvider = Provider<NotificationsApi>((ref) => NotificationsApi(ref.watch(dioProvider)));

/// Unread count for the bell badge, polled every 30 seconds while a bell is on screen.
final unreadNotificationsProvider = StreamProvider.autoDispose<int>((ref) async* {
  ref.watch(authControllerProvider.select((s) => s.user?.userId));
  final api = ref.watch(notificationsApiProvider);
  while (true) {
    yield await api.unreadCount();
    await Future<void>.delayed(const Duration(seconds: 30));
  }
});

final notificationsListProvider = FutureProvider.autoDispose<List<AppNotification>>(
  (ref) => ref.watch(notificationsApiProvider).list(),
);
