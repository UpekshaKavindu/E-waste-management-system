import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';

import '../../../core/auth/auth_controller.dart';
import '../../../core/network/api_error.dart';
import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_theme.dart';
import '../../../core/utils/format.dart';
import '../../../core/widgets/app_button.dart';
import '../../../core/widgets/app_shell.dart';
import '../../../core/widgets/feedback.dart';
import '../../../core/widgets/form_fields.dart';
import '../../../core/widgets/glass_card.dart';
import '../../../core/widgets/layout.dart';
import '../application/collector_providers.dart';
import '../data/collector_models.dart';

/// The collector's own profile: who they are and the vehicle jobs are matched to. Editable at any
/// time; email is the login and stays read-only.
class CollectorProfileScreen extends ConsumerWidget {
  const CollectorProfileScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final profileAsync = ref.watch(collectorProfileProvider);

    return ShellPage(
      onRefresh: () => ref.refresh(collectorProfileProvider.future),
      children: [
        PageHeader(
          title: 'Profile',
          subtitle: 'Your details and vehicle',
          icon: LucideIcons.circleUser,
          actions: [
            IconButton(
              tooltip: 'Sign out',
              onPressed: () => _confirmSignOut(context, ref),
              icon: const Icon(LucideIcons.logOut, size: 20, color: AppColors.ink800),
            ),
          ],
        ),
        switch (profileAsync) {
          AsyncData(:final value?) => _ProfileBody(key: ValueKey(value.collectorId), profile: value),
          AsyncError(:final error) => ErrorMessage(
              message: apiErrorMessage(error, 'Failed to load your profile.'),
              onRetry: () => ref.invalidate(collectorProfileProvider),
            ),
          _ => const GlassCard(child: LoadingState(label: 'Loading profile…')),
        },
      ],
    );
  }

  Future<void> _confirmSignOut(BuildContext context, WidgetRef ref) async {
    final ok = await showDialog<bool>(
      context: context,
      builder: (c) => AlertDialog(
        backgroundColor: Colors.white,
        title: Text('Sign out?', style: AppText.display(18)),
        content: const Text('You will need to sign in again to see your jobs.'),
        actions: [
          TextButton(onPressed: () => Navigator.pop(c, false), child: const Text('Cancel')),
          TextButton(onPressed: () => Navigator.pop(c, true), child: const Text('Sign out')),
        ],
      ),
    );
    if (ok == true) await ref.read(authControllerProvider.notifier).signOut();
  }
}

class _ProfileBody extends ConsumerStatefulWidget {
  const _ProfileBody({super.key, required this.profile});

  final CollectorProfile profile;

  @override
  ConsumerState<_ProfileBody> createState() => _ProfileBodyState();
}

class _ProfileBodyState extends ConsumerState<_ProfileBody> {
  final _formKey = GlobalKey<FormState>();
  late final _fullName = TextEditingController();
  late final _phone = TextEditingController();
  late final _vehicleType = TextEditingController();
  late final _capacityKg = TextEditingController();
  bool _editing = false;
  bool _saving = false;
  String? _error;
  bool _saved = false;

  @override
  void initState() {
    super.initState();
    _fill(widget.profile);
  }

  void _fill(CollectorProfile p) {
    _fullName.text = p.fullName;
    _phone.text = p.phone ?? '';
    _vehicleType.text = p.vehicleType;
    _capacityKg.text = p.capacityKg.toString();
  }

  @override
  void dispose() {
    _fullName.dispose();
    _phone.dispose();
    _vehicleType.dispose();
    _capacityKg.dispose();
    super.dispose();
  }

  void _startEditing() => setState(() {
        _fill(widget.profile);
        _editing = true;
        _saved = false;
        _error = null;
      });

  void _cancel() => setState(() {
        _fill(widget.profile);
        _editing = false;
        _error = null;
      });

  Future<void> _save() async {
    if (_saving || !_formKey.currentState!.validate()) return;
    FocusScope.of(context).unfocus();
    setState(() {
      _saving = true;
      _error = null;
    });
    try {
      final updated = await ref.read(collectorApiProvider).updateMyProfile(
            fullName: _fullName.text.trim(),
            phone: _phone.text.trim().isEmpty ? null : _phone.text.trim(),
            vehicleType: _vehicleType.text.trim(),
            capacityKg: parseDecimal(_capacityKg.text) ?? 0,
          );
      await ref.read(authControllerProvider.notifier).updateFullName(updated.fullName);
      ref.invalidate(collectorProfileProvider);
      if (mounted) {
        setState(() {
          _editing = false;
          _saved = true;
        });
      }
    } catch (e) {
      if (mounted) setState(() => _error = apiErrorMessage(e, 'Could not save your profile.'));
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final p = widget.profile;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        GlassCard(
          child: Row(
            children: [
              Container(
                width: 56,
                height: 56,
                alignment: Alignment.center,
                decoration: BoxDecoration(
                    gradient: AppColors.brandGradient, borderRadius: BorderRadius.circular(AppRadius.tile)),
                child: Text(Format.initials(p.fullName), style: AppText.display(20, color: Colors.white)),
              ),
              const SizedBox(width: 14),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(p.fullName.isEmpty ? 'Collector' : p.fullName, style: AppText.display(18)),
                    const SizedBox(height: 2),
                    Text(p.email, style: AppText.small),
                    const SizedBox(height: 6),
                    Wrap(
                      spacing: 12,
                      runSpacing: 4,
                      children: [
                        _Fact(LucideIcons.star, '${p.rating.toStringAsFixed(1)} rating'),
                        _Fact(p.isAvailable ? LucideIcons.circleCheck : LucideIcons.circleOff,
                            p.isAvailable ? 'Online' : 'Offline'),
                        _Fact(LucideIcons.truck, '${p.activeJobCount}/${p.maxActiveJobs} active jobs'),
                      ],
                    ),
                  ],
                ),
              ),
            ],
          ),
        ),
        const SizedBox(height: 16),
        if (_saved && !_editing) ...[
          const Notice(tone: NoticeTone.success, message: 'Your profile was updated.'),
          const SizedBox(height: 16),
        ],
        GlassCard(
          child: _editing ? _form() : _details(p),
        ),
      ],
    );
  }

  Widget _details(CollectorProfile p) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Row(
          children: [
            Expanded(child: Text('Details', style: AppText.display(16))),
            AppButton.secondary(label: 'Edit', icon: LucideIcons.pencil, onPressed: _startEditing),
          ],
        ),
        const SizedBox(height: 12),
        _Row('Full name', p.fullName.isEmpty ? '—' : p.fullName),
        _Row('Email', p.email.isEmpty ? '—' : p.email),
        _Row('Phone', (p.phone ?? '').isEmpty ? '—' : p.phone!),
        _Row('Vehicle', p.vehicleType.isEmpty ? '—' : p.vehicleType),
        _Row('Capacity', Format.kg(p.capacityKg)),
      ],
    );
  }

  Widget _form() {
    return Form(
      key: _formKey,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text('Edit details', style: AppText.display(16)),
          const SizedBox(height: 16),
          LabeledField(
            label: 'Full name',
            child: TextFormField(
              controller: _fullName,
              textInputAction: TextInputAction.next,
              validator: (v) => (v == null || v.trim().isEmpty) ? 'Enter your name.' : null,
            ),
          ),
          const SizedBox(height: 16),
          LabeledField(
            label: 'Phone',
            child: TextFormField(
              controller: _phone,
              keyboardType: TextInputType.phone,
              textInputAction: TextInputAction.next,
              decoration: const InputDecoration(hintText: 'e.g. 0771234567'),
            ),
          ),
          const SizedBox(height: 16),
          LabeledField(
            label: 'Vehicle type',
            child: TextFormField(
              controller: _vehicleType,
              textInputAction: TextInputAction.next,
              decoration: const InputDecoration(hintText: 'e.g. Van, Three-wheeler, Truck'),
              validator: (v) => (v == null || v.trim().isEmpty) ? 'Enter your vehicle type.' : null,
            ),
          ),
          const SizedBox(height: 16),
          LabeledField(
            label: 'Capacity',
            help: 'How much your vehicle can carry. Used to match jobs to you.',
            child: DecimalField(controller: _capacityKg, hint: '0.0'),
          ),
          if (_error != null) ...[
            const SizedBox(height: 16),
            Notice(tone: NoticeTone.error, message: _error),
          ],
          const SizedBox(height: 20),
          Row(
            children: [
              Expanded(
                child: AppButton.secondary(label: 'Cancel', expand: true, onPressed: _saving ? null : _cancel),
              ),
              const SizedBox(width: 12),
              Expanded(
                child: AppButton(label: _saving ? 'Saving…' : 'Save', expand: true, loading: _saving, onPressed: _save),
              ),
            ],
          ),
        ],
      ),
    );
  }

}

class _Row extends StatelessWidget {
  const _Row(this.label, this.value);

  final String label;
  final String value;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 8),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          SizedBox(width: 110, child: Text(label.toUpperCase(), style: AppText.label)),
          Expanded(child: Text(value, style: AppText.strong)),
        ],
      ),
    );
  }
}

class _Fact extends StatelessWidget {
  const _Fact(this.icon, this.text);

  final IconData icon;
  final String text;

  @override
  Widget build(BuildContext context) {
    return Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        Icon(icon, size: 13, color: AppColors.ink600),
        const SizedBox(width: 4),
        Text(text, style: AppText.small)
      ],
    );
  }
}
