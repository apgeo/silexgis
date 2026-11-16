// SPDX-License-Identifier: AGPL-3.0-or-later
import type VectorSource from 'ol/source/Vector';
import { GEOLOGICAL_UNIT_TYPE_CODE } from '../../map/lithologyAppearance.ts';
import { LITHOLOGY_CODES } from './lithology.ts';

/** One line of the key: a rock class drawn on the map now, as the map answer described it. */
export interface LithologyKeyEntry {
  /** The stored code, or null for units nobody classified. */
  lithology: string | null;
  /** The vocabulary's word on the class, as the server sent it; null when it is not known. */
  karstifiable: boolean | null;
}

/**
 * The classes the loaded geological units are drawn in, once each, in vocabulary order —
 * karstifiable classes first, as the vocabulary lists them — then unrecognised codes, then the
 * unclassified units.
 */
export function lithologyKeyOf(source: VectorSource): LithologyKeyEntry[] {
  const seen = new Map<string, LithologyKeyEntry>();
  for (const feature of source.getFeatures()) {
    if (feature.get('typeCode') !== GEOLOGICAL_UNIT_TYPE_CODE) continue;
    const raw: unknown = feature.get('lithology');
    const lithology = typeof raw === 'string' && raw !== '' ? raw : null;
    const flag: unknown = feature.get('karstifiable');
    const key = lithology ?? '';
    if (!seen.has(key)) seen.set(key, { lithology, karstifiable: typeof flag === 'boolean' ? flag : null });
  }
  const rank = (entry: LithologyKeyEntry) => {
    if (entry.lithology === null) return Number.MAX_SAFE_INTEGER;
    const index = (LITHOLOGY_CODES as readonly string[]).indexOf(entry.lithology);
    return index === -1 ? LITHOLOGY_CODES.length : index;
  };
  return [...seen.values()].sort(
    (a, b) => rank(a) - rank(b) || String(a.lithology).localeCompare(String(b.lithology)),
  );
}
