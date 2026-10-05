import React, { useState } from 'react';
import { ChevronDown, ChevronUp, Flag } from 'lucide-react';
import { formatKg, formatMoney } from '../processing/utils/format';

/** One item or CSV row as the Analyzer classified it; weight and value are for the whole row. */
export interface BreakdownItem {
  itemName: string | null;
  quantity?: number | null;
  wasteCategory: string | null;
  hazardLevel: string | null;
  estimatedVolumeKg: number | null;
  estimatedValueLkr: number | null;
  confidenceScore?: number | null;
}

// Same idea as the Validator's rules: these are what send a submission to a person.
const LOW_CONFIDENCE = 0.6;
const COLLAPSED_COUNT = 5;

const isSevere = (hazard: string | null) => hazard === 'High' || hazard === 'Critical';

export const flagReasons = (item: BreakdownItem): string[] => {
  const reasons: string[] = [];
  if (isSevere(item.hazardLevel)) reasons.push(`${item.hazardLevel} hazard`);
  if (!item.wasteCategory || item.wasteCategory === 'Uncategorized') reasons.push('not classified');
  if (item.confidenceScore != null && item.confidenceScore < LOW_CONFIDENCE) {
    reasons.push(`confidence ${Math.round(item.confidenceScore * 100)}%`);
  }
  return reasons;
};

/**
 * Each item's own AI classification. The headline figures above it are these combined (worst
 * hazard, totals), so this shows which items drove them: flagged items first, the rest collapsed
 * after the first few. Hidden for single-item submissions, where it would only repeat the headline.
 */
export const ItemBreakdown: React.FC<{ items?: BreakdownItem[]; className?: string }> = ({ items, className = '' }) => {
  const [expanded, setExpanded] = useState(false);
  if (!items || items.length < 2) return null;

  // Keep each item's original number (CSV row) while moving flagged ones to the top.
  const numbered = items.map((item, i) => ({ item, number: i + 1, reasons: flagReasons(item) }));
  const ordered = [...numbered.filter((x) => x.reasons.length > 0), ...numbered.filter((x) => x.reasons.length === 0)];
  const flaggedCount = numbered.length - numbered.filter((x) => x.reasons.length === 0).length;
  const visible = expanded ? ordered : ordered.slice(0, Math.max(COLLAPSED_COUNT, flaggedCount));
  const hidden = ordered.length - visible.length;

  return (
    <div className={className}>
      {flaggedCount > 0 && (
        <p className="mb-1 flex items-center gap-1 text-xs font-semibold text-red-700">
          <Flag size={12} /> {flaggedCount} of {items.length} items flagged
        </p>
      )}
      <ul className="divide-y divide-mint-100 rounded-xl border border-mint-100 bg-white/60 text-xs">
        {visible.map(({ item, number, reasons }) => (
          <li
            key={number}
            className={`flex flex-wrap items-center gap-x-4 gap-y-1 px-3 py-2 text-ink-800 ${reasons.length ? 'bg-red-50/60' : ''}`}
          >
            <span className="min-w-[8rem] font-semibold text-ink-900">
              {number}. {item.itemName || 'Item'}
              {item.quantity && item.quantity > 1 ? <span className="font-normal text-ink-600"> × {item.quantity}</span> : null}
            </span>
            <span>{item.wasteCategory ?? '—'}</span>
            <span className={`font-bold ${isSevere(item.hazardLevel) ? 'text-red-600' : 'text-mint-700'}`}>{item.hazardLevel ?? '—'}</span>
            {item.estimatedVolumeKg !== null && <span>{formatKg(item.estimatedVolumeKg)}</span>}
            {item.estimatedValueLkr !== null && <span>{formatMoney(item.estimatedValueLkr)}</span>}
            {reasons.length > 0 && <span className="font-semibold text-red-700">{reasons.join(', ')}</span>}
          </li>
        ))}
      </ul>
      {(hidden > 0 || expanded) && ordered.length > COLLAPSED_COUNT && (
        <button
          type="button"
          onClick={() => setExpanded((v) => !v)}
          className="mt-1 inline-flex items-center gap-1 text-xs font-semibold text-mint-700 hover:underline"
        >
          {expanded ? (
            <>
              <ChevronUp size={13} /> Show fewer
            </>
          ) : (
            <>
              <ChevronDown size={13} /> Show all {items.length} items ({hidden} more)
            </>
          )}
        </button>
      )}
    </div>
  );
};

export default ItemBreakdown;
