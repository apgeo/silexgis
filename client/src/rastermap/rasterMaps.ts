// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ResLink } from '../api/hooks.ts';
import { isMapViewCode, type MapViewKind, viewKindOf } from './vocabulary.ts';

/**
 * The maps a survey model has declared on it, read out of the links the model already has.
 *
 * <b>There is no map table, and none is invented here.</b> A map is a document whose current
 * file is an image, and "this image is the plan view of this model" is a resource link under
 * one of the three seeded map-of codes, with the document as the main member and the model as
 * the other — which is what somebody authors when they declare a map. This reads that back.
 *
 * <b>The code AND the structure are both required, and the two checks guard against different
 * mistakes.</b> The code alone must not turn a mislabeled link into a map tab: an installation
 * can hang a map-of relation between any two things, and only a document-onto-this-model pair
 * is a map of this model. Structure alone must not promote either: a plain "documents" link
 * between the same pair is an annotation, not a declaration, and drawing a tab for it would
 * put words in its author's mouth.
 */

/** One declared map: a tab on every surface that shows the model. */
export interface RasterMapDeclaration {
  /** The declaration link — deleting it removes the map from the tab strip, nothing else. */
  linkId: string;
  /** The document whose current file is the map image. */
  documentId: string;
  viewKind: MapViewKind;
  /**
   * The document's title as the link's display carries it, or null when the reader may not
   * see the document member's display. A map is titled by its document — no duplication.
   */
  title: string | null;
  createdAt: string;
}

/** The anchors the model member of a declaration may carry: the whole model, or a coverage
 * statement ("this sheet is the northern branch"). A member anchored to a single station or
 * a station range *point* is not how coverage is declared, and a link shaped that way is not
 * a declaration. */
const COVERAGE_ANCHOR_KINDS = ['whole', 'modelSurvey', 'modelStationRange'] as const;

const VIEW_KIND_ORDER: Record<MapViewKind, number> = { plan: 0, profile: 1, other: 2 };

/**
 * Every map declared on this model, in the order the tabs show them: plan before profile
 * before other, then by title, then by age — deterministic without a stored ordering,
 * because links have no home for one.
 */
export function rasterMapsFromLinks(
  links: readonly ResLink[],
  surveyModelId: string,
): RasterMapDeclaration[] {
  const maps: RasterMapDeclaration[] = [];

  for (const link of links) {
    const code = link.relationType?.code;
    if (code === undefined || code === null || !isMapViewCode(code)) {
      continue;
    }

    // The structural half: a whole-document member (the image), and a member that is this
    // model under a coverage-shaped anchor. A map-of link onto a *different* model is that
    // model's map; a map-of link with no document in it declares nothing drawable.
    const documentMember = link.members.find(
      (member) => member.targetType === 'document' && member.anchorKind === 'whole',
    );
    const coversThisModel = link.members.some(
      (member) =>
        member.targetType === 'surveyModel'
        && member.targetId === surveyModelId
        && (COVERAGE_ANCHOR_KINDS as readonly string[]).includes(member.anchorKind),
    );
    if (documentMember === undefined || !coversThisModel) {
      continue;
    }

    maps.push({
      linkId: link.id,
      documentId: documentMember.targetId,
      viewKind: viewKindOf(code),
      title: documentMember.display?.title ?? null,
      createdAt: link.createdAt,
    });
  }

  return maps.sort(
    (a, b) =>
      VIEW_KIND_ORDER[a.viewKind] - VIEW_KIND_ORDER[b.viewKind]
      || (a.title ?? '').localeCompare(b.title ?? '')
      || a.createdAt.localeCompare(b.createdAt)
      || a.linkId.localeCompare(b.linkId),
  );
}
