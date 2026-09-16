// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo, useRef } from 'react';
import { useResLinksForTarget, type PublicTripStationPicture } from '../api/hooks.ts';
import type { CaveViewMediaEntry } from './loadCaveView.ts';
import {
  restampStationMedia,
  stationMediaFromEnvelope,
  stationMediaFromLinks,
} from './stationMedia.ts';

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

/**
 * The same strip on a published page, built from the envelope because nothing else is readable.
 *
 * <b>Here, beside the hook above, for the reason that one gives.</b> There are two anonymous
 * surfaces that draw this strip — the followed page and the embed of it inside somebody's article —
 * and everything around the derivation is what they would otherwise each spell out: that the
 * pictures come from the envelope and never from a route, and that an envelope carrying none must
 * hand the viewer nothing rather than an empty map.
 *
 * <b>What it adds to the derivation is that the answer is the same object for as long as it is the
 * same photographs.</b> A published page keeps re-reading its envelope while it is open, and every
 * read re-signs every picture URL — so the derivation alone would hand the viewer a new source
 * every time, and the viewer drops the open strip and its hover listeners whenever it is handed
 * one. The map is therefore kept across reads and restamped in place; see
 * `restampStationMedia` for what that costs and what it buys. A page that memoised the
 * derivation itself would look correct and would dismiss a photograph somebody is looking at,
 * which is exactly the kind of thing only one of these two pages would ever be fixed for.
 */
export function usePublishedStationMedia(
  pictures: readonly PublicTripStationPicture[] | undefined,
): ReadonlyMap<string, readonly CaveViewMediaEntry[]> | undefined {
  const held = useRef<Map<string, CaveViewMediaEntry[]> | undefined>(undefined);
  return useMemo(() => {
    held.current = restampStationMedia(held.current, stationMediaFromEnvelope(pictures ?? []));
    return held.current;
  }, [pictures]);
}
