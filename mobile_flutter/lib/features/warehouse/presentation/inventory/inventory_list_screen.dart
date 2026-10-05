import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';

import '../../../../core/network/api_error.dart';
import '../../../../core/theme/app_colors.dart';
import '../../../../core/theme/app_theme.dart';
import '../../../../core/widgets/feedback.dart';
import '../../../../core/widgets/glass_card.dart';
import '../../../../core/widgets/layout.dart';
import '../../application/warehouse_providers.dart';
import '../../data/processing_enums.dart';
import '../../data/warehouse_models.dart';
import '../widgets/inventory_tile.dart';
import '../../../../core/widgets/pill_tabs.dart';

/// All inventory, newest first: search by item type, filter by kind and status, load more while scrolling.
class InventoryListScreen extends ConsumerStatefulWidget {
  const InventoryListScreen({super.key, this.initialStatus});

  final InventoryStatus? initialStatus;

  @override
  ConsumerState<InventoryListScreen> createState() => _InventoryListScreenState();
}

class _InventoryListScreenState extends ConsumerState<InventoryListScreen> {
  late InventoryStatus? _status = widget.initialStatus;
  ItemKind? _kind;
  final _search = TextEditingController();
  final _scroll = ScrollController();
  Timer? _debounce;

  final List<InventoryListItem> _items = [];
  int _page = 0;
  int _totalPages = 1;
  int _totalCount = 0;
  bool _loading = false;
  String? _error;

  // Ignores responses for a filter the user has already moved away from.
  int _requestId = 0;

  @override
  void initState() {
    super.initState();
    _scroll.addListener(() {
      if (_scroll.position.pixels > _scroll.position.maxScrollExtent - 300) _loadMore();
    });
    _reload();
  }

  @override
  void didUpdateWidget(covariant InventoryListScreen oldWidget) {
    super.didUpdateWidget(oldWidget);
    // Opening the tab again with a different ?status= (e.g. from a Home tile) changes the filter.
    if (oldWidget.initialStatus != widget.initialStatus) {
      _status = widget.initialStatus;
      _reload();
    }
  }

  @override
  void dispose() {
    _debounce?.cancel();
    _search.dispose();
    _scroll.dispose();
    super.dispose();
  }

  Future<void> _reload() async {
    _items.clear();
    _page = 0;
    _totalPages = 1;
    _totalCount = 0;
    await _fetch(1);
  }

  Future<void> _loadMore() async {
    if (_loading || _error != null || _page >= _totalPages) return;
    await _fetch(_page + 1);
  }

  Future<void> _fetch(int page) async {
    final id = ++_requestId;
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final result = await ref.read(warehouseApiProvider).listInventory(search: _search.text, status: _status, kind: _kind, page: page);
      if (!mounted || id != _requestId) return;
      setState(() {
        _items.addAll(result.items);
        _page = result.page;
        _totalPages = result.totalPages;
        _totalCount = result.totalCount;
      });
    } catch (e) {
      if (mounted && id == _requestId) setState(() => _error = apiErrorMessage(e, 'Failed to load inventory.'));
    } finally {
      if (mounted && id == _requestId) setState(() => _loading = false);
    }
  }

  void _onSearchChanged(String _) {
    _debounce?.cancel();
    _debounce = Timer(const Duration(milliseconds: 350), _reload);
  }

  void _setKind(ItemKind? kind) {
    if (kind == _kind) return;
    setState(() => _kind = kind);
    _reload();
  }

  void _setStatus(InventoryStatus? status) {
    if (status == _status) return;
    setState(() => _status = status);
    _reload();
  }

  @override
  Widget build(BuildContext context) {
    return RefreshIndicator(
      color: AppColors.mint600,
      onRefresh: _reload,
      child: ListView(
        controller: _scroll,
        physics: const AlwaysScrollableScrollPhysics(),
        padding: const EdgeInsets.fromLTRB(16, 16, 16, 110),
        children: [
          ResponsiveCenter(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                PageHeader(
                  title: 'Inventory',
                  subtitle: _loading && _items.isEmpty ? 'Loading…' : '$_totalCount item${_totalCount == 1 ? '' : 's'}',
                  icon: LucideIcons.boxes,
                ),
                TextField(
                  controller: _search,
                  onChanged: _onSearchChanged,
                  maxLength: Limits.search,
                  textInputAction: TextInputAction.search,
                  decoration: const InputDecoration(
                    hintText: 'Search item type…',
                    prefixIcon: Icon(LucideIcons.search, size: 16, color: AppColors.ink600),
                    counterText: '',
                  ),
                ),
                const SizedBox(height: 12),
                // Whole items come in at the dock; parts and materials come from dismantling.
                PillTabs<ItemKind?>(
                  value: _kind,
                  options: const {
                    null: 'All',
                    ItemKind.unit: 'Whole items',
                    ItemKind.component: 'Parts',
                    ItemKind.material: 'Materials',
                  },
                  onChanged: _setKind,
                ),
                const SizedBox(height: 10),
                SizedBox(
                  height: 38,
                  child: ListView(
                    scrollDirection: Axis.horizontal,
                    children: [
                      _FilterChip(label: 'All', selected: _status == null, onTap: () => _setStatus(null)),
                      for (final s in InventoryStatus.values)
                        _FilterChip(label: s.label, selected: _status == s, onTap: () => _setStatus(s)),
                    ],
                  ),
                ),
                const SizedBox(height: 14),
                if (_kind == ItemKind.material)
                  Padding(
                    padding: const EdgeInsets.only(bottom: 12),
                    child: Tile(
                      color: AppColors.mint50,
                      onTap: () => context.go('/warehouse/materials'),
                      child: const Row(
                        children: [
                          Icon(LucideIcons.packageOpen, size: 18, color: AppColors.mint700),
                          SizedBox(width: 10),
                          Expanded(child: Text('See the total weight of each material', style: AppText.strong)),
                          Icon(LucideIcons.chevronRight, size: 18, color: AppColors.mint700),
                        ],
                      ),
                    ),
                  ),
                if (_error != null && _items.isEmpty)
                  ErrorMessage(message: _error!, onRetry: _reload)
                else if (_loading && _items.isEmpty)
                  const GlassCard(child: LoadingState(label: 'Loading inventory…'))
                else if (_items.isEmpty)
                  GlassCard(
                    child: EmptyState(
                      icon: LucideIcons.boxes,
                      title: _search.text.isEmpty && _status == null && _kind == null ? 'No inventory yet' : 'No items match',
                      description: _search.text.isEmpty && _status == null && _kind == null
                          ? 'Receive a job or an extra-waste drop-off to get started.'
                          : 'Try a different search or filter.',
                    ),
                  )
                else ...[
                  for (final item in _items) InventoryTile(item: item),
                  if (_loading)
                    const Padding(
                      padding: EdgeInsets.all(16),
                      child: Center(child: SizedBox(width: 22, height: 22, child: CircularProgressIndicator(strokeWidth: 2))),
                    ),
                  if (_error != null) ErrorMessage(message: _error!, onRetry: _loadMore),
                ],
              ],
            ),
          ),
        ],
      ),
    );
  }
}

class _FilterChip extends StatelessWidget {
  const _FilterChip({required this.label, required this.selected, required this.onTap});

  final String label;
  final bool selected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.only(right: 6),
      child: Semantics(
        selected: selected,
        button: true,
        child: InkWell(
          onTap: onTap,
          borderRadius: BorderRadius.circular(999),
          child: AnimatedContainer(
            duration: const Duration(milliseconds: 160),
            padding: const EdgeInsets.symmetric(horizontal: 16),
            alignment: Alignment.center,
            decoration: BoxDecoration(
              color: selected ? AppColors.mint600 : Colors.white.withValues(alpha: 0.6),
              borderRadius: BorderRadius.circular(999),
              border: Border.all(color: selected ? AppColors.mint600 : AppColors.mint100),
            ),
            child: Text(
              label,
              style: TextStyle(fontSize: 13, fontWeight: FontWeight.w600, color: selected ? Colors.white : AppColors.ink800),
            ),
          ),
        ),
      ),
    );
  }
}
