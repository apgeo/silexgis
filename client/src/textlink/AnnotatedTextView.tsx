// SPDX-License-Identifier: AGPL-3.0-or-later
import { Fragment, useCallback, useMemo, useRef, type CSSProperties, type ReactNode } from 'react';
import { Popover } from 'antd';
import {
  blockRuns,
  blockStarts,
  canonicalText,
  type AnnotatedBlock,
  type AnnotatedMarkKind,
} from './blocks.ts';
import type { Highlight } from './highlights.ts';
import { linkColors, stackedColors, type LinkColors } from './linkPalette.ts';
import { sliceBlock, type GlobalRange, type TextPiece } from './ranges.ts';
import { readStreamSelection, PIECE_OFFSET_ATTRIBUTE } from './selection.ts';
import './AnnotatedTextView.css';

/**
 * Text with its links drawn on it.
 *
 * The component knows nothing about where the text came from, what a map is, or what happens when
 * somebody follows a link. It is handed blocks and passages and it draws them; every decision
 * about what a click means is the caller's. That separation is what lets the same component be
 * mounted in a panel beside a map, in a window of its own, and — when the text comes off a PDF's
 * text layer instead of out of a stored body — over a page image, without any of those cases
 * being a branch in here.
 *
 * <b>Nothing user-written is ever put on the page as markup.</b> A block's text becomes a text
 * node and its emphasis becomes elements this file chose from a closed set. That is the same rule
 * the discussion panel keeps for the same reason — prose one member typed is shown to another —
 * and it is the reason the stored format is blocks of plain text rather than a fragment of HTML:
 * there is nothing here that could be tempted to interpret what somebody wrote.
 */

export interface AnnotatedTextViewProps {
  blocks: readonly AnnotatedBlock[];
  /** The passages to draw, already resolved against these blocks. */
  highlights: readonly Highlight[];
  /** Whether the interface is currently dark, for the wash the passages wear. */
  dark: boolean;
  /** Following a link: a passage was clicked, or activated from the keyboard. */
  onActivate?: (highlight: Highlight) => void;
  /** What to show while the pointer rests on a passage. Omitted means no card. */
  renderCard?: (highlights: Highlight[]) => ReactNode;
  /** The passage drawn as current — what a reader has just followed, or is editing. */
  activeMemberId?: string | null;
  /**
   * Edit mode. When set, selecting text calls this with the passage selected; the caller decides
   * whether that opens a form, and clicking a passage still activates it.
   */
  onSelectPassage?: (range: GlobalRange, quote: string) => void;
  /** Extra content drawn at the very top — a toolbar, a notice about the revision. */
  header?: ReactNode;
}

/** A run of consecutive pieces covered by exactly the same set of passages. */
interface PieceGroup {
  covering: Highlight[];
  pieces: TextPiece<Highlight>[];
}

/**
 * Merges consecutive pieces that carry the same passages.
 *
 * The slicing splits wherever *anything* changes, emphasis included, so one passage can arrive as
 * several pieces. They are grouped back together before drawing because a passage is one thing to
 * a reader: one focusable element, one hover card, one hover state. Leaving them separate would
 * put a card behind each fragment of a highlighted sentence and flicker it as the pointer crossed
 * a bold word in the middle.
 */
function groupPieces(pieces: TextPiece<Highlight>[]): PieceGroup[] {
  const groups: PieceGroup[] = [];
  let key: string | null = null;

  for (const piece of pieces) {
    const pieceKey = piece.covering.map((h) => h.memberId).join('|');
    const previous = groups.at(-1);
    if (previous !== undefined && pieceKey === key) {
      previous.pieces.push(piece);
    } else {
      groups.push({ covering: piece.covering, pieces: [piece] });
      key = pieceKey;
    }
  }

  return groups;
}

/** Emphasis, drawn from a closed set of elements chosen here — never from stored markup. */
function withMarks(text: string, marks: readonly AnnotatedMarkKind[]): ReactNode {
  let node: ReactNode = text;
  // Applied outermost-last so the nesting is stable whatever order the marks arrive in; two
  // renders of the same piece must produce the same tree or React re-creates the nodes and the
  // browser drops any selection inside them.
  for (const kind of ['code', 'u', 'i', 'b'] as const) {
    if (marks.includes(kind)) {
      node =
        kind === 'b' ? <strong>{node}</strong>
        : kind === 'i' ? <em>{node}</em>
        : kind === 'u' ? <u>{node}</u>
        : <code>{node}</code>;
    }
  }

  return node;
}

function styleFor(colors: LinkColors, active: boolean): CSSProperties {
  return {
    backgroundColor: active ? colors.hoverBackground : colors.background,
    // The line is what carries the relation's colour when the wash is too faint to read as one —
    // and it is what a reader who cannot distinguish the hues still sees as "something is here".
    boxShadow: `inset 0 -2px 0 0 ${colors.underline}`,
  };
}

export default function AnnotatedTextView({
  blocks,
  highlights,
  dark,
  onActivate,
  renderCard,
  activeMemberId,
  onSelectPassage,
  header,
}: AnnotatedTextViewProps) {
  const rootRef = useRef<HTMLDivElement>(null);

  const starts = useMemo(() => blockStarts(blocks), [blocks]);
  const runs = useMemo(() => blockRuns(blocks), [blocks]);
  const stream = useMemo(() => canonicalText(blocks), [blocks]);

  const colorsByMember = useMemo(() => {
    const map = new Map<string, LinkColors>();
    for (const highlight of highlights) {
      map.set(highlight.memberId, linkColors(highlight.relationCode, dark));
    }

    return map;
  }, [highlights, dark]);

  const onMouseUp = useCallback(() => {
    if (onSelectPassage === undefined || rootRef.current === null) {
      return;
    }

    const range = readStreamSelection(rootRef.current);
    if (range !== null) {
      onSelectPassage(range, stream.slice(range.start, range.end));
    }
  }, [onSelectPassage, stream]);

  const renderBlock = (index: number): ReactNode => {
    const block = blocks[index];
    const pieces = sliceBlock(block, starts[index], highlights, (h) => h.range);

    return groupPieces(pieces).map((group, groupIndex) => {
      const body = group.pieces.map((piece) => (
        <span key={piece.start} {...{ [PIECE_OFFSET_ATTRIBUTE]: piece.start }}>
          {withMarks(piece.text, piece.marks)}
        </span>
      ));

      if (group.covering.length === 0) {
        return <Fragment key={`${index}-${groupIndex}`}>{body}</Fragment>;
      }

      const colors = stackedColors(
        group.covering.map((h) => colorsByMember.get(h.memberId)!),
        dark,
      )!;
      const active = activeMemberId != null && group.covering.some((h) => h.memberId === activeMemberId);

      const mark = (
        <mark
          className="tl-passage"
          style={styleFor(colors, active)}
          // A passage is a link and is reached the way every other link is reached: by tabbing
          // to it and pressing a key. A span with a click handler is invisible to anyone not
          // using a mouse, which for a panel whose entire purpose is following links means the
          // feature simply does not exist for them.
          role="link"
          tabIndex={0}
          aria-label={labelFor(group.covering)}
          data-testid="tl-passage"
          data-member-id={group.covering[0].memberId}
          onClick={() => onActivate?.(group.covering[0])}
          onKeyDown={(event) => {
            if (event.key === 'Enter' || event.key === ' ') {
              event.preventDefault();
              onActivate?.(group.covering[0]);
            }
          }}
        >
          {body}
        </mark>
      );

      if (renderCard === undefined) {
        return <Fragment key={`${index}-${groupIndex}`}>{mark}</Fragment>;
      }

      return (
        <Popover
          key={`${index}-${groupIndex}`}
          content={() => renderCard(group.covering)}
          trigger={['hover', 'focus']}
          // Long enough that dragging the pointer across a paragraph of dense links does not
          // strobe cards, short enough to feel like an answer to resting on one.
          mouseEnterDelay={0.4}
          mouseLeaveDelay={0.2}
          placement="top"
          // The card carries buttons a reader moves the pointer onto, so it must not close the
          // moment the pointer leaves the words themselves.
          destroyOnHidden
        >
          {mark}
        </Popover>
      );
    });
  };

  return (
    <div className="tl-root" ref={rootRef} onMouseUp={onMouseUp}>
      {header}
      {runs.map((run, runIndex) => {
        if (run.list !== null) {
          const List = run.list === 'ol' ? 'ol' : 'ul';
          return (
            <List key={`run-${runIndex}`} className="tl-list">
              {run.indexes.map((index) => (
                <li key={index}>{renderBlock(index)}</li>
              ))}
            </List>
          );
        }

        const index = run.indexes[0];
        const Tag = TAGS[blocks[index].type];
        return (
          <Tag key={`run-${runIndex}`} className={`tl-block tl-${blocks[index].type}`}>
            {renderBlock(index)}
          </Tag>
        );
      })}
    </div>
  );
}

/**
 * The element each block kind is drawn as. A total record over the block types, so a kind added
 * on the server stops this compiling rather than rendering as an unstyled paragraph.
 */
const TAGS = {
  p: 'p',
  h1: 'h2',
  h2: 'h3',
  h3: 'h4',
  quote: 'blockquote',
  code: 'pre',
  // Reached only when a list item stands outside a run, which the runs above prevent; a total
  // record has to answer for it anyway.
  ul: 'p',
  ol: 'p',
} as const satisfies Record<AnnotatedBlock['type'], keyof HTMLElementTagNameMap>;

/**
 * What a screen reader says about a passage before its card is opened. Deliberately the relation
 * and what it points at, not "link": a reader tabbing through a paragraph needs to know which of
 * six passages this is.
 */
function labelFor(covering: readonly Highlight[]): string {
  return covering
    .map((highlight) => {
      const targets = highlight.targets
        .map((target) => target.display?.title)
        .filter((title): title is string => Boolean(title));
      return [highlight.relationLabel, ...targets].filter(Boolean).join(': ');
    })
    .filter((part) => part.length > 0)
    .join('; ');
}
