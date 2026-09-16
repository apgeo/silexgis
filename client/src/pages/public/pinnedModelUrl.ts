// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useState } from 'react';

/**
 * Which drawing a delivery URL delivers, ignoring the signature that expires.
 *
 * A published envelope carries the survey as a signed address: the stored file's own path, and a
 * token good for about ten minutes. Every re-read of the envelope re-signs it, so the whole string
 * changes every minute while the path — the file being handed over — stays exactly what it was.
 * That path is the drawing's identity in the only sense this page needs one: the viewer draws
 * those bytes, and two addresses naming the same file name the same survey.
 *
 * <b>Deliberately not the model's identity, which this page is never sent.</b> A published trip
 * carries no internal identifier of anything, on purpose. What it does carry is the address of the
 * bytes, and the bytes are the better answer anyway: a station is drawn at a point of a particular
 * geometry, so the question "may this report be drawn here" is a question about the geometry on
 * screen and not about which row in a table produced it.
 */
export function modelDeliveryIdentity(url: string): string {
  const query = url.indexOf('?');
  return query === -1 ? url : url.slice(0, query);
}

/**
 * The survey address the viewer is given, held still while it is the same survey and replaced when
 * it is not.
 *
 * <b>Why it is held still.</b> The viewer downloads and parses the model when its address changes,
 * and the camera goes back to the view the model opens at when it does. A followed page re-reads
 * its envelope every minute, and every read mints a freshly signed address for the same file — so
 * passing the latest string straight through would re-download the survey and throw the camera
 * back to its opening view once a minute, for the whole time somebody sits watching a party
 * underground. Whether the first address still works an hour later is of no interest to a model
 * that is already in the browser.
 *
 * <b>Why it is not held still forever, which is the part that was wrong.</b> The address was pinned
 * on first sight and released only when the page was opened on a different trip. But the survey a
 * watch is pointed at can be changed while the party is underground — a corrected or re-imported
 * survey mid-trip is the exact situation this whole feature has to follow — and the server then
 * publishes stations measured in the <em>new</em> survey while the page goes on drawing the
 * <em>old</em> one. The viewer places a marker at whatever node of the old geometry happens to
 * carry that name, and a follower gets a confident marker for a person underground on geometry
 * their report was never measured against. A camera reset once, at the moment a coordinator
 * deliberately changed the survey, is the incomparably smaller cost.
 *
 * <b>A poll that failed changes nothing.</b> The envelope in hand is still the last true word, and
 * a page whose model went missing for a minute keeps drawing what it had rather than blanking the
 * survey a family is watching.
 *
 * @param modelUrl the address the latest envelope carried, or null when it carried no survey.
 * @param token the trip being followed. A different trip is a different page, pinned from scratch.
 */
export function usePinnedModelUrl(
  modelUrl: string | null | undefined,
  token: string | undefined,
): string | null {
  const [pinned, setPinned] = useState<string | null>(null);

  useEffect(() => setPinned(null), [token]);

  useEffect(() => {
    if (modelUrl === null || modelUrl === undefined) {
      return;
    }
    setPinned((current) =>
      current !== null && modelDeliveryIdentity(current) === modelDeliveryIdentity(modelUrl)
        ? current
        : modelUrl,
    );
  }, [modelUrl]);

  return pinned;
}
