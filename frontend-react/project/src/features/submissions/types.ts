// Must match backend/EWasteManagement.API/Features/Submissions/DTOs/SubmissionCategories.cs
// (SubmissionCategories.All) — keep both lists in sync by hand.
export const SUBMISSION_CATEGORIES = [
  'Household Electronics',
  'IT Equipment',
  'Batteries',
  'Heavy Appliances',
  'Other',
] as const;

// Mirrors SubmissionResponseDto on the backend. `status` is derived there
// from the job (once one exists) or the workflow — never set directly.
export type SubmissionStatus =
  | 'NotProcessed'
  | 'Analyzing'
  | 'AwaitingReview'
  | 'Scheduling'
  | 'Closed'
  | 'Rejected'
  | 'Failed'
  | 'CollectorAssigned'
  | 'AwaitingCollector'
  | 'Collected'
  | 'Cancelled';

/** Statuses where the background agent chain is still working on its own. */
export const IN_PROGRESS_STATUSES: SubmissionStatus[] = ['Analyzing', 'Scheduling'];

/** One item's own classification. */
export interface AnalyzedItem {
  itemName: string;
  /** Weight and value below are for the whole row (per unit × quantity). */
  quantity?: number;
  wasteCategory: string;
  hazardLevel: string;
  estimatedVolumeKg: number;
  estimatedValueLkr: number;
  confidenceScore: number;
}

/** The whole submission: worst hazard, total weight and value, lowest confidence across its items. */
export interface SubmissionAnalysis {
  wasteCategory: string;
  hazardLevel: string;
  estimatedVolumeKg: number;
  estimatedValueLkr: number;
  confidenceScore: number;
  /** Empty for submissions analysed before per-item analysis. */
  items?: AnalyzedItem[];
}

export interface SubmissionWorkflow {
  workflowId: string;
  status: string;
  approvalRequired: boolean;
  analysis: SubmissionAnalysis | null;
}

export interface SubmissionItem {
  id: string;
  itemName: string;
  description: string | null;
  imageUrl: string;
  /** CSV rows can stand for several identical units; manual items are always 1. */
  quantity?: number;
  /** Per unit, CSV only. */
  estimatedWeightKg?: number | null;
  categoryHint?: string | null;
}

export type SubmissionSource = 'Manual' | 'Csv';

export interface SubmissionResponse {
  id: string;
  userId: string;
  userType: string;
  category: string;
  estimatedWeight: number;
  pickupAddress: string;
  phoneNumber: string;
  createdAt: string;
  source?: SubmissionSource;
  items: SubmissionItem[];
  status: SubmissionStatus;
  statusLabel: string;
  statusReason: string | null;
  workflow: SubmissionWorkflow | null;
  jobId: string | null;
  jobStatus: string | null;
}

/** Manual: the item form. Csv: a corporate account's spreadsheet — category and weight come from its rows. */
export type CreateSubmissionPayload =
  | {
      source: 'Manual';
      category: string;
      estimatedWeight: number;
      pickupAddress: string;
      phoneNumber: string;
      items: { itemName: string; description: string; imageUrl: string }[];
    }
  | {
      source: 'Csv';
      pickupAddress: string;
      phoneNumber: string;
      items: {
        itemName: string;
        description: string;
        quantity: number;
        /** Per unit. */
        estimatedWeightKg: number | null;
        category: string | null;
      }[];
    };
