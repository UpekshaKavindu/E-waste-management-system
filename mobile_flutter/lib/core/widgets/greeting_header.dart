import 'package:flutter/material.dart';

import '../theme/app_colors.dart';
import '../theme/app_theme.dart';
import '../utils/format.dart';

/// Home-screen header: initials avatar, "Welcome back," and the user's name, with actions on the
/// right (e.g. the notification bell).
class GreetingHeader extends StatelessWidget {
  const GreetingHeader({super.key, required this.name, this.online, this.actions = const []});

  final String name;

  /// Shows a green (online) or grey (offline) dot on the avatar; null shows no dot.
  final bool? online;
  final List<Widget> actions;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.only(bottom: 16),
      child: Row(
        children: [
          Stack(
            clipBehavior: Clip.none,
            children: [
              Container(
                width: 52,
                height: 52,
                alignment: Alignment.center,
                decoration: const BoxDecoration(gradient: AppColors.brandGradient, shape: BoxShape.circle),
                child: Text(Format.initials(name), style: AppText.display(18, color: Colors.white)),
              ),
              if (online != null)
                Positioned(
                  right: 0,
                  bottom: 0,
                  child: Container(
                    width: 14,
                    height: 14,
                    decoration: BoxDecoration(
                      color: online! ? AppColors.mint500 : AppColors.ink600,
                      shape: BoxShape.circle,
                      border: Border.all(color: Colors.white, width: 2),
                    ),
                  ),
                ),
            ],
          ),
          const SizedBox(width: 12),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                const Text('Welcome back,', style: TextStyle(fontSize: 14, color: AppColors.ink600)),
                Text(name.isEmpty ? 'there' : name,
                    style: AppText.display(22), maxLines: 1, overflow: TextOverflow.ellipsis),
              ],
            ),
          ),
          ...actions,
        ],
      ),
    );
  }
}
