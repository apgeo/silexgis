// SPDX-License-Identifier: AGPL-3.0-or-later
import type { FeatureEnvelope } from '../api/hooks.ts';
import type { WorkspaceSelection } from '../stores/workspaceStore.ts';

/** The query parameter that names what the map has selected: `?select=cave:<id>`. */
export const SELECT_PARAM = 'select';

/** A selection as an address can carry it: what kind of thing, and which one. */
export interface SelectionRef {
  kind: 'feature' | 'cave' | 'entrance';
  id: string;
}

const KINDS: readonly SelectionRef['kind'][] = ['feature', 'cave', 'entrance'];

/** Every object id on the wire is a GUID; anything else in the address is not one of ours. */
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/**
 * What of a selection goes into the address. Only the three kinds a reader can be sent to by id
 * do; a cluster or a trip, whose selection is a place or a record with a page of its own, leaves
 * the address without one.
 */
export function selectionRefOf(selection: WorkspaceSelection | null): SelectionRef | null {
  switch (selection?.kind) {
    case 'feature':
      return { kind: 'feature', id: selection.featureId };
    case 'cave':
      return { kind: 'cave', id: selection.caveId };
    case 'entrance':
      return { kind: 'entrance', id: selection.entranceId };
    default:
      return null;
  }
}

export function formatSelectionParam(ref: SelectionRef): string {
  return `${ref.kind}:${ref.id}`;
}

/** The selection an address names, or null when it names none, or nothing that could be one. */
export function parseSelectionParam(value: string | null): SelectionRef | null {
  if (!value) {
    return null;
  }
  const separator = value.indexOf(':');
  const kind = value.slice(0, separator) as SelectionRef['kind'];
  const id = value.slice(separator + 1);
  return separator > 0 && KINDS.includes(kind) && GUID.test(id) ? { kind, id } : null;
}

/**
 * The selection an address asked for, once the object has been fetched the way any reader fetches
 * it — through the endpoint that decides what this reader may see. Null when what came back is
 * not the kind the address named (an id passed off as a cave that is not one selects nothing).
 */
export function selectionFromEnvelope(ref: SelectionRef, envelope: FeatureEnvelope): WorkspaceSelection | null {
  const id = envelope.feature.id;
  if (envelope.kind === 'cave') {
    return ref.kind === 'entrance' ? null : { kind: 'cave', caveId: id };
  }
  if (envelope.kind === 'caveEntrance' && envelope.entrance) {
    return ref.kind === 'cave'
      ? null
      : { kind: 'entrance', entranceId: id, caveId: envelope.entrance.caveFeatureId };
  }
  return ref.kind === 'feature' ? { kind: 'feature', featureId: id } : null;
}

/**
 * Writes the selection into the address without a navigation — the camera's own address keeps its
 * place in the hash, and the history gains no entry, the way the camera writes itself.
 */
export function writeSelectionToUrl(ref: SelectionRef | null): void {
  const url = new URL(window.location.href);
  const current = url.searchParams.get(SELECT_PARAM);
  const next = ref ? formatSelectionParam(ref) : null;
  if (current === next) {
    return;
  }
  if (next) {
    url.searchParams.set(SELECT_PARAM, next);
  } else {
    url.searchParams.delete(SELECT_PARAM);
  }
  window.history.replaceState(window.history.state, '', `${url.pathname}${url.search}${url.hash}`);
}
