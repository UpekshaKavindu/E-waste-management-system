/* eslint-disable react-hooks/set-state-in-effect */
import React, { useEffect, useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import {
  LayoutDashboard, Search, Filter, RefreshCw, ExternalLink,
} from 'lucide-react';
import { submissionApi } from './submissionApi';
import type { SubmissionResponse } from './types';
import {
  EmptyState, ErrorMessage, GlassCard, LoadingState, PageHeader, StatusPill,
  btnSecondary, inputClass,
} from '../../components/ui';
import type { StatusTone } from '../../components/ui/StatusPill';
import { formatMoney } from '../processing/utils/format';
import { ItemBreakdown } from './ItemBreakdown';

const STATUS_TONE: Record<string, StatusTone> = {
  CollectorAssigned: 'success',
  Collected: 'success',
  Rejected: 'error',
  Failed: 'error',
  Cancelled: 'error',
};

const AdminReviewPage: React.FC = () => {
  const [submissions, setSubmissions] = useState<SubmissionResponse[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const [searchTerm, setSearchTerm] = useState('');
  const [selectedStatus, setSelectedStatus] = useState<string>('All');
  const [selectedHazard, setSelectedHazard] = useState<string>('All');

  // ---- Load ----
  const load = async () => {
    setLoading(true);
    setError(null);
    try {
      setSubmissions(await submissionApi.list());
    } catch (e) {
      console.error(e);
      setError('Failed to fetch submissions.');
      setSubmissions([]);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    load();
  }, []);

  // ---- Filter ----
  // Approve/reject happens in Agentic Review (it resumes or stops the agent
  // chain); this page only shows where each submission is.
  const filtered = useMemo(() => {
    const term = searchTerm.toLowerCase();
    return submissions.filter((sub) => {
      const item = sub.items[0];
      const ai = sub.workflow?.analysis;

      const matchesSearch =
        (item?.description ?? '').toLowerCase().includes(term) ||
        (item?.itemName ?? '').toLowerCase().includes(term) ||
        (ai?.wasteCategory ?? '').toLowerCase().includes(term) ||
        sub.id.toLowerCase().includes(term);

      const matchesStatus =
        selectedStatus === 'All' || sub.status === selectedStatus;

      const hazard = ai?.hazardLevel ?? 'Unknown';
      const matchesHazard =
        selectedHazard === 'All' || hazard === selectedHazard;

      return matchesSearch && matchesStatus && matchesHazard;
    });
  }, [submissions, searchTerm, selectedStatus, selectedHazard]);

  return (
    <div>
      <PageHeader
        title="E-waste review panel"
        subtitle="Track every submission. Items flagged by the AI are approved or rejected in Agentic Review."
        icon={LayoutDashboard}
        actions={
          <button onClick={load} className={btnSecondary}>
            <RefreshCw size={14} className={loading ? 'animate-spin' : ''} /> Refresh
          </button>
        }
      />

      {/* Search + Filters */}
      <GlassCard hover={false} className="mb-5">
        <div className="grid gap-3 sm:grid-cols-[2fr_1fr_1fr]">
          <div className="relative">
            <Search size={16} className="pointer-events-none absolute left-3.5 top-1/2 -translate-y-1/2 text-ink-600" />
            <input
              type="text"
              placeholder="Search by description, ID or category…"
              value={searchTerm}
              onChange={(e) => setSearchTerm(e.target.value)}
              className={`${inputClass} pl-10`}
            />
          </div>

          <div className="relative">
            <Filter size={15} className="pointer-events-none absolute left-3.5 top-1/2 -translate-y-1/2 text-ink-600" />
            <select
              value={selectedStatus}
              onChange={(e) => setSelectedStatus(e.target.value)}
              className={`${inputClass} pl-9`}
            >
              <option value="All">All statuses</option>
              <option value="Analyzing">Analyzing</option>
              <option value="AwaitingReview">Awaiting review</option>
              <option value="Scheduling">Scheduling</option>
              <option value="CollectorAssigned">Collector assigned</option>
              <option value="AwaitingCollector">Awaiting collector</option>
              <option value="Collected">Collected</option>
              <option value="Rejected">Rejected</option>
              <option value="Failed">Failed</option>
              <option value="Closed">Closed</option>
              <option value="Cancelled">Cancelled</option>
              <option value="NotProcessed">Not processed</option>
            </select>
          </div>

          <select
            value={selectedHazard}
            onChange={(e) => setSelectedHazard(e.target.value)}
            className={inputClass}
          >
            <option value="All">All hazard levels</option>
            <option value="Low">Low</option>
            <option value="Medium">Medium</option>
            <option value="High">High</option>
            <option value="Critical">Critical</option>
          </select>
        </div>
      </GlassCard>

      {/* States */}
      {error && <ErrorMessage message={error} onRetry={load} className="mb-4" />}

      <GlassCard hover={false} padded={false}>
        {loading && !submissions.length ? (
          <LoadingState label="Loading submissions…" />
        ) : !error && filtered.length === 0 ? (
          <EmptyState icon={LayoutDashboard} title="No submissions matched your criteria" />
        ) : !error ? (
          <div className="divide-y divide-mint-50">
            {filtered.map((sub) => {
              const item = sub.items[0];
              const ai = sub.workflow?.analysis;
              const imgUrl = item?.imageUrl;
              const hazard = ai?.hazardLevel;
              const category = ai?.wasteCategory;
              const value = ai?.estimatedValueLkr;

              return (
                <div key={sub.id} className="flex flex-wrap items-center gap-4 p-5">
                  <div className="flex h-[70px] w-[70px] flex-shrink-0 items-center justify-center overflow-hidden rounded-xl bg-mint-50">
                    {imgUrl ? (
                      <img
                        src={imgUrl}
                        alt={item?.itemName || 'E-Waste'}
                        className="h-full w-full object-cover"
                        onError={(e) => {
                          (e.target as HTMLImageElement).onerror = null;
                          (e.target as HTMLImageElement).src = 'https://placehold.co/100x100?text=E-Waste';
                        }}
                      />
                    ) : (
                      <span className="text-[10px] text-ink-600">No image</span>
                    )}
                  </div>

                  <div className="min-w-0 flex-1">
                    <div className="flex items-center justify-between gap-3">
                      <h4 className="font-display font-bold text-ink-900">
                        {sub.source === 'Csv'
                          ? `CSV upload · ${sub.items.length} rows · ${sub.items.reduce((n, it) => n + (it.quantity ?? 1), 0)} items`
                          : item?.itemName || 'E-Waste Item'}
                      </h4>
                      <span className="flex-shrink-0 rounded-full bg-ink-100 px-2.5 py-0.5 font-mono text-[11px] text-ink-600">
                        {sub.id.substring(0, 8)}…
                      </span>
                    </div>
                    {sub.source !== 'Csv' && item?.description && (
                      <p className="mt-0.5 text-sm italic text-ink-600">&ldquo;{item.description}&rdquo;</p>
                    )}
                    {sub.statusReason && <p className="mt-1 text-xs font-medium text-red-600">{sub.statusReason}</p>}

                    {ai ? (
                      <div className="mt-2 flex flex-wrap gap-x-4 gap-y-1 rounded-xl bg-mint-50/60 px-3 py-2 text-xs text-ink-800">
                        <span><strong>Category:</strong> {category}</span>
                        <span>
                          <strong>Hazard:</strong>{' '}
                          <span className={`font-bold ${hazard === 'High' || hazard === 'Critical' ? 'text-red-600' : 'text-mint-700'}`}>
                            {hazard}
                          </span>
                        </span>
                        <span><strong>Value:</strong> {value !== undefined ? formatMoney(value) : '—'}</span>
                        <ItemBreakdown items={ai.items} className="mt-1 basis-full" />
                      </div>
                    ) : sub.status === 'Analyzing' ? (
                      <span className="mt-2 inline-block text-xs font-medium text-amber-700">Pending AI analysis…</span>
                    ) : null}
                  </div>

                  <div className="flex flex-col items-end gap-2">
                    <StatusPill label={sub.statusLabel} tone={STATUS_TONE[sub.status] ?? 'info'} />
                    {sub.status === 'AwaitingReview' && sub.workflow && (
                      <Link
                        to={`/processing/agentic-review?workflowId=${sub.workflow.workflowId}`}
                        className="flex items-center gap-1.5 text-xs font-semibold text-mint-700 hover:underline"
                      >
                        <ExternalLink size={13} /> Review in Agentic Review
                      </Link>
                    )}
                  </div>
                </div>
              );
            })}
          </div>
        ) : null}
      </GlassCard>
    </div>
  );
};

export default AdminReviewPage;
