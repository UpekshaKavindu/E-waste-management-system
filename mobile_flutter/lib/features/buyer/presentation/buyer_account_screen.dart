import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';

import '../../../core/auth/auth_controller.dart';
import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_theme.dart';
import '../../../core/utils/format.dart';
import '../../../core/widgets/app_button.dart';
import '../../../core/widgets/feedback.dart';
import '../../../core/widgets/glass_card.dart';
import '../../../core/widgets/layout.dart';
import '../../notifications/notification_bell.dart';
import '../application/buyer_providers.dart';
import 'buyer_shell.dart';

/// The signed-in buyer's own account: who they are, how much they have outstanding, and
/// the way out. Company details are maintained by staff on the web portal, so this screen
/// shows the account as it stands rather than editing it.
class BuyerAccountScreen extends ConsumerWidget {
  const BuyerAccountScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final user = ref.watch(authControllerProvider).user;
    final stats = ref.watch(buyerRequestStatsProvider);

    return BuyerPage(
      children: [
        PageHeader(
          title: 'Account',
          subtitle: user?.email,
          icon: LucideIcons.circleUser,
          actions: const [NotificationBell(), SizedBox(width: 10)],
        ),
        GlassCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Row(
                children: [
                  Container(
                    width: 56,
                    height: 56,
                    alignment: Alignment.center,
                    decoration: const BoxDecoration(gradient: AppColors.brandGradient, shape: BoxShape.circle),
                    child: Text(
                      Format.initials(user?.fullName ?? ''),
                      style: AppText.display(19, color: Colors.white),
                    ),
                  ),
                  const SizedBox(width: 14),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(user?.fullName ?? '', style: AppText.display(17), maxLines: 2),
                        const SizedBox(height: 3),
                        Row(
                          children: [
                            const Icon(LucideIcons.mail, size: 13, color: AppColors.ink600),
                            const SizedBox(width: 5),
                            Expanded(
                              child: Text(user?.email ?? '', style: AppText.small, overflow: TextOverflow.ellipsis),
                            ),
                          ],
                        ),
                      ],
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 16),
              const Divider(),
              const SizedBox(height: 12),
              _Row(icon: LucideIcons.handshake, label: 'Role', value: user?.role ?? '—'),
              _Row(icon: LucideIcons.clipboardList, label: 'Total requests', value: '${stats.total}'),
              _Row(icon: LucideIcons.loader, label: 'Still in progress', value: '${stats.open}'),
              _Row(icon: LucideIcons.tags, label: 'Awaiting you', value: Format.kg(stats.openKg)),
              _Row(icon: LucideIcons.packageCheck, label: 'Orders created', value: '${stats.planned}'),
            ],
          ),
        ),
        const SizedBox(height: 14),
        const GlassCard(
          child: Notice(
            tone: NoticeTone.info,
            title: 'Need a change to your company details?',
            message: 'Buyer profiles are maintained by the sales desk so that orders and invoices stay '
                'correct. Contact them to update your company name, contact person, address or account status.',
          ),
        ),
        const SizedBox(height: 18),
        AppButton.danger(
          label: 'Sign out',
          icon: LucideIcons.logOut,
          expand: true,
          onPressed: () => ref.read(authControllerProvider.notifier).signOut(),
        ),
      ],
    );
  }
}

class _Row extends StatelessWidget {
  const _Row({required this.icon, required this.label, required this.value});

  final IconData icon;
  final String label;
  final String value;

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.symmetric(vertical: 6),
        child: Row(
          children: [
            Icon(icon, size: 15, color: AppColors.ink600),
            const SizedBox(width: 9),
            Expanded(child: Text(label, style: AppText.body)),
            Text(value, style: AppText.strong),
          ],
        ),
      );
}
