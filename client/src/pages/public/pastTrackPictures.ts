// SPDX-License-Identifier: AGPL-3.0-or-later
import { picturesAt } from '../../caveview/trackingReplay.ts';
import { instantOf } from './publicTripParty.ts';

/**
 * The photographs a finished trip's replay may come with, folded to what is shown at a moment.
 *
 * <b>Only where the installation publishes them.</b> A published replay carries `pictures` — the
 * photographs hung on moments of the trip that are in the installation's public gallery — only on
 * an installation that has chosen to; everywhere else the list is empty, and an answer from a
 * server older than the list has none at all. All three read the same here: nothing is shown.
 *
 * <b>No framework, no wording, nothing about language</b>: an instant in milliseconds, a number in
 * the party or null, an address and a caption as they were sent. A page says "of Caver 2" in its
 * own words, by the name it already gives that number.
 */

/** One photograph as a published replay sends it. */
export interface PublishedMomentPicture {
  /** The moment the picture hangs on, as an ISO instant. */
  at: string;
  /**
   * The number in the party of the person the picture is about — the `ordinal` the track's own
   * rows carry — or null when it is a picture of the moment and of nobody in particular.
   */
  ordinal: number | null;
  /** An address that opens a rendering of the picture for a few minutes. */
  thumbnailUrl: string;
  caption: string | null;
}

/** One photograph placed on the replay's clock. */
export interface PastPicture {
  /** The moment the picture hangs on, in milliseconds. */
  at: number;
  ordinal: number | null;
  thumbnailUrl: string;
  caption: string | null;
}

/**
 * Every photograph of a replay, oldest moment first.
 *
 * A picture whose instant cannot be read is left out rather than placed at a guess — the same
 * reading a report with an unreadable instant gets. The order pictures of one moment were sent in
 * is kept.
 */
export function pastPictures(
  track: { pictures?: readonly PublishedMomentPicture[] | null } | null | undefined,
): PastPicture[] {
  const placed: PastPicture[] = [];
  for (const picture of track?.pictures ?? []) {
    const at = instantOf(picture.at);
    if (at === null) {
      continue;
    }
    placed.push({
      at,
      ordinal: picture.ordinal ?? null,
      thumbnailUrl: picture.thumbnailUrl,
      caption: picture.caption ?? null,
    });
  }
  // A stable sort, so pictures of one moment stay in the order they were sent.
  return placed.sort((left, right) => left.at - right.at);
}

/**
 * The photographs in force at a moment of a replay: those of the latest moment at or before it
 * that carries any, and none before the first.
 *
 * <b>Decided by the fold a coordinator's replay shows its pictures by</b>, so the two cannot come
 * to disagree about which photographs belong to a place on the rail. A handle stops at a thousand
 * places across a trip that is hours long and never lands on the instant a camera recorded; "in
 * force" is what makes a picture reachable at all.
 */
export function pastPicturesAt(
  track: { pictures?: readonly PublishedMomentPicture[] | null } | null | undefined,
  at: number,
): PastPicture[] {
  return picturesAt(pastPictures(track), at);
}
