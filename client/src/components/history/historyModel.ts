// SPDX-License-Identifier: AGPL-3.0-or-later
import GeoJSON from 'ol/format/GeoJSON';
import WKT from 'ol/format/WKT';

// Pure helpers for rendering and restoring history events. Kept free of React/antd so the
// diff-shaping and restore-compose logic can be unit-tested in isolation.

/** One property's before/after values in a history diff. */
export interface ChangePair {
  old: unknown;
  new: unknown;
}

export type ChangeSet = Record<string, ChangePair>;

/** A row in the per-event field table. Redacted rows carry no values (protected location). */
export interface HistoryFieldRow {
  prop: string;
  old: unknown;
  new: unknown;
  redacted: boolean;
}

function asChangeSet(changes: unknown): ChangeSet {
  return changes && typeof changes === 'object' ? (changes as ChangeSet) : {};
}

/**
 * Builds the display rows for a history event: one per changed property, plus a redacted
 * row for each protected property the server removed (so the UI can show "hidden" honestly).
 */
export function changeRows(changes: unknown, redactedProperties: string[]): HistoryFieldRow[] {
  const set = asChangeSet(changes);
  const rows: HistoryFieldRow[] = Object.entries(set).map(([prop, pair]) => ({
    prop,
    old: pair?.old,
    new: pair?.new,
    redacted: false,
  }));
  // Redacted props were stripped from `changes`, so they only appear here.
  for (const prop of redactedProperties) {
    if (!(prop in set)) {
      rows.push({ prop, old: undefined, new: undefined, redacted: true });
    }
  }

  return rows;
}

/** Props of an updated event a user may restore: present, non-redacted, with a real old value. */
export function restorableProps(rows: HistoryFieldRow[]): string[] {
  return rows.filter((r) => !r.redacted && r.old !== undefined).map((r) => r.prop);
}

/** A short human label for a JSON value in the diff table. */
export function formatValue(value: unknown): string {
  if (value === null || value === undefined) {
    return '—';
  }
  if (typeof value === 'string' || typeof value === 'number' || typeof value === 'boolean') {
    return String(value);
  }
  return JSON.stringify(value);
}

/** Audit property names are PascalCase (CLR); write-DTO fields are camelCase. */
export function toWriteField(prop: string): string {
  return prop.length === 0 ? prop : prop[0].toLowerCase() + prop.slice(1);
}

const wktFormat = new WKT();
const geoJsonFormat = new GeoJSON();
const WKT_GEOMETRY = /^\s*(POINT|LINESTRING|POLYGON|MULTIPOINT|MULTILINESTRING|MULTIPOLYGON|GEOMETRYCOLLECTION)\s*[ZM]*\s*\(/i;

/**
 * Converts an audit diff value to its write-DTO representation. Geometry is stored in the
 * audit trail as WKT (EPSG:4326, no reprojection); the write DTOs take GeoJSON, so parse it
 * with OpenLayers' WKT reader and re-emit as a GeoJSON geometry object.
 */
export function toWriteValue(value: unknown): unknown {
  if (typeof value === 'string' && WKT_GEOMETRY.test(value)) {
    try {
      return JSON.parse(geoJsonFormat.writeGeometry(wktFormat.readGeometry(value))) as unknown;
    } catch {
      return value; // leave malformed WKT untouched; the server validator rejects it clearly
    }
  }
  return value;
}

/**
 * Composes a restore: returns a copy of the current write DTO with the given properties set
 * back to their OLD values from the history event (mapped to write-DTO field names; geometry
 * WKT → GeoJSON). Every other field keeps its current value, so the resulting PUT re-runs
 * validation, permissions, the protection write-guard and audit by construction — restore is
 * never a raw database revert.
 */
export function applyRestore<T extends Record<string, unknown>>(
  current: T,
  changes: unknown,
  props: string[],
  fieldMap?: Record<string, string>,
): T {
  const set = asChangeSet(changes);
  const next: Record<string, unknown> = { ...current };
  for (const prop of props) {
    const pair = set[prop];
    if (pair) {
      next[fieldMap?.[prop] ?? toWriteField(prop)] = toWriteValue(pair.old);
    }
  }

  return next as T;
}

/**
 * Restore composer for surface features, which need two entity-specific fixups the generic
 * name mapping can't infer: the geometry is audited under the CLR name `Geom` but the write
 * DTO field is `geometry`, and Properties is stored as a jsonb *string* in the audit trail
 * while the write DTO expects a JSON object.
 */
export function applyFeatureRestore<T extends Record<string, unknown>>(
  current: T,
  changes: unknown,
  props: string[],
): T {
  const next = applyRestore(current, changes, props, { Geom: 'geometry' });
  const holder = next as { properties?: unknown };
  if (typeof holder.properties === 'string') {
    try {
      holder.properties = JSON.parse(holder.properties) as unknown;
    } catch {
      // Leave a malformed value untouched; the server validator rejects it clearly.
    }
  }

  return next;
}
