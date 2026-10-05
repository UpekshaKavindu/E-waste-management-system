import { api } from '../../../api/client';
import type {
  ReceivableJob,
  ReceiveDeliveryInput,
  ReceiveDeliveryResponse,
  ReceiptDetail,
  ReceiptListQuery,
  ReceiptListResponse,
  ReceiveExtraWasteInput,
  ReceiveExtraWasteResponse,
  ReceiveJobWasteInput,
  ReceiveJobWasteResponse,
} from './types';

export const receiveApi = {
  /** Completed jobs not yet received — filtered by the server, so a refresh never brings received jobs back. */
  listReceivableJobs: () =>
    api.get<ReceivableJob[]>('/api/v1/inventory/job-collection/receivable').then((r) => r.data),

  receiveJob: (input: ReceiveJobWasteInput) =>
    api.post<ReceiveJobWasteResponse>('/api/v1/inventory/job-collection/receive', input).then((r) => r.data),

  /** One collector's several completed jobs at once — all saved together or not at all. */
  receiveDelivery: (input: ReceiveDeliveryInput) =>
    api
      .post<ReceiveDeliveryResponse>('/api/v1/inventory/job-collection/receive-delivery', {
        collectorId: input.collectorId,
        warehouseLocationId: input.warehouseLocationId,
        notes: input.notes?.trim() || null,
        jobs: input.jobs,
      })
      .then((r) => r.data),

  receiveExtraWaste: (input: ReceiveExtraWasteInput) =>
    api
      .post<ReceiveExtraWasteResponse>('/api/v1/inventory/extra-waste/receive', {
        collectorId: input.collectorId,
        warehouseLocationId: input.warehouseLocationId,
        notes: input.notes?.trim() || null,
        idempotencyKey: input.idempotencyKey || null,
        items: input.items.map((i) => ({
          // The rate policy is matched on the exact text, so surrounding spaces would make it miss.
          itemType: i.itemType.trim(),
          weightKg: i.weightKg,
          accepted: i.accepted,
          rejectionReason: i.accepted ? null : i.rejectionReason?.trim() || null,
        })),
      })
      .then((r) => r.data),

  /** Receipt history, newest first. */
  listReceipts: (q: ReceiptListQuery) =>
    api.get<ReceiptListResponse>('/api/v1/inventory/extra-waste', { params: q }).then((r) => r.data),

  /** One receipt: accepted AND rejected lines, and what each contributed to the payment. */
  getReceipt: (id: string) => api.get<ReceiptDetail>(`/api/v1/inventory/extra-waste/${id}`).then((r) => r.data),
};
