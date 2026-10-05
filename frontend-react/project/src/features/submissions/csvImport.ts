/**
 * Corporate CSV upload: parsing, row checks and the downloadable template.
 *
 * The limits mirror CreateSubmissionDtoValidator's CSV rules on the backend (which stays the
 * authority) so a bad file is explained row by row before anything is sent.
 */

export const MAX_CSV_BYTES = 1024 * 1024; // 1 MB
export const MAX_CSV_ROWS = 100;
export const MAX_CSV_QUANTITY = 1000;
export const MAX_CSV_UNIT_WEIGHT_KG = 1000;
const MAX_NAME_LENGTH = 200;
const MAX_DESCRIPTION_LENGTH = 1000;
const MAX_CATEGORY_LENGTH = 100;

export const CSV_COLUMNS = ['item_name', 'description', 'quantity', 'estimated_weight_kg', 'category'] as const;
type Column = (typeof CSV_COLUMNS)[number];

export interface CsvRow {
  /** 1-based data row number (the header is not counted) — what errors refer to. */
  row: number;
  itemName: string;
  description: string;
  quantity: number;
  /** Per unit. */
  estimatedWeightKg: number | null;
  category: string;
  errors: string[];
}

export interface CsvParseResult {
  rows: CsvRow[];
  /** Problems with the file as a whole (size, header, row count). */
  fileErrors: string[];
  /** Columns in the header that aren't part of the format — ignored, but worth telling the user. */
  ignoredColumns: string[];
}

/** RFC 4180-style parsing: commas, quoted fields, "" escapes, line breaks inside quotes, CRLF or LF. */
export function parseCsvText(text: string): string[][] {
  const records: string[][] = [];
  let field = '';
  let record: string[] = [];
  let inQuotes = false;

  for (let i = 0; i < text.length; i++) {
    const ch = text[i];
    if (inQuotes) {
      if (ch === '"') {
        if (text[i + 1] === '"') {
          field += '"';
          i++;
        } else {
          inQuotes = false;
        }
      } else {
        field += ch;
      }
    } else if (ch === '"') {
      inQuotes = true;
    } else if (ch === ',') {
      record.push(field);
      field = '';
    } else if (ch === '\n' || ch === '\r') {
      if (ch === '\r' && text[i + 1] === '\n') i++;
      record.push(field);
      records.push(record);
      record = [];
      field = '';
    } else {
      field += ch;
    }
  }
  if (field !== '' || record.length > 0) {
    record.push(field);
    records.push(record);
  }
  // Blank lines (including the usual trailing newline) are not rows.
  return records.filter((r) => r.some((cell) => cell.trim() !== ''));
}

const normaliseHeader = (h: string): string => h.trim().toLowerCase().replace(/[\s-]+/g, '_');

export function readCsv(text: string): CsvParseResult {
  const fileErrors: string[] = [];
  const records = parseCsvText(text.replace(/^﻿/, '')); // UTF-8 with or without BOM

  if (records.length === 0) return { rows: [], fileErrors: ['The file is empty.'], ignoredColumns: [] };

  const header = records[0].map(normaliseHeader);
  const index = Object.fromEntries(CSV_COLUMNS.map((c) => [c, header.indexOf(c)])) as Record<Column, number>;
  const ignoredColumns = records[0].filter((_, i) => !(CSV_COLUMNS as readonly string[]).includes(header[i]));

  if (index.item_name < 0) {
    return {
      rows: [],
      fileErrors: [`The first line must be a header with an "item_name" column. Expected columns: ${CSV_COLUMNS.join(', ')}.`],
      ignoredColumns,
    };
  }

  const dataRecords = records.slice(1);
  if (dataRecords.length === 0) fileErrors.push('The file has a header but no item rows.');
  if (dataRecords.length > MAX_CSV_ROWS) {
    fileErrors.push(`The file has ${dataRecords.length} rows; a CSV upload can have at most ${MAX_CSV_ROWS}.`);
  }

  const cell = (record: string[], column: Column) => (index[column] >= 0 ? (record[index[column]] ?? '').trim() : '');

  const rows = dataRecords.map((record, i): CsvRow => {
    const errors: string[] = [];
    const itemName = cell(record, 'item_name');
    const description = cell(record, 'description');
    const category = cell(record, 'category');
    const quantityText = cell(record, 'quantity');
    const weightText = cell(record, 'estimated_weight_kg');

    if (!itemName) errors.push('Item name is required.');
    else if (itemName.length > MAX_NAME_LENGTH) errors.push(`Item name must be ${MAX_NAME_LENGTH} characters or fewer.`);
    if (description.length > MAX_DESCRIPTION_LENGTH) errors.push(`Description must be ${MAX_DESCRIPTION_LENGTH} characters or fewer.`);
    if (category.length > MAX_CATEGORY_LENGTH) errors.push(`Category must be ${MAX_CATEGORY_LENGTH} characters or fewer.`);

    let quantity = 1;
    if (quantityText) {
      quantity = Number(quantityText);
      if (!Number.isInteger(quantity) || quantity < 1 || quantity > MAX_CSV_QUANTITY) {
        errors.push(`Quantity must be a whole number from 1 to ${MAX_CSV_QUANTITY}.`);
      }
    }

    let estimatedWeightKg: number | null = null;
    if (weightText) {
      estimatedWeightKg = Number(weightText);
      if (!Number.isFinite(estimatedWeightKg) || estimatedWeightKg <= 0 || estimatedWeightKg > MAX_CSV_UNIT_WEIGHT_KG) {
        errors.push(`Weight per unit must be a number above 0 and at most ${MAX_CSV_UNIT_WEIGHT_KG} kg.`);
      }
    }

    return { row: i + 1, itemName, description, quantity, estimatedWeightKg, category, errors };
  });

  return { rows, fileErrors, ignoredColumns };
}

export const csvTotals = (rows: CsvRow[]) => ({
  units: rows.reduce((sum, r) => sum + (Number.isInteger(r.quantity) ? r.quantity : 0), 0),
  weightKg: rows.reduce((sum, r) => sum + (r.estimatedWeightKg && r.errors.length === 0 ? r.estimatedWeightKg * r.quantity : 0), 0),
  rowsWithoutWeight: rows.filter((r) => r.estimatedWeightKg === null).length,
});

const TEMPLATE =
  `${CSV_COLUMNS.join(',')}\r\n` +
  'Dell 24" monitor,"LCD monitors, working, no stands",40,4.5,IT Equipment\r\n' +
  'UPS battery,Sealed lead-acid 12V,10,12,Batteries\r\n' +
  'Keyboard,,25,,IT Equipment\r\n';

/** Starts a download of a filled-in example file. */
export function downloadCsvTemplate(): void {
  const url = URL.createObjectURL(new Blob(['﻿', TEMPLATE], { type: 'text/csv;charset=utf-8' }));
  const a = document.createElement('a');
  a.href = url;
  a.download = 'ewaste-submission-template.csv';
  a.click();
  URL.revokeObjectURL(url);
}
