// SPDX-License-Identifier: AGPL-3.0-or-later
import { decodeFilter, encodeFilter, FILTER_PARAM } from './url.ts';
import { emptyDocument, whereOf, withWhere } from './document.ts';
import type { FilterDocument, FilterNode } from './types.ts';
import type { FeatureCategory, FeatureKind } from '../api/hooks/types.ts';

/**
 * The feature list's controls, as one filter document and back again.
 *
 * The list keeps its familiar controls — a search box, a kind, a type, a category, a tag — but they
 * are not parameters of their own. They compose one document, and that document is what the list,
 * its export and a saved filter all read, so the file somebody downloads holds the rows on the
 * screen and a filter saved here means the same thing on the map or in another list.
 *
 * Unlike the cave registry, a feature document says nothing it was not told: with no control set
 * there is no document at all, and every kind of feature is listed.
 */

/** The world every feature list document is about. */
export const FEATURE_LIST_WORLD = 'feature';

/** The address parameter naming the saved filter the conditions were taken from. */
export const SAVED_FILTER_PARAM = 'saved';

const DEFAULT_PAGE_SIZE = 20;

/** What the list's own controls hold. Every member is optional: unset narrows nothing. */
export interface FeatureListControls {
  /** Matched anywhere in the name. */
  search?: string;
  kind?: FeatureKind;
  featureTypeId?: number;
  category?: FeatureCategory;
  /** A tag's slug. */
  tag?: string;
}

/**
 * What a document says, as the controls can show it. `extra` holds every condition the controls
 * have no place for; those are kept and sent, never dropped, because a condition silently lost on
 * the way in would widen the list while the person believed it was the filter they were sent.
 */
export interface FeatureListReading {
  controls: FeatureListControls;
  extra: FilterNode[];
}

/** The list's state as its address holds it. */
export interface FeatureListAddress extends FeatureListReading {
  /** A filter was in the address and could not be read; the list opens unfiltered and says so. */
  unreadableLink: boolean;
  /** The saved filter the conditions came from, if they came from one. */
  savedFilterId?: string;
  page: number;
  pageSize: number;
}

const FEATURE_KINDS: readonly FeatureKind[] = ['generic', 'cave', 'caveEntrance', 'centerline'];
const FEATURE_CATEGORIES: readonly FeatureCategory[] = ['surface', 'underground', 'area', 'structure'];

function idCondition(field: string, value: string): FilterNode {
  return { node: 'condition', field, op: 'equals', values: [{ type: 'id', value }] };
}

function trimmed(value: string | undefined): string | undefined {
  const t = value?.trim();
  return t ? t : undefined;
}

/** The conditions each set control stands for, in a fixed order so equal controls write equal documents. */
function controlNodes(controls: FeatureListControls): FilterNode[] {
  const nodes: FilterNode[] = [];
  const search = trimmed(controls.search);
  if (search !== undefined) {
    // The name only — what this list's search box has always matched. Other names are a cave's.
    nodes.push({ node: 'condition', field: 'name', op: 'contains', values: [{ type: 'text', value: search }] });
  }
  if (controls.kind !== undefined) {
    nodes.push(idCondition('kind', controls.kind));
  }
  if (controls.featureTypeId !== undefined) {
    nodes.push(idCondition('typeId', String(controls.featureTypeId)));
  }
  if (controls.category !== undefined) {
    nodes.push(idCondition('category', controls.category));
  }
  if (controls.tag !== undefined && controls.tag !== '') {
    nodes.push(idCondition('tag', controls.tag));
  }
  return nodes;
}

/** The document the controls and any extra conditions compose, or null when they narrow nothing. */
export function featureListDocument(
  controls: FeatureListControls,
  extra: readonly FilterNode[] = [],
): FilterDocument | null {
  const nodes = [...controlNodes(controls), ...extra];
  if (nodes.length === 0) {
    return null;
  }
  return withWhere(emptyDocument([FEATURE_LIST_WORLD]), FEATURE_LIST_WORLD, { node: 'allOf', of: nodes });
}

function singleValue(node: Extract<FilterNode, { node: 'condition' }>, type: 'id' | 'text'): string | undefined {
  const [value, ...rest] = node.values;
  return rest.length === 0 && value?.type === type ? value.value : undefined;
}

/** Which control one node is, if it is exactly what that control writes. */
function recognise(node: FilterNode): FeatureListControls | null {
  if (node.node !== 'condition') {
    return null;
  }
  if (node.field === 'name' && node.op === 'contains') {
    const search = singleValue(node, 'text');
    return search === undefined ? null : { search };
  }
  if (node.op !== 'equals') {
    return null;
  }
  const id = singleValue(node, 'id');
  if (id === undefined) {
    return null;
  }
  switch (node.field) {
    case 'kind':
      return (FEATURE_KINDS as readonly string[]).includes(id) ? { kind: id as FeatureKind } : null;
    case 'typeId': {
      const typeId = Number(id);
      return Number.isSafeInteger(typeId) ? { featureTypeId: typeId } : null;
    }
    case 'category':
      return (FEATURE_CATEGORIES as readonly string[]).includes(id) ? { category: id as FeatureCategory } : null;
    case 'tag':
      return { tag: id };
    default:
      return null;
  }
}

/**
 * The controls a document can be shown in, and what is left over.
 *
 * Only a node spelled exactly as a control writes it is taken by that control, and only the first
 * one: a second kind condition is a narrowing the controls cannot show, so it stays in `extra` where
 * it keeps narrowing. Null when the document is not about the feature world at all.
 */
export function readFeatureList(document: FilterDocument): FeatureListReading | null {
  if (!document.scope.some((s) => s.world === FEATURE_LIST_WORLD)) {
    return null;
  }

  const where = whereOf(document, FEATURE_LIST_WORLD);
  const nodes = where === null ? [] : where.node === 'allOf' ? where.of : [where];
  const controls: FeatureListControls = {};
  const extra: FilterNode[] = [];

  for (const node of nodes) {
    const found = recognise(node);
    const key = found === null ? undefined : (Object.keys(found)[0] as keyof FeatureListControls);
    if (found === null || key === undefined || controls[key] !== undefined) {
      extra.push(node);
      continue;
    }
    Object.assign(controls, found);
  }

  return { controls, extra };
}

/** Whether two readings ask the same question. */
export function sameFeatureQuestion(a: FeatureListReading, b: FeatureListReading): boolean {
  return JSON.stringify(featureListDocument(a.controls, a.extra))
    === JSON.stringify(featureListDocument(b.controls, b.extra));
}

function positiveInteger(raw: string | null, fallback: number): number {
  const value = Number(raw);
  return Number.isSafeInteger(value) && value > 0 ? value : fallback;
}

/** The list's state, read from its address. */
export function readFeatureListAddress(params: URLSearchParams): FeatureListAddress {
  const token = params.get(FILTER_PARAM);
  const document = decodeFilter(token);
  const reading = document === null ? null : readFeatureList(document);
  return {
    controls: reading?.controls ?? {},
    extra: reading?.extra ?? [],
    unreadableLink: token !== null && token !== '' && reading === null,
    savedFilterId: params.get(SAVED_FILTER_PARAM) ?? undefined,
    page: positiveInteger(params.get('page'), 1),
    pageSize: positiveInteger(params.get('pageSize'), DEFAULT_PAGE_SIZE),
  };
}

/** The address for a state; members left at their defaults are left out so a link stays short. */
export function writeFeatureListAddress(state: Omit<FeatureListAddress, 'unreadableLink'>): URLSearchParams {
  const params = new URLSearchParams();
  const document = featureListDocument(state.controls, state.extra);
  if (document !== null) {
    params.set(FILTER_PARAM, encodeFilter(document));
  }
  if (state.savedFilterId) {
    params.set(SAVED_FILTER_PARAM, state.savedFilterId);
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
export function featureListToken(reading: FeatureListReading): string | undefined {
  const document = featureListDocument(reading.controls, reading.extra);
  return document === null ? undefined : encodeFilter(document);
}
