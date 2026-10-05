import React from 'react';
import { AlertTriangle, CheckCircle, Download, FileSpreadsheet, X } from 'lucide-react';
import { Notice } from '../../components/ui';
import { formatKg } from '../processing/utils/format';
import {
  CSV_COLUMNS,
  MAX_CSV_BYTES,
  MAX_CSV_ROWS,
  csvTotals,
  downloadCsvTemplate,
  readCsv,
  type CsvParseResult,
} from './csvImport';

export interface CsvUploadState {
  fileName: string;
  result: CsvParseResult;
}

/** True when the upload can be submitted: a file, at least one row, and no file or row errors. */
export const csvIsReady = (state: CsvUploadState | null): state is CsvUploadState =>
  !!state &&
  state.result.fileErrors.length === 0 &&
  state.result.rows.length > 0 &&
  state.result.rows.every((r) => r.errors.length === 0);

interface Props {
  value: CsvUploadState | null;
  onChange: (value: CsvUploadState | null) => void;
  /** Row errors the backend returned (0-based row index → messages). */
  serverRowErrors?: Record<number, string[]>;
}

/**
 * The corporate "Upload CSV" tab: template, file picker, and a preview of every row with its
 * problems, so a file is fixed before it is sent rather than rejected afterwards.
 */
export const CsvUploadPanel: React.FC<Props> = ({ value, onChange, serverRowErrors = {} }) => {
  const [readError, setReadError] = React.useState<string | null>(null);

  const handleFile = async (file: File | undefined) => {
    if (!file) return;
    setReadError(null);
    if (!/\.csv$/i.test(file.name)) {
      setReadError('Choose a .csv file. In Excel or Google Sheets use "Save as / Download → CSV".');
      return;
    }
    if (file.size > MAX_CSV_BYTES) {
      setReadError('The file must be 1 MB or smaller.');
      return;
    }
    onChange({ fileName: file.name, result: readCsv(await file.text()) });
  };

  const rows = value?.result.rows ?? [];
  const totals = csvTotals(rows);
  const rowErrorCount = rows.filter((r) => r.errors.length > 0 || serverRowErrors[r.row - 1]).length;

  return (
    <div className="flex flex-col gap-3">
      <div className="rounded-2xl border border-mint-100 bg-white/60 p-4 text-sm text-ink-800">
        <p>
          One row per kind of item. Columns: <code className="text-xs">{CSV_COLUMNS.join(', ')}</code> —
          only <code className="text-xs">item_name</code> is required. Quantity defaults to 1 and the weight is <em>per unit</em>.
          Up to {MAX_CSV_ROWS} rows, 1 MB.
        </p>
        <button
          type="button"
          onClick={downloadCsvTemplate}
          className="mt-2 inline-flex items-center gap-1.5 text-xs font-semibold text-mint-700 hover:underline"
        >
          <Download size={13} /> Download the template
        </button>
      </div>

      {!value ? (
        <label className="flex cursor-pointer flex-col items-center gap-2 rounded-2xl border-2 border-dashed border-mint-300 bg-white/50 px-4 py-8 text-center text-sm text-ink-800 hover:bg-mint-50 focus-within:ring-2 focus-within:ring-mint-500">
          <FileSpreadsheet size={26} className="text-mint-600" />
          <span className="font-semibold">Choose a CSV file</span>
          <span className="text-xs text-ink-600">Saved from Excel, Google Sheets or any spreadsheet app</span>
          <input
            type="file"
            accept=".csv,text/csv"
            className="sr-only"
            onChange={(e) => {
              void handleFile(e.target.files?.[0]);
              e.target.value = '';
            }}
          />
        </label>
      ) : (
        <div className="flex flex-wrap items-center justify-between gap-2 rounded-2xl border border-mint-100 bg-white/60 px-4 py-3">
          <span className="flex items-center gap-2 text-sm font-semibold text-ink-900">
            <FileSpreadsheet size={16} className="text-mint-600" /> {value.fileName}
          </span>
          <button
            type="button"
            onClick={() => onChange(null)}
            className="flex items-center gap-1 rounded-lg px-2 py-1 text-xs font-semibold text-red-600 hover:bg-red-50"
          >
            <X size={13} /> Remove file
          </button>
        </div>
      )}

      {readError && <Notice tone="error">{readError}</Notice>}

      {value && value.result.fileErrors.length > 0 && (
        <Notice tone="error">
          <ul className="list-disc pl-4">
            {value.result.fileErrors.map((e) => (
              <li key={e}>{e}</li>
            ))}
          </ul>
        </Notice>
      )}

      {value && value.result.ignoredColumns.length > 0 && (
        <Notice tone="info">Ignored columns: {value.result.ignoredColumns.join(', ')}.</Notice>
      )}

      {rows.length > 0 && (
        <>
          <div className="flex flex-wrap items-center gap-x-5 gap-y-1 text-sm text-ink-800">
            <span><strong>{rows.length}</strong> row{rows.length === 1 ? '' : 's'}</span>
            <span><strong>{totals.units}</strong> item{totals.units === 1 ? '' : 's'}</span>
            <span>
              <strong>{formatKg(totals.weightKg)}</strong> total
              {totals.rowsWithoutWeight > 0 && (
                <span className="text-ink-600"> (+ {totals.rowsWithoutWeight} row{totals.rowsWithoutWeight === 1 ? '' : 's'} without a weight — the AI estimates those)</span>
              )}
            </span>
            {rowErrorCount > 0 ? (
              <span className="flex items-center gap-1 font-semibold text-red-600">
                <AlertTriangle size={14} /> {rowErrorCount} row{rowErrorCount === 1 ? '' : 's'} to fix
              </span>
            ) : (
              <span className="flex items-center gap-1 font-semibold text-mint-700">
                <CheckCircle size={14} /> Ready to submit
              </span>
            )}
          </div>

          <div className="max-h-80 overflow-auto rounded-2xl border border-mint-100 bg-white/70">
            <table className="w-full min-w-[640px] border-collapse text-xs">
              <thead className="sticky top-0 bg-mint-50 text-left font-mono uppercase tracking-wide text-ink-600">
                <tr>
                  <th className="px-3 py-2">Row</th>
                  <th className="px-3 py-2">Item</th>
                  <th className="px-3 py-2 text-right">Qty</th>
                  <th className="px-3 py-2 text-right">kg / unit</th>
                  <th className="px-3 py-2 text-right">Row kg</th>
                  <th className="px-3 py-2">Category</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((r) => {
                  const problems = [...r.errors, ...(serverRowErrors[r.row - 1] ?? [])];
                  const bad = problems.length > 0;
                  return (
                    <React.Fragment key={r.row}>
                      <tr className={`border-t border-mint-50 align-top ${bad ? 'bg-red-50/70' : ''}`}>
                        <td className="px-3 py-2 font-mono text-ink-600">{r.row}</td>
                        <td className="px-3 py-2 text-ink-900">
                          <span className="font-semibold">{r.itemName || <em className="text-red-600">missing</em>}</span>
                          {r.description && <span className="block text-ink-600">{r.description}</span>}
                        </td>
                        <td className="px-3 py-2 text-right font-mono">{Number.isFinite(r.quantity) ? r.quantity : '—'}</td>
                        <td className="px-3 py-2 text-right font-mono">{r.estimatedWeightKg !== null && Number.isFinite(r.estimatedWeightKg) ? r.estimatedWeightKg : '—'}</td>
                        <td className="px-3 py-2 text-right font-mono">
                          {r.estimatedWeightKg !== null && !bad ? formatKg(r.estimatedWeightKg * r.quantity) : '—'}
                        </td>
                        <td className="px-3 py-2 text-ink-800">{r.category || '—'}</td>
                      </tr>
                      {bad && (
                        <tr className="bg-red-50/70">
                          <td />
                          <td colSpan={5} className="px-3 pb-2 text-red-700">
                            {problems.join(' ')}
                          </td>
                        </tr>
                      )}
                    </React.Fragment>
                  );
                })}
              </tbody>
            </table>
          </div>
        </>
      )}
    </div>
  );
};

export default CsvUploadPanel;
