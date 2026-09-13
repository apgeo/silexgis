// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ResLink, ResLinkMember } from '../api/hooks.ts';
import { thumbnailAtSize } from '../components/documents/derivativeUrl.ts';
import type { CaveViewMediaEntry, CaveViewStationMediaSource } from './loadCaveView.ts';

/**
 * The pictures the viewer shows at a station, read out of the links the model already has.
 *
 * <b>There is no photo-per-station table, and none is invented here.</b> A link already relates
 * any number of resources, and a member already says which part of its target it means — for a
 * survey model, a station of it. So "this photograph was taken at station 7" is a link with a
 * member anchored to that station and a member that is the photograph, which is what somebody
 * authors by clicking the station in the viewer and linking it. This reads that back.
 *
 * <b>Only a station carries a strip.</b> A link anchored to a run of passage or to a named part
 * of the survey says the picture belongs to a stretch of cave, not to a point of it, and hanging
 * it on one end would place a photograph somewhere it was not taken.
 *
 * <b>The pictures are asked for at the sizes the reader is entitled to.</b> Both URLs are derived
 * from the published thumbnail URL, which carries its own token: a rendering is served to a caller
 * who may not have the stored bytes, so reaching for the original "because it is only showing it"
 * would hand over exactly what was withheld from somebody who may not be told where a photograph
 * was taken.
 */

/** The rendering shown when a thumbnail is clicked — meant to be read, not glanced at. */
const FULL_SIZE = 1200;
/** The rendering shown in the strip; the strip's thumbnails are 64px by default. */
const STRIP_SIZE = 160;

/** Whether a member anchors its link to a station of the model on screen. */
function stationOf(member: ResLinkMember, surveyModelId: string): string | null {
  if (
    member.targetType !== 'surveyModel'
    || member.targetId !== surveyModelId
    || member.anchorKind !== 'modelStation'
  ) {
    return null;
  }
  // The anchor payload is withheld from a reader who may not read it, and arrives as null; a
  // strip placed at a guessed station is the failure this is arranged to avoid.
  const anchor = (typeof member.anchor === 'object' && member.anchor !== null ? member.anchor : {}) as Record<
    string,
    unknown
  >;
  const station = anchor.station;
  return typeof station === 'string' && station.length > 0 ? station : null;
}

/** Whether a member is a picture that can be shown, and what to show of it. */
function pictureOf(member: ResLinkMember): CaveViewMediaEntry | null {
  const display = member.display;
  if (
    member.targetType !== 'document'
    || display == null
    || display.thumbnailUrl == null
    || !(display.mediaType ?? '').startsWith('image/')
  ) {
    return null;
  }
  return {
    url: thumbnailAtSize(display.thumbnailUrl, FULL_SIZE),
    thumbnailUrl: thumbnailAtSize(display.thumbnailUrl, STRIP_SIZE),
    caption: display.title,
  };
}

/**
 * Every station of one model that has pictures, keyed by the station path the viewer resolves —
 * the dotted string the anchor stores, which is the viewer's own spelling of it.
 *
 * A link holding several stations and several pictures puts all of its pictures on all of its
 * stations: what the link says is that these things belong together, and it names no pairing
 * inside itself for this to read one out of.
 */
export function stationMediaFromLinks(
  links: readonly ResLink[],
  surveyModelId: string,
): Map<string, CaveViewMediaEntry[]> {
  const media = new Map<string, CaveViewMediaEntry[]>();

  for (const link of links) {
    const stations: string[] = [];
    const pictures: CaveViewMediaEntry[] = [];
    for (const member of link.members) {
      const station = stationOf(member, surveyModelId);
      if (station !== null) {
        stations.push(station);
        continue;
      }
      const picture = pictureOf(member);
      if (picture !== null) {
        pictures.push(picture);
      }
    }

    for (const station of stations) {
      const entries = media.get(station) ?? [];
      for (const picture of pictures) {
        // The same photograph can reach one station through two links — it is linked to the
        // station and to the passage it stands in — and the strip would then show it twice.
        if (!entries.some((entry) => entry.url === picture.url)) {
          entries.push(picture);
        }
      }
      if (entries.length > 0) {
        media.set(station, entries);
      }
    }
  }

  return media;
}

/**
 * What one station has, asked of either kind of source.
 *
 * Wanted because a tap has to know whether there is a strip to show before it asks for one: with no
 * pointer to hover, the strip is opened by focusing the station, and that also flies the camera to
 * it — so asking at a station with nothing to show would answer a tap by moving the view for
 * nothing. A Map is keyed by the station's dotted path; a function is asked about the station
 * itself, which is the object the viewer hands over with the click.
 */
export function mediaForStation(
  source: CaveViewStationMediaSource,
  path: string,
  station: unknown,
): readonly CaveViewMediaEntry[] {
  const entries = typeof source === 'function' ? source(station) : source.get(path);
  return entries ?? [];
}
