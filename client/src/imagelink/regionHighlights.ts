// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ResLink, ResLinkAnchorState, ResLinkMember } from '../api/hooks.ts';
import type { HighlightTarget } from '../textlink/highlights.ts';
import { readRegion, type ImageRegion } from './regions.ts';

/**
 * Turning the links a picture participates in into the regions to draw on it.
 *
 * Deliberately the same shape as the passages drawn over a text, and for the same reason: there
 * is no annotation table behind either. A link already relates any number of resources under a
 * relation, and a member already says which part of its target it means — for a picture, a region
 * of it. So a drawn region is a member of a link whose target is *this* document, carrying a
 * region anchor measured against *this* file, and what it points at is the link's other members.
 *
 * <b>The file is part of the filter, not a detail.</b> A document goes on having new versions
 * uploaded, and a region measured against one picture means nothing on the next. Every region
 * names the file its fractions were measured against, and only the ones naming the file being
 * looked at are drawn. Without that a region drawn on last year's scan would be painted over this
 * year's, in the right-looking place, with nothing to say it was never measured there.
 */

/** A region of this picture, and what it links to. */
export interface RegionHighlight {
  /** The member carrying the anchor — this region's identity within its link. */
  memberId: string;
  linkId: string;
  shortCode: string;
  relationCode: string | null;
  /** The relation as it should read on screen: a seeded code is translated, a custom one is not. */
  relationLabel: string | null;
  description: string | null;
  mayEdit: boolean;
  region: ImageRegion;
  anchorState: ResLinkAnchorState;
  targets: HighlightTarget[];
}

/** Whether a member is a region of this picture, measured against the file on screen. */
function isRegionOf(member: ResLinkMember, documentId: string, fileId: string): boolean {
  return (
    member.targetType === 'document'
    && member.targetId === documentId
    && member.anchorKind === 'imageRegion'
    && member.anchorFileId === fileId
  );
}

/**
 * Every region drawn on this picture.
 *
 * @param relationLabel how to render a relation code — the caller holds the translation function
 *   and the vocabulary, and this holds no opinion about either.
 */
export function regionHighlightsFrom(
  links: readonly ResLink[],
  documentId: string,
  fileId: string,
  relationLabel: (link: ResLink) => string | null,
): RegionHighlight[] {
  const highlights: RegionHighlight[] = [];

  for (const link of links) {
    for (const member of link.members) {
      if (!isRegionOf(member, documentId, fileId)) {
        continue;
      }

      const region = readRegion(member.anchor);
      if (region === null) {
        // Either the payload was withheld — the reader may not read this document's own member,
        // which can happen — or it is not a region this client can place. Drawing something at a
        // guessed position is the failure this module is arranged to avoid, so nothing is drawn.
        continue;
      }

      highlights.push({
        memberId: member.id,
        linkId: link.id,
        shortCode: link.shortCode,
        relationCode: link.relationType?.code ?? null,
        relationLabel: relationLabel(link),
        description: link.description,
        mayEdit: link.mayEdit,
        region,
        anchorState: member.anchorState,
        targets: link.members
          .filter((other) => other.id !== member.id)
          .map((other) => ({
            memberId: other.id,
            targetType: other.targetType,
            targetId: other.targetId,
            anchorKind: other.anchorKind,
            anchor: other.anchor,
            display: other.display,
            note: other.note,
          })),
      });
    }
  }

  // Largest first, so that a region drawn inside another is painted on top of it and is the one
  // a reader sees. Picking resolves the same overlap the other way round — the smallest shape
  // under the pointer wins — so the shape on top is the shape that answers, which is the only
  // pairing a reader can predict.
  return highlights.sort((a, b) => area(b.region) - area(a.region) || a.memberId.localeCompare(b.memberId));
}

/**
 * How much of the picture a region covers, in the normalised frame. Only ever compared against
 * another region of the same picture, so the frame's own anisotropy does not matter: a point is
 * the smallest thing there is, and everything else is ordered consistently.
 */
function area(region: ImageRegion): number {
  switch (region.shape) {
    case 'point':
      return 0;
    case 'rect':
      return region.w * region.h;
    case 'circle':
      return Math.PI * region.r * region.r;
    case 'polygon': {
      // The shoelace formula, unsigned: the enclosed area whichever way the outline was drawn.
      let sum = 0;
      for (let i = 0, j = region.points.length - 1; i < region.points.length; j = i++) {
        sum += region.points[j][0] * region.points[i][1] - region.points[i][0] * region.points[j][1];
      }
      return Math.abs(sum) / 2;
    }
  }
}
