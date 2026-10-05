import { getApiErrorMessage, getApiErrorStatus } from '../processing/utils/apiError';

/**
 * Field-level errors from CreateSubmissionDtoValidator's 400 response, e.g.
 * { "PickupAddress": ["Pickup address is required."], "Items[0].ItemName": [...] }
 * — so they can be shown next to the input that caused them, instead of only
 * as one combined message.
 */
export interface SubmissionFieldErrors {
  pickupAddress?: string;
  phoneNumber?: string;
  category?: string;
  estimatedWeight?: string;
  itemsGeneral?: string;
  items: Record<number, { itemName?: string; description?: string; imageUrl?: string }>;
  /** Every item-level message by 0-based index, whatever the field — used to label CSV rows. */
  itemMessages: Record<number, string[]>;
}

const ITEM_FIELD_KEY = /^Items\[(\d+)]\.(\w+)$/;

/** Returns field-level errors for a 400 with an `errors` object, otherwise null. */
export function extractFieldErrors(error: unknown): SubmissionFieldErrors | null {
  if (getApiErrorStatus(error) !== 400) return null;

  const data = (error as { response?: { data?: unknown } })?.response?.data as
    | { errors?: Record<string, string[] | string> }
    | undefined;
  const errors = data?.errors;
  if (!errors || typeof errors !== 'object') return null;

  const result: SubmissionFieldErrors = { items: {}, itemMessages: {} };

  for (const [key, value] of Object.entries(errors)) {
    const message = Array.isArray(value) ? value[0] : value;
    if (!message) continue;

    const itemMatch = key.match(ITEM_FIELD_KEY);
    if (itemMatch) {
      const index = Number(itemMatch[1]);
      (result.itemMessages[index] ??= []).push(message);
      const entry = (result.items[index] ??= {});
      if (itemMatch[2] === 'ItemName') entry.itemName = message;
      else if (itemMatch[2] === 'Description') entry.description = message;
      else if (itemMatch[2] === 'ImageUrl') entry.imageUrl = message;
      continue;
    }

    switch (key) {
      case 'PickupAddress': result.pickupAddress = message; break;
      case 'PhoneNumber': result.phoneNumber = message; break;
      case 'Category': result.category = message; break;
      case 'EstimatedWeight': result.estimatedWeight = message; break;
      case 'Items': result.itemsGeneral = message; break;
      default: break; // unrecognized key — ignored rather than silently mis-shown
    }
  }

  return result;
}

/** One combined message for errors that aren't field-shaped (network, 401/403/500, ...). */
export function extractGeneralError(error: unknown): string {
  return getApiErrorMessage(error, 'Failed to submit item. Make sure the .NET backend is running.');
}
