import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';

import '../../../core/auth/auth_controller.dart';
import '../../../core/network/api_error.dart';
import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_theme.dart';
import '../../../core/widgets/app_button.dart';
import '../../../core/widgets/feedback.dart';
import '../../../core/widgets/form_fields.dart';
import '../../../core/widgets/glass_card.dart';
import '../../../core/widgets/layout.dart';
import '../../../core/widgets/photo_picker_field.dart';
import '../application/submissions_providers.dart';
import '../data/submission_field_errors.dart';
import '../data/submission_models.dart';

// Same limit as the backend (CreateSubmissionDtoValidator.MaxItems): each item is classified on its own.
const _maxItems = 3;

/// Submit form for Household/Corporate: address, phone, category, weight, and 1-10 repeatable
/// items — the same fields and limits as the web app's SubmitPage (CreateSubmissionDtoValidator
/// on the backend enforces the same rules either way).
class SubmitItemScreen extends ConsumerStatefulWidget {
  const SubmitItemScreen({super.key, required this.onSubmitted});

  /// Called once a submission is created, so the shell can switch to My Submissions.
  final VoidCallback onSubmitted;

  @override
  ConsumerState<SubmitItemScreen> createState() => _SubmitItemScreenState();
}

class _SubmitItemScreenState extends ConsumerState<SubmitItemScreen> {
  final _formKey = GlobalKey<FormState>();
  final _pickupAddress = TextEditingController();
  final _phoneNumber = TextEditingController();
  final _weight = TextEditingController();
  String? _category;
  List<SubmissionItemDraft> _items = [SubmissionItemDraft()];

  bool _loading = false;
  String? _generalError;
  SubmissionFieldErrors? _fieldErrors;

  @override
  void dispose() {
    _pickupAddress.dispose();
    _phoneNumber.dispose();
    _weight.dispose();
    super.dispose();
  }

  void _addItem() {
    if (_items.length >= _maxItems) return;
    setState(() => _items = [..._items, SubmissionItemDraft()]);
  }

  void _removeItem(int index) {
    if (_items.length <= 1) return;
    setState(() => _items = [..._items]..removeAt(index));
  }

  Future<void> _submit() async {
    if (_loading || !_formKey.currentState!.validate() || _category == null) {
      if (_category == null) setState(() => _generalError = 'Choose a category.');
      return;
    }
    setState(() {
      _loading = true;
      _generalError = null;
      _fieldErrors = null;
    });
    try {
      await ref.read(submissionsApiProvider).create(
            category: _category!,
            estimatedWeight: parseDecimal(_weight.text) ?? 0,
            pickupAddress: _pickupAddress.text.trim(),
            phoneNumber: _phoneNumber.text.trim(),
            items: _items,
          );
      if (!mounted) return;
      ref.invalidate(mySubmissionsProvider);
      _resetForm();
      widget.onSubmitted();
    } catch (e) {
      if (!mounted) return;
      final fields = extractSubmissionFieldErrors(e);
      setState(() {
        if (fields != null) {
          _fieldErrors = fields;
        } else {
          _generalError = apiErrorMessage(e, 'Failed to submit. Please try again.');
        }
      });
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  void _resetForm() {
    _pickupAddress.clear();
    _phoneNumber.clear();
    _weight.clear();
    setState(() {
      _category = null;
      _items = [SubmissionItemDraft()];
    });
    _formKey.currentState?.reset();
  }

  @override
  Widget build(BuildContext context) {
    // CSV upload (up to 100 rows) is a web-only, corporate-only feature; households never see this.
    final isCorporate = ref.watch(authControllerProvider).user?.role.toLowerCase() == 'corporate';
    return ListView(
      padding: const EdgeInsets.fromLTRB(16, 16, 16, 32),
      children: [
        const PageHeader(
          title: 'Submit e-waste',
          subtitle: 'Our AI assesses hazard level and category automatically.',
          icon: LucideIcons.cpu,
        ),
        if (isCorporate) ...[
          const Notice(
            tone: NoticeTone.info,
            title: 'Submitting a lot of items?',
            message: 'Bulk upload is available on the web: sign in there and use "Upload CSV" '
                'to send up to 100 rows in one submission.',
          ),
          const SizedBox(height: 12),
        ],
        GlassCard(
          child: Form(
            key: _formKey,
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                LabeledField(
                  label: 'Category',
                  help: _fieldErrors?.category,
                  helpColor: AppColors.red600,
                  child: AppDropdown<String>(
                    value: _category,
                    hint: 'Select a category…',
                    items: [for (final c in submissionCategories) DropdownMenuItem(value: c, child: Text(c))],
                    onChanged: (v) => setState(() => _category = v),
                  ),
                ),
                const SizedBox(height: 16),
                LabeledField(
                  label: 'Estimated weight (kg)',
                  help: _fieldErrors?.estimatedWeight,
                  helpColor: AppColors.red600,
                  child: DecimalField(controller: _weight, hint: '0.0'),
                ),
                const SizedBox(height: 16),
                LabeledField(
                  label: 'Pickup address',
                  help: _fieldErrors?.pickupAddress,
                  helpColor: AppColors.red600,
                  child: TextFormField(
                    controller: _pickupAddress,
                    minLines: 1,
                    maxLines: 3,
                    validator: (v) => (v == null || v.trim().isEmpty) ? 'Enter a pickup address.' : null,
                  ),
                ),
                const SizedBox(height: 16),
                LabeledField(
                  label: 'Phone number',
                  help: _fieldErrors?.phoneNumber,
                  helpColor: AppColors.red600,
                  child: TextFormField(
                    controller: _phoneNumber,
                    keyboardType: TextInputType.phone,
                    decoration: const InputDecoration(hintText: '07XXXXXXXX'),
                    validator: (v) => (v == null || v.trim().isEmpty) ? 'Enter a phone number.' : null,
                  ),
                ),
              ],
            ),
          ),
        ),
        const SizedBox(height: 16),
        SectionTitle('Items (${_items.length}/$_maxItems)', icon: LucideIcons.package),
        if (_fieldErrors?.itemsGeneral != null) ...[
          Notice(tone: NoticeTone.error, message: _fieldErrors!.itemsGeneral),
          const SizedBox(height: 8),
        ],
        for (var i = 0; i < _items.length; i++) ...[
          _ItemCard(
            index: i,
            item: _items[i],
            errors: _fieldErrors?.items[i],
            canRemove: _items.length > 1,
            onRemove: () => _removeItem(i),
          ),
          const SizedBox(height: 12),
        ],
        OutlinedButton.icon(
          onPressed: _items.length >= _maxItems ? null : _addItem,
          icon: const Icon(LucideIcons.plus, size: 16),
          label: const Text('Add another item'),
          style: OutlinedButton.styleFrom(
            foregroundColor: AppColors.mint700,
            side: const BorderSide(color: AppColors.mint200),
            shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(AppRadius.input)),
          ),
        ),
        if (_generalError != null) ...[
          const SizedBox(height: 16),
          Notice(tone: NoticeTone.error, message: _generalError),
        ],
        const SizedBox(height: 20),
        AppButton(
          label: _loading ? 'Submitting…' : 'Submit e-waste item',
          loading: _loading,
          expand: true,
          onPressed: _submit,
        ),
      ],
    );
  }
}

class _ItemCard extends StatelessWidget {
  const _ItemCard({required this.index, required this.item, this.errors, required this.canRemove, required this.onRemove});

  final int index;
  final SubmissionItemDraft item;
  final SubmissionItemFieldErrors? errors;
  final bool canRemove;
  final VoidCallback onRemove;

  @override
  Widget build(BuildContext context) {
    return GlassCard(
      padding: const EdgeInsets.all(14),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              Expanded(child: Text('Item ${index + 1}', style: AppText.strong)),
              if (canRemove)
                IconButton(
                  onPressed: onRemove,
                  icon: const Icon(LucideIcons.trash2, size: 18, color: AppColors.red600),
                  visualDensity: VisualDensity.compact,
                ),
            ],
          ),
          LabeledField(
                  label: 'Name',
                  help: errors?.itemName,
                  helpColor: AppColors.red600,
                  child: TextFormField(
              initialValue: item.itemName,
              onChanged: (v) => item.itemName = v,
              decoration: const InputDecoration(hintText: 'e.g. Laptop'),
              validator: (v) => (v == null || v.trim().isEmpty) ? 'Item name is required.' : null,
            ),
                ),
          const SizedBox(height: 12),
          LabeledField(
                  label: 'Description',
                  help: errors?.description,
                  helpColor: AppColors.red600,
                  child: TextFormField(
              initialValue: item.description,
              minLines: 1,
              maxLines: 3,
              onChanged: (v) => item.description = v,
              decoration: const InputDecoration(hintText: 'e.g. Old laptop, screen cracked, still boots'),
              validator: (v) => (v == null || v.trim().isEmpty) ? 'Description is required.' : null,
            ),
                ),
          const SizedBox(height: 12),
          LabeledField(
                  label: 'Photo',
                  help: errors?.imageUrl,
                  helpColor: AppColors.red600,
                  child: PhotoPickerField(imageUrl: item.imageUrl, onChanged: (url) => item.imageUrl = url),
                ),
        ],
      ),
    );
  }
}
