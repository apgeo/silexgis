// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo } from 'react';
import { useResLinksForTarget } from '../api/hooks.ts';
import type { CaveViewMediaEntry } from './loadCaveView.ts';
import { stationMediaFromLinks } from './stationMedia.ts';

/** How many of a model's links are read for pictures. The same bound the links panel uses. */
const MAX_LINKS = 200;

/**
 * The pictures a signed-in surface shows over a survey model, read once and the same way everywhere.
 *
 * <b>This exists because the second surface arrived.</b> Deriving pictures from links is already in
 * one place; what was about to be copied is everything around it — which route answers them, that it
 * is asked about the model rather than the cave, the page bound, and that the answer is a map even
 * before anything has arrived. A surface that copied those would drift from this one silently,
 * because every one of them is invisible until a model has pictures on it.
 *
 * <b>`enabled` is load-bearing, not a convenience.</b> A model panel on a tab somebody opened to
 * record that the party went in must cost nothing until the model is actually opened — on a phone on
 * a hillside that is the difference between a page that loads and one that does not. So the caller
 * says when the pictures are worth asking for, and the answer until then is an empty map rather than
 * "not yet": the map's presence is what tells the viewer this surface shows pictures at all, and a
 * surface that showed none until a fetch landed would have to say so in a second way.
 *
 * <b>Anonymous surfaces cannot use this, and that is the whole reason it is written down here.</b>
 * The route it reads takes an account. A published trip page is read by somebody holding one link
 * and nothing else, and every other address in this installation refuses them — so a public page
 * that reached for this hook would fire a request that answers 401, show no pictures, and look
 * exactly like a cave whose stations have none. There is no anonymous route for a model's links and
 * none is invented on the client: the only thing a visitor can read is the published envelope, so
 * the day a published page shows pictures is the day that envelope carries them, and the derivation
 * for it belongs beside this one rather than inside a page.
 */
export function useStationMedia(
  surveyModelId: string | undefined,
  enabled: boolean,
): ReadonlyMap<string, CaveViewMediaEntry[]> {
  const { data } = useResLinksForTarget(
    'surveyModel',
    surveyModelId ?? '',
    { pageSize: MAX_LINKS },
    enabled && surveyModelId !== undefined,
  );

  return useMemo(
    () => stationMediaFromLinks(data?.items ?? [], surveyModelId ?? ''),
    [data, surveyModelId],
  );
}
