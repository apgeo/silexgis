// SPDX-License-Identifier: AGPL-3.0-or-later
import { useCallback, useMemo, useState } from 'react';
import { Flex, Popover, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import { useResLinksForTarget, type FileInfo, type ResLink } from '../api/hooks.ts';
import { memberRoute } from '../components/reslinks/registry.ts';
import { relationPhraseFor } from '../components/reslinks/relations.ts';
import { displayableImageUrl } from '../components/documents/derivativeUrl.ts';
import { resolveDark } from '../theme.ts';
import { useUiPrefsStore } from '../stores/uiPrefsStore.ts';
import { useWorkspaceStore } from '../stores/workspaceStore.ts';
import { linkColors } from '../textlink/linkPalette.ts';
import { refFor, type HighlightTarget } from '../textlink/highlights.ts';
import LinkHoverCard from '../textlink/LinkHoverCard.tsx';
import { reveal } from '../viewlinks/viewTargets.ts';
import ImageRegionCanvas, { type DrawnRegion } from './ImageRegionCanvas.tsx';
import { regionHighlightsFrom, type RegionHighlight } from './regionHighlights.ts';

/**
 * A picture read beside the views its regions point into.
 *
 * The same joinery the annotated text has, and the same decision at the centre of it:
 * <b>following a region moves the views, it does not replace the page.</b> Somebody working
 * through a survey photograph beside a map wants the map to go to what the region names and wants
 * to still be looking at the photograph afterwards. Opening a target's own page is a separate
 * control in the card that says so.
 *
 * Every region on the picture is drawn, not only the one somebody arrived for: a reader who was
 * sent to one region and cannot see that there are three others has been told less than the
 * picture knows. The one they came for is drawn heavier; the rest are dimmed but live, and
 * clicking any of them opens its own card.
 */

export interface AnnotatedImageViewProps {
  documentId: string;
  file: FileInfo;
  /** The member to draw heavier — a reader arriving from a link chip somewhere else. */
  highlightedMemberId?: string | null;
  maxHeight?: number;
}

export default function AnnotatedImageView({
  documentId,
  file,
  highlightedMemberId = null,
  maxHeight,
}: AnnotatedImageViewProps) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const dark = useUiPrefsStore((s) => resolveDark(s.appearance.theme));
  const setSelection = useWorkspaceStore((s) => s.setSelection);
  const [openMemberId, setOpenMemberId] = useState<string | null>(null);

  const { data } = useResLinksForTarget('document', documentId);

  const relationLabel = useCallback(
    (link: ResLink) => relationPhraseFor(link, 'document', documentId, t),
    [documentId, t],
  );

  const highlights = useMemo(
    () => regionHighlightsFrom(data?.items ?? [], documentId, file.id, relationLabel),
    [data?.items, documentId, file.id, relationLabel],
  );

  const drawn: DrawnRegion[] = useMemo(
    () =>
      highlights.map((highlight) => {
        const colors = linkColors(highlight.relationCode, dark);
        return {
          id: highlight.memberId,
          region: highlight.region,
          stroke: colors.underline,
          fill: colors.background,
          label: highlight.relationLabel ?? undefined,
        };
      }),
    [dark, highlights],
  );

  const revealEverywhere = useCallback(
    (target: HighlightTarget) => {
      // Set first and unconditionally: the workspace selection is what the detail panel, the
      // history and the breadcrumb read, and it is not a fact about any view being open.
      if (target.targetType === 'feature') {
        setSelection({ kind: 'feature', featureId: target.targetId });
      }
      reveal(refFor(target));
    },
    [setSelection],
  );

  const onOpen = useCallback(
    (target: HighlightTarget, newWindow: boolean) => {
      const route = memberRoute(target.targetType, target.targetId, target.display);
      if (route === null) {
        return;
      }
      if (newWindow) {
        // `noopener` because the opened page belongs to this application and has no business
        // reaching back into the one that opened it.
        window.open(route, '_blank', 'noopener');
      } else {
        void navigate(route);
      }
    },
    [navigate],
  );

  const src = displayableImageUrl(file);
  if (src === null) {
    return null;
  }

  const open = highlights.find((highlight) => highlight.memberId === openMemberId) ?? null;

  const canvas = (
    <ImageRegionCanvas
      src={src}
      alt={file.originalName}
      regions={drawn}
      highlightedId={openMemberId ?? highlightedMemberId}
      maxHeight={maxHeight}
      onPick={(memberId) => {
        setOpenMemberId(memberId);
        // Following happens on the pick rather than from inside the card, so that the views move
        // while the reader is still looking at the picture — which is the behaviour the whole
        // feature exists for. The card then offers the narrower journeys.
        const picked = highlights.find((highlight) => highlight.memberId === memberId);
        for (const target of picked?.targets ?? []) {
          revealEverywhere(target);
        }
      }}
    />
  );

  return (
    <Flex vertical gap={8} align="start">
      <Popover
        open={open !== null}
        onOpenChange={(next) => {
          if (!next) {
            setOpenMemberId(null);
          }
        }}
        trigger="click"
        placement="rightTop"
        content={
          open === null ? null : (
            <LinkHoverCard
              highlights={[asCardHighlight(open)]}
              onReveal={revealEverywhere}
              onRevealIn={(target, address) => reveal(refFor(target), [address])}
              onOpen={onOpen}
            />
          )
        }
      >
        {canvas}
      </Popover>
      {highlights.length > 0 && (
        <Typography.Text type="secondary" style={{ fontSize: 12 }}>
          {t('imageLink.regionCount', { count: highlights.length })}
        </Typography.Text>
      )}
    </Flex>
  );
}

/**
 * The card is written against a passage of text, and every field it reads is one a region has
 * too — except the range, which it never draws and which a region has no equivalent of. Rather
 * than widening the card's own type to carry a member that means nothing to it, the region is
 * presented in the shape it already accepts, with the one text-only field filled in emptily.
 *
 * That is a deliberately small piece of dishonesty and it is contained here, in one function,
 * where it can be removed by giving the card a narrower prop the day a third kind of anchor
 * wants the same treatment.
 */
function asCardHighlight(highlight: RegionHighlight) {
  return {
    memberId: highlight.memberId,
    linkId: highlight.linkId,
    shortCode: highlight.shortCode,
    relationCode: highlight.relationCode,
    relationLabel: highlight.relationLabel,
    description: highlight.description,
    mayEdit: highlight.mayEdit,
    range: { start: 0, end: 0 },
    anchorState: highlight.anchorState,
    relocated: false,
    targets: highlight.targets,
  };
}
