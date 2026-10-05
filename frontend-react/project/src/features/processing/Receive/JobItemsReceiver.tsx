import React, { useState } from 'react';
import { Scale } from 'lucide-react';
import type { ReceivableJob, ReceivableJobItem, SuggestionSource } from './types';
import { formatKg, formatSignedKg } from '../utils/format';
import { btnSecondary, inputClass, labelClass } from '../components';

/** What staff enter for one submission item / CSV row. Strings, straight from the inputs. */
export interface ItemEntry {
  received: string;
  itemType: string;
  weight: string;
}

/** A ticked job's entries: one per item, or one whole-job weight/type when the job has no items. */
export interface JobEntry {
  selected: boolean;
  items: Record<string, ItemEntry>;
  wholeWeight: string;
  wholeType: string;
}

const SOURCE_LABEL: Record<SuggestionSource, string> = {
  name: 'from the item name',
  category: 'from the CSV category',
  ai: "from the AI's classification",
  submission: "from the submission's category",
};

export const entryFor = (job: ReceivableJob): JobEntry => ({
  selected: false,
  items: Object.fromEntries(
    job.items.map((item) => [
      item.submissionItemId,
      {
        received: String(item.quantity),
        itemType: item.suggestedItemType ?? '',
        // A one-item job weighs what the collector reported; staff only correct it.
        weight: job.items.length === 1 && job.reportedWeightKg !== null ? String(job.reportedWeightKg) : '',
      },
    ]),
  ),
  wholeWeight: job.reportedWeightKg !== null ? String(job.reportedWeightKg) : '',
  wholeType: job.suggestedItemType ?? '',
});

const receivedOf = (e: ItemEntry | undefined) => {
  const n = Number(e?.received);
  return Number.isInteger(n) && n > 0 ? n : 0;
};

/** Problems with one ticked job, phrased for the list under the form. */
export function jobProblems(job: ReceivableJob, entry: JobEntry, label: string): string[] {
  if (job.items.length === 0) {
    const problems: string[] = [];
    if (!entry.wholeType) problems.push(`${label}: choose the item type.`);
    if (!(Number(entry.wholeWeight) > 0)) problems.push(`${label}: enter the verified weight.`);
    return problems;
  }

  const problems: string[] = [];
  let anyReceived = false;
  job.items.forEach((item) => {
    const e = entry.items[item.submissionItemId];
    const n = Number(e?.received);
    if (!Number.isInteger(n) || n < 0 || n > item.quantity) {
      problems.push(`${label} · ${item.itemName}: received must be a whole number from 0 to ${item.quantity}.`);
      return;
    }
    if (n === 0) return;
    anyReceived = true;
    if (!e.itemType) problems.push(`${label} · ${item.itemName}: choose the item type.`);
    if (!(Number(e.weight) > 0)) problems.push(`${label} · ${item.itemName}: enter the verified weight.`);
  });
  if (!anyReceived) problems.push(`${label}: nothing is marked as received — untick the job instead.`);
  return problems;
}

/** The request line for one ticked job. */
export function jobLine(job: ReceivableJob, entry: JobEntry) {
  if (job.items.length === 0) {
    return { jobId: job.jobId, items: [] as [], verifiedWeightKg: Number(entry.wholeWeight), itemType: entry.wholeType };
  }
  return {
    jobId: job.jobId,
    items: job.items.map((item) => {
      const e = entry.items[item.submissionItemId];
      const received = receivedOf(e);
      return {
        submissionItemId: item.submissionItemId,
        receivedQuantity: received,
        itemType: received > 0 ? e.itemType : null,
        verifiedWeightKg: received > 0 ? Number(e.weight) : 0,
      };
    }),
  };
}

/**
 * Spreads one weighed total over the received rows: by each row's expected weight (CSV or AI
 * estimate) when every received row has one, otherwise by units. Rounded to 0.01 kg, with the
 * rounding left on the last row so the rows add up exactly to the total.
 */
function splitTotal(items: ReceivableJobItem[], entries: Record<string, ItemEntry>, total: number): Record<string, ItemEntry> {
  const rows = items.filter((i) => receivedOf(entries[i.submissionItemId]) > 0);
  if (rows.length === 0) return entries;

  const byWeight = rows.every((i) => i.expectedWeightKg && i.expectedWeightKg > 0);
  const share = (i: ReceivableJobItem) => {
    const received = receivedOf(entries[i.submissionItemId]);
    return byWeight ? (i.expectedWeightKg! * received) / i.quantity : received;
  };
  const sum = rows.reduce((s, i) => s + share(i), 0);

  const next = { ...entries };
  let assigned = 0;
  rows.forEach((item, index) => {
    const kg = index === rows.length - 1 ? total - assigned : Math.round(((total * share(item)) / sum) * 100) / 100;
    assigned += kg;
    next[item.submissionItemId] = { ...next[item.submissionItemId], weight: (Math.round(kg * 100) / 100).toString() };
  });
  return next;
}

interface Props {
  job: ReceivableJob;
  entry: JobEntry;
  itemTypes: string[];
  itemTypesLoading: boolean;
  onChange: (entry: JobEntry) => void;
}

/**
 * The items of one ticked job: how many were brought, what each is, and what it weighs. Every row
 * received becomes its own inventory item (a lot when more than one unit); rows set to 0 are
 * recorded as not brought. The job's payment is on the total.
 */
export const JobItemsReceiver: React.FC<Props> = ({ job, entry, itemTypes, itemTypesLoading, onChange }) => {
  const [splitting, setSplitting] = useState(false);
  const [total, setTotal] = useState('');

  const setItem = (id: string, change: Partial<ItemEntry>) =>
    onChange({ ...entry, items: { ...entry.items, [id]: { ...entry.items[id], ...change } } });

  const typeSelect = (id: string, value: string, onPick: (v: string) => void, disabled = false) => (
    <select id={id} value={value} onChange={(e) => onPick(e.target.value)} className={inputClass} disabled={itemTypesLoading || disabled}>
      <option value="">{itemTypesLoading ? 'Loading…' : 'Choose what it is…'}</option>
      {itemTypes.map((t) => (
        <option key={t} value={t}>
          {t}
        </option>
      ))}
    </select>
  );

  // A job whose submission has no items: weighed and typed as one.
  if (job.items.length === 0) {
    return (
      <div className="mt-3 grid gap-3 pl-7 sm:grid-cols-2">
        <div>
          <label className={labelClass} htmlFor={`jr-type-${job.jobId}`}>Item type</label>
          {typeSelect(`jr-type-${job.jobId}`, entry.wholeType, (v) => onChange({ ...entry, wholeType: v }))}
        </div>
        <div>
          <label className={labelClass} htmlFor={`jr-weight-${job.jobId}`}>Verified weight (kg)</label>
          <input
            id={`jr-weight-${job.jobId}`}
            type="number" min="0" step="0.01" value={entry.wholeWeight}
            onChange={(e) => onChange({ ...entry, wholeWeight: e.target.value })}
            className={inputClass} placeholder="Weighed at the warehouse"
          />
        </div>
      </div>
    );
  }

  const expectedUnits = job.items.reduce((s, i) => s + i.quantity, 0);
  const receivedUnits = job.items.reduce((s, i) => s + receivedOf(entry.items[i.submissionItemId]), 0);
  const verifiedTotal = job.items.reduce(
    (s, i) => s + (receivedOf(entry.items[i.submissionItemId]) > 0 ? Number(entry.items[i.submissionItemId]?.weight) || 0 : 0),
    0,
  );
  const difference = job.reportedWeightKg !== null && verifiedTotal > 0 ? verifiedTotal - job.reportedWeightKg : null;

  return (
    <div className="mt-3 pl-7">
      <div className="overflow-x-auto rounded-2xl border border-mint-100 bg-white/70">
        <table className="w-full min-w-[720px] border-collapse text-sm">
          <thead className="bg-mint-50/80 text-left font-mono text-[11px] uppercase tracking-wide text-ink-600">
            <tr>
              <th className="px-3 py-2">Item</th>
              <th className="w-24 px-3 py-2 text-center">Expected</th>
              <th className="w-24 px-3 py-2 text-center">Received</th>
              <th className="w-56 px-3 py-2">Item type</th>
              <th className="w-36 px-3 py-2">Verified kg</th>
            </tr>
          </thead>
          <tbody>
            {job.items.map((item) => {
              const e = entry.items[item.submissionItemId];
              const brought = receivedOf(e) > 0;
              const short = brought && receivedOf(e) < item.quantity;
              const suggested = item.suggestedItemType && e?.itemType === item.suggestedItemType && item.suggestionSource;
              return (
                <tr key={item.submissionItemId} className={`border-t border-mint-50 align-top ${brought ? '' : 'bg-ink-50/60 text-ink-600'}`}>
                  <td className="px-3 py-2">
                    <div className="font-semibold text-ink-900">{item.itemName}</div>
                    {item.description && <div className="text-xs text-ink-600">{item.description}</div>}
                    {!brought && <div className="text-xs font-semibold text-amber-700">Not brought</div>}
                    {short && <div className="text-xs font-semibold text-amber-700">{item.quantity - receivedOf(e)} short</div>}
                  </td>
                  {/* Padded to the input's height so the number lines up with the Received box beside it. */}
                  <td className="px-3 py-2 text-center font-mono">
                    <div className="py-[11px] leading-5">{item.quantity}</div>
                  </td>
                  <td className="px-3 py-2">
                    <input
                      aria-label={`${item.itemName}: units received`}
                      type="number" min="0" max={item.quantity} step="1" value={e?.received ?? ''}
                      onChange={(ev) => setItem(item.submissionItemId, { received: ev.target.value })}
                      className={`${inputClass} text-center`}
                    />
                  </td>
                  <td className="px-3 py-2">
                    {typeSelect(`jr-type-${item.submissionItemId}`, e?.itemType ?? '', (v) => setItem(item.submissionItemId, { itemType: v }), !brought)}
                    {suggested ? (
                      <div className="mt-1 text-[11px] text-mint-700">Suggested {SOURCE_LABEL[item.suggestionSource!]}</div>
                    ) : brought && !e?.itemType ? (
                      <div className="mt-1 text-[11px] text-amber-700">No match — choose the type</div>
                    ) : null}
                  </td>
                  <td className="px-3 py-2">
                    <input
                      aria-label={`${item.itemName}: verified weight in kg`}
                      type="number" min="0" step="0.01" value={brought ? e?.weight ?? '' : ''}
                      onChange={(ev) => setItem(item.submissionItemId, { weight: ev.target.value })}
                      className={inputClass} disabled={!brought}
                      placeholder={item.expectedWeightKg ? `~${formatKg(item.expectedWeightKg)}` : 'All units together'}
                    />
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>

      <div className="mt-2 flex flex-wrap items-center justify-between gap-3 text-xs text-ink-800">
        <span className="flex flex-wrap gap-x-4 gap-y-1">
          <span><strong>{receivedUnits}</strong> of {expectedUnits} item{expectedUnits === 1 ? '' : 's'} received</span>
          <span>Verified total <strong>{formatKg(verifiedTotal)}</strong></span>
          <span>Reported {job.reportedWeightKg !== null ? formatKg(job.reportedWeightKg) : 'n/a'}</span>
          {difference !== null && Math.abs(difference) >= 0.005 && (
            <span className={Math.abs(difference) > Math.max(1, (job.reportedWeightKg ?? 0) * 0.1) ? 'font-semibold text-amber-700' : ''}>
              Difference {formatSignedKg(difference)}
            </span>
          )}
        </span>

        {splitting ? (
          <span className="flex items-center gap-2">
            <input
              aria-label="Total weight to split"
              type="number" min="0" step="0.01" value={total} onChange={(e) => setTotal(e.target.value)}
              className={`${inputClass} w-32`} placeholder="Total kg"
            />
            <button
              type="button"
              className={btnSecondary}
              disabled={!(Number(total) > 0) || receivedUnits === 0}
              onClick={() => {
                onChange({ ...entry, items: splitTotal(job.items, entry.items, Number(total)) });
                setSplitting(false);
              }}
            >
              Split
            </button>
            <button type="button" className="text-ink-600 hover:underline" onClick={() => setSplitting(false)}>Cancel</button>
          </span>
        ) : (
          job.items.length > 1 && (
            <button type="button" onClick={() => setSplitting(true)} className="inline-flex items-center gap-1 font-semibold text-mint-700 hover:underline">
              <Scale size={13} /> Weigh as one total
            </button>
          )
        )}
      </div>
    </div>
  );
};

export default JobItemsReceiver;
