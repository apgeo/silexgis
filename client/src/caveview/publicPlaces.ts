// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * One place a cave has named, as a published survey is handed it: a station of that survey, the
 * depth the cave declared it at, and what people call it.
 *
 * Declared here rather than taken from the generated API types, so that this module holds
 * strings and numbers only and a page with no build step can ask the same question of the same
 * answer.
 */
export interface PublishedPlace {
  /** The station, spelled as every reported `stationName` of the same answer is spelled. */
  station: string;
  /**
   * Metres below the entrance as the cave declared them — a magnitude, never negative. A person's
   * own reported depth keeps the sign it was reported with, so the two are not the same number
   * for the same place and are not compared here.
   */
  depthM: number;
  /** What the cave calls the place. */
  label: string;
}

/**
 * What the cave calls the station somebody was reported at, or null when it has not said — which
 * is the ordinary answer, and the only one on an installation that does not publish these names.
 *
 * <b>Null means "say the station as the survey names it", exactly as before these names existed.</b>
 * The list is null on every installation that has not chosen to publish it and for every cave
 * that has named nothing on the survey being drawn — the server sends a list only when it has
 * entries — and a missing list, an answer from a server older than the member, and an empty one
 * are read the same way; so a caller that falls back to the station on null behaves, for all of
 * those, precisely as it always did.
 *
 * <b>Matched on the station as written, and on nothing else.</b> The server sends each name with
 * the station in the spelling the reports of the same answer use, having already checked that the
 * survey being drawn holds it; so there is no folding of case and no second spelling to try here,
 * and a near match would be this page putting a name on a place the cave did not give it to. Not
 * matched by depth either: a report made as a depth arrives already resolved to its station, and
 * a place's declared depth and a person's reported one do not even share a sign.
 *
 * <b>The name is an addition, never a replacement.</b> What comes back is for showing beside the
 * station, which stays — it is what the drawing labels and what a link to a moment names.
 *
 * Where a cave has given one station two names, at two depths, the shallower is the one answered:
 * the list arrives shallowest first and the first match is taken.
 */
export function placeLabelFor(
  station: string | null | undefined,
  places: readonly PublishedPlace[] | null | undefined,
): string | null {
  if (station === null || station === undefined || station.length === 0) {
    return null;
  }
  if (places === null || places === undefined) {
    return null;
  }
  for (const place of places) {
    if (place.station === station && place.label.length > 0) {
      return place.label;
    }
  }
  return null;
}
