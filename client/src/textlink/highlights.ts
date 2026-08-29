// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ResLink, ResLinkAnchorState, ResLinkMember, ResLinkTargetDisplay } from '../api/hooks.ts';
import type { ResourceRef } from '../viewlinks/resourceRef.ts';
import { locateAnchor, type GlobalRange, type TextRangeAnchorPayload } from './ranges.ts';

/**
 * Turning the links a document participates in into the passages to paint.
 *
 * There is no annotation table behind this and there was never going to be one. A resource link
 * already relates any number of resources under a relation, and a member already says which part
 * of its target it means — for a document, a passage of the extracted text. So a highlight is a
 * member of a link whose target is *this* document with a text-range anchor, and what the
 * highlight points at is simply the link's other members. Everything that follows from that comes
 * free: a passage can point at several things at once because a link has many members; the same
 * passage can carry two links because two links may overlap; who may see each end is already
 * decided by that end's own rules, and a member the reader may not see arrives with no display
 * and is drawn as such rather than disappearing and leaving a hole in the sentence.
 */

/** One end of a link, other than the passage itself. */
export interface HighlightTarget {
  memberId: string;
  targetType: string;
  targetId: string;
  anchorKind: string;
  anchor: unknown;
  /**
   * What may be said about it — null when the reader may not read the target at all. Null is
   * rendered as a neutral entry rather than being dropped: a link that quietly showed fewer
   * targets to some readers would look to them like a link somebody built wrong.
   */
  display: ResLinkTargetDisplay | null;
  note: string | null;
}

/** A passage of this document, and what it links to. */
export interface Highlight {
  /** The member carrying the anchor — the identity of this passage within its link. */
  memberId: string;
  linkId: string;
  shortCode: string;
  relationCode: string | null;
  /** The relation as it should read on screen: a seeded code is translated, a custom one is not. */
  relationLabel: string | null;
  description: string | null;
  mayEdit: boolean;
  /** Where the passage is in the stream as it stands now. */
  range: GlobalRange;
  /** What the server said about how well the anchor still fits. */
  anchorState: ResLinkAnchorState;
  /**
   * True when the stored offsets did not name the quote and it had to be found again here.
   * Worth knowing separately from the server's own answer: the server re-measures when *it*
   * rewrites the body, so this being true means the reader is looking at a revision the anchor
   * was never measured against.
   */
  relocated: boolean;
  targets: HighlightTarget[];
}

function readAnchor(anchor: unknown): TextRangeAnchorPayload | null {
  if (typeof anchor !== 'object' || anchor === null) {
    return null;
  }

  const value = anchor as Record<string, unknown>;
  if (
    typeof value.start !== 'number'
    || typeof value.end !== 'number'
    || typeof value.quote !== 'string'
    || value.quote.length === 0
  ) {
    return null;
  }

  return {
    start: value.start,
    end: value.end,
    quote: value.quote,
    prefix: typeof value.prefix === 'string' ? value.prefix : undefined,
    suffix: typeof value.suffix === 'string' ? value.suffix : undefined,
  };
}

/** Whether a member is a passage of this document. */
function isPassageOf(member: ResLinkMember, documentId: string): boolean {
  return (
    member.targetType === 'document'
    && member.targetId === documentId
    && member.anchorKind === 'textRange'
  );
}

/**
 * Every passage of this document, in stream order.
 *
 * @param relationLabel how to render a relation code — the caller holds the translation
 *   function and the vocabulary, and this holds no opinion about either.
 */
export function highlightsFrom(
  links: readonly ResLink[],
  documentId: string,
  stream: string,
  relationLabel: (link: ResLink) => string | null,
): Highlight[] {
  const highlights: Highlight[] = [];

  for (const link of links) {
    for (const member of link.members) {
      if (!isPassageOf(member, documentId)) {
        continue;
      }

      const anchor = readAnchor(member.anchor);
      if (anchor === null) {
        // The payload was withheld — the reader may not read this document's own member, which
        // can happen — or is not a text range. Either way there is no passage to draw, and
        // drawing one at a guessed position is the failure this whole module is arranged to
        // avoid.
        continue;
      }

      const range = locateAnchor(stream, anchor);
      if (range === null) {
        // The words are not in the text on screen. The link still exists and is still listed
        // wherever links are listed; what cannot honestly happen is highlighting something.
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
        range,
        anchorState: member.anchorState,
        relocated: range.start !== anchor.start,
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

  // Stream order, then longest first where two begin together. Only the order the hover card
  // lists them in depends on this — the painting is a partition and does not care — but a list
  // that reordered itself between renders is a list nobody can click reliably.
  return highlights.sort(
    (a, b) => a.range.start - b.range.start || b.range.end - a.range.end || a.memberId.localeCompare(b.memberId),
  );
}

/**
 * What a target means to a view control. The same vocabulary a link member speaks, because a
 * navigation event is a link member — see the reference type's own note.
 */
export function refFor(target: HighlightTarget): ResourceRef {
  return {
    targetType: target.targetType,
    targetId: target.targetId,
    anchorKind: target.anchorKind,
    anchor: target.anchor,
    label: target.display?.title,
  };
}

/**
 * Whether a passage is worth a warning: the anchor no longer sits where it was measured, either
 * because the document has moved on under it or because this reader is looking at a revision it
 * was never measured against.
 */
export function highlightIsUncertain(highlight: Highlight): boolean {
  return highlight.relocated || highlight.anchorState !== 'exact';
}
