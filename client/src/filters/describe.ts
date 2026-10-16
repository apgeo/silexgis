// SPDX-License-Identifier: AGPL-3.0-or-later
import { readCaveRegistry } from './caveRegistry.ts';
import type { FilterDocument } from './types.ts';

/**
 * One thing a filter asks, as a part a screen can put into words.
 *
 * Identities stay identities here — a cave type's id, a tag's slug. Turning one into a name is the
 * screen's business and is done only through lists its reader may already read, so a value the
 * reader cannot resolve is shown as not available to them rather than as a name they were never
 * given or a bare id.
 */
export type ConditionPart =
  | { kind: 'search'; text: string }
  | { kind: 'region'; text: string }
  | { kind: 'minLength'; length: number }
  | { kind: 'caveType'; id: string }
  | { kind: 'tag'; slug: string }
  | { kind: 'more'; count: number };

/**
 * What a cave registry filter asks, part by part, or null when it is not about caves at all.
 *
 * Conditions the registry's controls cannot show are counted rather than spelled out: a readable
 * account of an arbitrary tree belongs to the condition editor, and a half-right paraphrase of one
 * would be worse than a count.
 */
export function describeCaveRegistry(document: FilterDocument): ConditionPart[] | null {
  const reading = readCaveRegistry(document);
  if (reading === null) {
    return null;
  }

  const { controls, extra } = reading;
  const parts: ConditionPart[] = [];
  if (controls.search !== undefined) parts.push({ kind: 'search', text: controls.search });
  if (controls.caveTypeId !== undefined) parts.push({ kind: 'caveType', id: String(controls.caveTypeId) });
  if (controls.region !== undefined) parts.push({ kind: 'region', text: controls.region });
  if (controls.minLength !== undefined) parts.push({ kind: 'minLength', length: controls.minLength });
  if (controls.tag !== undefined) parts.push({ kind: 'tag', slug: controls.tag });
  if (extra.length > 0) parts.push({ kind: 'more', count: extra.length });
  return parts;
}
