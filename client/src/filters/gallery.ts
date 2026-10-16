// SPDX-License-Identifier: AGPL-3.0-or-later
import { decodeFilter, encodeFilter, FILTER_PARAM } from './url.ts';
import { emptyDocument, whereOf, withWhere } from './document.ts';
import type { FilterDocument, FilterNode } from './types.ts';

/**
 * The gallery's filter: its own controls and the photograph world's other conditions, as one
 * document in the gallery's address and back.
 *
 * The search box, the tag and the date range are written into the document, as the cave registry
 * writes its controls, so the list, a link and a saved gallery view all carry them. Each is written
 * as the condition that matches exactly what the control always matched: the search box as "the
 * title, the caption or the place name contains", the tag by its slug, and the date range over the
 * date the gallery dates a picture by — when it was taken, or when it was uploaded if its camera
 * recorded no time — from the first day's start up to the last day's end, in UTC as the server has
 * always read it.
 *
 * The other controls stay parameters of this screen and are not kept in a saved view: the
 * photographer is a person, an album is an object with its own visibility, "without a position" is
 * a question about position, and a cave, a trip, a camp or a map extent is what a link opened the
 * gallery on. A filter that can be saved and shared deliberately asks about none of them.
 *
 * Nothing here knows about a component, so a pasted link, the back button and an applied saved
 * filter all arrive at the same state by the same road.
 */

/** The world every gallery document is about. */
export const PHOTO_WORLD = 'photograph';

/** The address member naming the saved filter the conditions came from. */
export const GALLERY_SAVED_PARAM = 'saved';

/** What the gallery's saveable controls hold. Every member is optional: unset narrows nothing. */
export interface GalleryControls {
  /** Matched against the title, the caption and the place name. */
  search?: string;
  /** A tag's slug. */
  tag?: string;
  /** The first day of the range, as YYYY-MM-DD. */
  from?: string;
  /** The last day of the range, as YYYY-MM-DD, itself included. */
  to?: string;
}

/**
 * What a gallery's conditions say, as its controls can show them. `extra` holds every condition the
 * controls have no place for — written in the editor, or brought in by a saved filter — kept and sent,
 * never dropped, since a condition lost on the way in would widen the list behind the person's back.
 */
export interface GalleryReading {
  controls: GalleryControls;
  extra: FilterNode[];
}

function contains(field: string, text: string): FilterNode {
  return { node: 'condition', field, op: 'contains', values: [{ type: 'text', value: text }] };
}

const DAY = /^\d{4}-\d{2}-\d{2}$/;

/** Midnight UTC at the start of a day, or undefined for a value that is not a day. */
function startOf(day: string | undefined): string | undefined {
  if (day === undefined || !DAY.test(day)) return undefined;
  const at = new Date(`${day}T00:00:00Z`);
  return Number.isNaN(at.getTime()) ? undefined : `${day}T00:00:00Z`;
}

/** The day after, as YYYY-MM-DD. */
function nextDay(day: string): string {
  const at = new Date(`${day}T00:00:00Z`);
  at.setUTCDate(at.getUTCDate() + 1);
  return at.toISOString().slice(0, 10);
}

/** The day before, as YYYY-MM-DD. */
function previousDay(day: string): string {
  const at = new Date(`${day}T00:00:00Z`);
  at.setUTCDate(at.getUTCDate() - 1);
  return at.toISOString().slice(0, 10);
}

/** The conditions each set control stands for, in a fixed order so equal controls write equal conditions. */
function controlNodes(controls: GalleryControls): FilterNode[] {
  const nodes: FilterNode[] = [];
  const search = controls.search?.trim();
  if (search) {
    nodes.push({
      node: 'anyOf',
      of: [contains('title', search), contains('caption', search), contains('placeName', search)],
    });
  }
  if (controls.tag) {
    nodes.push({ node: 'condition', field: 'tag', op: 'equals', values: [{ type: 'id', value: controls.tag }] });
  }
  const from = startOf(controls.from);
  if (from !== undefined) {
    nodes.push({ node: 'condition', field: 'datedAt', op: 'greaterOrEqual', values: [{ type: 'instant', value: from }] });
  }
  if (controls.to !== undefined && startOf(controls.to) !== undefined) {
    // Up to the start of the next day, so the last day chosen is included whole.
    nodes.push({
      node: 'condition',
      field: 'datedAt',
      op: 'lessThan',
      values: [{ type: 'instant', value: startOf(nextDay(controls.to))! }],
    });
  }
  return nodes;
}

/** The conditions the controls and the extra conditions compose, or null when they narrow nothing. */
export function galleryWhere(controls: GalleryControls, extra: readonly FilterNode[] = []): FilterNode | null {
  const nodes = [...controlNodes(controls), ...extra];
  return nodes.length === 0 ? null : { node: 'allOf', of: nodes };
}

type Condition = Extract<FilterNode, { node: 'condition' }>;

function single(node: Condition, type: 'text' | 'id' | 'instant'): string | undefined {
  const [value, ...rest] = node.values;
  return rest.length === 0 && value?.type === type ? value.value : undefined;
}

function isCondition(node: FilterNode | undefined, field: string, op: string): node is Condition {
  return node !== undefined && node.node === 'condition' && node.field === field && node.op === op;
}

/** The day a midnight-UTC instant starts, or undefined when the instant is not one. */
function dayAt(instant: string | undefined): string | undefined {
  if (instant === undefined) return undefined;
  const match = /^(\d{4}-\d{2}-\d{2})T00:00:00(?:\.0+)?(?:Z|\+00:00)$/.exec(instant);
  return match?.[1];
}

/** Which control one node is, if it is exactly what that control writes. */
function recognise(node: FilterNode): GalleryControls | null {
  if (node.node === 'anyOf' && node.of.length === 3) {
    const [title, caption, place] = node.of;
    if (isCondition(title, 'title', 'contains') && isCondition(caption, 'caption', 'contains')
      && isCondition(place, 'placeName', 'contains')) {
      const search = single(title, 'text');
      return search !== undefined && search === single(caption, 'text') && search === single(place, 'text')
        ? { search }
        : null;
    }
    return null;
  }
  if (isCondition(node, 'tag', 'equals')) {
    const tag = single(node, 'id');
    return tag === undefined ? null : { tag };
  }
  if (isCondition(node, 'datedAt', 'greaterOrEqual')) {
    const from = dayAt(single(node, 'instant'));
    return from === undefined ? null : { from };
  }
  if (isCondition(node, 'datedAt', 'lessThan')) {
    const end = dayAt(single(node, 'instant'));
    return end === undefined ? null : { to: previousDay(end) };
  }
  return null;
}

/**
 * The controls a gallery's conditions can be shown in, and what is left over. Only a node spelled
 * exactly as a control writes it is taken by that control, and only the first one: a second tag is
 * a narrowing the controls cannot show, so it stays in `extra` where it keeps narrowing.
 */
export function readGalleryControls(where: FilterNode | null): GalleryReading {
  const nodes = where === null ? [] : where.node === 'allOf' ? where.of : [where];
  const controls: GalleryControls = {};
  const extra: FilterNode[] = [];
  for (const node of nodes) {
    const found = recognise(node);
    const key = found === null ? undefined : (Object.keys(found)[0] as keyof GalleryControls);
    if (found === null || key === undefined || controls[key] !== undefined) {
      extra.push(node);
      continue;
    }
    Object.assign(controls, found);
  }
  return { controls, extra };
}

/** The gallery's filter as its address holds it. */
export interface GalleryFilter {
  /** The photograph world's conditions — the controls' and the rest — or null when there are none. */
  where: FilterNode | null;
  /** A filter was in the address and could not be read; the gallery opens without it and says so. */
  unreadableLink: boolean;
  /** The saved filter the conditions came from, if they came from one. */
  savedFilterId?: string;
}

/** The document a gallery's conditions compose, or null when they narrow nothing. */
export function galleryDocument(where: FilterNode | null): FilterDocument | null {
  return where === null ? null : withWhere(emptyDocument([PHOTO_WORLD]), PHOTO_WORLD, where);
}

/** The gallery's conditions as the condition editor opens them; empty when there are none. */
export function galleryEditorDocument(where: FilterNode | null): FilterDocument {
  return withWhere(emptyDocument([PHOTO_WORLD]), PHOTO_WORLD, where);
}

/**
 * What a document asks of photographs, or undefined when it is not about photographs at all.
 *
 * A filter about documents is not taken as one about photographs, although every photograph is a
 * document: the two worlds offer different conditions, and the server refuses the substitution too.
 */
export function readGalleryDocument(document: FilterDocument): FilterNode | null | undefined {
  if (!document.scope.some((s) => s.world === PHOTO_WORLD)) {
    return undefined;
  }
  return whereOf(document, PHOTO_WORLD);
}

/** The gallery's filter, read from its address. */
export function readGalleryFilter(params: URLSearchParams): GalleryFilter {
  const token = params.get(FILTER_PARAM);
  const document = decodeFilter(token);
  const where = document === null ? undefined : readGalleryDocument(document);
  return {
    where: where ?? null,
    // A token that was there and said nothing this page can use — truncated, from a newer version,
    // or about caves — opens the gallery without it, and the page says why rather than pretending.
    unreadableLink: token !== null && token !== '' && where === undefined,
    savedFilterId: params.get(GALLERY_SAVED_PARAM) ?? undefined,
  };
}

/**
 * The address with the gallery's filter replaced and every other member — the controls that are not
 * saved, the map extent — kept as it was. A filter that narrows nothing leaves no token behind.
 */
export function writeGalleryFilter(
  params: URLSearchParams,
  filter: { where: FilterNode | null; savedFilterId?: string },
): URLSearchParams {
  const next = new URLSearchParams(params);
  const document = galleryDocument(filter.where);
  if (document === null) {
    next.delete(FILTER_PARAM);
  } else {
    next.set(FILTER_PARAM, encodeFilter(document));
  }
  if (filter.savedFilterId) {
    next.set(GALLERY_SAVED_PARAM, filter.savedFilterId);
  } else {
    next.delete(GALLERY_SAVED_PARAM);
  }
  return next;
}

/** The token the gallery's request carries for these conditions, or undefined when there are none. */
export function galleryToken(where: FilterNode | null): string | undefined {
  const document = galleryDocument(where);
  return document === null ? undefined : encodeFilter(document);
}

/**
 * Whether two sets of conditions ask the same question. Compared as the controls and the extra
 * conditions write them, so a saved view of one bare condition and the same condition after a
 * control was set and cleared again are the same question.
 */
export function sameGalleryQuestion(a: FilterNode | null, b: FilterNode | null): boolean {
  const left = readGalleryControls(a);
  const right = readGalleryControls(b);
  return JSON.stringify(galleryWhere(left.controls, left.extra))
    === JSON.stringify(galleryWhere(right.controls, right.extra));
}
