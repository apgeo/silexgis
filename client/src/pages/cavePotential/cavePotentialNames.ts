// SPDX-License-Identifier: AGPL-3.0-or-later
import type { TFunction } from 'i18next';
import type {
  CavePotentialDefinition,
  CavePotentialMethod,
  EvidenceLayerInfo,
} from '../../api/hooks/cavePotential.ts';

/** What a class of a categorical layer is called, in the words its own legend uses elsewhere. */
export function categoryName(t: TFunction, layer: string, code: string): string {
  return layer === 'lithology'
    ? t(`geology.lithology.${code}`, { defaultValue: code })
    : t(`terrain.landforms.${code}`, { defaultValue: code });
}

/** An evidence layer's name with the unit its values are in. */
export function evidenceName(t: TFunction, info: Pick<EvidenceLayerInfo, 'code' | 'unit'>): string {
  const name = t(`cavePotential.evidence.${info.code}`, { defaultValue: info.code });
  return info.unit ? `${name} (${t(`cavePotential.units.${info.unit}`, { defaultValue: info.unit })})` : name;
}

/**
 * Whether a method counts its weights from known entrances instead of taking them from its author.
 * Such a model names no weights, scores or comparisons, and cuts every measured layer into classes.
 */
export function isDataDriven(method: CavePotentialMethod): boolean {
  return method === 'frequencyRatio' || method === 'weightsOfEvidence';
}

/**
 * What one class of a layer's weight table is called.
 *
 * A class of a measured layer is named by its bounds, which are the model's as it was run and come
 * with its definition — so a reader who is not shown the model is told only which class it is, in
 * order. A value on a bound belongs to the class that starts there, hence "from … up to …". A
 * layer that is classes already names each by its own word.
 */
export function classLabel(
  t: TFunction,
  layer: string,
  index: number,
  definition: CavePotentialDefinition | null | undefined,
  info: Pick<EvidenceLayerInfo, 'categories'> | undefined,
): string {
  const category = info?.categories[index];
  if (category !== undefined) {
    return categoryName(t, layer, category);
  }
  const rows = definition?.layers.find((setting) => setting.layer === layer)?.transform.classes;
  if (!rows || index >= rows.length) {
    return t('cavePotential.training.classNumber', { n: index + 1 });
  }
  const lower = index === 0 ? null : (rows[index - 1]?.upperBound ?? null);
  const upper = index === rows.length - 1 ? null : (rows[index]?.upperBound ?? null);
  if (lower === null && upper === null) {
    return t('cavePotential.training.classNumber', { n: index + 1 });
  }
  if (lower === null) {
    return t('cavePotential.training.classBelow', { upper });
  }
  if (upper === null) {
    return t('cavePotential.training.classFrom', { lower });
  }
  return t('cavePotential.training.classBetween', { lower, upper });
}
