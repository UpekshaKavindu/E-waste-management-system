import 'dart:async';
import 'dart:convert';

import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';

import '../config/env.dart';
import 'auth_models.dart';

/// Same key names as the web app's localStorage, but kept in the platform's secure storage
/// (Keychain / Keystore) because the value is a bearer token.
const _tokenKey = 'access_token';
const _userKey = 'auth_user';

final secureStorageProvider = Provider<FlutterSecureStorage>((ref) => const FlutterSecureStorage());

final authControllerProvider = NotifierProvider<AuthController, AuthState>(AuthController.new);

class AuthController extends Notifier<AuthState> {
  Timer? _expiryTimer;

  FlutterSecureStorage get _storage => ref.read(secureStorageProvider);

  @override
  AuthState build() {
    ref.onDispose(() => _expiryTimer?.cancel());
    Future.microtask(_restore);
    return const AuthState.restoring();
  }

  /// Rehydrates the session on app start. An expired token is dropped straight away instead of
  /// failing on the first request (the backend issues 60-minute tokens with no refresh).
  Future<void> _restore() async {
    try {
      final token = await _storage.read(key: _tokenKey);
      final rawUser = await _storage.read(key: _userKey);
      final expiry = token == null ? null : _tokenExpiry(token);
      if (token == null || rawUser == null || expiry == null || !expiry.isAfter(DateTime.now())) {
        await _clear();
        state = const AuthState.signedOut();
        return;
      }
      final user = AuthUser.fromJson(jsonDecode(rawUser) as Map<String, dynamic>);
      _scheduleExpiry(expiry);
      state = AuthState.signedIn(user, token);
    } catch (_) {
      await _clear();
      state = const AuthState.signedOut();
    }
  }

  /// POST /api/auth/login. Throws the DioException on failure so the screen can show the message.
  Future<void> signIn(String email, String password) => _authenticate(
        '/api/auth/login',
        {'email': email.trim(), 'password': password},
      );

  /// POST /api/auth/register, one of Household | Corporate | Collector. The
  /// backend's AuthResponse already includes a token, so a successful
  /// registration signs the user straight in — no separate login step.
  Future<void> signUp({
    required String fullName,
    required String email,
    required String password,
    required String phone,
    required String role,
  }) =>
      _authenticate('/api/auth/register', {
        'fullName': fullName.trim(),
        'email': email.trim(),
        'password': password,
        'phone': phone.trim(),
        'role': role,
      });

  Future<void> _authenticate(String path, Map<String, dynamic> body) async {
    final dio = Dio(BaseOptions(
      baseUrl: Env.apiBaseUrl,
      connectTimeout: const Duration(seconds: 15),
      receiveTimeout: const Duration(seconds: 30),
    ));
    final response = await dio.post<Map<String, dynamic>>(path, data: body);
    final data = response.data!;
    final token = data['token'] as String;
    final user = AuthUser.fromJson(data);

    await _storage.write(key: _tokenKey, value: token);
    await _storage.write(key: _userKey, value: jsonEncode(user.toJson()));
    final expiry = _tokenExpiry(token);
    if (expiry != null) _scheduleExpiry(expiry);
    state = AuthState.signedIn(user, token);
  }

  /// Keeps the cached session in step after the user renames themselves (the token is unchanged).
  Future<void> updateFullName(String fullName) async {
    final user = state.user;
    final token = state.token;
    if (user == null || token == null) return;
    final updated = AuthUser(userId: user.userId, email: user.email, fullName: fullName, role: user.role);
    await _storage.write(key: _userKey, value: jsonEncode(updated.toJson()));
    state = AuthState.signedIn(updated, token);
  }

  Future<void> signOut({String? reason}) async {
    if (state.status == AuthStatus.signedOut) return;
    _expiryTimer?.cancel();
    await _clear();
    state = AuthState.signedOut(reason: reason);
  }

  void _scheduleExpiry(DateTime expiry) {
    _expiryTimer?.cancel();
    _expiryTimer = Timer(expiry.difference(DateTime.now()), () {
      signOut(reason: 'Your session has expired. Please sign in again.');
    });
  }

  Future<void> _clear() async {
    await _storage.delete(key: _tokenKey);
    await _storage.delete(key: _userKey);
  }

  /// Reads the JWT's `exp` claim. The signature isn't checked here — the server does that;
  /// this only avoids using a token we already know is dead.
  static DateTime? _tokenExpiry(String token) {
    final parts = token.split('.');
    if (parts.length != 3) return null;
    try {
      final payload = jsonDecode(utf8.decode(base64Url.decode(base64Url.normalize(parts[1])))) as Map<String, dynamic>;
      final exp = payload['exp'];
      return exp is num ? DateTime.fromMillisecondsSinceEpoch(exp.toInt() * 1000) : null;
    } catch (_) {
      return null;
    }
  }
}
