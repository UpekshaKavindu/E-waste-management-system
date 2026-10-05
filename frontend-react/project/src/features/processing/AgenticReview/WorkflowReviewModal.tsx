import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { Bot, CheckCircle2, ShieldCheck, ThumbsDown, ThumbsUp, XCircle } from 'lucide-react';
import { agenticReviewApi } from './agenticReviewApi';
import { parseAnalyzerResult, parseValidatorResult } from './parseWorkflow';
import { WORKFLOW_STATUS_LABELS, type ApprovalEntry, type ExecutionStep, type WorkflowStatus, type WorkflowSummary } from './types';
import { getApiErrorMessage } from '../utils/apiError';
import { formatDateTime, formatKg, formatMoney, shortId } from '../utils/format';
import { ItemBreakdown } from '../../submissions/ItemBreakdown';
import {
  ErrorMessage,
  GlassCard,
  Modal,
  Notice,
  StatusBadge,
  btnDanger,
  btnPrimary,
  btnSecondary,
  inputClass,
  labelClass,
  tableCellClass,
  tableHeadClass,
} from '../components';

interface WorkflowReviewModalProps {
  workflow: WorkflowSummary | null;
  /** Admins and management staff may approve/reject; the decision is recorded with their name. */
  canDecide: boolean;
  onClose: () => void;
  /** Called after a decision so the list behind can refresh. */
  onDecided: (message: string) => void;
}

const statusLabel = (s: string): string => WORKFLOW_STATUS_LABELS[s as WorkflowStatus] ?? s;

const AgentCard: React.FC<{ name: string; role: string; ran: boolean | null; children: React.ReactNode }> = ({ name, role, ran, children }) => (
  <div className="rounded-2xl border border-mint-100 bg-white/60 p-4">
    <div className="mb-2 flex items-start justify-between gap-2">
      <div>
        <h5 className="flex items-center gap-1.5 text-sm font-bold text-ink-900">
          <Bot size={14} className="text-mint-600" /> {name}
        </h5>
        <p className="text-[11px] text-ink-600">{role}</p>
      </div>
      {ran === false && <span className="rounded-full bg-ink-100 px-2 py-0.5 text-[11px] font-medium text-ink-600">No result</span>}
    </div>
    {children}
  </div>
);

const KeyValue: React.FC<{ label: string; children: React.ReactNode }> = ({ label, children }) => (
  <div className="flex items-baseline justify-between gap-3 py-1 text-sm">
    <dt className="text-xs text-ink-600">{label}</dt>
    <dd className="text-right text-ink-900">{children}</dd>
  </div>
);

const yesNo = (v: boolean | null): string => (v === null ? 'Not recorded' : v ? 'Yes' : 'No');

// Only the Analyzer and Validator are shown here; the Planner and Matcher still run, their output is just not displayed.
const isShownAgent = (agentName: string): boolean => /analy[sz]er|validator/i.test(agentName);

const durationLabel = (s: ExecutionStep): string => {
  if (!s.completedAt) return 'running / unfinished';
  const ms = new Date(s.completedAt).getTime() - new Date(s.startedAt).getTime();
  if (!Number.isFinite(ms) || ms < 0) return '—';
  return ms < 1000 ? `${ms} ms` : `${(ms / 1000).toFixed(1)} s`;
};

const WorkflowReviewModal: React.FC<WorkflowReviewModalProps> = ({ workflow, canDecide, onClose, onDecided }) => {
  const [steps, setSteps] = useState<ExecutionStep[]>([]);
  const [approvals, setApprovals] = useState<ApprovalEntry[]>([]);
  const [detailError, setDetailError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  const [decision, setDecision] = useState<'approve' | 'reject' | null>(null);
  const [comments, setComments] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [decisionError, setDecisionError] = useState<string | null>(null);

  const workflowId = workflow?.workflowId ?? null;

  const loadDetails = useCallback(async () => {
    if (!workflowId) return;
    setLoading(true);
    setDetailError(null);
    const [logResult, approvalResult] = await Promise.allSettled([
      agenticReviewApi.executionLog(workflowId),
      agenticReviewApi.approvals(workflowId),
    ]);
    setSteps(logResult.status === 'fulfilled' ? logResult.value.filter((s) => isShownAgent(s.agentName)) : []);
    setApprovals(approvalResult.status === 'fulfilled' ? approvalResult.value : []);
    const failed = [logResult, approvalResult].find((r) => r.status === 'rejected');
    if (failed && failed.status === 'rejected') setDetailError(getApiErrorMessage(failed.reason, 'Some review details could not be loaded.'));
    setLoading(false);
  }, [workflowId]);

  useEffect(() => {
    setSteps([]);
    setApprovals([]);
    setDecision(null);
    setComments('');
    setDecisionError(null);
    loadDetails();
  }, [loadDetails]);

  const analyzer = useMemo(() => parseAnalyzerResult(workflow?.analyzerResultJson ?? null), [workflow]);
  const validator = useMemo(() => parseValidatorResult(workflow?.validatorResultJson ?? null), [workflow]);

  if (!workflow) return null;

  const pending = workflow.status === 'PendingApproval';
  const confidencePct = analyzer?.confidenceScore != null ? Math.round(analyzer.confidenceScore * 100) : null;
  const decisiveAction = [...approvals].reverse().find((a) => a.actionType === 'Approved' || a.actionType === 'Rejected');

  const submitDecision = async () => {
    if (!decision) return;
    if (decision === 'reject' && !comments.trim()) {
      setDecisionError('Please give a reason for rejecting — it is kept in the approval history.');
      return;
    }
    setSubmitting(true);
    setDecisionError(null);
    try {
      const res = decision === 'approve' ? await agenticReviewApi.approve(workflow.workflowId, comments) : await agenticReviewApi.reject(workflow.workflowId, comments);
      setDecision(null);
      onDecided(res.message || (decision === 'approve' ? 'Approved — workflow resumed.' : 'Rejected.'));
    } catch (e) {
      setDecisionError(getApiErrorMessage(e, 'The decision could not be recorded.'));
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <>
      <Modal
        open
        onClose={onClose}
        title="Workflow review"
        subtitle={`Submission ${shortId(workflow.submissionId)} · workflow ${shortId(workflow.workflowId)}`}
        size="xl"
        footer={
          <>
            <button type="button" className={btnSecondary} onClick={onClose}>
              Close
            </button>
            {pending && canDecide && (
              <>
                <button type="button" className={btnDanger} onClick={() => { setDecision('reject'); setDecisionError(null); }}>
                  <ThumbsDown size={14} /> Reject
                </button>
                <button type="button" className={btnPrimary} onClick={() => { setDecision('approve'); setDecisionError(null); }}>
                  <ThumbsUp size={14} /> Approve
                </button>
              </>
            )}
          </>
        }
      >
        <div className="space-y-5">
          {/* Status + timestamps */}
          <div className="flex flex-wrap items-center gap-x-6 gap-y-2 rounded-2xl bg-white/60 p-4">
            <StatusBadge status={workflow.status} label={statusLabel(workflow.status)} className="!text-sm" />
            <dl className="flex flex-wrap gap-x-6 gap-y-1 text-xs text-ink-600">
              <div>Created {formatDateTime(workflow.createdAt)}</div>
              <div>Updated {formatDateTime(workflow.updatedAt)}</div>
              <div>Completed {formatDateTime(workflow.completedAt)}</div>
              {workflow.resultingJobId && <div>Job created: <span className="font-mono">{shortId(workflow.resultingJobId)}</span></div>}
            </dl>
          </div>

          {/* Human review */}
          {pending ? (
            <Notice tone="warning" title="Awaiting a human decision">
              {validator && validator.reasons.length > 0 ? (
                <>
                  Review is required because:
                  <ul className="mt-1 list-disc pl-4">
                    {validator.reasons.map((r) => (
                      <li key={r}>{r}</li>
                    ))}
                  </ul>
                </>
              ) : (
                'This workflow is paused until an admin or a management staff member approves or rejects it.'
              )}
              {!canDecide && <p className="mt-2 font-semibold">Only admins and management staff can approve or reject. You can review the details below.</p>}
            </Notice>
          ) : workflow.approvalRequired ? (
            <Notice tone={decisiveAction?.actionType === 'Rejected' ? 'error' : 'info'} title="Human review was required">
              {decisiveAction ? (
                <>
                  {decisiveAction.actionType} by {decisiveAction.performedByName ?? `user ${shortId(decisiveAction.performedByUserId)}`} on {formatDateTime(decisiveAction.performedAt)}
                  {decisiveAction.comments ? ` — “${decisiveAction.comments}”` : ''}
                </>
              ) : (
                'No approval or rejection has been recorded yet.'
              )}
            </Notice>
          ) : (
            <Notice tone="success" title="No human review was required">
              The validator approved this workflow for automatic assignment.
            </Notice>
          )}

          {detailError && <ErrorMessage message={detailError} onRetry={loadDetails} />}

          {/* Agents */}
          <section>
            <h4 className="mb-2 font-display text-sm font-bold text-ink-900">Analyzer and Validator results</h4>
            <div className="grid gap-3 md:grid-cols-2">
              <AgentCard name="Analyzer agent" role="Classifies each submitted item (category, hazard, value)" ran={analyzer !== null}>
                {analyzer ? (
                  <>
                  <dl>
                    <KeyValue label="Waste category">{analyzer.wasteCategory ?? '—'}</KeyValue>
                    <KeyValue label="Hazard level">{analyzer.hazardLevel ?? '—'}</KeyValue>
                    <KeyValue label="Estimated volume">{analyzer.estimatedVolumeKg !== null ? formatKg(analyzer.estimatedVolumeKg) : '—'}</KeyValue>
                    <KeyValue label="Estimated value">{analyzer.estimatedValueLkr !== null ? formatMoney(analyzer.estimatedValueLkr) : '—'}</KeyValue>
                    <KeyValue label="Confidence">
                      {confidencePct !== null ? (
                        <span className="inline-flex items-center gap-2">
                          <span className="h-1.5 w-20 overflow-hidden rounded-full bg-ink-100">
                            <span className={`block h-full ${confidencePct < 60 ? 'bg-amber-500' : 'bg-mint-500'}`} style={{ width: `${confidencePct}%` }} />
                          </span>
                          {confidencePct}%
                        </span>
                      ) : (
                        '—'
                      )}
                    </KeyValue>
                  </dl>
                  <ItemBreakdown items={analyzer.items} className="mt-2" />
                  </>
                ) : (
                  <p className="text-xs text-ink-600">The analyzer has not produced a result for this workflow.</p>
                )}
              </AgentCard>

              <AgentCard name="Validator agent" role="Applies fixed business rules to the analyzer's result" ran={validator !== null}>
                {validator ? (
                  <>
                    <dl>
                      <KeyValue label="Approved for auto-assignment">{yesNo(validator.approvedForAutoAssignment)}</KeyValue>
                      <KeyValue label="Human review required">{yesNo(validator.requiresHumanApproval)}</KeyValue>
                    </dl>
                    {validator.reasons.length > 0 ? (
                      <div className="mt-2">
                        <p className="text-[11px] font-mono uppercase tracking-wide text-ink-600">Reasons</p>
                        <ul className="mt-1 list-disc pl-4 text-sm text-ink-900">
                          {validator.reasons.map((r) => (
                            <li key={r}>{r}</li>
                          ))}
                        </ul>
                      </div>
                    ) : (
                      <p className="mt-2 text-xs text-ink-600">No rule was triggered.</p>
                    )}
                  </>
                ) : (
                  <p className="text-xs text-ink-600">The validator has not produced a result for this workflow.</p>
                )}
              </AgentCard>
            </div>
          </section>

          {/* Approvals */}
          <section>
            <h4 className="mb-2 font-display text-sm font-bold text-ink-900">Approval history</h4>
            {approvals.length === 0 ? (
              <p className="text-sm text-ink-600">{loading ? 'Loading…' : 'No approval or rejection has been recorded for this workflow.'}</p>
            ) : (
              <GlassCard padded={false} className="overflow-x-auto">
                <table className="w-full min-w-[480px] border-collapse">
                  <thead>
                    <tr className="border-b border-mint-100">
                      <th className={`${tableHeadClass} px-4 py-2`}>Action</th>
                      <th className={`${tableHeadClass} px-4 py-2`}>By</th>
                      <th className={`${tableHeadClass} px-4 py-2`}>When</th>
                      <th className={`${tableHeadClass} px-4 py-2`}>Comment</th>
                    </tr>
                  </thead>
                  <tbody>
                    {approvals.map((a) => (
                      <tr key={a.actionId} className="border-b border-mint-50 last:border-0">
                        <td className={`${tableCellClass} font-semibold`}>
                          <span className="inline-flex items-center gap-1">
                            {a.actionType === 'Approved' ? <ShieldCheck size={14} className="text-mint-600" /> : a.actionType === 'Rejected' ? <XCircle size={14} className="text-red-500" /> : null}
                            {a.actionType}
                          </span>
                        </td>
                        <td className={tableCellClass}>{a.performedByName ?? `User ${shortId(a.performedByUserId)}`}</td>
                        <td className={`${tableCellClass} whitespace-nowrap`}>{formatDateTime(a.performedAt)}</td>
                        <td className={tableCellClass}>{a.comments || '—'}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </GlassCard>
            )}
          </section>

          {/* Execution steps */}
          <section>
            <h4 className="mb-2 font-display text-sm font-bold text-ink-900">Analyzer and Validator execution steps</h4>
            {steps.length === 0 ? (
              <p className="text-sm text-ink-600">{loading ? 'Loading…' : 'No execution steps were logged.'}</p>
            ) : (
              <ol className="space-y-1.5">
                {steps.map((s) => (
                  <li key={s.logId} className="flex flex-wrap items-center justify-between gap-2 rounded-xl bg-white/60 px-4 py-2 text-sm">
                    <span className="flex items-center gap-2">
                      {s.succeeded ? <CheckCircle2 size={15} className="text-mint-600" /> : <XCircle size={15} className="text-red-500" />}
                      <span className="font-semibold text-ink-900">
                        {s.stepNumber}. {s.agentName}
                      </span>
                      {!s.succeeded && <span className="text-xs text-red-700">step failed — details are in the server logs</span>}
                    </span>
                    <span className="text-xs text-ink-600">
                      {formatDateTime(s.startedAt)} · {durationLabel(s)}
                    </span>
                  </li>
                ))}
              </ol>
            )}
          </section>

          <p className="text-xs text-ink-600">
            This page only reads the existing workflow results. It does not start, re-run or change any agent, and the AI values cannot be edited here — a reviewer can only approve or reject.
          </p>
        </div>
      </Modal>

      {/* Decision confirmation */}
      <Modal
        open={decision !== null}
        onClose={() => setDecision(null)}
        title={decision === 'approve' ? 'Approve this workflow' : 'Reject this workflow'}
        size="sm"
        busy={submitting}
        footer={
          <>
            <button type="button" className={btnSecondary} onClick={() => setDecision(null)} disabled={submitting}>
              Cancel
            </button>
            <button type="button" className={decision === 'reject' ? btnDanger : btnPrimary} onClick={submitDecision} disabled={submitting}>
              {submitting ? 'Saving…' : decision === 'approve' ? 'Confirm approval' : 'Confirm rejection'}
            </button>
          </>
        }
      >
        <div className="space-y-4">
          <Notice tone={decision === 'reject' ? 'error' : 'warning'}>
            {decision === 'approve'
              ? 'Approving resumes the workflow: it continues to collector matching and, when finalised, creates and assigns a pickup job.'
              : 'Rejecting ends the workflow. No pickup job will be created.'}{' '}
            The decision and your comment are recorded in the approval history.
          </Notice>
          <div>
            <label className={labelClass} htmlFor="wf-comments">
              Comment {decision === 'reject' ? '(required)' : '(optional)'}
            </label>
            <textarea id="wf-comments" rows={3} value={comments} onChange={(e) => setComments(e.target.value)} className={inputClass} />
          </div>
          {decisionError && <ErrorMessage message={decisionError} />}
        </div>
      </Modal>
    </>
  );
};

export default WorkflowReviewModal;
