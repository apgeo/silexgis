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

/**
 * How many pictures one station is handed over with.
 *
 * <b>Because the strip fetches every one of them, and the screen shows a handful.</b> The viewer
 * builds a thumbnail for each entry and sets its source as the strip is drawn, so a station that
 * somebody has linked forty photographs to is forty requests every time a finger lands on it — on
 * the tracking tab, which is read on a phone on a hillside. What can be seen of them is much less:
 * measured on a 286px-wide model surface, the width a 360px phone leaves this panel, a wrapped
 * strip of coarse-pointer thumbnails shows two across and rather under three rows down.
 *
 * Twelve is chosen as what a fine pointer fits on a single row of a desk-width model, so on the
 * screen where the room exists the bound is never what decides; where it does decide, it is bounding
 * a block of pictures that was already larger than the model it is drawn over. Whoever wants all of
 * them is looking at a document list rather than at a line drawing.
 */
const MAX_PER_STATION = 12;

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
    // Which document this is, so a host opening its own viewer on the clicked thumbnail knows what
    // it is showing. It is the member's target — the photograph itself — and not the link's.
    documentId: member.targetId,
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
        if (entries.length >= MAX_PER_STATION) {
          break;
        }
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
 * Wanted because a thumbnail that is clicked is one of a set, and the picture viewer it opens shows
 * the set: the strip is bounded by the model surface it is drawn over — two thumbnails across on a
 * phone — so the pictures past what fits are reached by opening one of the ones that do and moving
 * on from it. A Map is keyed by the station's dotted path; a function is asked about the station
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
