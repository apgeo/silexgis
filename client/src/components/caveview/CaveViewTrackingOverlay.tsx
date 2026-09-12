// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { CloseOutlined, EyeInvisibleOutlined, TeamOutlined } from '@ant-design/icons';
import { Button, Switch, Tag, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { shortNameOf } from '../../caveview/modelParts.ts';
import type { TrackedCaver, TrackedCaverPosition } from '../../caveview/trackedCavers.ts';
import { useIsMobile } from '../../hooks/useIsMobile.ts';
import './CaveViewTrackingOverlay.css';

export interface CaveViewTrackingOverlayProps {
  /** Everybody on the watch — including those no marker could be drawn for. */
  cavers: readonly TrackedCaver[];
  /** Whether each marker carries the time of its last report as its second line. */
  showTimes: boolean;
  onShowTimesChange(showTimes: boolean): void;
  /** Whose card is open, or null when none is. Owned by the panel, which the viewer talks to. */
  openCaverId: string | null;
  onOpenCaver(caverId: string | null): void;
  /** Lifted clear of a toolbar placed against the same edge. */
  raised?: boolean;
}

/**
 * Who is where, over the model.
 *
 * <b>Every marker has a tap path.</b> The list is not a second way of saying what the markers
 * already say — it is the only way to reach a caver's details on a device with no hovering
 * pointer, where the marker hover this card also opens never happens. So everybody on the watch
 * is listed, whether or not a marker could be drawn for them, and a row opens exactly the card a
 * hover does.
 *
 * <b>A position that was withheld is listed as withheld.</b> Somebody whose position this reader
 * may not be told has no marker — there is nowhere to put one — and leaving them out of the list
 * as well would turn a withholding into an absence, which reads as nobody knowing where they are.
 */
export default function CaveViewTrackingOverlay({
  cavers,
  showTimes,
  onShowTimesChange,
  openCaverId,
  onOpenCaver,
  raised = false,
}: CaveViewTrackingOverlayProps) {
  const { t, i18n } = useTranslation();
  const narrow = useIsMobile();
  // Null until somebody says otherwise, so the list follows the width it is opened at — a list of
  // names takes a third of a phone screen, and on one it is folded behind its own count.
  const [expanded, setExpanded] = useState<boolean | null>(null);
  const listOpen = expanded ?? !narrow;

  const open = cavers.find((caver) => caver.caverId === openCaverId) ?? null;
  const when = (value: string | null) =>
    value === null ? '—' : new Date(value).toLocaleString(i18n.language);

  /** The short form for a list row: enough to recognise, never wide enough to push the name out. */
  const shortPlace = (position: TrackedCaverPosition) => {
    switch (position.kind) {
      case 'station':
        return shortNameOf(position.station);
      case 'depth':
        return t('trips.metres', { value: position.depthM });
      case 'withheld':
        return (
          <Tag
            icon={<EyeInvisibleOutlined />}
            data-testid={
              position.certain
                ? 'caveview-position-withheld'
                : 'caveview-position-maybe-withheld'
            }
          >
            {t(
              position.certain
                ? 'caveview.tracking.positionWithheld'
                : 'caveview.tracking.positionMaybeWithheld',
            )}
          </Tag>
        );
      case 'unreported':
        return '—';
    }
  };

  /** The same answer at length, where there is room to say why an absence is not an absence. */
  const fullPlace = (position: TrackedCaverPosition) => {
    switch (position.kind) {
      case 'station':
        return position.station;
      case 'depth':
        return t('trips.metres', { value: position.depthM });
      case 'withheld':
        return t(
          position.certain
            ? 'caveview.tracking.positionWithheld'
            : 'caveview.tracking.positionMaybeWithheld',
        );
      case 'unreported':
        return t('caveview.tracking.positionUnreported');
    }
  };

  // Said on the element rather than left to a `:has()` in the stylesheet, so what the layout
  // branches on is a fact this component states and a test can read back: on a screen too short
  // to hold the list and a card at once, the card is what the reader asked for.
  const classes = [
    'caveview-tracking',
    raised ? 'caveview-tracking-raised' : '',
    open !== null ? 'caveview-tracking-carded' : '',
  ].filter(Boolean);

  return (
    <div className={classes.join(' ')} data-testid="caveview-tracking">
      {/* Reversed in the stylesheet: the box is the anchor and the card grows up out of it. */}
      <div className="caveview-tracking-box">
        <Button
          type="text"
          size="small"
          icon={<TeamOutlined />}
          aria-expanded={listOpen}
          onClick={() => setExpanded(!listOpen)}
          data-testid="caveview-tracking-toggle"
        >
          {t('caveview.tracking.people', { count: cavers.length })}
        </Button>
        {listOpen && (
          <>
            <label className="caveview-tracking-switch">
              <Typography.Text style={{ fontSize: 12 }}>
                {t('caveview.tracking.showTimes')}
              </Typography.Text>
              <Switch
                size="small"
                checked={showTimes}
                onChange={onShowTimesChange}
                data-testid="caveview-tracking-times"
              />
            </label>
            <div className="caveview-tracking-people">
              {cavers.map((caver) => (
                <Button
                  key={caver.caverId}
                  type="text"
                  size="small"
                  className="caveview-tracking-person"
                  aria-pressed={caver.caverId === openCaverId}
                  onClick={() =>
                    onOpenCaver(caver.caverId === openCaverId ? null : caver.caverId)
                  }
                  data-testid={`caveview-caver-${caver.caverId}`}
                >
                  <span className="caveview-tracking-person-name">{caver.name}</span>
                  <span className="caveview-tracking-person-place">
                    {shortPlace(caver.position)}
                  </span>
                </Button>
              ))}
            </div>
          </>
        )}
      </div>

      {open !== null && (
        <div
          className="caveview-tracking-card"
          // In the tab order and closed by Escape, like every other dismissible surface drawn
          // over a scene here. Not focused automatically: the card opens from a pointer resting
          // on a marker, and pulling focus out from under somebody looking around the model
          // would be worse than making them press Tab.
          tabIndex={0}
          role="group"
          aria-label={open.name}
          onKeyDown={(event) => {
            if (event.key === 'Escape') {
              event.stopPropagation();
              onOpenCaver(null);
            }
          }}
          data-testid="caveview-caver-card"
        >
          <div className="caveview-tracking-card-head">
            <Typography.Text strong ellipsis={{ tooltip: open.name }}>
              {open.name}
            </Typography.Text>
            <Button
              type="text"
              size="small"
              icon={<CloseOutlined />}
              aria-label={t('caveview.tracking.close')}
              onClick={() => onOpenCaver(null)}
              data-testid="caveview-caver-card-close"
            />
          </div>
          {open.out && <Tag data-testid="caveview-caver-out">{t('caveview.tracking.out')}</Tag>}
          <div className="caveview-tracking-card-row">
            <span className="caveview-tracking-card-label">{t('caveview.tracking.cardTeam')}</span>
            <span className="caveview-tracking-card-value">
              {open.teamTitle ?? t('caveview.tracking.noTeam')}
            </span>
          </div>
          <div className="caveview-tracking-card-row">
            <span className="caveview-tracking-card-label">
              {t('caveview.tracking.cardEntered')}
            </span>
            <span className="caveview-tracking-card-value">{when(open.enteredAt)}</span>
          </div>
          <div className="caveview-tracking-card-row">
            <span className="caveview-tracking-card-label">
              {t('caveview.tracking.cardLastUpdate')}
            </span>
            <span className="caveview-tracking-card-value">{when(open.lastRecordedAt)}</span>
          </div>
          <div className="caveview-tracking-card-row">
            <span className="caveview-tracking-card-label">
              {t('caveview.tracking.cardPosition')}
            </span>
            <span className="caveview-tracking-card-value">{fullPlace(open.position)}</span>
          </div>
          {open.position.kind === 'withheld' && (
            <Typography.Text type="secondary" style={{ fontSize: 11 }}>
              {t(
                open.position.certain
                  ? 'caveview.tracking.positionWithheldDetail'
                  : 'caveview.tracking.positionMaybeWithheldDetail',
              )}
            </Typography.Text>
          )}
        </div>
      )}
    </div>
  );
}
