// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useState } from 'react';
import {
  AimOutlined,
  ExportOutlined,
  LinkOutlined,
  SelectOutlined,
  WarningOutlined,
} from '@ant-design/icons';
import { Button, Divider, Dropdown, Flex, Space, Tooltip, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { targetTypeEntry, anchorSummary, memberRoute, linkPageRoute } from '../components/reslinks/registry.ts';
import type { ViewControlDescriptor } from '../viewlinks/resourceRef.ts';
import { canRevealHere, refreshRoster, subscribeToRoster } from '../viewlinks/viewTargets.ts';
import { highlightIsUncertain, refFor, type Highlight, type HighlightTarget } from './highlights.ts';

/**
 * What appears when the pointer rests on a passage: what it links to, and every way of going
 * there.
 *
 * <b>Three different journeys, and they are not the same button.</b> Following a passage moves
 * the views that are already on screen — the map pans, the scene flies — and leaves the reader
 * exactly where they were, which is the point of reading beside a map. *Opening* a target
 * replaces what is on screen with that target's own page, which is a different act and is
 * offered as its own control with its own "in a new window" variant. Conflating them is how a
 * reader loses the document they were reading by clicking what they thought was a highlight.
 *
 * <b>The roster is gathered when the card opens, not held.</b> Which views can be sent to
 * depends on which windows are open right now, and a list kept up to date by heartbeats would
 * cost every window a message a second to keep a menu nobody is looking at current — and still
 * be stale at the moment somebody looked.
 */

export interface LinkHoverCardProps {
  highlights: Highlight[];
  /** Send this target to every view that can show it. */
  onReveal: (target: HighlightTarget) => void;
  /** Send it to exactly one control. */
  onRevealIn: (target: HighlightTarget, address: string) => void;
  /** Open the target's own page — in this window, or a new one. */
  onOpen: (target: HighlightTarget, newWindow: boolean) => void;
  /** Edit this passage's link, where the reader may. Omitted outside edit mode. */
  onEdit?: (highlight: Highlight) => void;
  onDelete?: (highlight: Highlight) => void;
}

export default function LinkHoverCard({
  highlights,
  onReveal,
  onRevealIn,
  onOpen,
  onEdit,
  onDelete,
}: LinkHoverCardProps) {
  const { t } = useTranslation();
  const [controls, setControls] = useState<ViewControlDescriptor[]>([]);

  useEffect(() => {
    let alive = true;
    void refreshRoster().then((roster) => {
      if (alive) {
        setControls(roster);
      }
    });

    // A window opening or closing while the card is up changes the answer; the roster announces
    // itself on both, so the card follows without asking again.
    const unsubscribe = subscribeToRoster(() => {
      void refreshRoster().then((roster) => {
        if (alive) {
          setControls(roster);
        }
      });
    });

    return () => {
      alive = false;
      unsubscribe();
    };
  }, []);

  return (
    <Flex vertical gap={8} style={{ maxWidth: 340 }}>
      {highlights.map((highlight, index) => (
        <Flex vertical gap={6} key={highlight.memberId}>
          {index > 0 && <Divider style={{ margin: '2px 0' }} />}

          <Flex align="baseline" gap={6} wrap>
            <Typography.Text strong style={{ fontSize: 12 }}>
              {highlight.relationLabel ?? t('resLinks.relations.unspecified')}
            </Typography.Text>
            {highlightIsUncertain(highlight) && (
              // Said rather than hidden. A passage whose anchor no longer sits where it was
              // measured may be highlighting the wrong words, and a reader has no other way of
              // telling that from a passage that is exactly right.
              <Tooltip title={t('textLink.card.uncertainDetail')}>
                <Typography.Text type="warning" style={{ fontSize: 12 }}>
                  <WarningOutlined /> {t('textLink.card.uncertain')}
                </Typography.Text>
              </Tooltip>
            )}
          </Flex>

          {highlight.description && (
            <Typography.Text type="secondary" style={{ fontSize: 12 }}>
              {highlight.description}
            </Typography.Text>
          )}

          {highlight.targets.length === 0 ? (
            <Typography.Text type="secondary" style={{ fontSize: 12 }}>
              {t('textLink.card.noTargets')}
            </Typography.Text>
          ) : (
            highlight.targets.map((target) => (
              <TargetRow
                key={target.memberId}
                target={target}
                controls={controls}
                onReveal={onReveal}
                onRevealIn={onRevealIn}
                onOpen={onOpen}
              />
            ))
          )}

          <Space size={4}>
            <Button
              size="small"
              type="link"
              icon={<LinkOutlined />}
              href={linkPageRoute(highlight.shortCode)}
              style={{ paddingInline: 0 }}
            >
              {t('textLink.card.openLink')}
            </Button>
            {onEdit && highlight.mayEdit && (
              <Button size="small" type="link" onClick={() => onEdit(highlight)}>
                {t('common.edit')}
              </Button>
            )}
            {onDelete && highlight.mayEdit && (
              <Button size="small" type="link" danger onClick={() => onDelete(highlight)}>
                {t('resLinks.delete')}
              </Button>
            )}
          </Space>
        </Flex>
      ))}
    </Flex>
  );
}

function TargetRow({
  target,
  controls,
  onReveal,
  onRevealIn,
  onOpen,
}: {
  target: HighlightTarget;
  controls: ViewControlDescriptor[];
  onReveal: LinkHoverCardProps['onReveal'];
  onRevealIn: LinkHoverCardProps['onRevealIn'];
  onOpen: LinkHoverCardProps['onOpen'];
}) {
  const { t } = useTranslation();
  const entry = targetTypeEntry(target.targetType);
  const Icon = entry.icon;
  const anchorNote = anchorSummary(target.anchorKind, target.anchor, t);
  const route = memberRoute(target.targetType, target.targetId, target.display);

  // Only this window's controls can be asked whether they could show it — the answer is a
  // function, and functions do not cross a BroadcastChannel. A control in another window is
  // offered and answers for itself when the request arrives, which is the honest asymmetry:
  // the alternative is a round trip per remote control every time a card opens.
  const ableHere = canRevealHere(refFor(target));

  if (target.display === null) {
    // A target this reader may not read. Named as withheld and nothing more — no title, no note,
    // nothing that would narrow down what it is.
    return (
      <Typography.Text type="secondary" style={{ fontSize: 12 }}>
        <Icon /> {t('resLinks.restricted')}
      </Typography.Text>
    );
  }

  return (
    <Flex vertical gap={2}>
      <Flex align="baseline" gap={6} wrap>
        <Typography.Text style={{ fontSize: 13 }}>
          <Icon /> {target.display.title}
        </Typography.Text>
        {anchorNote && (
          <Typography.Text type="secondary" style={{ fontSize: 11 }}>
            {anchorNote}
          </Typography.Text>
        )}
      </Flex>

      {target.display.path && target.display.path.length > 0 && (
        <Typography.Text type="secondary" style={{ fontSize: 11 }}>
          {target.display.path.join(' › ')}
        </Typography.Text>
      )}

      <Space size={2} wrap>
        <Tooltip title={t('textLink.card.revealHint')}>
          <Button size="small" type="text" icon={<AimOutlined />} onClick={() => onReveal(target)}>
            {t('textLink.card.reveal')}
          </Button>
        </Tooltip>

        {controls.length > 0 && (
          <Dropdown
            menu={{
              items: controls.map((control) => ({
                key: control.address,
                label: t(control.labelKey) + (control.local ? '' : ` — ${t('textLink.card.otherWindow')}`),
                // Greyed only where this window knows the answer. A remote control is offered
                // because it is the one that can answer for itself.
                disabled: control.local === true && !ableHere.has(control.address),
                onClick: () => onRevealIn(target, control.address),
              })),
            }}
          >
            <Button size="small" type="text" icon={<SelectOutlined />}>
              {t('textLink.card.revealIn')}
            </Button>
          </Dropdown>
        )}

        {route !== null && (
          <>
            <Button size="small" type="text" onClick={() => onOpen(target, false)}>
              {t('textLink.card.open')}
            </Button>
            <Tooltip title={t('textLink.card.openNewWindow')}>
              <Button
                size="small"
                type="text"
                icon={<ExportOutlined />}
                aria-label={t('textLink.card.openNewWindow')}
                onClick={() => onOpen(target, true)}
              />
            </Tooltip>
          </>
        )}
      </Space>
    </Flex>
  );
}
