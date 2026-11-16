// SPDX-License-Identifier: AGPL-3.0-or-later
import type { TerrainAccuracy, TerrainAccuracyRequest } from '../../../api/hooks.ts';

/** The four accuracy fields as a form holds them: empty means "not stated". */
export interface AccuracyFormValues {
  resolutionM?: number | null;
  accuracyM?: number | null;
  accuracyStatistic?: string | null;
  accuracySource?: string | null;
}

/**
 * What to send for an accuracy form, or undefined when nothing was stated.
 *
 * Undefined rather than an object of nulls on a new build: leaving the section alone must let the
 * server fill in the open dataset's published figures, and an explicitly empty record would not
 * say the same thing.
 */
export function accuracyRequest(values: AccuracyFormValues): TerrainAccuracyRequest | undefined {
  const resolution = values.resolutionM ?? null;
  const accuracy = values.accuracyM ?? null;
  const source = (values.accuracySource ?? '').trim();
  if (resolution === null && accuracy === null && source === '') {
    return undefined;
  }
  return {
    sourceResolutionM: resolution,
    verticalAccuracyM: accuracy,
    verticalAccuracyStatistic: accuracy === null ? null : (values.accuracyStatistic ?? null),
    accuracySource: source === '' ? null : source,
  };
}

/** A build's recorded accuracy as form values, for the editor. */
export function accuracyFormValues(accuracy: TerrainAccuracy | null | undefined): AccuracyFormValues {
  return {
    resolutionM: accuracy?.sourceResolutionM ?? null,
    accuracyM: accuracy?.verticalAccuracyM ?? null,
    accuracyStatistic: accuracy?.verticalAccuracyStatistic ?? 'RMSE',
    accuracySource: accuracy?.accuracySource ?? '',
  };
}

/**
 * The accuracy as a short line for a table cell, or null when nothing is recorded, e.g.
 * "30 m spacing, 4 m LE90". Each part appears only when it is known.
 */
export function describeAccuracy(
  accuracy: TerrainAccuracy | null | undefined,
  t: (key: string, options?: Record<string, unknown>) => string,
): string | null {
  if (!accuracy) {
    return null;
  }
  const parts: string[] = [];
  if (accuracy.sourceResolutionM != null) {
    parts.push(t('terrain.accuracy.spacing', { value: accuracy.sourceResolutionM }));
  }
  if (accuracy.verticalAccuracyM != null) {
    parts.push(
      t('terrain.accuracy.vertical', {
        value: accuracy.verticalAccuracyM,
        statistic: accuracy.verticalAccuracyStatistic ?? '',
      }),
    );
  }
  return parts.length > 0 ? parts.join(', ') : null;
}
