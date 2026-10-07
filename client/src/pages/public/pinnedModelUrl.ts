// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { modelDeliveryIdentity } from '../../caveview/modelDelivery.ts';

// What the pin below compares addresses by: the file an address delivers, whatever signature it
// carries. A published trip is sent no internal identifier of anything, on purpose, and the bytes
// are the better answer here anyway — a station is drawn at a point of a particular geometry, so
// "may this report be drawn here" is a question about the geometry on screen and not about which
// row in a table produced it.

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
 * The viewer has since been taught the same thing about signatures for itself, for every page that
 * mounts it; the pin stays for the two things below, which only the page can decide.
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
 * <b>Decided while rendering, never a render late.</b> The pages branch on this answer: null is
 * where they say there is no drawing for this trip. Settled in an effect it was null for the whole
 * of the render in which an address first arrived — and the old trip's address for the render in
 * which a different trip was chosen — so a frame in somebody's article could commit "there is no
 * survey drawing" over a trip that has one. So the pin is state that is brought up to date in the
 * render that sees the change, and the value returned is always the updated one.
 *
 * @param modelUrl the address the latest envelope carried, or null when it carried no survey.
 * @param token the trip being followed. A different trip is a different page, pinned from scratch.
 */
export function usePinnedModelUrl(
  modelUrl: string | null | undefined,
  token: string | undefined,
): string | null {
  const [held, setHeld] = useState<{ token: string | undefined; url: string | null }>(() => ({
    token,
    url: modelUrl ?? null,
  }));

  let next = held;
  if (held.token !== token) {
    next = { token, url: modelUrl ?? null };
  } else if (
    modelUrl !== null
    && modelUrl !== undefined
    && (held.url === null || modelDeliveryIdentity(held.url) !== modelDeliveryIdentity(modelUrl))
  ) {
    next = { token, url: modelUrl };
  }
  if (next !== held) {
    // Stored during render, which React applies by rendering again before anything of this
    // render is committed — the documented way to keep state in step with a changed prop.
    setHeld(next);
  }

  return next.url;
}
