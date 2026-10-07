// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo } from 'react';
import { useSurveyModels, type SurveyModelInfo } from '../api/hooks.ts';
import { compareOffer, type SurveyCompareOffer } from './surveyCompare.ts';

/**
 * What the survey a panel is showing can be compared with: the other line plots of its cave.
 *
 * One home for the three mounts that offer a comparison — the viewer over the window, the pane
 * beside the map and the window of its own — so that all of them read the cave's list the same way
 * and none decides for itself which surveys count.
 *
 * Answers nothing until the cave's list has arrived, and nothing for a cave with no other line
 * plot. The list is the one the cave's own page keeps, re-read every few minutes while anything
 * shows it, so the addresses in the answer are fresh whenever a comparison is started.
 */
export function useCompareOffer(
  model: Pick<SurveyModelInfo, 'id' | 'caveId'> | null | undefined,
): SurveyCompareOffer | undefined {
  const { data } = useSurveyModels(model?.caveId);
  return useMemo(() => compareOffer(data, model?.id) ?? undefined, [data, model?.id]);
}
