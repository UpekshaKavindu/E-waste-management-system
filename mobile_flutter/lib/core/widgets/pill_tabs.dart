import 'package:flutter/material.dart';

import '../theme/app_colors.dart';
import 'glass_card.dart';

/// The web app's pill tab list (`rounded-full px-4 py-2`, selected = mint-600): a few big,
/// equal-width choices in one row.
class PillTabs<T> extends StatelessWidget {
  const PillTabs({super.key, required this.value, required this.options, required this.onChanged});

  final T value;
  final Map<T, String> options;
  final ValueChanged<T> onChanged;

  @override
  Widget build(BuildContext context) {
    return GlassCard(
      padding: const EdgeInsets.all(6),
      radius: 999,
      child: Row(
        children: [
          for (final entry in options.entries)
            Expanded(
              child: Semantics(
                selected: entry.key == value,
                button: true,
                child: InkWell(
                  onTap: () => onChanged(entry.key),
                  borderRadius: BorderRadius.circular(999),
                  child: AnimatedContainer(
                    duration: const Duration(milliseconds: 180),
                    padding: const EdgeInsets.symmetric(vertical: 11, horizontal: 6),
                    alignment: Alignment.center,
                    decoration: BoxDecoration(
                      color: entry.key == value ? AppColors.mint600 : Colors.transparent,
                      borderRadius: BorderRadius.circular(999),
                      boxShadow: entry.key == value
                          ? const [BoxShadow(color: Color(0x4D10B981), blurRadius: 10, offset: Offset(0, 4))]
                          : null,
                    ),
                    child: FittedBox(
                      fit: BoxFit.scaleDown,
                      child: Text(
                        entry.value,
                        maxLines: 1,
                        style: TextStyle(
                          fontSize: 14,
                          fontWeight: FontWeight.w600,
                          color: entry.key == value ? Colors.white : AppColors.ink800,
                        ),
                      ),
                    ),
                  ),
                ),
              ),
            ),
        ],
      ),
    );
  }
}
