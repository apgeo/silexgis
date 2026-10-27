// SPDX-License-Identifier: AGPL-3.0-or-later
import type { TFunction } from 'i18next';
import type { HeldBatch, HeldLine } from '../../api/hooks.ts';

/**
 * What the review screens make of a held batch's lines.
 *
 * The server hands a proposal over as plain JSON whose shape depends on the part of the upload it
 * belongs to. Everything that reads those shapes lives here, so the screens render a handful of
 * plain facts and the reading can be tested without a page.
 */

/** The parts of a phone upload, in the order a reviewer reads them: places before what hangs on them. */
export const SECTION_ORDER = [
  'rows',
  'people',
  'documents',
  'documentLinks',
  'tripRecordings',
  'tripPoints',
  'rasterMaps',
  'rasterPins',
] as const;

export type Json = Record<string, unknown>;

function isObject(value: unknown): value is Json {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function str(value: unknown): string | null {
  return typeof value === 'string' && value.length > 0 ? value : null;
}

/** The member `name` of a proposal's record, or an empty object when it is not there. */
function record(proposal: unknown, name: string): Json {
  return isObject(proposal) && isObject(proposal[name]) ? (proposal[name] as Json) : {};
}

/** A line's act: what the phone asked for. Lines of file imports have none. */
export function actOf(line: HeldLine): string | null {
  const proposal = line.proposal;
  return isObject(proposal) ? str(proposal.act) : null;
}

/** A line that adds something, rather than changing or removing it. */
export function isCreation(line: HeldLine): boolean {
  return line.action === 'create' || actOf(line) === 'create';
}

/** The record a line of a phone upload proposes, by the part it belongs to. */
export function recordOf(line: HeldLine): Json {
  switch (line.section) {
    case 'rows':
    case 'documents':
    case 'documentLinks':
      return record(line.proposal, 'row');
    case 'people':
      return record(line.proposal, 'person');
    case 'tripRecordings':
      return record(line.proposal, 'recording');
    case 'tripPoints':
      return record(line.proposal, 'point');
    case 'rasterMaps':
      return record(line.proposal, 'map');
    case 'rasterPins':
      return record(line.proposal, 'pin');
    default:
      return {};
  }
}

/** The name a line is listed under. Falls back to nothing: the screen supplies a generic word. */
export function titleOf(line: HeldLine): string | null {
  const proposal = isObject(line.proposal) ? line.proposal : {};
  const rec = recordOf(line);
  switch (line.section) {
    case 'rows':
    case 'tripRecordings':
    case 'rasterMaps':
      return str(rec.name) ?? str(rec.title);
    case 'documents':
      return str(rec.title) ?? str(rec.fileName);
    case 'people': {
      const full = [str(rec.firstName), str(rec.lastName)].filter(Boolean).join(' ');
      return full || str(rec.username);
    }
    default:
      // A file import's candidate carries its own name.
      return str(proposal.name) ?? str(rec.name) ?? str(rec.title);
  }
}

/** The identifier the line's own record has in the phone's data, which other lines point at. */
export function ownIdOf(line: HeldLine): string | null {
  const rec = recordOf(line);
  return str(line.rowId) ?? str(rec.id) ?? str(rec.deviceUserId);
}

/** Which cave a line belongs under, as the id of the cave, or null for a line under none. */
export interface Placement {
  /** The cave (or area) the line hangs on; null for the people and for lines with no cave. */
  caveId: string | null;
}

/**
 * Where each line of a batch is listed: under the cave it concerns.
 *
 * A cave is its own heading; an entrance or a place hangs on its container; a recording, a raster
 * map on its cave; a scan follows its recording and a pin its map; a link follows the object it
 * hangs on, and the document on its link. What names no cave at all goes under no heading.
 */
export function placeLines(lines: readonly HeldLine[]): Map<string, Placement> {
  const byOwnId = new Map<string, HeldLine>();
  for (const line of lines) {
    const id = ownIdOf(line);
    if (id) {
      byOwnId.set(id, line);
    }
  }
  const linkOfDocument = new Map<string, HeldLine>();
  for (const line of lines) {
    if (line.section === 'documentLinks') {
      const documentId = str(recordOf(line).documentId);
      if (documentId) {
        linkOfDocument.set(documentId, line);
      }
    }
  }

  const placed = new Map<string, Placement>();
  const resolving = new Set<string>();

  const resolve = (line: HeldLine): string | null => {
    const known = placed.get(line.reviewId);
    if (known) {
      return known.caveId;
    }
    // A cycle in what points at what must not hang the page.
    if (resolving.has(line.reviewId)) {
      return null;
    }
    resolving.add(line.reviewId);
    const via = (id: string | null) => {
      const other = id ? byOwnId.get(id) : undefined;
      return other && other !== line ? resolve(other) : null;
    };
    const rec = recordOf(line);
    let cave: string | null = null;
    switch (line.section) {
      case 'rows':
        cave = str(rec.kind) === 'cave' ? str(rec.id) : (str(rec.parentId) ?? null);
        break;
      case 'tripRecordings':
        cave = str(rec.caveId);
        break;
      case 'rasterMaps':
        cave = str(rec.caveId) ?? str(rec.caveAreaId);
        break;
      case 'tripPoints':
        cave = via(str(rec.recordingId));
        break;
      case 'rasterPins':
        cave = via(str(rec.rasterMapId));
        break;
      case 'documentLinks': {
        const proposal = isObject(line.proposal) ? line.proposal : {};
        cave = str(proposal.targetId);
        break;
      }
      case 'documents': {
        const link = linkOfDocument.get(str(rec.id) ?? '');
        cave = link ? resolve(link) : null;
        break;
      }
      default:
        cave = null;
    }
    resolving.delete(line.reviewId);
    placed.set(line.reviewId, { caveId: cave });
    return cave;
  };

  for (const line of lines) {
    resolve(line);
  }
  return placed;
}

export interface LineGroup {
  /** The cave the group is about; null for the lines under none. */
  caveId: string | null;
  lines: HeldLine[];
}

/** Lines grouped by cave, caves first in the order they first appear, the rest last. */
export function groupByCave(lines: readonly HeldLine[]): LineGroup[] {
  const placement = placeLines(lines);
  const order: (string | null)[] = [];
  const groups = new Map<string | null, HeldLine[]>();
  for (const line of lines) {
    const cave = placement.get(line.reviewId)?.caveId ?? null;
    if (!groups.has(cave)) {
      groups.set(cave, []);
      order.push(cave);
    }
    groups.get(cave)!.push(line);
  }
  const sectionRank = (line: HeldLine) => {
    const rank = SECTION_ORDER.indexOf((line.section ?? '') as (typeof SECTION_ORDER)[number]);
    return rank < 0 ? SECTION_ORDER.length : rank;
  };
  // How many lines deep a line sits on others, so a cave reads before the entrance that needs it.
  const byId = new Map(lines.map((l) => [l.reviewId, l]));
  const depthOf = (line: HeldLine, seen: ReadonlySet<string> = new Set()): number =>
    seen.has(line.reviewId)
      ? 0
      : 1 +
        Math.max(
          0,
          ...line.dependsOnReviewIds.flatMap((id) => {
            const dep = byId.get(id);
            return dep ? [depthOf(dep, new Set(seen).add(line.reviewId))] : [];
          }),
        );
  return order
    .sort((a, b) => (a === null ? 1 : 0) - (b === null ? 1 : 0))
    .map((caveId) => ({
      caveId,
      lines: [...groups.get(caveId)!].sort((a, b) => sectionRank(a) - sectionRank(b) || depthOf(a) - depthOf(b)),
    }));
}

/** The lines a decision on `lineId` would also accept: the waiting lines it depends on, all the way down. */
export function dependenciesOf(lineId: string, lines: readonly HeldLine[]): HeldLine[] {
  const byId = new Map(lines.map((l) => [l.reviewId, l]));
  const out: HeldLine[] = [];
  const seen = new Set<string>([lineId]);
  const walk = (id: string) => {
    for (const dep of byId.get(id)?.dependsOnReviewIds ?? []) {
      if (seen.has(dep)) {
        continue;
      }
      seen.add(dep);
      const line = byId.get(dep);
      if (line && (line.decision === 'pending' || line.decision === 'rejected')) {
        out.push(line);
        walk(dep);
      }
    }
  };
  walk(lineId);
  return out;
}

/**
 * The ids a decision on `lineId` depends on that are not lines of this batch: lines of an earlier
 * upload (a cave it left waiting). This batch cannot say what became of them, only that they exist.
 */
export function foreignDependenciesOf(lineId: string, lines: readonly HeldLine[]): string[] {
  const byId = new Map(lines.map((l) => [l.reviewId, l]));
  const foreign = new Set<string>();
  const seen = new Set<string>([lineId]);
  const walk = (id: string) => {
    for (const dep of byId.get(id)?.dependsOnReviewIds ?? []) {
      if (seen.has(dep)) {
        continue;
      }
      seen.add(dep);
      const line = byId.get(dep);
      if (line === undefined) {
        foreign.add(dep);
      } else {
        walk(dep);
      }
    }
  };
  walk(lineId);
  return [...foreign];
}

/** The waiting lines a rejection of `lineId` would also reject: whatever depends on it, all the way up. */
export function dependentsOf(lineId: string, lines: readonly HeldLine[]): HeldLine[] {
  const out: HeldLine[] = [];
  const seen = new Set<string>([lineId]);
  const walk = (id: string) => {
    for (const line of lines) {
      if (!seen.has(line.reviewId) && line.dependsOnReviewIds.includes(id)) {
        seen.add(line.reviewId);
        if (line.decision === 'pending') {
          out.push(line);
        }
        walk(line.reviewId);
      }
    }
  };
  walk(lineId);
  return out;
}

/** One field of an edit as the reviewer sees it: the row now, and what the phone proposes. */
export interface FieldDiff {
  field: string;
  /** What the server holds now; undefined when it could not be read. */
  current: unknown;
  proposed: unknown;
  /** Whether the proposal differs from the current row (true when the current one is unknown). */
  differs: boolean;
}

/** A stable text for a JSON value, so equal documents compare equal whatever their key order. */
export function canonical(value: unknown): string {
  if (Array.isArray(value)) {
    return `[${value.map(canonical).join(',')}]`;
  }
  if (isObject(value)) {
    return `{${Object.keys(value)
      .filter((k) => value[k] !== undefined)
      .sort()
      .map((k) => `${JSON.stringify(k)}:${canonical(value[k])}`)
      .join(',')}}`;
  }
  return JSON.stringify(value ?? null);
}

/** What the server holds of a feature row, as far as the review compares it. */
export interface CurrentRow {
  name: string | null;
  description: string | null;
  properties: unknown;
  geometry: unknown;
}

/**
 * The fields of a held edit that can be accepted on their own, each with the row now beside what
 * the phone proposes.
 *
 * A field the server did not offer for acceptance is not listed. The position is listed only when
 * the proposal carries one: a proposal whose position the server held back (because this reviewer
 * may not place the row) has nothing to accept there, and showing it as unchanged would claim a
 * comparison nobody made.
 */
export function fieldDiffs(line: HeldLine, current: CurrentRow | undefined): FieldDiff[] {
  const row = recordOf(line);
  const diffs: FieldDiff[] = [];
  for (const field of line.acceptableFields) {
    if (field === 'position') {
      if (row.geometry === undefined || row.geometry === null) {
        continue;
      }
      diffs.push({
        field,
        current: current?.geometry,
        proposed: row.geometry,
        differs: current === undefined || canonical(current.geometry) !== canonical(row.geometry),
      });
      continue;
    }
    const proposed = row[field];
    const now = current?.[field as keyof CurrentRow];
    diffs.push({
      field,
      current: now,
      proposed,
      differs: current === undefined || canonical(now ?? null) !== canonical(proposed ?? null),
    });
  }
  return diffs;
}

/** The fields to write by default: those the proposal changes, or all of them when nothing is known. */
export function defaultFields(diffs: readonly FieldDiff[]): string[] {
  const changed = diffs.filter((d) => d.differs).map((d) => d.field);
  return changed.length > 0 ? changed : diffs.map((d) => d.field);
}

/** A short text for a value in a field comparison. */
export function shortValue(value: unknown): string {
  if (value === undefined || value === null || value === '') {
    return '';
  }
  if (typeof value === 'string') {
    return value;
  }
  if (typeof value === 'number' || typeof value === 'boolean') {
    return String(value);
  }
  return canonical(value);
}

/** A point's coordinates as text, or null when the value is not a point. */
export function pointText(geometry: unknown): string | null {
  if (!isObject(geometry) || geometry.type !== 'Point' || !Array.isArray(geometry.coordinates)) {
    return null;
  }
  const [lon, lat] = geometry.coordinates as unknown[];
  return typeof lon === 'number' && typeof lat === 'number' ? `${lat.toFixed(5)}, ${lon.toFixed(5)}` : null;
}

/** The scalar members of a record, for the generic view of a part this screen has no special view of. */
export function scalarEntries(value: unknown): [string, string][] {
  if (!isObject(value)) {
    return [];
  }
  return Object.entries(value)
    .filter(([, v]) => ['string', 'number', 'boolean'].includes(typeof v) && v !== '')
    .map(([k, v]) => [k, String(v)]);
}

/** The members of a file import's saved choice that are shown, in the order they are shown. Ids and coordinates are not. */
export const CANDIDATE_MEMBERS = ['line', 'action', 'name', 'caveTypeCode', 'createTrip'] as const;

/** The members of a candidate's decision that have a label, as [member, value]; the rest are left out. */
export function candidateEntries(decision: unknown): [(typeof CANDIDATE_MEMBERS)[number], string][] {
  const all = new Map(scalarEntries(decision));
  return CANDIDATE_MEMBERS.flatMap((m) => (all.has(m) ? [[m, all.get(m)!] as [(typeof CANDIDATE_MEMBERS)[number], string]] : []));
}

/** Where a batch came from, in words: the phone, or the kind of file import. */
export function sourceLabel(t: TFunction, source: HeldBatch['source']): string {
  return t(`review.source.${source}`);
}
