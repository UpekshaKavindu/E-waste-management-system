import React, { useCallback, useEffect, useRef, useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { ArrowDownAZ, ArrowUpAZ, Boxes, PackagePlus, RefreshCw, Search, X } from 'lucide-react';
import { inventoryApi } from './inventoryApi';
import {
  INVENTORY_SORT_FIELDS,
  INVENTORY_SORT_LABELS,
  type InventoryListResponse,
  type InventorySortField,
} from './types';
import {
  CLASSIFICATION_CATEGORIES,
  CLASSIFICATION_CATEGORY_LABELS,
  INVENTORY_STATUSES,
  INVENTORY_STATUS_LABELS,
  ITEM_KINDS,
  ITEM_KIND_LABELS,
  LIMITS,
  ORIGIN_TYPES,
  ORIGIN_TYPE_LABELS,
  isClassificationCategory,
  isInventoryStatus,
  isItemKind,
  isOriginType,
  type ItemKind,
} from '../processingEnums';
import { useWarehouseLocations } from '../hooks/useLookups';
import { useDebouncedValue } from '../hooks/useDebouncedValue';
import { getApiErrorMessage } from '../utils/apiError';
import { formatDate, formatKg } from '../utils/format';
import {
  CategoryBadge,
  EmptyState,
  ErrorMessage,
  GlassCard,
  LoadingState,
  PageHeader,
  Pagination,
  StatusBadge,
  btnPrimary,
  btnSecondary,
  inputClass,
  tableCellClass,
  tableHeadClass,
} from '../components';

const GUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

const KIND_TAB_LABELS: Record<ItemKind, string> = {
  Unit: 'Received items',
  Component: 'Dismantled components',
  Material: 'Recovered materials',
};
const isSortField = (v: unknown): v is InventorySortField => INVENTORY_SORT_FIELDS.includes(v as InventorySortField);

const InventoryListPage: React.FC = () => {
  const navigate = useNavigate();
  const [params, setParams] = useSearchParams();
  const locations = useWarehouseLocations();

  // The URL is the source of truth for filters, so dashboard links, refresh and back/forward all work.
  const statusParam = params.get('status');
  const categoryParam = params.get('category');
  const originParam = params.get('origin');
  const kindParam = params.get('kind');
  const locationParam = params.get('location');
  const sortParam = params.get('sort');
  const searchParam = params.get('q') ?? '';

  const status = isInventoryStatus(statusParam) ? statusParam : undefined;
  const category = isClassificationCategory(categoryParam) ? categoryParam : undefined;
  const originType = isOriginType(originParam) ? originParam : undefined;
  const kind = isItemKind(kindParam) ? kindParam : undefined;
  const locationId = locationParam && GUID_RE.test(locationParam) ? locationParam : undefined;
  const sortBy: InventorySortField = isSortField(sortParam) ? sortParam : 'CreatedAt';
  const descending = params.get('dir') !== 'asc';
  const page = Math.max(1, Number(params.get('page')) || 1);

  const [searchInput, setSearchInput] = useState(searchParam);
  const debouncedSearch = useDebouncedValue(searchInput);

  const [data, setData] = useState<InventoryListResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const requestId = useRef(0);

  const updateParams = useCallback(
    (changes: Record<string, string | null>, replace = false) => {
      setParams(
        (prev) => {
          const next = new URLSearchParams(prev);
          Object.entries(changes).forEach(([key, value]) => {
            if (value === null || value === '') next.delete(key);
            else next.set(key, value);
          });
          if (!('page' in changes)) next.delete('page'); // any filter change goes back to page 1
          return next;
        },
        { replace },
      );
    },
    [setParams],
  );

  // Push the debounced search text into the URL (replace, so typing doesn't flood history).
  useEffect(() => {
    if (debouncedSearch.trim() !== searchParam) updateParams({ q: debouncedSearch.trim() }, true);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [debouncedSearch]);

  const load = useCallback(async () => {
    const id = ++requestId.current;
    setLoading(true);
    setError(null);
    try {
      const result = await inventoryApi.list({
        search: searchParam || undefined,
        status,
        category,
        originType,
        kind,
        locationId,
        sortBy,
        descending,
        page,
        pageSize: LIMITS.pageSize,
      });
      if (id === requestId.current) setData(result);
    } catch (e) {
      if (id === requestId.current) setError(getApiErrorMessage(e, 'Failed to load inventory.'));
    } finally {
      if (id === requestId.current) setLoading(false);
    }
  }, [searchParam, status, category, originType, kind, locationId, sortBy, descending, page]);

  useEffect(() => {
    load();
  }, [load]);

  const hasFilters = Boolean(searchParam || status || category || originType || locationId);
  const clearFilters = () => {
    setSearchInput('');
    setParams(kind ? new URLSearchParams({ kind }) : new URLSearchParams());
  };

  const rows = data?.items ?? [];
  const tabClass = (active: boolean) =>
    `rounded-full px-3.5 py-1.5 text-sm font-semibold transition-colors ${
      active ? 'bg-mint-600 text-white shadow-md shadow-mint-500/30' : 'bg-white/70 text-ink-800 hover:bg-mint-50'
    }`;

  return (
    <div>
      <PageHeader
        title="Inventory"
        subtitle="Every item received into the warehouse, including dismantled components and recovered materials — search, filter and open an item to process it."
        icon={Boxes}
        actions={
          <>
            <button type="button" onClick={load} className={btnSecondary} disabled={loading}>
              <RefreshCw size={14} className={loading ? 'animate-spin' : ''} /> Refresh
            </button>
            <Link to="/processing/receive" className={btnPrimary}>
              <PackagePlus size={14} /> Receive waste
            </Link>
          </>
        }
      />

      <div role="tablist" aria-label="Item kind" className="mb-4 flex flex-wrap gap-2">
        <button type="button" role="tab" aria-selected={!kind} className={tabClass(!kind)} onClick={() => updateParams({ kind: null })}>
          All
        </button>
        {ITEM_KINDS.map((k) => (
          <button key={k} type="button" role="tab" aria-selected={kind === k} className={tabClass(kind === k)} onClick={() => updateParams({ kind: k })}>
            {KIND_TAB_LABELS[k]}
          </button>
        ))}
      </div>

      {/* Filters */}
      <GlassCard className="mb-4">
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-6">
          <div className="relative sm:col-span-2">
            <Search size={16} className="pointer-events-none absolute left-3.5 top-1/2 -translate-y-1/2 text-ink-600" />
            <input
              type="search"
              value={searchInput}
              maxLength={LIMITS.search}
              onChange={(e) => setSearchInput(e.target.value)}
              placeholder="Search item type…"
              aria-label="Search item type"
              className={`${inputClass} pl-10`}
            />
          </div>

          <select
            aria-label="Status"
            value={status ?? ''}
            onChange={(e) => updateParams({ status: e.target.value })}
            className={inputClass}
          >
            <option value="">All statuses</option>
            {INVENTORY_STATUSES.map((s) => (
              <option key={s} value={s}>
                {INVENTORY_STATUS_LABELS[s]}
              </option>
            ))}
          </select>

          <select
            aria-label="Category"
            value={category ?? ''}
            onChange={(e) => updateParams({ category: e.target.value })}
            className={inputClass}
          >
            <option value="">All categories</option>
            {CLASSIFICATION_CATEGORIES.map((c) => (
              <option key={c} value={c}>
                {CLASSIFICATION_CATEGORY_LABELS[c]}
              </option>
            ))}
          </select>

          <select
            aria-label="Origin"
            value={originType ?? ''}
            onChange={(e) => updateParams({ origin: e.target.value })}
            className={inputClass}
          >
            <option value="">All origins</option>
            {ORIGIN_TYPES.map((o) => (
              <option key={o} value={o}>
                {ORIGIN_TYPE_LABELS[o]}
              </option>
            ))}
          </select>

          <select
            aria-label="Warehouse location"
            value={locationId ?? ''}
            onChange={(e) => updateParams({ location: e.target.value })}
            className={inputClass}
          >
            <option value="">All locations</option>
            {locations.data.map((l) => (
              <option key={l.id} value={l.id}>
                {l.name}
              </option>
            ))}
          </select>
        </div>

        <div className="mt-3 flex flex-wrap items-center justify-between gap-3">
          <div className="flex items-center gap-2">
            <label className="text-xs font-mono uppercase tracking-wide text-ink-600" htmlFor="inv-sort">
              Sort by
            </label>
            <select
              id="inv-sort"
              value={sortBy}
              onChange={(e) => updateParams({ sort: e.target.value === 'CreatedAt' ? null : e.target.value })}
              className={`${inputClass} !w-auto`}
            >
              {INVENTORY_SORT_FIELDS.map((f) => (
                <option key={f} value={f}>
                  {INVENTORY_SORT_LABELS[f]}
                </option>
              ))}
            </select>
            <button
              type="button"
              onClick={() => updateParams({ dir: descending ? 'asc' : null })}
              className={`${btnSecondary} !px-3`}
              aria-label={descending ? 'Sorted descending — switch to ascending' : 'Sorted ascending — switch to descending'}
              title={descending ? 'Descending' : 'Ascending'}
            >
              {descending ? <ArrowDownAZ size={14} /> : <ArrowUpAZ size={14} />}
              {descending ? 'Desc' : 'Asc'}
            </button>
          </div>
          {hasFilters && (
            <button type="button" onClick={clearFilters} className="inline-flex items-center gap-1 text-xs font-semibold text-mint-700 hover:underline">
              <X size={12} /> Clear filters
            </button>
          )}
        </div>
      </GlassCard>

      {locations.error && <ErrorMessage className="mb-4" message={locations.error} onRetry={locations.reload} />}

      {/* Results */}
      <GlassCard padded={false}>
        {error ? (
          <div className="p-5">
            <ErrorMessage message={error} onRetry={load} />
          </div>
        ) : loading && !data ? (
          <LoadingState label="Loading inventory…" />
        ) : rows.length === 0 ? (
          <EmptyState
            icon={Boxes}
            title={hasFilters ? 'No items match your filters' : kind ? `No ${KIND_TAB_LABELS[kind].toLowerCase()} yet` : 'No inventory yet'}
            description={
              hasFilters
                ? 'Try removing a filter or searching for a different item type.'
                : 'Items appear here once a completed job or an extra-waste drop-off has been received.'
            }
            action={
              hasFilters ? (
                <button type="button" onClick={clearFilters} className={btnSecondary}>
                  Clear filters
                </button>
              ) : (
                <Link to="/processing/receive" className={btnPrimary}>
                  <PackagePlus size={14} /> Receive waste
                </Link>
              )
            }
          />
        ) : (
          <>
            <div className={`overflow-x-auto transition-opacity ${loading ? 'opacity-60' : ''}`}>
              <table className="w-full min-w-[820px] border-collapse">
                <thead>
                  <tr className="border-b border-mint-100">
                    <th className={`${tableHeadClass} px-4 py-3`}>Item</th>
                    <th className={`${tableHeadClass} px-4 py-3`}>Status</th>
                    <th className={`${tableHeadClass} px-4 py-3`}>Category</th>
                    <th className={`${tableHeadClass} px-4 py-3`}>Origin</th>
                    <th className={`${tableHeadClass} px-4 py-3 text-right`}>Weight</th>
                    <th className={`${tableHeadClass} px-4 py-3`}>Location</th>
                    <th className={`${tableHeadClass} px-4 py-3`}>Received</th>
                  </tr>
                </thead>
                <tbody>
                  {rows.map((item) => (
                    <tr
                      key={item.id}
                      onClick={() => navigate(`/processing/inventory/${item.id}`)}
                      className="cursor-pointer border-b border-mint-50 transition-colors last:border-0 hover:bg-mint-50/70"
                    >
                      <td className={tableCellClass}>
                        <Link
                          to={`/processing/inventory/${item.id}`}
                          onClick={(e) => e.stopPropagation()}
                          className="font-semibold text-ink-900 hover:text-mint-700"
                        >
                          {item.itemType}
                          {(item.quantity ?? 1) > 1 && <span className="font-normal text-ink-600"> × {item.quantity}</span>}
                        </Link>
                        {item.kind !== 'Unit' && <div className="text-[11px] text-ink-600">{ITEM_KIND_LABELS[item.kind]}</div>}
                      </td>
                      <td className={tableCellClass}>
                        <StatusBadge status={item.status} />
                      </td>
                      <td className={tableCellClass}>
                        <CategoryBadge category={item.category} />
                      </td>
                      <td className={tableCellClass}>{ORIGIN_TYPE_LABELS[item.originType] ?? item.originType}</td>
                      <td className={`${tableCellClass} text-right font-mono`}>{formatKg(item.verifiedWeightKg)}</td>
                      <td className={tableCellClass}>{item.currentLocationName}</td>
                      <td className={`${tableCellClass} whitespace-nowrap`}>{formatDate(item.receivedAt)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <div className="px-4 pb-4">
              <Pagination
                page={data?.page ?? page}
                totalPages={data?.totalPages ?? 1}
                totalCount={data?.totalCount ?? 0}
                pageSize={data?.pageSize ?? LIMITS.pageSize}
                disabled={loading}
                onPageChange={(p) => updateParams({ page: p === 1 ? null : String(p) })}
              />
            </div>
          </>
        )}
      </GlassCard>
    </div>
  );
};

export default InventoryListPage;
