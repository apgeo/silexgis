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

/** The act of writing roster times from the tracking log, as its history entry states it. */
export interface RosterTimesAct {
  /** The zone the roster's times were read in; null when the entry does not say. */
  timeZone: string | null;
  /** How many people's times were written. */
  people: number | null;
  /** How many roster rows that was — a person holds one per job. */
  rows: number | null;
}

/**
 * Reads a history entry as "roster times were taken from the tracking log", or null when it is
 * anything else.
 *
 * That entry records an act on the trip rather than fields of it that changed: its members
 * carry no earlier value, and their names are the server's own. It is told apart by the one
 * member that names the act, so that it can be worded as a sentence instead of being drawn as
 * a table of changes from nothing to a keyword.
 */
export function rosterTimesAct(changes: unknown): RosterTimesAct | null {
  const set = asChangeSet(changes);
  if (set.RosterTimes?.new !== 'fromTracking') {
    return null;
  }
  const text = (value: unknown) => (typeof value === 'string' && value !== '' ? value : null);
  const count = (value: unknown) => (typeof value === 'number' && Number.isFinite(value) ? value : null);
  return {
    timeZone: text(set.TimeZone?.new),
    people: count(set.People?.new),
    rows: count(set.Rows?.new),
  };
}

/**
 * A geometry the audit trail recorded as a summary rather than in full: a large line or survey
 * is noted by its shape type, its point count and a digest of its bytes, so the trail says that
 * it changed and whether two versions are the same without storing megabytes per row.
 */
export interface GeometrySummary {
  geometryType: string;
  points: number;
  sha256: string;
  omitted: true;
}

export function isGeometrySummary(value: unknown): value is GeometrySummary {
  if (!value || typeof value !== 'object') {
    return false;
  }
  const candidate = value as Record<string, unknown>;
  return (
    candidate.omitted === true &&
    typeof candidate.geometryType === 'string' &&
    typeof candidate.points === 'number' &&
    typeof candidate.sha256 === 'string'
  );
}

/**
 * Props of an updated event a user may restore: present, non-redacted, with a real old value.
 * A summarised geometry is not a value — there is nothing in it to write back.
 */
export function restorableProps(rows: HistoryFieldRow[]): string[] {
  return rows
    .filter((r) => !r.redacted && r.old !== undefined && !isGeometrySummary(r.old))
    .map((r) => r.prop);
}

/**
 * Props of an event the server may be asked to put back: present, non-redacted, with a recorded
 * old value. Unlike {@link restorableProps}, a summarised geometry is included — the server keeps
 * the full shape beside the summary for history written since it began to, and says
 * `no_pre_image` for the rest, so whether it can be put back is the server's to say, not the
 * screen's.
 */
export function serverRestorableProps(rows: HistoryFieldRow[]): string[] {
  return rows.filter((r) => !r.redacted && r.old !== undefined).map((r) => r.prop);
}

/** The body of `POST /history/restore` for one history event and the fields wanted back. */
export interface RestoreRequestBody {
  entityType: 'feature';
  entityId: string;
  auditEntryId: number;
  fields: string[];
}

/**
 * The request that puts fields of an event back on the server. The event's own entity id is
 * sent, not the id of the page the timeline is on: an entrance's change shows in its cave's
 * timeline but is a change to the entrance.
 */
export function restoreRequest(event: { id: number; entityId: string }, fields: string[]): RestoreRequestBody {
  return { entityType: 'feature', entityId: event.entityId, auditEntryId: event.id, fields };
}

/** Events of one change set, in the order the timeline lists them. */
export interface HistoryGroup<E> {
  /** Stable across re-renders: the change set's id, or the single event's id when it stands alone. */
  key: string;
  /** Null for an event on its own or from before change sets were recorded. */
  changeSetId: string | null;
  events: E[];
}

/**
 * Groups a timeline's events by the change set they were written in, so one upload, one restore
 * or one request reads as one entry with its parts under it. A group sits where its first
 * (newest) event does. An event with no change set — history from before they were recorded —
 * stands alone, and so does a change set that only one event of this timeline belongs to.
 */
export function groupByChangeSet<E extends { id: number; changeSetId?: string | null }>(
  events: E[],
): HistoryGroup<E>[] {
  const bySet = new Map<string, E[]>();
  for (const event of events) {
    if (event.changeSetId) {
      const members = bySet.get(event.changeSetId);
      if (members) {
        members.push(event);
      } else {
        bySet.set(event.changeSetId, [event]);
      }
    }
  }

  const groups: HistoryGroup<E>[] = [];
  const placed = new Set<string>();
  for (const event of events) {
    const set = event.changeSetId;
    const members = set ? bySet.get(set) : undefined;
    if (set && members && members.length > 1) {
      if (!placed.has(set)) {
        placed.add(set);
        groups.push({ key: `set-${set}`, changeSetId: set, events: members });
      }
    } else {
      groups.push({ key: `event-${event.id}`, changeSetId: null, events: [event] });
    }
  }

  return groups;
}

/**
 * The i18n key under which an audit entity type is worded. Feature history is kind-qualified
 * (`Feature:Cave`, `Feature:CaveEntrance`, `Feature:Generic`), and a colon is the namespace
 * separator to i18next, so the qualified name cannot be a key as it stands.
 */
export function entityLabelKey(entityType: string): string {
  const kind = entityType.startsWith('Feature:') ? entityType.slice('Feature:'.length) : entityType;
  return kind === 'Generic' ? 'SurfaceFeature' : kind;
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

/** A readable label for an audit property name nobody has translated: `LocationNotes` becomes `Location notes`. */
export function humanize(prop: string): string {
  const spaced = prop.replace(/([a-z0-9])([A-Z])/g, '$1 $2');
  return spaced.charAt(0).toUpperCase() + spaced.slice(1).toLowerCase();
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

/** The three report sections of a trip, which a restore never carries along by accident. */
const TRIP_SECTION_FIELDS = ['fieldData', 'logistics', 'safety'] as const;

/**
 * Restore composer for trip logs, which need two entity-specific fixups.
 *
 * A section this restore does not name travels as null — no section at all — rather than as the
 * value the trip currently holds. Echoing an untouched section back would have it measured again
 * against the schema its purpose carries *now*: correcting a title on a report written under a
 * looser schema could then be refused over a section nobody opened, and every section's version
 * stamp would silently move to the current one. The stamp exists precisely so that tightening a
 * schema does not retroactively invalidate reports already written.
 *
 * A section that *is* named is restored on purpose, and is measured — but it arrives from the
 * audit trail as a jsonb string while the write DTO takes an object, so it is parsed back first.
 *
 * The cave list travels as null for a related reason and a sharper one. It is not a restorable
 * property of a trip at all — restore is offered only for the trip row's own audited fields — so
 * what sits here is never an older value being put back, only whatever copy of the trip happens
 * to be loaded. Echoing it would turn restoring a title into an instruction about the trip's
 * caves, given from a copy that may already be stale. And the list a reader is handed is
 * deliberately short of every cave they may not be told about, so echoing it is precisely the
 * shape that reads as "forget those". No list at all is the only honest thing a restore has to
 * say about one.
 */
export function applyTripRestore<T extends Record<string, unknown>>(
  current: T,
  changes: unknown,
  props: string[],
): T {
  const base: Record<string, unknown> = { ...current };
  for (const field of TRIP_SECTION_FIELDS) {
    base[field] = null;
  }
  base.caveIds = null;

  const next = applyRestore(base, changes, props);
  for (const field of TRIP_SECTION_FIELDS) {
    if (typeof next[field] === 'string') {
      try {
        next[field] = JSON.parse(next[field] as string) as unknown;
      } catch {
        // Leave a malformed value untouched; the server validator rejects it clearly.
      }
    }
  }

  return next as T;
}
