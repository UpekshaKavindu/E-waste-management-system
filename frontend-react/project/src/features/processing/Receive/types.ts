import type { PagedResponse } from '../types';

// GET /api/v1/inventory/job-collection/receivable
// Completed jobs with an assigned collector that have NOT been received yet — filtered on the server.
export interface ReceivableJob {
  jobId: string;
  collectorId: string;
  collectorName: string | null;
  collectorVehicleType: string | null;
  pickupAddress: string;
  reportedWeightKg: number | null;
  estimatedDistanceKm: number | null;
  completedAt: string | null;
  /** The category the customer chose, as they wrote it. */
  submissionCategory: string | null;
  /** That category matched to the item-type list; null when staff must choose the type. */
  suggestedItemType: string | null;
  /** What the customer submitted — one row per item / CSV row. Empty → the job is received as a whole. */
  items: ReceivableJobItem[];
}

export type SuggestionSource = 'name' | 'category' | 'ai' | 'submission';

export interface ReceivableJobItem {
  submissionItemId: string;
  itemName: string;
  description: string | null;
  /** Units expected (CSV quantity; 1 for manual items). */
  quantity: number;
  /** Best guess for the whole row (CSV per-unit × quantity, else the AI's estimate) — used to split a total. */
  expectedWeightKg: number | null;
  suggestedItemType: string | null;
  suggestionSource: SuggestionSource | null;
}

// POST /api/v1/inventory/job-collection/receive
// The collector must be the one assigned to the job; the server rejects any other (409).
export interface ReceiveJobWasteInput {
  jobId: string;
  collectorId: string;
  warehouseLocationId: string;
  verifiedWeightKg: number;
  /** From the item-type list. The server falls back to the submission category when omitted. */
  itemType: string;
}

// POST /api/v1/inventory/job-collection/receive-delivery
// Several completed jobs of one collector in one visit: one item and one payment per job, grouped.
export interface ReceiveDeliveryInput {
  collectorId: string;
  warehouseLocationId: string;
  notes?: string;
  jobs: DeliveryJobInput[];
}

/** A job with items is received item by item; one without items as a whole (weight + type). */
export type DeliveryJobInput =
  | { jobId: string; items: DeliveryItemInput[] }
  | { jobId: string; items: []; verifiedWeightKg: number; itemType: string };

export interface DeliveryItemInput {
  submissionItemId: string;
  /** 0 = not brought. */
  receivedQuantity: number;
  itemType: string | null;
  /** The whole row on the scale. */
  verifiedWeightKg: number;
}

export interface DeliveryItemResult {
  submissionItemId: string | null;
  itemName: string;
  expectedQuantity: number;
  receivedQuantity: number;
  /** Null when nothing was brought. */
  inventoryItemId: string | null;
  itemType: string | null;
  verifiedWeightKg: number;
}

export interface DeliveryJobResult {
  jobId: string;
  /** Sum of the rows — what the payment uses. */
  verifiedWeightKg: number;
  reportedWeightKg: number | null;
  discrepancyKg: number | null;
  expectedQuantity: number;
  receivedQuantity: number;
  items: DeliveryItemResult[];
  paymentId: string;
  paymentAmount: number;
}

export interface ReceiveDeliveryResponse {
  deliveryId: string;
  collectorId: string;
  receivedAt: string;
  jobs: DeliveryJobResult[];
  totalPendingAmount: number;
}

export interface ReceiveJobWasteResponse {
  inventoryItemId: string;
  jobId: string;
  itemType: string;
  verifiedWeightKg: number;
  reportedWeightKg: number | null;
  discrepancyKg: number | null;
  receivedAt: string;
}

// POST /api/v1/inventory/extra-waste/receive
export interface ReceiveExtraWasteItemInput {
  itemType: string;
  weightKg: number;
  accepted: boolean;
  rejectionReason?: string;
}

export interface ReceiveExtraWasteInput {
  collectorId: string;
  warehouseLocationId: string;
  notes?: string;
  idempotencyKey?: string;
  items: ReceiveExtraWasteItemInput[];
}

export interface ExtraWasteReceiptItemResult {
  itemType: string;
  accepted: boolean;
  rejectionReason: string | null;
  inventoryItemId: string | null;
}

export interface ReceiveExtraWasteResponse {
  extraWasteReceiptId: string;
  receivedAt: string;
  items: ExtraWasteReceiptItemResult[];
}

// GET /api/v1/inventory/extra-waste  (receipt history)
export interface ReceiptListQuery {
  collectorId?: string;
  page?: number;
  pageSize?: number;
}

export interface ReceiptListItem {
  receiptId: string;
  receivedAt: string;
  collectorId: string;
  collectorName: string | null;
  itemCount: number;
  acceptedCount: number;
  rejectedCount: number;
  totalWeightKg: number;
  acceptedWeightKg: number;
  /** Null when every line was rejected (no payment is raised). */
  paymentId: string | null;
  paymentStatus: 'Pending' | 'Paid' | null;
  paymentAmount: number | null;
}

export type ReceiptListResponse = PagedResponse<ReceiptListItem>;

// GET /api/v1/inventory/extra-waste/{id}
export interface ReceiptLine {
  id: string;
  itemType: string;
  weightKg: number;
  accepted: boolean;
  rejectionReason: string | null;
  inventoryItemId: string | null;
  /** True only for accepted lines of a receipt that produced a payment. */
  contributesToPayment: boolean;
  /** From the payment's saved snapshot; null when none was saved (older receipts). */
  ratePerKg: number | null;
  /** What the line contributed: its saved amount, or 0 for a rejected line; null when unknown. */
  lineAmount: number | null;
}

export interface ReceiptPaymentSummary {
  paymentId: string;
  status: 'Pending' | 'Paid';
  amount: number;
  hasSnapshot: boolean;
}

export interface ReceiptDetail {
  receiptId: string;
  receivedAt: string;
  notes: string | null;
  collectorId: string;
  collectorName: string | null;
  collectorVehicleType: string | null;
  receivedByStaffId: string;
  receivedByName: string | null;
  acceptedCount: number;
  rejectedCount: number;
  totalWeightKg: number;
  acceptedWeightKg: number;
  /** Null when nothing was accepted, so no payment exists. */
  payment: ReceiptPaymentSummary | null;
  items: ReceiptLine[];
}
