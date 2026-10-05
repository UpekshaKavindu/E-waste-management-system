import React, { useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import {
  CheckCircle, AlertTriangle, Cpu, ShieldAlert,
  Tag, Weight, Banknote, Plus, Trash2, Clock, Send, Image as ImageIcon, Loader2, X,
} from 'lucide-react';
import { submissionApi } from './submissionApi';
import { extractFieldErrors, extractGeneralError, type SubmissionFieldErrors } from './submissionErrors';
import { IN_PROGRESS_STATUSES, SUBMISSION_CATEGORIES, type SubmissionResponse } from './types';
import { SubmissionProgress } from './SubmissionProgress';
import { GlassCard, Notice, PageHeader, StatusPill, btnPrimary, inputClass, labelClass } from '../../components/ui';
import type { StatusTone } from '../../components/ui/StatusPill';
import { uploadApi } from '../../api/uploadApi';
import { formatMoney } from '../processing/utils/format';
import { ItemBreakdown } from './ItemBreakdown';
import { CsvUploadPanel, csvIsReady, type CsvUploadState } from './CsvUploadPanel';
import { csvTotals } from './csvImport';
import { useCurrentUser } from '../processing/hooks/useCurrentUser';

// Same limit as CreateSubmissionDtoValidator.MaxItems: each item is classified on its own.
const MAX_ITEMS = 3;

// Statuses SubmissionProgress can represent as a normal step reached along
// the happy path (including the successful end states it marks "done").
const PROGRESS_STATUSES = ['Analyzing', 'AwaitingReview', 'Scheduling', 'CollectorAssigned', 'AwaitingCollector', 'Collected', 'Closed'];

const POLL_INTERVAL_MS = 1500;
const POLL_TIMEOUT_MS = 2 * 60 * 1000;

// Must match UploadRequestValidator on the backend — checked here too so a
// bad file is rejected instantly, without a round trip.
const ALLOWED_IMAGE_TYPES = ['image/jpeg', 'image/png', 'image/webp'];
const MAX_IMAGE_BYTES = 5 * 1024 * 1024;

const STATUS_TONE: Record<string, StatusTone> = {
  CollectorAssigned: 'success',
  AwaitingCollector: 'success',
  Collected: 'success',
  Closed: 'success',
  Rejected: 'error',
  Failed: 'error',
  Cancelled: 'error',
};

interface ItemDraft {
  key: string;
  itemName: string;
  description: string;
  imageUrl: string;
  imageUploading: boolean;
  imageError: string | null;
}

const newItem = (): ItemDraft => ({
  key: crypto.randomUUID(), itemName: '', description: '', imageUrl: '', imageUploading: false, imageError: null,
});

type EntryTab = 'items' | 'csv';

const SubmitPage: React.FC = () => {
  // The CSV tab is for corporate accounts only (the backend refuses CSV from anyone else).
  // It goes by account type, so a corporate account that is also a buyer still sees it.
  const isCorporate = useCurrentUser()?.role?.toLowerCase() === 'corporate';
  const [tab, setTab] = useState<EntryTab>('items');
  const mode: EntryTab = isCorporate ? tab : 'items';
  const [csv, setCsv] = useState<CsvUploadState | null>(null);
  const csvUnits = csv ? csvTotals(csv.result.rows).units : 0;

  const [category, setCategory] = useState('');
  const [estimatedWeight, setEstimatedWeight] = useState('');
  const [pickupAddress, setPickupAddress] = useState('');
  const [phoneNumber, setPhoneNumber] = useState('');
  const [items, setItems] = useState<ItemDraft[]>([newItem()]);

  const [loading, setLoading] = useState(false);
  const [submission, setSubmission] = useState<SubmissionResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [fieldErrors, setFieldErrors] = useState<SubmissionFieldErrors | null>(null);
  const [timedOut, setTimedOut] = useState(false);

  const polling = !!submission && IN_PROGRESS_STATUSES.includes(submission.status) && !timedOut;

  const updateItem = (key: string, patch: Partial<ItemDraft>) =>
    setItems((prev) => prev.map((it) => (it.key === key ? { ...it, ...patch } : it)));
  const addItem = () => setItems((prev) => (prev.length >= MAX_ITEMS ? prev : [...prev, newItem()]));
  const removeItem = (key: string) => setItems((prev) => (prev.length <= 1 ? prev : prev.filter((it) => it.key !== key)));

  const handleImageSelect = async (key: string, file: File | undefined) => {
    if (!file) return;

    if (!ALLOWED_IMAGE_TYPES.includes(file.type)) {
      updateItem(key, { imageError: 'Image must be JPEG, PNG, or WEBP.' });
      return;
    }
    if (file.size > MAX_IMAGE_BYTES) {
      updateItem(key, { imageError: 'Image must be 5 MB or smaller.' });
      return;
    }

    updateItem(key, { imageUploading: true, imageError: null });
    try {
      const url = await uploadApi.uploadImage(file);
      updateItem(key, { imageUrl: url, imageUploading: false });
    } catch (err) {
      console.error(err);
      updateItem(key, { imageUploading: false, imageError: extractGeneralError(err) });
    }
  };

  const anyImageUploading = items.some((it) => it.imageUploading);

  const handleSubmit = async (e: React.FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    setLoading(true);
    setSubmission(null);
    setError(null);
    setFieldErrors(null);
    setTimedOut(false);

    try {
      // Owner and user type come from the logged-in session on the backend
      // (the JWT), never from this payload.
      const data = await submissionApi.create(
        mode === 'csv' && csv
          ? {
              source: 'Csv',
              pickupAddress,
              phoneNumber,
              items: csv.result.rows.map((r) => ({
                itemName: r.itemName,
                description: r.description,
                quantity: r.quantity,
                estimatedWeightKg: r.estimatedWeightKg,
                category: r.category || null,
              })),
            }
          : {
              source: 'Manual',
              category,
              estimatedWeight: Number(estimatedWeight),
              pickupAddress,
              phoneNumber,
              items: items.map(({ itemName, description, imageUrl }) => ({ itemName, description, imageUrl })),
            },
      );
      setSubmission(data);
    } catch (err) {
      console.error(err);
      const fields = extractFieldErrors(err);
      if (fields) setFieldErrors(fields);
      else setError(extractGeneralError(err));
    } finally {
      setLoading(false);
    }
  };

  // Poll while the agent chain is still working on its own. Stops as soon as
  // the derived status leaves Analyzing/Scheduling — that covers every pause
  // (AwaitingReview) and every terminal outcome (CollectorAssigned, Rejected,
  // Failed, Cancelled, Closed, ...) in one check, not a list of old values.
  const submissionId = submission?.id;
  const pollStartedAt = useRef<number>(0);
  useEffect(() => {
    if (!polling || !submissionId) return;
    pollStartedAt.current = Date.now();
    const timer = setInterval(async () => {
      if (Date.now() - pollStartedAt.current >= POLL_TIMEOUT_MS) {
        setTimedOut(true);
        return;
      }
      try {
        setSubmission(await submissionApi.getById(submissionId));
      } catch (err) {
        console.error('Polling error:', err);
      }
    }, POLL_INTERVAL_MS);
    return () => clearInterval(timer);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [polling, submissionId]);

  const ai = submission?.workflow?.analysis;

  return (
    <div className="mx-auto max-w-3xl">
      <PageHeader
        title="Smart e-waste collector"
        subtitle="Submit e-waste items for pickup — our AI assesses hazard level and category automatically."
        icon={Cpu}
      />

      <GlassCard hover={false} className="p-6">
        <form onSubmit={handleSubmit} className="flex flex-col gap-5">
          {isCorporate ? (
            <>
              <div>
                <label className={labelClass}>Pickup address</label>
                <input
                  type="text"
                  className={inputClass}
                  placeholder="e.g. 12 Main Street, Colombo 03"
                  value={pickupAddress}
                  onChange={(e) => setPickupAddress(e.target.value)}
                  required
                />
                <FieldError message={fieldErrors?.pickupAddress} />
              </div>
              <div>
                <label className={labelClass}>Phone number</label>
                <input
                  type="tel"
                  className={inputClass}
                  placeholder="e.g. 0771234567 or +94771234567"
                  value={phoneNumber}
                  onChange={(e) => setPhoneNumber(e.target.value)}
                  required
                />
                <FieldError message={fieldErrors?.phoneNumber} />
              </div>

              <div role="tablist" aria-label="How to add items" className="inline-flex self-start rounded-xl border border-mint-100 bg-white/60 p-1">
                {([['items', 'Add items'], ['csv', 'Upload CSV']] as const).map(([key, label]) => (
                  <button
                    key={key}
                    type="button"
                    role="tab"
                    aria-selected={tab === key}
                    onClick={() => setTab(key)}
                    className={`rounded-lg px-4 py-1.5 text-sm font-semibold transition ${
                      tab === key ? 'bg-mint-600 text-white shadow-sm' : 'text-ink-800 hover:bg-mint-50'
                    }`}
                  >
                    {label}
                  </button>
                ))}
              </div>

              {/* Each tab's entries live in this page's state, so switching tabs loses nothing. */}
              {tab === 'items' ? (
                <>
                  <div>
                    <label className={labelClass}>Category</label>
                    <select
                      className={inputClass}
                      value={category}
                      onChange={(e) => setCategory(e.target.value)}
                      required
                    >
                      <option value="" disabled>Select a category…</option>
                      {SUBMISSION_CATEGORIES.map((c) => (
                        <option key={c} value={c}>{c}</option>
                      ))}
                    </select>
                    <FieldError message={fieldErrors?.category} />
                  </div>
                  <div>
                    <label className={labelClass}>Estimated weight (kg)</label>
                    <input
                      type="number"
                      min="0.1"
                      step="0.1"
                      className={inputClass}
                      value={estimatedWeight}
                      onChange={(e) => setEstimatedWeight(e.target.value)}
                      required
                    />
                    <FieldError message={fieldErrors?.estimatedWeight} />
                  </div>




                  <div>
                    <label className={labelClass}>Items ({items.length}/{MAX_ITEMS})</label>
                    <FieldError message={fieldErrors?.itemsGeneral} />

                    <div className="flex flex-col gap-3 mt-1">
                      {items.map((item, i) => (
                        <div key={item.key} className="rounded-2xl border border-mint-100 bg-white/60 p-4">
                          <div className="flex items-center justify-between mb-2.5">
                            <span className="text-xs font-mono uppercase tracking-wide text-ink-600">Item {i + 1}</span>
                            {items.length > 1 && (
                              <button
                                type="button"
                                onClick={() => removeItem(item.key)}
                                className="flex items-center rounded-lg p-1.5 text-red-600 hover:bg-red-50"
                                aria-label={`Remove item ${i + 1}`}
                              >
                                <Trash2 size={14} />
                              </button>
                            )}
                          </div>

                          <input
                            type="text"
                            className={`${inputClass} mb-2`}
                            placeholder="Item name (e.g. Laptop)"
                            value={item.itemName}
                            onChange={(e) => updateItem(item.key, { itemName: e.target.value })}
                            required
                          />
                          <FieldError message={fieldErrors?.items[i]?.itemName} />

                          <textarea
                            rows={2}
                            className={`${inputClass} mb-2`}
                            placeholder="Description (e.g. Old laptop, screen cracked, still boots)"
                            value={item.description}
                            onChange={(e) => updateItem(item.key, { description: e.target.value })}
                            required
                          />
                          <FieldError message={fieldErrors?.items[i]?.description} />

                          <label className="inline-flex cursor-pointer items-center gap-1.5 rounded-xl border border-dashed border-mint-300 px-3.5 py-2 text-xs font-semibold text-mint-700 hover:bg-mint-50 focus-within:ring-2 focus-within:ring-mint-500">
                            <ImageIcon size={14} /> {item.imageUrl ? 'Change photo' : 'Add photo (optional)'}
                            <input
                              type="file"
                              accept="image/jpeg,image/png,image/webp"
                              className="sr-only"
                              onChange={(e) => {
                                void handleImageSelect(item.key, e.target.files?.[0]);
                                e.target.value = ''; // lets the same file be re-picked after an error
                              }}
                            />
                          </label>

                          {item.imageUploading && (
                            <p className="mt-2 flex items-center gap-1.5 text-xs text-amber-700">
                              <Loader2 size={13} className="animate-spin motion-reduce:animate-none" /> Uploading…
                            </p>
                          )}
                          {item.imageUrl && !item.imageUploading && (
                            <div className="mt-2 flex items-center gap-2">
                              <img src={item.imageUrl} alt="" className="h-16 w-16 rounded-xl border border-mint-100 object-cover" />
                              <button
                                type="button"
                                onClick={() => updateItem(item.key, { imageUrl: '' })}
                                className="flex items-center rounded-lg p-1.5 text-red-600 hover:bg-red-50"
                                aria-label="Remove photo"
                              >
                                <X size={14} />
                              </button>
                            </div>
                          )}
                          <FieldError message={item.imageError ?? undefined} />
                          <FieldError message={fieldErrors?.items[i]?.imageUrl} />
                        </div>
                      ))}
                    </div>

                    <button
                      type="button"
                      onClick={addItem}
                      disabled={items.length >= MAX_ITEMS}
                      className="mt-3 flex items-center gap-1.5 rounded-xl border border-dashed border-mint-300 px-3.5 py-2 text-xs font-semibold text-mint-700 hover:bg-mint-50 disabled:opacity-50"
                    >
                      <Plus size={14} /> Add another item
                    </button>
                  </div>

                </>
              ) : (
                <CsvUploadPanel value={csv} onChange={setCsv} serverRowErrors={fieldErrors?.itemMessages} />
              )}
            </>
          ) : (
            <>
              <div>
                <label className={labelClass}>Category</label>
                <select
                  className={inputClass}
                  value={category}
                  onChange={(e) => setCategory(e.target.value)}
                  required
                >
                  <option value="" disabled>Select a category…</option>
                  {SUBMISSION_CATEGORIES.map((c) => (
                    <option key={c} value={c}>{c}</option>
                  ))}
                </select>
                <FieldError message={fieldErrors?.category} />
              </div>
              <div>
                <label className={labelClass}>Estimated weight (kg)</label>
                <input
                  type="number"
                  min="0.1"
                  step="0.1"
                  className={inputClass}
                  value={estimatedWeight}
                  onChange={(e) => setEstimatedWeight(e.target.value)}
                  required
                />
                <FieldError message={fieldErrors?.estimatedWeight} />
              </div>
              <div>
                <label className={labelClass}>Pickup address</label>
                <input
                  type="text"
                  className={inputClass}
                  placeholder="e.g. 12 Main Street, Colombo 03"
                  value={pickupAddress}
                  onChange={(e) => setPickupAddress(e.target.value)}
                  required
                />
                <FieldError message={fieldErrors?.pickupAddress} />
              </div>
              <div>
                <label className={labelClass}>Phone number</label>
                <input
                  type="tel"
                  className={inputClass}
                  placeholder="e.g. 0771234567 or +94771234567"
                  value={phoneNumber}
                  onChange={(e) => setPhoneNumber(e.target.value)}
                  required
                />
                <FieldError message={fieldErrors?.phoneNumber} />
              </div>




              <div>
                <label className={labelClass}>Items ({items.length}/{MAX_ITEMS})</label>
                <FieldError message={fieldErrors?.itemsGeneral} />

                <div className="flex flex-col gap-3 mt-1">
                  {items.map((item, i) => (
                    <div key={item.key} className="rounded-2xl border border-mint-100 bg-white/60 p-4">
                      <div className="flex items-center justify-between mb-2.5">
                        <span className="text-xs font-mono uppercase tracking-wide text-ink-600">Item {i + 1}</span>
                        {items.length > 1 && (
                          <button
                            type="button"
                            onClick={() => removeItem(item.key)}
                            className="flex items-center rounded-lg p-1.5 text-red-600 hover:bg-red-50"
                            aria-label={`Remove item ${i + 1}`}
                          >
                            <Trash2 size={14} />
                          </button>
                        )}
                      </div>

                      <input
                        type="text"
                        className={`${inputClass} mb-2`}
                        placeholder="Item name (e.g. Laptop)"
                        value={item.itemName}
                        onChange={(e) => updateItem(item.key, { itemName: e.target.value })}
                        required
                      />
                      <FieldError message={fieldErrors?.items[i]?.itemName} />

                      <textarea
                        rows={2}
                        className={`${inputClass} mb-2`}
                        placeholder="Description (e.g. Old laptop, screen cracked, still boots)"
                        value={item.description}
                        onChange={(e) => updateItem(item.key, { description: e.target.value })}
                        required
                      />
                      <FieldError message={fieldErrors?.items[i]?.description} />

                      <label className="inline-flex cursor-pointer items-center gap-1.5 rounded-xl border border-dashed border-mint-300 px-3.5 py-2 text-xs font-semibold text-mint-700 hover:bg-mint-50 focus-within:ring-2 focus-within:ring-mint-500">
                        <ImageIcon size={14} /> {item.imageUrl ? 'Change photo' : 'Add photo (optional)'}
                        <input
                          type="file"
                          accept="image/jpeg,image/png,image/webp"
                          className="sr-only"
                          onChange={(e) => {
                            void handleImageSelect(item.key, e.target.files?.[0]);
                            e.target.value = ''; // lets the same file be re-picked after an error
                          }}
                        />
                      </label>

                      {item.imageUploading && (
                        <p className="mt-2 flex items-center gap-1.5 text-xs text-amber-700">
                          <Loader2 size={13} className="animate-spin motion-reduce:animate-none" /> Uploading…
                        </p>
                      )}
                      {item.imageUrl && !item.imageUploading && (
                        <div className="mt-2 flex items-center gap-2">
                          <img src={item.imageUrl} alt="" className="h-16 w-16 rounded-xl border border-mint-100 object-cover" />
                          <button
                            type="button"
                            onClick={() => updateItem(item.key, { imageUrl: '' })}
                            className="flex items-center rounded-lg p-1.5 text-red-600 hover:bg-red-50"
                            aria-label="Remove photo"
                          >
                            <X size={14} />
                          </button>
                        </div>
                      )}
                      <FieldError message={item.imageError ?? undefined} />
                      <FieldError message={fieldErrors?.items[i]?.imageUrl} />
                    </div>
                  ))}
                </div>

                <button
                  type="button"
                  onClick={addItem}
                  disabled={items.length >= MAX_ITEMS}
                  className="mt-3 flex items-center gap-1.5 rounded-xl border border-dashed border-mint-300 px-3.5 py-2 text-xs font-semibold text-mint-700 hover:bg-mint-50 disabled:opacity-50"
                >
                  <Plus size={14} /> Add another item
                </button>
              </div>

            </>
          )}

          <button
            type="submit"
            disabled={loading || polling || (mode === 'items' ? anyImageUploading : !csvIsReady(csv))}
            className={`${btnPrimary} self-start`}
          >
            <Send size={14} />{' '}
            {loading
              ? 'Submitting…'
              : polling
                ? 'Processing…'
                : mode === 'csv'
                  ? csv
                    ? `Submit ${csvUnits} item${csvUnits === 1 ? '' : 's'} from CSV`
                    : 'Choose a CSV file to submit'
                  : anyImageUploading
                    ? 'Uploading photo…'
                    : 'Submit e-waste item'}
          </button>
        </form>
      </GlassCard>

      {error && (
        <Notice tone="error" className="mt-5">
          <AlertTriangle size={16} className="inline -mt-0.5 mr-1" /> {error}
        </Notice>
      )}

      {submission && (
        <GlassCard hover={false} className="mt-5 p-6">
          <h3 className="flex items-center gap-2 font-display font-bold text-mint-700 mb-3">
            <CheckCircle size={18} /> Submission recorded
          </h3>
          <p className="text-sm text-ink-800 mb-1"><strong>ID:</strong> <code className="text-xs">{submission.id}</code></p>
          <div className="mb-2 flex items-center gap-2 text-sm text-ink-800">
            <strong>Status:</strong> <StatusPill label={submission.statusLabel} tone={STATUS_TONE[submission.status] ?? 'info'} />
          </div>

          {PROGRESS_STATUSES.includes(submission.status) && (
            <SubmissionProgress
              status={submission.status}
              approvalRequired={!!submission.workflow?.approvalRequired}
            />
          )}

          {submission.status === 'Failed' && submission.statusReason && (
            <Notice tone="error" className="mt-2" title="Failure reason">{submission.statusReason}</Notice>
          )}
          {submission.status === 'Rejected' && (
            <Notice tone="error" className="mt-2" title="Rejected">{submission.statusReason ?? undefined}</Notice>
          )}

          {timedOut ? (
            <Notice tone="warning" className="mt-4">
              <Clock size={14} className="inline -mt-0.5 mr-1" /> Still processing after 2 minutes. Check{' '}
              <Link to="/submissions/mine" className="font-semibold underline underline-offset-2">My Submissions</Link>{' '}
              for updates.
            </Notice>
          ) : polling ? (
            <Notice tone="info" className="mt-4">AI is analyzing your submission…</Notice>
          ) : ai ? (
            <div className="mt-4 rounded-2xl border border-mint-100 bg-mint-50/60 p-4">
              <h4 className="font-display font-bold text-ink-900 mb-3 flex items-center gap-2">
                <Cpu size={16} className="text-mint-700" /> AI assessment
              </h4>
              <div className="grid grid-cols-2 gap-3 text-sm text-ink-800">
                <p className="flex items-center gap-1.5"><Tag size={14} className="text-mint-700" /> <strong>Category:</strong> {ai.wasteCategory}</p>
                <p className="flex items-center gap-1.5">
                  <ShieldAlert size={14} className={ai.hazardLevel === 'Critical' || ai.hazardLevel === 'High' ? 'text-red-600' : 'text-mint-700'} />
                  <strong>Hazard:</strong>{' '}
                  <span className={`font-bold ${ai.hazardLevel === 'Critical' || ai.hazardLevel === 'High' ? 'text-red-600' : 'text-mint-700'}`}>
                    {ai.hazardLevel}
                  </span>
                </p>
                <p className="flex items-center gap-1.5"><Weight size={14} className="text-mint-700" /> <strong>Est. weight:</strong> {ai.estimatedVolumeKg} kg</p>
                <p className="flex items-center gap-1.5"><Banknote size={14} className="text-mint-700" /> <strong>Est. value:</strong> {formatMoney(ai.estimatedValueLkr)}</p>
              </div>
              <ItemBreakdown items={ai.items} className="mt-3" />
              {submission.workflow?.approvalRequired && submission.status === 'AwaitingReview' && (
                <p className="mt-3 flex items-center gap-1.5 text-sm font-bold text-amber-700">
                  <AlertTriangle size={14} /> Requires admin approval
                </p>
              )}
            </div>
          ) : null}
        </GlassCard>
      )}
    </div>
  );
};

const FieldError: React.FC<{ message?: string }> = ({ message }) =>
  message ? <p className="mt-1 text-xs font-medium text-red-600">{message}</p> : null;

export default SubmitPage;
