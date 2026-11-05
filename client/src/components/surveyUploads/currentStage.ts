// SPDX-License-Identifier: AGPL-3.0-or-later
import type { StageProgress } from '../../api/hooks.ts';

/**
 * The step the run is on, for the steps' `current`: the first that has neither finished nor been
 * passed over, and one past the last when every step has.
 *
 * Said explicitly because the steps component marks whichever step is `current` as the active one,
 * drawn stronger than its neighbours, and takes the first step when told nothing. A finished run
 * then showed its first step lit up beside the other finished ones as if it were still going on.
 */
export function currentStageIndex(stages: readonly Pick<StageProgress, 'state'>[]): number {
  const index = stages.findIndex(({ state }) => state !== 'done' && state !== 'skipped');
  return index === -1 ? stages.length : index;
}
