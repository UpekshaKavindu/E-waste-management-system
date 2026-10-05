import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';

import '../../../core/network/api_error.dart';
import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_theme.dart';
import '../../../core/widgets/app_button.dart';
import '../application/buyer_providers.dart';

/// Bottom sheet for raising a new material request. Pops true once the request is
/// accepted so the caller can refresh its list.
class RequestMaterialSheet extends ConsumerStatefulWidget {
  const RequestMaterialSheet({super.key});

  @override
  ConsumerState<RequestMaterialSheet> createState() => _RequestMaterialSheetState();
}

class _RequestMaterialSheetState extends ConsumerState<RequestMaterialSheet> {
  final _formKey = GlobalKey<FormState>();
  final _materialType = TextEditingController();
  final _quantityKg = TextEditingController();
  bool _saving = false;
  String? _error;

  @override
  void dispose() {
    _materialType.dispose();
    _quantityKg.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => Padding(
        padding: EdgeInsets.only(bottom: MediaQuery.viewInsetsOf(context).bottom),
        child: Container(
          padding: const EdgeInsets.fromLTRB(20, 14, 20, 22),
          decoration: const BoxDecoration(color: Colors.white, borderRadius: BorderRadius.vertical(top: Radius.circular(22))),
          child: Form(
            key: _formKey,
            child: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Row(children: [
                  const Expanded(child: Text('Request material', style: AppText.strong)),
                  IconButton(tooltip: 'Close', onPressed: () => Navigator.pop(context), icon: const Icon(LucideIcons.x)),
                ]),
                const SizedBox(height: 8),
                TextFormField(
                  controller: _materialType,
                  maxLength: 100,
                  textCapitalization: TextCapitalization.words,
                  decoration: const InputDecoration(labelText: 'Material type', hintText: 'e.g. Copper'),
                  validator: (value) => value == null || value.trim().isEmpty ? 'Enter a material type.' : null,
                ),
                const SizedBox(height: 10),
                TextFormField(
                  controller: _quantityKg,
                  keyboardType: const TextInputType.numberWithOptions(decimal: true),
                  decoration: const InputDecoration(
                    labelText: 'Quantity (kg)',
                    helperText: 'Export buyers must request at least 20 kg.',
                  ),
                  validator: (value) {
                    final quantity = double.tryParse(value?.trim() ?? '');
                    if (quantity == null || quantity <= 0 || quantity > 1000000) {
                      return 'Enter a quantity greater than 0 and no more than 1,000,000 kg.';
                    }
                    return null;
                  },
                ),
                if (_error != null) ...[
                  const SizedBox(height: 10),
                  Text(_error!, style: const TextStyle(color: AppColors.red600, fontSize: 13)),
                ],
                const SizedBox(height: 14),
                AppButton(
                  label: _saving ? 'Submitting…' : 'Submit request',
                  icon: LucideIcons.packagePlus,
                  loading: _saving,
                  expand: true,
                  onPressed: _submit,
                ),
              ],
            ),
          ),
        ),
      );

  Future<void> _submit() async {
    if (_saving || !_formKey.currentState!.validate()) return;
    setState(() {
      _saving = true;
      _error = null;
    });
    try {
      await ref.read(buyerApiProvider).requestMaterial(
            materialType: _materialType.text,
            quantityKg: double.parse(_quantityKg.text.trim()),
          );
      ref.invalidate(buyerRequestsProvider);
      if (mounted) Navigator.pop(context, true);
    } catch (error) {
      if (mounted) setState(() => _error = apiErrorMessage(error, 'Could not submit the material request.'));
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }
}

/// Opens [RequestMaterialSheet]; resolves to true when a request was created.
Future<bool> showRequestMaterialSheet(BuildContext context) async {
  final created = await showModalBottomSheet<bool>(
    context: context,
    isScrollControlled: true,
    useSafeArea: true,
    backgroundColor: Colors.transparent,
    builder: (_) => const RequestMaterialSheet(),
  );
  return created == true;
}
