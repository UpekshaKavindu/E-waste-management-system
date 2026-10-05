import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { CheckCircle2, MapPin, RefreshCw, Search, Truck, Wallet } from 'lucide-react';
import { receiveApi } from './receiveApi';
import type { ReceivableJob, ReceiveDeliveryResponse } from './types';
import { JobItemsReceiver, entryFor, jobLine, jobProblems, type JobEntry } from './JobItemsReceiver';
import { paymentApi } from '../Payments/paymentApi';
import { useItemTypes, useWarehouseLocations } from '../hooks/useLookups';
import { useCurrentUser } from '../hooks/useCurrentUser';
import { getApiErrorMessage } from '../utils/apiError';
import { formatDateTime, formatKg, formatMoney, formatSignedKg, shortId } from '../utils/format';
import {
  EmptyState,
  ErrorMessage,
  GlassCard,
  LoadingState,
  Notice,
  btnPrimary,
  btnSecondary,
  inputClass,
  labelClass,
  tableCellClass,
  tableHeadClass,
} from '../components';

interface DriverGroup {
  id: string;
  name: string;
  vehicle: string | null;
  jobs: ReceivableJob[];
}

/**
 * A collector often brings several completed jobs in one visit. Choose the collector, tick the jobs
 * they brought, then for each job go through its items: how many came, what each is and what it
 * weighs. Every item brought becomes its own inventory item (a lot for several units); each job
 * still gets ONE payment on its total weight. Everything is saved together.
 */
const JobReceiveForm: React.FC = () => {
  const locations = useWarehouseLocations();
  const itemTypes = useItemTypes();
  const role = useCurrentUser()?.role.toLowerCase();
  const canPay = role === 'admin' || role === 'staff';

  // The server only returns completed jobs that have a collector and are not yet received.
  const [jobs, setJobs] = useState<ReceivableJob[]>([]);
  const [jobsLoading, setJobsLoading] = useState(true);
  const [jobsError, setJobsError] = useState<string | null>(null);

  const [collectorId, setCollectorId] = useState('');
  const [entries, setEntries] = useState<Record<string, JobEntry>>({});
  const [locationId, setLocationId] = useState('');
  const [notes, setNotes] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [submitted, setSubmitted] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const [result, setResult] = useState<ReceiveDeliveryResponse | null>(null);
  const [paying, setPaying] = useState(false);
  const [paid, setPaid] = useState(false);
  const [payError, setPayError] = useState<string | null>(null);

  const loadJobs = useCallback(async () => {
    setJobsLoading(true);
    setJobsError(null);
    try {
      setJobs(await receiveApi.listReceivableJobs());
    } catch (e) {
      setJobsError(getApiErrorMessage(e, 'Failed to load the jobs waiting to be received.'));
    } finally {
      setJobsLoading(false);
    }
  }, []);

  useEffect(() => {
    loadJobs();
  }, [loadJobs]);

  // Default the destination to the receiving bay once locations arrive.
  useEffect(() => {
    if (!locationId && locations.data.length > 0) {
      const receiving = locations.data.find((l) => /receiv/i.test(l.name)) ?? locations.data[0];
      setLocationId(receiving.id);
    }
  }, [locations.data, locationId]);

  // Every completed job waiting, grouped by the driver who collected it (jobs arrive newest first).
  const drivers = useMemo(() => {
    const byCollector = new Map<string, DriverGroup>();
    jobs.forEach((j) => {
      const existing = byCollector.get(j.collectorId);
      if (existing) existing.jobs.push(j);
      else
        byCollector.set(j.collectorId, {
          id: j.collectorId,
          name: j.collectorName ?? `Collector ${shortId(j.collectorId)}`,
          vehicle: j.collectorVehicleType,
          jobs: [j],
        });
    });
    return [...byCollector.values()];
  }, [jobs]);

  // Search by driver name or vehicle (shows all their jobs), or by address / job id (shows just those jobs).
  const [search, setSearch] = useState('');
  const visibleDrivers = useMemo(() => {
    const q = search.trim().toLowerCase();
    if (!q) return drivers;
    return drivers
      .map((d) => {
        if (d.name.toLowerCase().includes(q) || (d.vehicle ?? '').toLowerCase().includes(q)) return d;
        const hits = d.jobs.filter((j) => j.pickupAddress.toLowerCase().includes(q) || j.jobId.toLowerCase().startsWith(q));
        return hits.length > 0 ? { ...d, jobs: hits } : null;
      })
      .filter((d): d is DriverGroup => d !== null);
  }, [drivers, search]);

  const selectedDriver = drivers.find((d) => d.id === collectorId) ?? null;
  const collectorJobs = useMemo(() => jobs.filter((j) => j.collectorId === collectorId), [jobs, collectorId]);

  // Choosing a driver opens their jobs; choosing one job also ticks it.
  const chooseCollector = (id: string, tickJobId?: string) => {
    setCollectorId(id);
    const next: Record<string, JobEntry> = {};
    jobs.filter((j) => j.collectorId === id).forEach((j) => {
      next[j.jobId] = { ...entryFor(j), selected: j.jobId === tickJobId };
    });
    setEntries(next);
    setSubmitted(false);
    setError(null);
  };

  const changeDriver = () => {
    setCollectorId('');
    setEntries({});
    setSubmitted(false);
    setError(null);
  };

  // Opened from a collected job ("Receive into warehouse"): pre-choose its collector and tick it.
  const [params] = useSearchParams();
  const linkedJobId = params.get('jobId');
  const [linkHandled, setLinkHandled] = useState(false);
  const [linkMissing, setLinkMissing] = useState(false);
  useEffect(() => {
    if (!linkedJobId || linkHandled || jobsLoading) return;
    setLinkHandled(true);
    const linked = jobs.find((j) => j.jobId === linkedJobId);
    if (!linked) {
      setLinkMissing(true);
      return;
    }
    const next: Record<string, JobEntry> = {};
    jobs.filter((j) => j.collectorId === linked.collectorId).forEach((j) => {
      next[j.jobId] = { ...entryFor(j), selected: j.jobId === linked.jobId };
    });
    setCollectorId(linked.collectorId);
    setEntries(next);
  }, [linkedJobId, linkHandled, jobsLoading, jobs]);

  const update = (jobId: string, change: Partial<JobEntry>) =>
    setEntries((prev) => ({ ...prev, [jobId]: { ...prev[jobId], ...change } }));

  const selectedJobs = collectorJobs.filter((j) => entries[j.jobId]?.selected);
  const allSelected = collectorJobs.length > 0 && selectedJobs.length === collectorJobs.length;

  const problems: string[] = [];
  if (!collectorId) problems.push('Choose the collector.');
  else if (selectedJobs.length === 0) problems.push('Tick at least one job the collector brought.');
  selectedJobs.forEach((j, i) => {
    problems.push(...jobProblems(j, entries[j.jobId], `Job ${i + 1} (${shortId(j.jobId)})`));
  });
  if (!locationId) problems.push('Choose the warehouse location.');

  const submit = async (ev: React.FormEvent) => {
    ev.preventDefault();
    setSubmitted(true);
    if (problems.length > 0) return;
    setSubmitting(true);
    setError(null);
    try {
      const res = await receiveApi.receiveDelivery({
        collectorId,
        warehouseLocationId: locationId,
        notes,
        jobs: selectedJobs.map((j) => jobLine(j, entries[j.jobId])),
      });
      setResult(res);
    } catch (err) {
      setError(getApiErrorMessage(err, 'Failed to receive the delivery. Nothing was saved.'));
      loadJobs();
    } finally {
      setSubmitting(false);
    }
  };

  const payNow = async () => {
    if (!result) return;
    setPaying(true);
    setPayError(null);
    try {
      await paymentApi.payDelivery(result.deliveryId);
      setPaid(true);
    } catch (err) {
      setPayError(getApiErrorMessage(err, 'The payment could not be recorded.'));
    } finally {
      setPaying(false);
    }
  };

  const reset = () => {
    setResult(null);
    setPaid(false);
    setPayError(null);
    setCollectorId('');
    setEntries({});
    setNotes('');
    setSearch('');
    setSubmitted(false);
    setError(null);
    loadJobs();
  };

  // ---------------------------------------------------------------- success view
  if (result) {
    const collectorName = drivers.find((d) => d.id === result.collectorId)?.name ?? 'the collector';
    return (
      <GlassCard>
        <div className="flex items-start gap-3">
          <span className="flex h-10 w-10 flex-shrink-0 items-center justify-center rounded-2xl bg-mint-100 text-mint-700">
            <CheckCircle2 size={22} />
          </span>
          <div>
            <h3 className="font-display text-lg font-bold text-ink-900">
              {result.jobs.length} job{result.jobs.length === 1 ? '' : 's'} received from {collectorName}
            </h3>
            <p className="text-sm text-ink-600">Each item brought became its own inventory item; each job has one payment on its total weight. Together they are one delivery.</p>
          </div>
        </div>

        <div className="mt-5 overflow-x-auto">
          <table className="w-full min-w-[680px] border-collapse">
            <thead>
              <tr className="border-b border-mint-100">
                <th className={`${tableHeadClass} px-4 py-2`}>Item</th>
                <th className={`${tableHeadClass} px-4 py-2`}>Type</th>
                <th className={`${tableHeadClass} px-4 py-2 text-right`}>Received</th>
                <th className={`${tableHeadClass} px-4 py-2 text-right`}>Verified</th>
                <th className={`${tableHeadClass} px-4 py-2 text-right`}>Payment</th>
              </tr>
            </thead>
            <tbody>
              {result.jobs.map((j) => (
                <React.Fragment key={j.jobId}>
                  <tr className="border-b border-mint-100 bg-mint-50/60">
                    <td colSpan={2} className={`${tableCellClass} text-xs`}>
                      <span className="font-mono">Job {shortId(j.jobId)}</span>
                      <span className="ml-3 text-ink-600">
                        {j.receivedQuantity} of {j.expectedQuantity} item{j.expectedQuantity === 1 ? '' : 's'} received
                        {j.discrepancyKg !== null && Math.abs(j.discrepancyKg) >= 0.005 && ` · ${formatSignedKg(j.discrepancyKg)} vs reported`}
                      </span>
                    </td>
                    <td />
                    <td className={`${tableCellClass} text-right font-mono font-semibold`}>{formatKg(j.verifiedWeightKg)}</td>
                    <td className={`${tableCellClass} text-right font-mono font-semibold`}>{formatMoney(j.paymentAmount)}</td>
                  </tr>
                  {j.items.map((item, i) => (
                    <tr key={item.submissionItemId ?? i} className="border-b border-mint-50">
                      <td className={tableCellClass}>
                        {item.inventoryItemId ? (
                          <Link to={`/processing/inventory/${item.inventoryItemId}`} className="font-semibold text-mint-700 hover:underline">
                            {item.itemName}
                          </Link>
                        ) : (
                          <span className="text-ink-600">{item.itemName}</span>
                        )}
                      </td>
                      <td className={tableCellClass}>{item.itemType ?? <span className="text-amber-700">Not brought</span>}</td>
                      <td className={`${tableCellClass} text-right font-mono`}>
                        {item.receivedQuantity} / {item.expectedQuantity}
                      </td>
                      <td className={`${tableCellClass} text-right font-mono`}>{item.receivedQuantity > 0 ? formatKg(item.verifiedWeightKg) : '—'}</td>
                      <td />
                    </tr>
                  ))}
                </React.Fragment>
              ))}
              <tr>
                <td colSpan={4} className={`${tableCellClass} text-right font-semibold text-ink-900`}>
                  {paid ? 'Total paid' : 'Total pending'}
                </td>
                <td className={`${tableCellClass} text-right font-mono text-base font-bold text-ink-900`}>{formatMoney(result.totalPendingAmount)}</td>
              </tr>
            </tbody>
          </table>
        </div>

        {paid && (
          <Notice tone="success" className="mt-4">
            All {result.jobs.length} payment{result.jobs.length === 1 ? '' : 's'} of this delivery were marked as paid.
          </Notice>
        )}
        {payError && <ErrorMessage className="mt-4" message={payError} />}

        <div className="mt-6 flex flex-wrap gap-2">
          {canPay && !paid && (
            <button type="button" onClick={payNow} className={btnPrimary} disabled={paying}>
              <Wallet size={14} /> {paying ? 'Paying…' : `Pay ${formatMoney(result.totalPendingAmount)} now`}
            </button>
          )}
          <Link to="/processing/payments" className={btnSecondary}>
            <Wallet size={14} /> View payments
          </Link>
          <button type="button" onClick={reset} className={btnSecondary}>
            Receive another delivery
          </button>
        </div>
      </GlassCard>
    );
  }

  // ---------------------------------------------------------------- form view
  return (
    <form onSubmit={submit} noValidate className="space-y-5">
      {linkMissing && (
        <Notice tone="info">That job is not waiting to be received. It may already be in inventory.</Notice>
      )}
      <GlassCard>
        <div className="flex flex-wrap items-start justify-between gap-2">
          <div>
            <h3 className="font-display text-base font-bold text-ink-900">1 · Which driver is delivering?</h3>
            <p className="text-xs text-ink-600">
              {selectedDriver
                ? 'Their completed jobs are listed below.'
                : 'Completed jobs waiting to be received. Choose a driver, or pick one of their jobs.'}
            </p>
          </div>
          <div className="flex gap-2">
            {selectedDriver && (
              <button type="button" onClick={changeDriver} className={btnSecondary}>
                Change driver
              </button>
            )}
            <button type="button" onClick={loadJobs} className={btnSecondary} disabled={jobsLoading}>
              <RefreshCw size={14} className={jobsLoading ? 'animate-spin' : ''} /> Refresh
            </button>
          </div>
        </div>

        {selectedDriver ? (
          <div className="mt-3 flex items-center gap-3 rounded-2xl border border-mint-400 bg-mint-50/70 p-3.5">
            <span className="flex h-9 w-9 flex-shrink-0 items-center justify-center rounded-xl bg-mint-100 text-mint-700">
              <Truck size={18} />
            </span>
            <div className="text-sm">
              <div className="font-semibold text-ink-900">{selectedDriver.name}</div>
              <div className="text-xs text-ink-600">
                {selectedDriver.vehicle ? `${selectedDriver.vehicle} · ` : ''}
                {selectedDriver.jobs.length} completed job{selectedDriver.jobs.length === 1 ? '' : 's'} waiting
              </div>
            </div>
          </div>
        ) : (
          <>
            <div className="relative mt-3 max-w-md">
              <Search size={14} className="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-ink-600" />
              <input
                id="jr-driver-search"
                type="search"
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                placeholder="Search by driver name, vehicle or address…"
                className={`${inputClass} pl-9`}
                autoComplete="off"
              />
            </div>

            <div className="mt-4 space-y-3">
              {jobsLoading && jobs.length === 0 ? (
                <LoadingState label="Loading completed jobs…" />
              ) : drivers.length === 0 ? (
                !jobsError && (
                  <EmptyState icon={Truck} title="No completed jobs waiting" description="Jobs appear here as soon as a driver marks them completed." />
                )
              ) : visibleDrivers.length === 0 ? (
                <EmptyState icon={Search} title="No matches" description={`No driver or job matches “${search.trim()}”.`} />
              ) : (
                visibleDrivers.map((d) => (
                  <div key={d.id} className="rounded-2xl border border-mint-100 bg-white/60">
                    <div className="flex flex-wrap items-center justify-between gap-2 border-b border-mint-50 px-3.5 py-2.5">
                      <div className="flex items-center gap-2 text-sm">
                        <Truck size={15} className="text-mint-600" />
                        <span className="font-semibold text-ink-900">{d.name}</span>
                        <span className="text-xs text-ink-600">
                          {d.vehicle ? `${d.vehicle} · ` : ''}
                          {d.jobs.length} job{d.jobs.length === 1 ? '' : 's'}
                        </span>
                      </div>
                      <button type="button" onClick={() => chooseCollector(d.id)} className={btnSecondary}>
                        Receive from {d.name.split(' ')[0]}
                      </button>
                    </div>
                    <ul>
                      {d.jobs.map((job) => (
                        <li key={job.jobId}>
                          <button
                            type="button"
                            onClick={() => chooseCollector(d.id, job.jobId)}
                            className="flex w-full flex-wrap items-center justify-between gap-x-4 gap-y-1 px-3.5 py-2 text-left text-xs text-ink-600 transition hover:bg-mint-50/70"
                          >
                            <span className="flex min-w-0 items-center gap-1.5 text-sm text-ink-900">
                              <MapPin size={13} className="flex-shrink-0 text-mint-600" />
                              <span className="truncate">{job.pickupAddress || 'No address recorded'}</span>
                            </span>
                            <span className="flex flex-wrap gap-x-4">
                              {job.submissionCategory && <span>{job.submissionCategory}</span>}
                              <span>{job.reportedWeightKg !== null ? formatKg(job.reportedWeightKg) : 'n/a'}</span>
                              <span>Completed {formatDateTime(job.completedAt)}</span>
                              <span className="font-mono">{shortId(job.jobId)}</span>
                            </span>
                          </button>
                        </li>
                      ))}
                    </ul>
                  </div>
                ))
              )}
            </div>
          </>
        )}
        {jobsError && <ErrorMessage className="mt-3" message={jobsError} onRetry={loadJobs} />}
      </GlassCard>

      {collectorId && (
        <GlassCard padded={false}>
          <div className="flex flex-wrap items-center justify-between gap-2 px-5 pt-5">
            <div>
              <h3 className="font-display text-base font-bold text-ink-900">2 · Tick the jobs they brought, then check each item</h3>
              <p className="text-xs text-ink-600">
                Set how many of each item came, confirm its type and weigh it. Each item becomes its own inventory item; each job gets one payment.
              </p>
            </div>
            {collectorJobs.length > 1 && (
              <label className="flex items-center gap-2 text-sm font-semibold text-ink-800">
                <input
                  type="checkbox"
                  checked={allSelected}
                  onChange={(e) => collectorJobs.forEach((j) => update(j.jobId, { selected: e.target.checked }))}
                  className="h-4 w-4 accent-mint-600"
                />
                Select all
              </label>
            )}
          </div>
          <div className="space-y-2 p-5 pt-3">
            {jobsLoading && collectorJobs.length === 0 ? (
              <LoadingState label="Loading jobs…" />
            ) : collectorJobs.length === 0 ? (
              <EmptyState icon={Truck} title="No jobs waiting" description="This collector has no completed jobs left to receive." />
            ) : (
              collectorJobs.map((job) => {
                const e = entries[job.jobId] ?? entryFor(job);
                return (
                  <div
                    key={job.jobId}
                    className={`rounded-2xl border p-3.5 transition ${e.selected ? 'border-mint-400 bg-mint-50/70' : 'border-mint-100 bg-white/60'}`}
                  >
                    <label className="flex cursor-pointer items-start gap-3">
                      <input
                        type="checkbox"
                        checked={e.selected}
                        onChange={(ev) => update(job.jobId, { selected: ev.target.checked })}
                        className="mt-1 h-4 w-4 accent-mint-600"
                      />
                      <span className="min-w-0 flex-1">
                        <span className="flex items-start justify-between gap-3">
                          <span className="flex items-start gap-1.5 text-sm font-semibold text-ink-900">
                            <MapPin size={14} className="mt-0.5 flex-shrink-0 text-mint-600" /> {job.pickupAddress || 'No address recorded'}
                          </span>
                          <span className="whitespace-nowrap font-mono text-xs text-ink-600">{shortId(job.jobId)}</span>
                        </span>
                        <span className="mt-1 flex flex-wrap gap-x-4 gap-y-1 text-xs text-ink-600">
                          {job.items.length > 0 && (
                            <span>
                              {job.items.length} item{job.items.length === 1 ? '' : 's'}
                              {job.items.some((it) => it.quantity > 1) && ` · ${job.items.reduce((n, it) => n + it.quantity, 0)} units`}
                            </span>
                          )}
                          {job.submissionCategory && <span>Category: {job.submissionCategory}</span>}
                          <span>Reported {job.reportedWeightKg !== null ? formatKg(job.reportedWeightKg) : 'n/a'}</span>
                          {job.estimatedDistanceKm !== null && <span>{job.estimatedDistanceKm} km</span>}
                          <span>Completed {formatDateTime(job.completedAt)}</span>
                        </span>
                      </span>
                    </label>

                    {e.selected && (
                      <JobItemsReceiver
                        job={job}
                        entry={e}
                        itemTypes={itemTypes.data}
                        itemTypesLoading={itemTypes.loading}
                        onChange={(next) => setEntries((prev) => ({ ...prev, [job.jobId]: next }))}
                      />
                    )}
                  </div>
                );
              })
            )}
            {itemTypes.error && <ErrorMessage message={itemTypes.error} onRetry={itemTypes.reload} />}
          </div>
        </GlassCard>
      )}

      {collectorId && (
        <GlassCard>
          <h3 className="font-display text-base font-bold text-ink-900">3 · Where it goes</h3>
          <div className="mt-3 grid gap-4 sm:grid-cols-2">
            <div>
              <label className={labelClass} htmlFor="jr-location">
                Warehouse location
              </label>
              <select id="jr-location" value={locationId} onChange={(e) => setLocationId(e.target.value)} className={inputClass} disabled={locations.loading}>
                {locations.loading && <option value="">Loading…</option>}
                {locations.data.map((l) => (
                  <option key={l.id} value={l.id}>
                    {l.name}
                  </option>
                ))}
              </select>
              {locations.error && <ErrorMessage className="mt-2" message={locations.error} onRetry={locations.reload} />}
            </div>
            <div>
              <label className={labelClass} htmlFor="jr-notes">
                Notes (optional)
              </label>
              <input id="jr-notes" value={notes} maxLength={1000} onChange={(e) => setNotes(e.target.value)} className={inputClass} />
            </div>
          </div>

          {submitted && problems.length > 0 && (
            <Notice tone="error" className="mt-4">
              <ul className="list-disc pl-4">
                {problems.map((p) => (
                  <li key={p}>{p}</li>
                ))}
              </ul>
            </Notice>
          )}
          {error && <ErrorMessage className="mt-4" message={error} />}

          <div className="mt-5 flex flex-wrap items-center gap-3">
            <button type="submit" className={btnPrimary} disabled={submitting}>
              {submitting
                ? 'Receiving…'
                : `Receive ${selectedJobs.length || ''} job${selectedJobs.length === 1 ? '' : 's'}`.replace('  ', ' ')}
            </button>
            <p className="text-xs text-ink-600">All ticked jobs are saved together — if one fails, none are saved.</p>
          </div>
        </GlassCard>
      )}
    </form>
  );
};

export default JobReceiveForm;
