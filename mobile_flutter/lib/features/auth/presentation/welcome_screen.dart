import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';

import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_theme.dart';
import '../../../core/widgets/app_background.dart';
import '../../../core/widgets/app_button.dart';
import '../../../core/widgets/glass_card.dart';
import '../../../core/widgets/layout.dart';

// Same words as the web landing page (frontend-react/src/features/landing/LandingPage.jsx),
// cut down to what fits a first screen on a phone.
const _trustPoints = ['AI-assisted intake', 'Doorstep pickup', 'Hazard-aware processing', 'Verified buyers'];

const _journey = [
  ('Submit', 'Describe your device and add a photo. Our AI reads back its category, hazard level and estimated value in seconds.'),
  ('Review & schedule', 'Flagged items get a quick staff review; everything else is scheduled straight into a collection job.'),
  ('Collect', 'A collector picks it up from your door, guided by our dedicated collector app.'),
  ('Recover & resell', 'The warehouse dismantles and sorts it; recovered materials are sold to verified buyers — never to landfill.'),
];

/// First screen for anyone signed out: what the app is, how it works, and the way in. Sign in and
/// Create account are pushed on top, so Back returns here.
class WelcomeScreen extends StatelessWidget {
  const WelcomeScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: Stack(
        children: [
          const AppBackground(blobOpacity: 0.35, showYellowBlob: true),
          SafeArea(
            child: Column(
              children: [
                Expanded(
                  child: ListView(
                    padding: const EdgeInsets.fromLTRB(16, 12, 16, 24),
                    children: [
                      ResponsiveCenter(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.stretch,
                          children: [
                            Row(
                              children: [
                                const BrandMark(),
                                const Spacer(),
                                TextButton(onPressed: () => context.push('/login'), child: const Text('Sign in')),
                              ],
                            ),
                            const SizedBox(height: 16),
                            const _FadeUp(index: 0, child: _Hero()),
                            const SizedBox(height: 24),
                            const _FadeUp(index: 1, child: _Headline()),
                            const SizedBox(height: 16),
                            const _FadeUp(index: 2, child: _TrustPoints()),
                            const SizedBox(height: 28),
                            const _FadeUp(index: 3, child: _HowItWorks()),
                          ],
                        ),
                      ),
                    ],
                  ),
                ),
                const _Actions(),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

class _Hero extends StatelessWidget {
  const _Hero();

  @override
  Widget build(BuildContext context) {
    return ClipRRect(
      borderRadius: BorderRadius.circular(AppRadius.card),
      child: Stack(
        children: [
          Image.asset(
            'assets/images/recycling-scene.jpg',
            height: 260,
            width: double.infinity,
            fit: BoxFit.cover,
            alignment: const Alignment(0, -0.1),
            semanticLabel: 'A recycling bin full of electronics, with a collection truck, a worker and a recycling plant',
          ),
          Positioned(
            left: 12,
            bottom: 12,
            child: Container(
              padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 7),
              decoration: BoxDecoration(
                color: Colors.white.withValues(alpha: 0.85),
                borderRadius: BorderRadius.circular(999),
                border: Border.all(color: AppColors.glassBorder),
              ),
              child: Row(
                mainAxisSize: MainAxisSize.min,
                children: [
                  const _PulseDot(),
                  const SizedBox(width: 8),
                  Text('E-WASTE MANAGEMENT SYSTEM',
                      style: AppText.label.copyWith(color: AppColors.mint800, letterSpacing: 1.6)),
                ],
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class _Headline extends StatelessWidget {
  const _Headline();

  @override
  Widget build(BuildContext context) {
    final style = AppText.display(32, weight: FontWeight.w800);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text('Turn e-waste into', style: style),
        ShaderMask(
          blendMode: BlendMode.srcIn,
          shaderCallback: (bounds) =>
              const LinearGradient(colors: [AppColors.mint500, AppColors.mint700]).createShader(bounds),
          child: Text('recovered value.', style: style),
        ),
        const SizedBox(height: 12),
        const Text(
          'Collect, process, submit and sell recovered materials — all in one platform, '
          'from the first pickup to the export order.',
          style: TextStyle(fontSize: 16, color: AppColors.ink600, height: 1.45),
        ),
      ],
    );
  }
}

class _TrustPoints extends StatelessWidget {
  const _TrustPoints();

  @override
  Widget build(BuildContext context) {
    return Wrap(
      spacing: 8,
      runSpacing: 8,
      children: [
        for (final label in _trustPoints)
          Container(
            padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 6),
            decoration: BoxDecoration(
              color: Colors.white.withValues(alpha: 0.7),
              borderRadius: BorderRadius.circular(999),
              border: Border.all(color: AppColors.mint100),
            ),
            child: Row(
              mainAxisSize: MainAxisSize.min,
              children: [
                const Icon(LucideIcons.check, size: 13, color: AppColors.mint600),
                const SizedBox(width: 5),
                Text(label, style: const TextStyle(fontSize: 12, fontWeight: FontWeight.w600, color: AppColors.ink800)),
              ],
            ),
          ),
      ],
    );
  }
}

class _HowItWorks extends StatelessWidget {
  const _HowItWorks();

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Text('HOW IT WORKS', style: AppText.label.copyWith(color: AppColors.mint700, letterSpacing: 2.4)),
        const SizedBox(height: 6),
        Text('From your doorstep to resale, in four steps.', style: AppText.display(20)),
        const SizedBox(height: 14),
        GlassCard(
          padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 6),
          child: Column(
            children: [
              for (var i = 0; i < _journey.length; i++) ...[
                if (i > 0) const Divider(),
                Padding(
                  padding: const EdgeInsets.symmetric(vertical: 14),
                  child: Row(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Container(
                        width: 34,
                        height: 34,
                        alignment: Alignment.center,
                        decoration: const BoxDecoration(
                          gradient: AppColors.brandGradient,
                          shape: BoxShape.circle,
                          boxShadow: [BoxShadow(color: Color(0x4D10B981), blurRadius: 8, offset: Offset(0, 3))],
                        ),
                        child: Text('${i + 1}', style: AppText.display(14, color: Colors.white)),
                      ),
                      const SizedBox(width: 14),
                      Expanded(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Text(_journey[i].$1, style: AppText.display(16)),
                            const SizedBox(height: 3),
                            Text(_journey[i].$2, style: AppText.small),
                          ],
                        ),
                      ),
                    ],
                  ),
                ),
              ],
            ],
          ),
        ),
      ],
    );
  }
}

/// Pinned at the bottom so the way in is always one tap away, however far the user scrolls.
class _Actions extends StatelessWidget {
  const _Actions();

  @override
  Widget build(BuildContext context) {
    return Container(
      decoration: BoxDecoration(
        color: Colors.white.withValues(alpha: 0.8),
        border: const Border(top: BorderSide(color: AppColors.mint100)),
      ),
      padding: const EdgeInsets.fromLTRB(16, 14, 16, 10),
      child: ResponsiveCenter(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            AppButton(
              label: 'Create an account',
              icon: LucideIcons.arrowRight,
              expand: true,
              onPressed: () => context.push('/register'),
            ),
            const SizedBox(height: 10),
            AppButton.secondary(label: 'Sign in', expand: true, onPressed: () => context.push('/login')),
            TextButton(
              onPressed: () => context.push('/register/buyer'),
              child: const Text('Buying recovered materials? Register as a buyer'),
            ),
          ],
        ),
      ),
    );
  }
}

/// The web hero's staggered fade-up, played once when the screen opens.
class _FadeUp extends StatelessWidget {
  const _FadeUp({required this.index, required this.child});

  final int index;
  final Widget child;

  @override
  Widget build(BuildContext context) {
    final delay = 0.08 * index;
    return TweenAnimationBuilder<double>(
      tween: Tween(begin: 0, end: 1),
      duration: Duration(milliseconds: 550 + (delay * 1000).round()),
      curve: Interval(delay / (0.55 + delay), 1, curve: const Cubic(0.22, 1, 0.36, 1)),
      builder: (context, t, child) => Opacity(
        opacity: t,
        child: Transform.translate(offset: Offset(0, 18 * (1 - t)), child: child),
      ),
      child: child,
    );
  }
}

/// The hero badge's live dot (`animate-ping` on the web).
class _PulseDot extends StatefulWidget {
  const _PulseDot();

  @override
  State<_PulseDot> createState() => _PulseDotState();
}

class _PulseDotState extends State<_PulseDot> with SingleTickerProviderStateMixin {
  late final _controller = AnimationController(vsync: this, duration: const Duration(milliseconds: 1400))..repeat();

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return SizedBox(
      width: 10,
      height: 10,
      child: AnimatedBuilder(
        animation: _controller,
        builder: (context, _) => Stack(
          alignment: Alignment.center,
          children: [
            Transform.scale(
              scale: 1 + _controller.value * 1.2,
              child: Container(
                width: 8,
                height: 8,
                decoration: BoxDecoration(
                  color: AppColors.mint400.withValues(alpha: 0.75 * (1 - _controller.value)),
                  shape: BoxShape.circle,
                ),
              ),
            ),
            Container(
              width: 8,
              height: 8,
              decoration: const BoxDecoration(color: AppColors.mint500, shape: BoxShape.circle),
            ),
          ],
        ),
      ),
    );
  }
}
