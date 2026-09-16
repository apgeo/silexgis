// SPDX-License-Identifier: AGPL-3.0-or-later
import type { TrackingParticipant } from '../../api/hooks.ts';

/** How a followed page ends up naming one member of the party. */
export type PublicNaming =
  /** A caption somebody typed for this trip. It wins over everything, in both directions. */
  | 'caption'
  /** No caption, and this installation publishes the names the roster holds. */
  | 'realName'
  /** No caption, and this installation publishes places in the party — Caver 1, Caver 2. */
  | 'placeInParty';

/**
 * What the published page will call one person, decided here and in no second place.
 *
 * <b>The order is the whole rule and it is read top to bottom.</b> A caption an administrator typed
 * wins over everything, because that field exists precisely so one person can be kept off a public
 * page by name while the rest of the party is named — if the installation's setting could override
 * it, somebody who asked not to appear would appear the moment a setting somewhere else was
 * flipped. Failing a caption, the roster's own name, where the installation publishes names.
 * Failing both, a place in the party and no name at all.
 *
 * <b>A caption is not only a way to hide.</b> It outranks the setting in the other direction too:
 * on an installation that publishes nobody's name, a captioned person is the one the page names —
 * which is how a trip leader's contact name gets onto a page whose party is otherwise numbered.
 * Anything that drew this as "hidden unless" would get that case backwards.
 *
 * <b>Anything but an explicit "no" is read as the naming case</b>, exactly as the panel that mints
 * a link reads it. A server that did not answer the question — an older build, a read that has not
 * landed — leaves an administrator warned about names that may not appear, which costs them a
 * moment; the other way round costs somebody else their name on a public page.
 */
export function publicNamingOf(
  participant: Pick<TrackingParticipant, 'label'>,
  publishesRealNames: boolean,
): PublicNaming {
  // Blank-but-present is the caption that was cleared and not yet re-read; it names nobody, and
  // treating it as a caption would draw an empty quotation where a name belongs.
  if (participant.label !== null && participant.label.trim().length > 0) {
    return 'caption';
  }
  return publishesRealNames !== false ? 'realName' : 'placeInParty';
}
