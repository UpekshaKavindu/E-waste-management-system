import 'package:flutter/material.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';
import 'package:google_fonts/google_fonts.dart';

import 'app_colors.dart';

/// Text styles matching the web app: Sora for headings (`font-display`), the system sans-serif
/// for body text, and a monospace uppercase style for field labels (`font-mono uppercase`).
abstract final class AppText {
  static TextStyle display(double size, {FontWeight weight = FontWeight.w700, Color color = AppColors.ink900}) =>
      GoogleFonts.sora(fontSize: size, fontWeight: weight, color: color, height: 1.2);

  static const mono = TextStyle(
    fontFamily: 'monospace',
    fontFamilyFallback: ['Menlo', 'Courier New', 'Courier'],
  );

  /// `text-xs font-mono uppercase tracking-wide text-ink-600` — the label above every input.
  static final label = mono.copyWith(fontSize: 11, letterSpacing: 0.8, color: AppColors.ink600);

  static const body = TextStyle(fontSize: 14, color: AppColors.ink800, height: 1.4);
  static const small = TextStyle(fontSize: 12, color: AppColors.ink600, height: 1.35);
  static const strong = TextStyle(fontSize: 14, fontWeight: FontWeight.w600, color: AppColors.ink900, height: 1.35);
}

/// Radii used by the web app: rounded-xl (inputs) 12, rounded-2xl 16, rounded-3xl (cards) 24.
abstract final class AppRadius {
  static const input = 12.0;
  static const tile = 16.0;
  static const card = 24.0;
}

ThemeData buildAppTheme() {
  final base = ThemeData(
    useMaterial3: true,
    brightness: Brightness.light,
    colorScheme: ColorScheme.fromSeed(
      seedColor: AppColors.mint600,
      primary: AppColors.mint600,
      onPrimary: Colors.white,
      secondary: AppColors.mint700,
      surface: AppColors.canvas,
      onSurface: AppColors.ink900,
      error: AppColors.red600,
    ),
  );

  const inputBorder = OutlineInputBorder(
    borderRadius: BorderRadius.all(Radius.circular(AppRadius.input)),
    borderSide: BorderSide(color: AppColors.mint100),
  );

  return base.copyWith(
    scaffoldBackgroundColor: Colors.transparent,
    textTheme: base.textTheme.apply(bodyColor: AppColors.ink900, displayColor: AppColors.ink900),
    iconTheme: const IconThemeData(color: AppColors.ink800),
    // Every AppBar's automatic back button: a round white chip with a chevron instead of a bare arrow.
    actionIconTheme: ActionIconThemeData(backButtonIconBuilder: (_) => const _RoundBackIcon()),
    inputDecorationTheme: InputDecorationTheme(
      filled: true,
      fillColor: AppColors.inputFill,
      isDense: true,
      contentPadding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
      hintStyle: TextStyle(color: AppColors.ink600.withValues(alpha: 0.5), fontSize: 14),
      border: inputBorder,
      enabledBorder: inputBorder,
      disabledBorder: inputBorder,
      focusedBorder: inputBorder.copyWith(borderSide: const BorderSide(color: AppColors.mint400, width: 2)),
      errorBorder: inputBorder.copyWith(borderSide: const BorderSide(color: AppColors.red200)),
      focusedErrorBorder: inputBorder.copyWith(borderSide: const BorderSide(color: AppColors.red500, width: 2)),
    ),
    dropdownMenuTheme: const DropdownMenuThemeData(
      menuStyle: MenuStyle(backgroundColor: WidgetStatePropertyAll(Colors.white)),
    ),
    snackBarTheme: SnackBarThemeData(
      behavior: SnackBarBehavior.floating,
      backgroundColor: AppColors.ink900,
      contentTextStyle: const TextStyle(color: Colors.white, fontSize: 14),
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(AppRadius.tile)),
    ),
    switchTheme: SwitchThemeData(
      thumbColor: WidgetStateProperty.resolveWith((s) => s.contains(WidgetState.selected) ? Colors.white : null),
      trackColor: WidgetStateProperty.resolveWith((s) => s.contains(WidgetState.selected) ? AppColors.mint600 : null),
    ),
    checkboxTheme: CheckboxThemeData(
      fillColor: WidgetStateProperty.resolveWith((s) => s.contains(WidgetState.selected) ? AppColors.mint600 : null),
    ),
    progressIndicatorTheme: const ProgressIndicatorThemeData(color: AppColors.mint600),
    textSelectionTheme: const TextSelectionThemeData(cursorColor: AppColors.mint600),
    dividerTheme: const DividerThemeData(color: AppColors.mint100, space: 1, thickness: 1),
  );
}

class _RoundBackIcon extends StatelessWidget {
  const _RoundBackIcon();

  @override
  Widget build(BuildContext context) {
    return Container(
      width: 40,
      height: 40,
      decoration: BoxDecoration(
        color: Colors.white.withValues(alpha: 0.92),
        shape: BoxShape.circle,
        border: Border.all(color: AppColors.glassBorder),
        boxShadow: const [BoxShadow(color: AppColors.glassShadow, blurRadius: 12, offset: Offset(0, 4))],
      ),
      alignment: Alignment.center,
      // Nudged left so the chevron looks centred in the circle.
      child: const Padding(
        padding: EdgeInsets.only(right: 2),
        child: Icon(LucideIcons.chevronLeft, size: 22, color: AppColors.ink900),
      ),
    );
  }
}
