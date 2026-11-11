// SPDX-License-Identifier: AGPL-3.0-or-later
import { useCaveSummary, type SurveyModelInfo } from '../../api/hooks.ts';

/** Deepest and highest a cave's zero level can plausibly sit, matching what the server accepts. */
export const LOWEST_HEIGHT_M = -500;
export const HIGHEST_HEIGHT_M = 9000;

/** What a person says about where a survey file sits: the fields both forms that ask it share. */
export interface SurveyPlacementForm {
  coordinates: 'local' | 'projected';
  sourceEpsg?: number;
  originLongitude?: number;
  originLatitude?: number;
  originHeightM?: number;
}

/** Why the cave's entrance cannot be offered as a local file's origin, when it cannot. */
export type OriginProblem = 'obfuscated' | 'noEntrance';

/**
 * The cave's entrance as an origin for a local file — the true position, or nothing.
 *
 * <b>Why the origin is only sometimes offered.</b> A local export is the ordinary case and the
 * cave's own entrance is usually its origin, so the entrance position is filled in — but a cave
 * whose location is protected shows a deliberately-blurred position to accounts without
 * exact-location access, and seeding the form with that would write a wrong anchor into the
 * database and look like a success. The offer is made only when the server says the position this
 * account holds is the true one.
 *
 * Why there is nothing to offer is three different things and must not be said as one. Only one
 * of them is a protection decision: a cave with no entrance position on record is described as
 * what it is, and a summary still on its way says nothing at all rather than accusing the
 * installation of something while it loads.
 */
export function useExactCaveOrigin(caveId: string): {
  exactOrigin: { longitude: number; latitude: number } | undefined;
  originProblem: OriginProblem | undefined;
} {
  const { data: summary } = useCaveSummary(caveId);
  const entrance = summary?.mainEntrance;
  const exactOrigin =
    summary?.permissions.canViewExactLocation === true &&
    entrance?.approximateLocation === false &&
    entrance.geom
      ? {
          longitude: Number(entrance.geom.coordinates[0]),
          latitude: Number(entrance.geom.coordinates[1]),
        }
      : undefined;
  const originProblem: OriginProblem | undefined = !summary
    ? undefined
    : exactOrigin
      ? undefined
      : summary.permissions.canViewExactLocation !== true || entrance?.approximateLocation === true
        ? 'obfuscated'
        : 'noEntrance';
  return { exactOrigin, originProblem };
}

/** Whether a reading found a declaration that would place the model at its cave. */
export function hasPlacementSuggestion(model: SurveyModelInfo): boolean {
  return typeof model.suggestedSourceEpsg === 'number' || model.suggestsLocalMetres === true;
}

/** A distance in kilometres as the placement notes say it: tenths only where they matter. */
export function formatKm(km: number): string {
  return km < 10 ? km.toFixed(1) : Math.round(km).toString();
}

/** A length of passage, in metres under a kilometre and in kilometres above. */
export function formatPassageLength(metres: number): string {
  return metres < 1000 ? `${Math.round(metres)} m` : `${(metres / 1000).toFixed(1)} km`;
}
