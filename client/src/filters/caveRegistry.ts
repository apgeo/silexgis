// SPDX-License-Identifier: AGPL-3.0-or-later
import { decodeFilter, encodeFilter, FILTER_PARAM } from './url.ts';
import { emptyDocument, whereOf, withWhere } from './document.ts';
import type { FilterDocument, FilterNode } from './types.ts';

/**
 * The cave registry's familiar controls, as one filter document and back again.
 *
 * The controls stay what people know — a search box, a type, a region, a length, a tag — but they
 * are no longer parameters of one screen. They compose one document, and that document is what the
 * list, both exports and a saved filter all read, so the file somebody downloads holds exactly the
 * caves on the screen and a filter saved from here means the same thing wherever it is run.
 *
 * Nothing here knows about a component. The page reads and writes its address through these
 * functions, which is what lets a pasted link, the back button and a saved filter all arrive at
 * the same controls by the same road.
 */

/** The world every cave registry document is about. */
export const CAVE_WORLD = 'feature';

/** What the registry's own controls hold. Every member is optional: unset narrows nothing. */
export interface CaveRegistryControls {
  /** Matched against the name and the other names a cave is known by. */
  search?: string;
  caveTypeId?: number;
  /** Matched anywhere in the recorded region. */
  region?: string;
  /** Surveyed length, in metres, at least this. */
  minLength?: number;
  /** A tag's slug. */
  tag?: string;
}

/**
 * What a document says, as the controls can show it.
 *
 * `extra` holds every condition the controls have no place for — from a saved filter written
 * elsewhere, or a link sent by someone with a richer editor. They are kept and sent, never dropped:
 * a condition silently lost on the way into this page would widen what it lists while the person
 * believed it was the filter they were sent.
 */
export interface CaveRegistryReading {
  controls: CaveRegistryControls;
  extra: FilterNode[];
}

/** The registry's state as its address holds it. */
export interface CaveRegistryAddress extends CaveRegistryReading {
  /** A filter was in the address and could not be read; the list opens unfiltered and says so. */
  unreadableLink: boolean;
  /** The saved filter the conditions came from, if they came from one. */
  savedFilterId?: string;
  sort?: string;
  page: number;
  pageSize: number;
}

export const SAVED_FILTER_PARAM = 'saved';
const DEFAULT_PAGE_SIZE = 20;

const kindIsCave: FilterNode = {
  node: 'condition',
  field: 'kind',
  op: 'equals',
  values: [{ type: 'id', value: 'cave' }],
};

function contains(field: string, text: string): FilterNode {
  return { node: 'condition', field, op: 'contains', values: [{ type: 'text', value: text }] };
}

function trimmed(value: string | undefined): string | undefined {
  const t = value?.trim();
  return t ? t : undefined;
}

/** The conditions each set control stands for, in a fixed order so equal controls write equal documents. */
function controlNodes(controls: CaveRegistryControls): FilterNode[] {
  const nodes: FilterNode[] = [];
  const search = trimmed(controls.search);
  if (search !== undefined) {
    // The name or another name — what the registry's search box has always matched.
    nodes.push({ node: 'anyOf', of: [contains('name', search), contains('otherToponyms', search)] });
  }
  if (controls.caveTypeId !== undefined) {
    nodes.push({
      node: 'condition',
      field: 'caveTypeId',
      op: 'equals',
      values: [{ type: 'id', value: String(controls.caveTypeId) }],
    });
  }
  const region = trimmed(controls.region);
  if (region !== undefined) {
    nodes.push(contains('region', region));
  }
  if (controls.minLength !== undefined && Number.isFinite(controls.minLength)) {
    // "At least": no operator says it directly, and "not less than" would let in every cave with
    // no length recorded, so it is spelled as more-than or exactly.
    const value = { type: 'number' as const, value: controls.minLength };
    nodes.push({
      node: 'anyOf',
      of: [
        { node: 'condition', field: 'surveyedLength', op: 'greaterThan', values: [value] },
        { node: 'condition', field: 'surveyedLength', op: 'equals', values: [value] },
      ],
    });
  }
  if (controls.tag !== undefined && controls.tag !== '') {
    nodes.push({ node: 'condition', field: 'tag', op: 'equals', values: [{ type: 'id', value: controls.tag }] });
  }
  return nodes;
}

/**
 * The document the controls and any extra conditions compose, or null when they narrow nothing.
 *
 * It always says "caves" as well, so a filter saved from here means caves on any page that runs
 * it — a map layer, another list — rather than everything that happens to share a tag with one.
 */
export function caveRegistryDocument(
  controls: CaveRegistryControls,
  extra: readonly FilterNode[] = [],
): FilterDocument | null {
  const nodes = [...controlNodes(controls), ...extra];
  if (nodes.length === 0) {
    return null;
  }
  return withWhere(emptyDocument([CAVE_WORLD]), CAVE_WORLD, { node: 'allOf', of: [kindIsCave, ...nodes] });
}

function isCondition(
  node: FilterNode,
  field: string,
  op: string,
): node is Extract<FilterNode, { node: 'condition' }> {
  return node.node === 'condition' && node.field === field && node.op === op;
}

function singleText(node: Extract<FilterNode, { node: 'condition' }>): string | undefined {
  const [value, ...rest] = node.values;
  return rest.length === 0 && value?.type === 'text' ? value.value : undefined;
}

function singleId(node: Extract<FilterNode, { node: 'condition' }>): string | undefined {
  const [value, ...rest] = node.values;
  return rest.length === 0 && value?.type === 'id' ? value.value : undefined;
}

function singleNumber(node: Extract<FilterNode, { node: 'condition' }>): number | undefined {
  const [value, ...rest] = node.values;
  return rest.length === 0 && value?.type === 'number' ? value.value : undefined;
}

/** Which control one node is, if it is exactly what that control writes. */
function recognise(node: FilterNode): CaveRegistryControls | 'kind' | null {
  if (isCondition(node, 'kind', 'equals') && singleId(node) === 'cave') {
    return 'kind';
  }
  if (isCondition(node, 'caveTypeId', 'equals')) {
    const id = Number(singleId(node));
    return Number.isSafeInteger(id) ? { caveTypeId: id } : null;
  }
  if (isCondition(node, 'region', 'contains')) {
    const region = singleText(node);
    return region === undefined ? null : { region };
  }
  if (isCondition(node, 'tag', 'equals')) {
    const tag = singleId(node);
    return tag === undefined ? null : { tag };
  }
  if (node.node === 'anyOf' && node.of.length === 2) {
    const [first, second] = node.of;
    if (first === undefined || second === undefined) {
      return null;
    }
    if (isCondition(first, 'name', 'contains') && isCondition(second, 'otherToponyms', 'contains')) {
      const search = singleText(first);
      return search !== undefined && search === singleText(second) ? { search } : null;
    }
    if (isCondition(first, 'surveyedLength', 'greaterThan') && isCondition(second, 'surveyedLength', 'equals')) {
      const length = singleNumber(first);
      return length !== undefined && length === singleNumber(second) ? { minLength: length } : null;
    }
  }
  return null;
}

/**
 * The controls a document can be shown in, and what is left over.
 *
 * Only a node spelled exactly as a control writes it is taken by that control, and only the first
 * one: a second region condition is a narrowing the controls cannot show, so it stays in `extra`
 * where it keeps narrowing. Null when the document is not about caves' world at all.
 */
export function readCaveRegistry(document: FilterDocument): CaveRegistryReading | null {
  if (!document.scope.some((s) => s.world === CAVE_WORLD)) {
    return null;
  }

  const where = whereOf(document, CAVE_WORLD);
  const nodes = where === null ? [] : where.node === 'allOf' ? where.of : [where];
  const controls: CaveRegistryControls = {};
  const extra: FilterNode[] = [];

  for (const node of nodes) {
    const found = recognise(node);
    if (found === 'kind') {
      continue;
    }
    const key = found === null ? undefined : (Object.keys(found)[0] as keyof CaveRegistryControls);
    if (found === null || key === undefined || controls[key] !== undefined) {
      extra.push(node);
      continue;
    }
    Object.assign(controls, found);
  }

  return { controls, extra };
}

/** Whether two readings ask the same question. */
export function sameQuestion(a: CaveRegistryReading, b: CaveRegistryReading): boolean {
  return JSON.stringify(caveRegistryDocument(a.controls, a.extra))
    === JSON.stringify(caveRegistryDocument(b.controls, b.extra));
}

function positiveInteger(raw: string | null, fallback: number): number {
  const value = Number(raw);
  return Number.isSafeInteger(value) && value > 0 ? value : fallback;
}

/** The registry's state, read from its address. */
export function readCaveRegistryAddress(params: URLSearchParams): CaveRegistryAddress {
  const token = params.get(FILTER_PARAM);
  const document = decodeFilter(token);
  const reading = document === null ? null : readCaveRegistry(document);
  return {
    controls: reading?.controls ?? {},
    extra: reading?.extra ?? [],
    // A token that was there and said nothing this page can use — truncated, from a newer version,
    // or about trips — opens the list unfiltered, and the page says why rather than pretending.
    unreadableLink: token !== null && token !== '' && reading === null,
    savedFilterId: params.get(SAVED_FILTER_PARAM) ?? undefined,
    sort: params.get('sort') ?? undefined,
    page: positiveInteger(params.get('page'), 1),
    pageSize: positiveInteger(params.get('pageSize'), DEFAULT_PAGE_SIZE),
  };
}

/** The address for a state; members left at their defaults are left out so a link stays short. */
export function writeCaveRegistryAddress(state: Omit<CaveRegistryAddress, 'unreadableLink'>): URLSearchParams {
  const params = new URLSearchParams();
  const document = caveRegistryDocument(state.controls, state.extra);
  if (document !== null) {
    params.set(FILTER_PARAM, encodeFilter(document));
  }
  if (state.savedFilterId) {
    params.set(SAVED_FILTER_PARAM, state.savedFilterId);
  }
  if (state.sort) {
    params.set('sort', state.sort);
  }
  if (state.page !== 1) {
    params.set('page', String(state.page));
  }
  if (state.pageSize !== DEFAULT_PAGE_SIZE) {
    params.set('pageSize', String(state.pageSize));
  }
  return params;
}

/** The token a request carries for these controls, or undefined when they narrow nothing. */
export function caveRegistryToken(reading: CaveRegistryReading): string | undefined {
  const document = caveRegistryDocument(reading.controls, reading.extra);
  return document === null ? undefined : encodeFilter(document);
}
