import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';

import '../../../../core/theme/app_colors.dart';
import '../../../../core/utils/format.dart';
import '../../../../core/widgets/glass_card.dart';
import '../../data/processing_enums.dart';
import '../../data/warehouse_models.dart';
import 'badges.dart';

/// One inventory item in a list: type, badges, weight, location, received date. Opens the item.
class InventoryTile extends StatelessWidget {
  const InventoryTile({super.key, required this.item});

  final InventoryListItem item;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.only(bottom: 8),
      child: Tile(
        onTap: () => context.go('/warehouse/inventory/${item.id}'),
        child: Row(
          children: [
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    children: [
                      Flexible(
                        child: Text(
                          item.quantity > 1 ? '${item.itemType} × ${item.quantity}' : item.itemType,
                          overflow: TextOverflow.ellipsis,
                          style: const TextStyle(fontSize: 15, fontWeight: FontWeight.w600, color: AppColors.ink900),
                        ),
                      ),
                      if (item.kind != ItemKind.unit) ...[
                        const SizedBox(width: 6),
                        Tooltip(
                          message: item.kind.label,
                          child: Icon(
                            item.kind == ItemKind.material ? LucideIcons.packageOpen : LucideIcons.gitFork,
                            size: 14,
                            color: AppColors.violet500,
                          ),
                        ),
                      ],
                    ],
                  ),
                  const SizedBox(height: 6),
                  Wrap(spacing: 6, runSpacing: 6, children: [StatusBadge(item.status), CategoryBadge(item.category)]),
                  const SizedBox(height: 6),
                  Text(
                    '${Format.kg(item.verifiedWeightKg)} · ${item.currentLocationName} · ${Format.date(item.receivedAt)}',
                    style: const TextStyle(fontSize: 12, color: AppColors.ink600),
                  ),
                ],
              ),
            ),
            const Icon(LucideIcons.chevronRight, size: 18, color: AppColors.ink600),
          ],
        ),
      ),
    );
  }
}
