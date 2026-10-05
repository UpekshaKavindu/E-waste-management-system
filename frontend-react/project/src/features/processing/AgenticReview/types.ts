// The existing intake workflow (Planner → Analyzer → Validator → Matcher → Planner). These types mirror
// what /api/workflows already returns; this screen only reads it and calls the existing approve/reject.

export type WorkflowStatus =
  | 'Planning'
  | 'Analyzing'
  | 'Validating'
  | 'PendingApproval'
  | 'Matching'
  | 'Finalizing'
  | 'Completed'
  | 'Rejected'
  | 'Failed';

export const WORKFLOW_STATUS_LABELS: Record<WorkflowStatus, string> = {
  Planning: 'Planning',
  Analyzing: 'Analyzing',
  Validating: 'Validating',
  PendingApproval: 'Awaiting approval',
  Matching: 'Matching',
  Finalizing: 'Finalizing',
  Completed: 'Completed',
  Rejected: 'Rejected',
  Failed: 'Failed',
};

export const IN_PROGRESS_STATUSES: readonly string[] = ['Planning', 'Analyzing', 'Validating', 'Matching', 'Finalizing'];

/** GET /api/workflows — the result columns are JSON text written by the agents' results. */
export interface WorkflowSummary {
  workflowId: string;
  submissionId: string;
  status: string;
  planJson: string | null;
  analyzerResultJson: string | null;
  validatorResultJson: string | null;
  matcherResultJson: string | null;
  finalReasoningSummary: string | null;
  approvalRequired: boolean;
  resultingJobId: string | null;
  createdAt: string;
  updatedAt: string | null;
  completedAt: string | null;
}

/**
 * GET /api/workflows/{id}/execution-log. The server also returns each step's raw input/output and error
 * text; they are deliberately NOT part of this type, so this screen can never render them.
 */
export interface ExecutionStep {
  logId: string;
  agentName: string;
  stepNumber: number;
  startedAt: string;
  completedAt: string | null;
  succeeded: boolean;
}

/** GET /api/workflows/{id}/approvals */
export interface ApprovalEntry {
  actionId: string;
  workflowId: string;
  actionType: 'Submitted' | 'RevisionRequested' | 'Approved' | 'Rejected' | string;
  performedByUserId: string;
  performedByName: string | null;
  comments: string | null;
  performedAt: string;
}

// ---- parsed agent results (all fields optional: the stored JSON belongs to the agent services) ----

/** One item or CSV row; weight and value are for the whole row (per unit × quantity). */
export interface AnalyzerItemResult {
  itemName: string | null;
  quantity: number | null;
  wasteCategory: string | null;
  hazardLevel: string | null;
  estimatedVolumeKg: number | null;
  estimatedValueLkr: number | null;
  confidenceScore: number | null;
}

/** The whole submission (worst hazard, totals, lowest confidence) plus each item's own result. */
export interface AnalyzerResult {
  wasteCategory: string | null;
  hazardLevel: string | null;
  estimatedVolumeKg: number | null;
  estimatedValueLkr: number | null;
  confidenceScore: number | null;
  items: AnalyzerItemResult[];
}

export interface ValidatorResult {
  approvedForAutoAssignment: boolean | null;
  requiresHumanApproval: boolean | null;
  reasons: string[];
}

export interface MatcherResult {
  recommendedCollectorId: string | null;
  autoAssign: boolean | null;
  ambiguous: boolean | null;
  reasoning: string | null;
}

export interface PlanInfo {
  skipMatcher: boolean | null;
  reasoning: string | null;
}
