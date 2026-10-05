import React, { useCallback, useEffect, useRef, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import {
  ArrowLeft, Boxes, CheckCircle2, Inbox, ListChecks, Lock, MapPin, RefreshCw, ShieldAlert,
  ShoppingCart, Ship, Tag, Wrench, ArrowRight, PackageOpen, Route as RouteIcon, type LucideIcon,
} from 'lucide-react';
import { inventoryApi } from './inventoryApi';
import type { InventoryChild, InventoryDetail, ProcessingLogEntry } from './types';
import ClassifyModal from './ClassifyModal';
import DismantleModal from './DismantleModal';
import MoveLocationModal from './MoveLocationModal';
import TransitionModal from './TransitionModal';
import OnHoldReviewPanel from './OnHoldReviewPanel';
import ReceiptDetailModal from '../Receive/ReceiptDetailModal';
import {
  CLASSIFICATION_SOURCE_LABELS,
  INVENTORY_STATUS_LABELS,
  ITEM_KIND_LABELS,
  ORIGIN_TYPE_LABELS,
  canAddDismantleStep,
  canClassify,
  getManualTransitions,
  isInventoryStatus,
  isTerminalStatus,
  type InventoryStatus,
} from '../processingEnums';
import { useWarehouseLocations } from '../hooks/useLookups';
import { useCurrentUser } from '../hooks/useCurrentUser';
import { getApiErrorMessage, getApiErrorStatus } from '../utils/apiError';
import { formatDateTime, formatKg, shortId } from '../utils/format';
import {
  CategoryBadge,
  EmptyState,
  ErrorMessage,
  GlassCard,
  LoadingState,
  Notice,
  StatusBadge,
  StatusStepper,
  btnDanger,
  btnPrimary,
  btnSecondary,
  tableCellClass,
  tableHeadClass,
} from '../components';

type ModalState =
  | { type: 'classify' }
  | { type: 'dismantle' }
  | { type: 'move' }
  | { type: 'transition'; next: InventoryStatus }
  | null;

const TRANSITION_BUTTON_LABELS: Partial<Record<InventoryStatus, string>> = {
  Sorting: 'Start sorting',
  ReadyForSale: 'Mark ready for sale',
  ExportOnly: 'Reserve for export',
  OnHold: 'Put on hold',
};

const HISTORY_ICONS: Record<string, LucideIcon> = {
  Received: Inbox,
  Recovered: PackageOpen,
  Sorting: ListChecks,
  Dismantling: Wrench,
  DismantleStep: Wrench,
  Classified: Tag,
  ReadyForSale: ShoppingCart,
  ExportOnly: Ship,
  OnHold: ShieldAlert,
  LocationMoved: MapPin,
};

const historyLabel = (action: string): string => {
  if (action === 'DismantleStep') return 'Dismantle step';
  if (action === 'LocationMoved') return 'Location moved';
  if (action === 'Received') return 'Received at warehouse';
  return isInventoryStatus(action) ? `Status → ${INVENTORY_STATUS_LABELS[action]}` : action;
};

const OutputTable: React.FC<{
  title: string;
  empty: string;
  rows: InventoryChild[];
  nameHeader: string;
  footer?: React.ReactNode;
}> = ({ title, empty, rows, nameHeader, footer }) => (
  <GlassCard padded={false}>
    <div className="px-5 pb-2 pt-5">
      <h3 className="font-display text-base font-bold text-ink-900">{title}</h3>
    </div>
    {rows.length === 0 ? (
      <p className="px-5 pb-5 text-sm text-ink-600">{empty}</p>
    ) : (
      <div className="overflow-x-auto">
        <table className="w-full min-w-[420px] border-collapse">
          <thead>
            <tr className="border-b border-mint-100">
              <th className={`${tableHeadClass} px-5 py-2`}>{nameHeader}</th>
              <th className={`${tableHeadClass} px-5 py-2`}>Status</th>
              <th className={`${tableHeadClass} px-5 py-2 text-right`}>Weight</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((c) => (
              <tr key={c.id} className="border-b border-mint-50 last:border-0">
                <td className={`${tableCellClass} px-5`}>
                  <Link to={`/processing/inventory/${c.id}`} className="font-semibold text-ink-900 hover:text-mint-700">
                    {c.itemType}
                  </Link>
                </td>
                <td className={`${tableCellClass} px-5`}>
                  <StatusBadge status={c.status} />
                </td>
                <td className={`${tableCellClass} px-5 text-right font-mono`}>{formatKg(c.verifiedWeightKg)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    )}
    {rows.length > 0 && footer && <div className="border-t border-mint-50 px-5 py-3">{footer}</div>}
  </GlassCard>
);

const DetailRow: React.FC<{ label: string; children: React.ReactNode }> = ({ label, children }) => (
  <div className="flex flex-col gap-0.5 sm:flex-row sm:items-baseline sm:gap-4">
    <dt className="w-40 flex-shrink-0 text-[11px] font-mono uppercase tracking-wide text-ink-600">{label}</dt>
    <dd className="min-w-0 break-words text-sm text-ink-900">{children}</dd>
  </div>
);

const InventoryDetailPage: React.FC = () => {
  const { id } = useParams<{ id: string }>();
  const user = useCurrentUser();
  const locations = useWarehouseLocations();

  const [item, setItem] = useState<InventoryDetail | null>(null);
  const [history, setHistory] = useState<ProcessingLogEntry[]>([]);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notFound, setNotFound] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const [modal, setModal] = useState<ModalState>(null);
  const [receiptOpen, setReceiptOpen] = useState(false);
  const requestId = useRef(0);

  const load = useCallback(
    async (silent = false) => {
      if (!id) return;
      const reqId = ++requestId.current;
      if (silent) setRefreshing(true);
      else setLoading(true);
      setError(null);
      try {
        const [detail, log] = await Promise.all([inventoryApi.get(id), inventoryApi.history(id)]);
        if (reqId !== requestId.current) return;
        setItem(detail);
        setHistory(log);
        setNotFound(false);
      } catch (e) {
        if (reqId !== requestId.current) return;
        if (getApiErrorStatus(e) === 404) setNotFound(true);
        else setError(getApiErrorMessage(e, 'Failed to load this inventory item.'));
      } finally {
        if (reqId === requestId.current) {
          setLoading(false);
          setRefreshing(false);
        }
      }
    },
    [id],
  );

  useEffect(() => {
    setItem(null);
    setNotice(null);
    setModal(null);
    load();
  }, [load]);

  const handleDone = (message: string) => {
    setModal(null);
    setNotice(message);
    load(true);
  };

  if (loading && !item) {
    return <LoadingState label="Loading item…" />;
  }

  if (notFound) {
    return (
      <GlassCard>
        <EmptyState
          icon={Boxes}
          title="Item not found"
          description="This inventory item does not exist or was removed."
          action={
            <Link to="/processing/inventory" className={btnPrimary}>
              <ArrowLeft size={14} /> Back to inventory
            </Link>
          }
        />
      </GlassCard>
    );
  }

  if (!item) {
    return (
      <div>
        <Link to="/processing/inventory" className="mb-4 inline-flex items-center gap-1 text-sm font-semibold text-mint-700 hover:underline">
          <ArrowLeft size={14} /> Inventory
        </Link>
        <ErrorMessage message={error ?? 'Failed to load this inventory item.'} onRetry={() => load()} />
      </div>
    );
  }

  const status = item.status;
  const manualTransitions = getManualTransitions(status, item.classification?.category);
  const showDismantle = canAddDismantleStep(status);
  const showClassify = canClassify(status);
  const terminal = isTerminalStatus(status);
  const classification = item.classification;
  const historyNewestFirst = [...history].reverse();
  const components = item.children.filter((c) => c.kind !== 'Material');
  const materials = item.children.filter((c) => c.kind === 'Material');

  return (
    <div>
      <Link to="/processing/inventory" className="mb-4 inline-flex items-center gap-1 text-sm font-semibold text-mint-700 hover:underline">
        <ArrowLeft size={14} /> Inventory
      </Link>

      {notice && (
        <Notice tone="success" className="mb-4">
          {notice}
        </Notice>
      )}
      {error && <ErrorMessage className="mb-4" message={error} onRetry={() => load(true)} />}

      {/* Header */}
      <GlassCard className="mb-5">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div className="min-w-0">
            <h2 className="font-display text-2xl font-bold text-ink-900">
              {item.itemType}
              {(item.quantity ?? 1) > 1 && <span className="font-normal text-ink-600"> × {item.quantity}</span>}
            </h2>
            {(item.quantity ?? 1) > 1 && (
              <p className="mt-1 text-sm text-ink-600">A lot of {item.quantity} units received together — weight and status apply to the whole lot.</p>
            )}
            <p className="mt-1 break-all font-mono text-[11px] text-ink-600">{item.id}</p>
            <div className="mt-3 flex flex-wrap items-center gap-2">
              <StatusBadge status={status} />
              <CategoryBadge category={classification?.category} />
              <span className="rounded-full bg-ink-100 px-2.5 py-1 text-xs font-medium text-ink-800">{ORIGIN_TYPE_LABELS[item.originType]}</span>
              {item.kind !== 'Unit' && (
                <span className="rounded-full bg-violet-100 px-2.5 py-1 text-xs font-medium text-violet-800">{ITEM_KIND_LABELS[item.kind]}</span>
              )}
            </div>
          </div>
          <button type="button" onClick={() => load(true)} className={btnSecondary} disabled={refreshing}>
            <RefreshCw size={14} className={refreshing ? 'animate-spin' : ''} /> Refresh
          </button>
        </div>
        <div className="mt-6">
          <StatusStepper status={status} />
        </div>
      </GlassCard>

      <div className="grid gap-5 lg:grid-cols-3">
        {/* Left column */}
        <div className="space-y-5 lg:col-span-2">
          {status === 'OnHold' && <OnHoldReviewPanel item={item} history={history} currentUserId={user?.userId} />}

          <GlassCard>
            <h3 className="mb-3 font-display text-base font-bold text-ink-900">Details</h3>
            <dl className="space-y-2.5">
              <DetailRow label="Weight">{formatKg(item.verifiedWeightKg)}</DetailRow>
              <DetailRow label="Location">{item.currentLocationName}</DetailRow>
              <DetailRow label="Received">{formatDateTime(item.receivedAt)}</DetailRow>
              <DetailRow label="Origin">{ORIGIN_TYPE_LABELS[item.originType]}</DetailRow>
              {item.jobId && (
                <DetailRow label="Job">
                  <span className="font-mono text-xs">{item.jobId}</span>
                </DetailRow>
              )}
              {item.extraWasteReceiptId && (
                <DetailRow label="Extra-waste receipt">
                  <span className="font-mono text-xs">{item.extraWasteReceiptId}</span>{' '}
                  <button type="button" onClick={() => setReceiptOpen(true)} className="ml-1 text-xs font-semibold text-mint-700 hover:underline">
                    View receipt
                  </button>
                </DetailRow>
              )}
              {item.submissionId && (
                <DetailRow label="Submission">
                  <span className="font-mono text-xs">{item.submissionId}</span>
                </DetailRow>
              )}
              {item.parentInventoryItemId && (
                <DetailRow label="Parent item">
                  <Link to={`/processing/inventory/${item.parentInventoryItemId}`} className="inline-flex items-center gap-1 font-semibold text-mint-700 hover:underline">
                    {shortId(item.parentInventoryItemId)} <ArrowRight size={12} />
                  </Link>
                </DetailRow>
              )}
            </dl>
          </GlassCard>

          <GlassCard>
            <h3 className="mb-3 font-display text-base font-bold text-ink-900">Classification</h3>
            {classification ? (
              <dl className="space-y-2.5">
                <DetailRow label="Category">
                  <CategoryBadge category={classification.category} />
                </DetailRow>
                <DetailRow label="Sub-category">{classification.subCategory || '—'}</DetailRow>
                <DetailRow label="Classified by">{CLASSIFICATION_SOURCE_LABELS[classification.source]}</DetailRow>
                <DetailRow label="Confidence">
                  {classification.confidenceScore === null ? '—' : `${Math.round(classification.confidenceScore * 100)}%`}
                </DetailRow>
                <DetailRow label="Final">{classification.isFinal ? 'Yes' : 'No'}</DetailRow>
                <DetailRow label="Classified at">{formatDateTime(classification.classifiedAt)}</DetailRow>
                <DetailRow label="Classified by staff">
                  {classification.classifiedByStaffId
                    ? classification.classifiedByStaffId === user?.userId
                      ? 'You'
                      : shortId(classification.classifiedByStaffId)
                    : '—'}
                </DetailRow>
              </dl>
            ) : (
              <p className="text-sm text-ink-600">Not classified yet. Classification becomes available once the item is being sorted.</p>
            )}
          </GlassCard>

          <OutputTable
            title="Dismantled components"
            nameHeader="Component"
            rows={components}
            empty="No components have been split off this item."
          />
          <OutputTable
            title="Recovered materials"
            nameHeader="Material"
            rows={materials}
            empty="No materials have been recovered from this item."
            footer={
              <Link to="/processing/material-stock" className="inline-flex items-center gap-1 text-sm font-semibold text-mint-700 hover:underline">
                See all material stock <ArrowRight size={13} />
              </Link>
            }
          />
        </div>

        {/* Right column */}
        <div className="space-y-5">
          <GlassCard>
            <h3 className="mb-3 font-display text-base font-bold text-ink-900">Actions</h3>

            {terminal && (
              <Notice tone={status === 'OnHold' ? 'error' : 'info'} className="mb-3">
                <span className="flex items-start gap-1.5">
                  <Lock size={14} className="mt-0.5 flex-shrink-0" />
                  <span>
                    {status === 'OnHold'
                      ? 'This item is on hold (quarantined). Its status is final and cannot be released from here.'
                      : `This item is ${INVENTORY_STATUS_LABELS[status].toLowerCase()}. Its status is final.`}
                  </span>
                </span>
              </Notice>
            )}

            <div className="flex flex-col gap-2">
              {(status === 'Received' || status === 'Recovered') &&
                manualTransitions.includes('Sorting') && (
                  <button type="button" className={btnPrimary} onClick={() => setModal({ type: 'transition', next: 'Sorting' })}>
                    <ListChecks size={15} /> {TRANSITION_BUTTON_LABELS.Sorting}
                  </button>
                )}

              {showDismantle && (
                <button type="button" className={btnSecondary} onClick={() => setModal({ type: 'dismantle' })}>
                  <Wrench size={15} /> Add dismantle step
                </button>
              )}
              {showClassify && (
                <button type="button" className={btnPrimary} onClick={() => setModal({ type: 'classify' })}>
                  <Tag size={15} /> Classify item
                </button>
              )}

              {status === 'Classified' &&
                manualTransitions.map((next) => (
                  <button
                    key={next}
                    type="button"
                    className={next === 'OnHold' ? btnDanger : next === 'ReadyForSale' ? btnPrimary : btnSecondary}
                    onClick={() => setModal({ type: 'transition', next })}
                  >
                    {next === 'ReadyForSale' ? <ShoppingCart size={15} /> : next === 'ExportOnly' ? <Ship size={15} /> : <ShieldAlert size={15} />}
                    {TRANSITION_BUTTON_LABELS[next]}
                  </button>
                ))}

              <button type="button" className={btnSecondary} onClick={() => setModal({ type: 'move' })}>
                <MapPin size={15} /> Move location
              </button>
            </div>

            {status === 'Sorting' && (
              <p className="mt-3 text-xs text-ink-600">Dismantle first if the item has parts to track separately, or classify it straight away.</p>
            )}
          </GlassCard>

          <GlassCard>
            <h3 className="mb-4 flex items-center gap-2 font-display text-base font-bold text-ink-900">
              <RouteIcon size={16} className="text-mint-600" /> History
            </h3>
            {historyNewestFirst.length === 0 ? (
              <p className="text-sm text-ink-600">No history recorded.</p>
            ) : (
              <ol className="relative space-y-4 border-l-2 border-mint-100 pl-5">
                {historyNewestFirst.map((entry, i) => {
                  const Icon = HISTORY_ICONS[entry.action] ?? CheckCircle2;
                  return (
                    <li key={`${entry.performedAt}-${i}`} className="relative">
                      <span className="absolute -left-[31px] flex h-6 w-6 items-center justify-center rounded-full bg-mint-600 text-white ring-4 ring-white/70">
                        <Icon size={12} />
                      </span>
                      <p className="text-sm font-semibold text-ink-900">{historyLabel(entry.action)}</p>
                      {entry.notes && <p className="mt-0.5 text-xs text-ink-800">{entry.notes}</p>}
                      <p className="mt-0.5 text-[11px] text-ink-600">
                        {formatDateTime(entry.performedAt)} · by {entry.performedByStaffId === user?.userId ? 'you' : `staff ${shortId(entry.performedByStaffId)}`}
                      </p>
                    </li>
                  );
                })}
              </ol>
            )}
          </GlassCard>
        </div>
      </div>

      {/* Modals */}
      <ReceiptDetailModal receiptId={receiptOpen ? item.extraWasteReceiptId : null} onClose={() => setReceiptOpen(false)} />
      <ClassifyModal open={modal?.type === 'classify'} item={item} onClose={() => setModal(null)} onDone={handleDone} />
      <DismantleModal open={modal?.type === 'dismantle'} item={item} onClose={() => setModal(null)} onDone={handleDone} />
      <MoveLocationModal open={modal?.type === 'move'} item={item} locations={locations.data} onClose={() => setModal(null)} onDone={handleDone} />
      <TransitionModal
        open={modal?.type === 'transition'}
        item={item}
        nextStatus={modal?.type === 'transition' ? modal.next : null}
        locations={locations.data}
        onClose={() => setModal(null)}
        onDone={handleDone}
      />
    </div>
  );
};

export default InventoryDetailPage;
