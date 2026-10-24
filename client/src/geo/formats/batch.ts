// SPDX-License-Identifier: AGPL-3.0-or-later
import { readPosition, writePosition, type FormatId, type FormatSettings } from './registry.ts';
import type { ParseAssumption, ParseError, ReadingRules } from './types.ts';

/**
 * Many positions converted at once, from text pasted into the converter: one position per line,
 * or a table (CSV) with the position in a chosen column — or in two, latitude and longitude kept
 * apart the way spreadsheets usually keep them. Everything happens in the browser; nothing pasted
 * here is sent anywhere.
 */

export type Separator = ',' | ';' | '\t';

export type BatchSource =
  | { mode: 'lines' }
  | {
      mode: 'csv';
      /** The column separator, or `'auto'` to take the one the first line uses most. */
      separator: Separator | 'auto';
      /** The first line names the columns and holds no position. */
      header: boolean;
      /** Zero-based columns holding the position; two are read together, joined by a space. */
      columns: readonly number[];
    };

/** Why one line produced no position. */
export type BatchError = ParseError | 'ambiguous' | 'missingColumn' | 'empty';

export type BatchOutcome =
  | { kind: 'ok'; lon: number; lat: number; format: string; assumptions: readonly ParseAssumption[] }
  | { kind: 'error'; error: BatchError };

export interface BatchRow {
  /** One-based line number in the pasted text, so an error can be found where it was pasted. */
  line: number;
  /** The text that was read as the position. */
  input: string;
  outcome: BatchOutcome;
}

/**
 * Beyond this many lines the rest is left unread, so that a pasted file of a whole register cannot
 * hang the page; the converter says how many were left.
 */
export const MAX_BATCH_LINES = 5000;

/** Readings closer than this (in degrees, about a centimetre) are one position read twice. */
const SAME_POSITION_DEGREES = 1e-7;

/** The separator a line uses most: tab first, then semicolon, then comma — a comma is also a decimal mark. */
export function detectSeparator(line: string): Separator {
  const count = (separator: string) => line.split(separator).length - 1;
  if (count('\t') > 0) return '\t';
  if (count(';') > 0) return ';';
  return ',';
}

/** One CSV line split into its fields, honouring double quotes ("a, b" is one field; "" is a quote). */
export function splitCsvLine(line: string, separator: Separator): string[] {
  const fields: string[] = [];
  let field = '';
  let quoted = false;
  for (let i = 0; i < line.length; i++) {
    const c = line[i];
    if (quoted) {
      if (c === '"' && line[i + 1] === '"') {
        field += '"';
        i++;
      } else if (c === '"') {
        quoted = false;
      } else {
        field += c;
      }
    } else if (c === '"' && field.trim() === '') {
      quoted = true;
      field = '';
    } else if (c === separator) {
      fields.push(field);
      field = '';
    } else {
      field += c;
    }
  }
  fields.push(field);
  return fields.map((value) => value.trim());
}

/** The lines of a pasted text, numbered, without the blank ones, and at most {@link MAX_BATCH_LINES}. */
export function numberedLines(text: string): { lines: { line: number; text: string }[]; leftOut: number } {
  const all = text
    .split(/\r?\n/)
    .map((value, index) => ({ line: index + 1, text: value }))
    .filter((entry) => entry.text.trim() !== '');
  return { lines: all.slice(0, MAX_BATCH_LINES), leftOut: Math.max(0, all.length - MAX_BATCH_LINES) };
}

/** One text read as a position, with a reading that is genuinely two positions refused as ambiguous. */
export function readOne(text: string, only?: FormatId, rules?: ReadingRules): BatchOutcome {
  if (text.trim() === '') {
    return { kind: 'error', error: 'empty' };
  }
  const reading = readPosition(text, only, rules);
  if (reading.kind === 'invalid') {
    return { kind: 'error', error: reading.error };
  }
  const [first, ...rest] = reading.candidates;
  const distinct = rest.some(
    (other) =>
      Math.abs(other.lon - first.lon) > SAME_POSITION_DEGREES || Math.abs(other.lat - first.lat) > SAME_POSITION_DEGREES,
  );
  if (distinct) {
    return { kind: 'error', error: 'ambiguous' };
  }
  return { kind: 'ok', lon: first.lon, lat: first.lat, format: first.format, assumptions: first.assumptions };
}

/** Every position in a pasted text, a row per non-blank line (the header line excepted). */
export function readBatch(
  text: string,
  source: BatchSource,
  only?: FormatId,
  rules?: ReadingRules,
): { rows: BatchRow[]; leftOut: number } {
  const { lines, leftOut } = numberedLines(text);
  if (source.mode === 'lines') {
    return { rows: lines.map(({ line, text: value }) => ({ line, input: value.trim(), outcome: readOne(value, only, rules) })), leftOut };
  }
  const separator = source.separator === 'auto' ? detectSeparator(lines[0]?.text ?? '') : source.separator;
  const body = source.header ? lines.slice(1) : lines;
  const rows = body.map(({ line, text: value }) => {
    const fields = splitCsvLine(value, separator);
    const picked = source.columns.map((column) => fields[column]);
    if (picked.length === 0 || picked.some((field) => field === undefined || field === '')) {
      return { line, input: value.trim(), outcome: { kind: 'error', error: 'missingColumn' } as BatchOutcome };
    }
    const input = picked.join(' ');
    return { line, input, outcome: readOne(input, only, rules) };
  });
  return { rows, leftOut };
}

/** The columns of a table's first line, for choosing which holds the position. */
export function csvColumns(text: string, separator: Separator | 'auto'): string[] {
  const first = numberedLines(text).lines[0]?.text ?? '';
  return splitCsvLine(first, separator === 'auto' ? detectSeparator(first) : separator);
}

function csvField(value: string): string {
  return /[",\n\r]/.test(value) ? `"${value.replaceAll('"', '""')}"` : value;
}

export interface BatchCsvLabels {
  line: string;
  input: string;
  latitude: string;
  longitude: string;
  error: string;
  format: (id: FormatId) => string;
  errorText: (error: BatchError) => string;
}

/**
 * The converted rows as a CSV file: the line and the text read, latitude and longitude in decimal
 * degrees to seven places (about a centimetre — what was read, not rounded to a setting), the
 * position in every format asked for with the person's settings, and the reason a line was not
 * read. A format with no reference for a position leaves its cell empty.
 */
export function batchCsv(
  rows: readonly BatchRow[],
  formats: readonly FormatId[],
  settings: Partial<FormatSettings>,
  labels: BatchCsvLabels,
): string {
  const header = [labels.line, labels.input, labels.latitude, labels.longitude, ...formats.map(labels.format), labels.error];
  const lines = rows.map((row) => {
    const { outcome } = row;
    if (outcome.kind === 'error') {
      return [String(row.line), row.input, '', '', ...formats.map(() => ''), labels.errorText(outcome.error)];
    }
    return [
      String(row.line),
      row.input,
      outcome.lat.toFixed(7),
      outcome.lon.toFixed(7),
      ...formats.map((id) => writePosition(id, outcome.lon, outcome.lat, settings) ?? ''),
      '',
    ];
  });
  return [header, ...lines].map((fields) => fields.map(csvField).join(',')).join('\r\n') + '\r\n';
}
