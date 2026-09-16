// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo, useState } from 'react';
import { CloseOutlined, EyeInvisibleOutlined, SwapOutlined, TeamOutlined } from '@ant-design/icons';
import { Button, Switch, Tag, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { shortNameOf } from '../../caveview/modelParts.ts';
import {
  teamStation,
  trackedCaverTeams,
  type TrackedCaver,
  type TrackedCaverPosition,
} from '../../caveview/trackedCavers.ts';
import { useIsMobile } from '../../hooks/useIsMobile.ts';
import './CaveViewTrackingOverlay.css';

/**
 * A place in the model the list is pointing at, so whoever owns the viewer can show it.
 *
 * <b>The kind is part of the identity, not decoration.</b> A team of one and its only member name
 * the same station, and without the kind the list could not tell which of the two rows a reader
 * pressed — so pressing the heading again would fail to take the mark off, and the other row would
 * light up instead.
 */
export interface TrackedPlace {
  kind: 'caver' | 'team';
  /** A caver id, or a team id — null for the group of everybody on no team. */
  id: string | null;
  /** The station to fly to, as the watch spells it. */
  station: string;
}

/** Whether two answers from the list are the same one. */
function samePlace(left: TrackedPlace | null, right: TrackedPlace | null): boolean {
  return left !== null && right !== null && left.kind === right.kind && left.id === right.id;
}

export interface CaveViewTrackingOverlayProps {
  /** Everybody on the watch — including those no marker could be drawn for. */
  cavers: readonly TrackedCaver[];
  /**
   * Whether each marker's label carries the time of its last report beside the name.
   *
   * <b>Beside the name rather than on a line the pointer reveals.</b> A marker collapsed with
   * others has no such line — the viewer has nowhere to put one — so a switch that put the time
   * there would have meant two different things depending on whether anybody else happened to be
   * standing at the same station, which is a thing the reader neither chose nor can see.
   */
  showTimes: boolean;
  onShowTimesChange(showTimes: boolean): void;
  /**
   * Whether the markers say who they are — the names on the model, not the markers themselves.
   *
   * <b>Offered to every reader, including the ones who may not write a thing.</b> The published
   * trip page and the embed mount this read-only, and this control is still theirs: it takes text
   * off their own screen and nothing else. Nothing is hidden by it that they were being shown for
   * a reason — everybody on the watch is in the list either way, withheld positions included —
   * and a stranger reading a live trip on a phone is exactly who a model crowded by a party at one
   * station happens to, so withholding the way to clear it would be withholding it from the reader
   * who needs it most.
   */
  showLabels: boolean;
  onShowLabelsChange(showLabels: boolean): void;
  /** Whose card is open, or null when none is. Owned by the panel, which the viewer talks to. */
  openCaverId: string | null;
  onOpenCaver(caverId: string | null): void;
  /**
   * Which row the camera was last sent to, or null. Owned by the panel for the same reason the
   * open card is: the move itself is a call on the viewer, which only the panel holds.
   */
  shown: TrackedPlace | null;
  onShow(place: TrackedPlace | null): void;
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
 *
 * <b>A row is also the way to the place itself.</b> Reading a station name tells somebody who knows
 * the cave where a caver is; on a model of two hundred stations it tells nobody else anything. So
 * pressing a row sends the camera there and marks the station, and pressing it again takes the mark
 * off — which is the only dismissal there is, since nothing else in the scene knows the list made
 * it. A row for somebody with no station to fly to clears the mark rather than leaving it standing
 * on the person pressed before, because a mark left on the wrong person is worse than no mark.
 *
 * <b>The team heading is a row of the same kind.</b> A party is organised in teams and moves in
 * them, so "where is the second team" is the question asked at least as often as "where is Ana" —
 * and the heading is where somebody already looks for it.
 */
export default function CaveViewTrackingOverlay({
  cavers,
  showTimes,
  onShowTimesChange,
  showLabels,
  onShowLabelsChange,
  openCaverId,
  onOpenCaver,
  shown,
  onShow,
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

  const groups = useMemo(() => trackedCaverTeams(cavers), [cavers]);
  // Headings are drawn only where they say something. A trip whose party was never divided into
  // teams would otherwise gain one heading reading "No team" above the whole list, which is a line
  // of the phone's screen spent restating that there is nothing to say.
  const grouped = groups.some((group) => group.teamId !== null);

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
      // A place is known and belongs to another survey. Drawn as its own tag rather than as the
      // dash next door: the dash means nobody has said where this person is, which of all the
      // wrong things this row could say is the one a reader would act on.
      case 'otherModel':
        return (
          <Tag icon={<SwapOutlined />} data-testid="caveview-position-other-model">
            {t('caveview.tracking.positionOtherModel')}
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
      case 'otherModel':
        return t('caveview.tracking.positionOtherModel');
      case 'unreported':
        return t('caveview.tracking.positionUnreported');
    }
  };

  /** Pressing a person: their card, and the place the camera is sent to. */
  const onPressCaver = (caver: TrackedCaver) => {
    const wasOpen = caver.caverId === openCaverId;
    onOpenCaver(wasOpen ? null : caver.caverId);
    const place: TrackedPlace | null =
      caver.position.kind === 'station'
        ? { kind: 'caver', id: caver.caverId, station: caver.position.station }
        : null;
    onShow(wasOpen || place === null || samePlace(shown, place) ? null : place);
  };

  const caverRow = (caver: TrackedCaver) => {
    const here: TrackedPlace | null =
      caver.position.kind === 'station'
        ? { kind: 'caver', id: caver.caverId, station: caver.position.station }
        : null;
    const marked = samePlace(shown, here);
    return (
      <Button
        key={caver.caverId}
        type="text"
        size="small"
        className={`caveview-tracking-person${marked ? ' caveview-tracking-marked' : ''}`}
        aria-pressed={caver.caverId === openCaverId}
        // Said as well as drawn: the mark is a colour, and a colour is not an answer to "which one
        // is the model showing me" for a reader who cannot see it.
        aria-current={marked ? 'true' : undefined}
        onClick={() => onPressCaver(caver)}
        data-testid={`caveview-caver-${caver.caverId}`}
      >
        <span className="caveview-tracking-person-name">{caver.name}</span>
        {/* Said on the row and not only inside the card, so that the one place a reader can see
            the whole party at once answers the question they came with. Out-ness reached only by
            opening each card in turn is out-ness nobody checks on a crowded station — and this is
            the surface somebody reads when they are deciding whether a party is still underground.
            It is the same word the card uses and the same word the markers carry, so the model,
            the list and the card do not each have their own way of saying it. */}
        {caver.out && (
          <span
            className="caveview-tracking-person-out"
            data-testid={`caveview-row-out-${caver.caverId}`}
          >
            {t('caveview.tracking.out')}
          </span>
        )}
        <span className="caveview-tracking-person-place">{shortPlace(caver.position)}</span>
      </Button>
    );
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
            {/* The two switches are a pair and are drawn as one: the first decides whether the
                markers say anything, the second what they say. In that order, because the second
                is a refinement of the first — and it is turned off with it, since a time nothing
                is drawing is a switch that answers a press with no change anybody can see. */}
            <label className="caveview-tracking-switch">
              <Typography.Text style={{ fontSize: 12 }}>
                {t('caveview.tracking.showLabels')}
              </Typography.Text>
              <Switch
                size="small"
                checked={showLabels}
                onChange={onShowLabelsChange}
                data-testid="caveview-tracking-labels"
              />
            </label>
            <label className="caveview-tracking-switch">
              <Typography.Text style={{ fontSize: 12 }} disabled={!showLabels}>
                {t('caveview.tracking.showTimes')}
              </Typography.Text>
              <Switch
                size="small"
                checked={showTimes}
                disabled={!showLabels}
                onChange={onShowTimesChange}
                data-testid="caveview-tracking-times"
              />
            </label>
            <div className="caveview-tracking-people">
              {grouped
                ? groups.map((group) => {
                    const station = teamStation(group.members);
                    const here: TrackedPlace | null =
                      station === null ? null : { kind: 'team', id: group.teamId, station };
                    const marked = samePlace(shown, here);
                    return (
                      <div className="caveview-tracking-group" key={group.teamId ?? 'no-team'}>
                        <Button
                          type="text"
                          size="small"
                          className={`caveview-tracking-team${marked ? ' caveview-tracking-marked' : ''}`}
                          // A heading for a team none of whose members has been placed is drawn and
                          // is not pressable: there is no station to fly to, and a heading that
                          // answered a press with nothing would read as a viewer that had stopped
                          // working rather than as a party nobody has reported yet.
                          disabled={here === null}
                          aria-current={marked ? 'true' : undefined}
                          onClick={() => onShow(marked || here === null ? null : here)}
                          data-testid={`caveview-team-${group.teamId ?? 'none'}`}
                        >
                          <span className="caveview-tracking-person-name">
                            {group.title ?? t('caveview.tracking.noTeam')}
                          </span>
                          <span className="caveview-tracking-person-place">
                            {station === null ? '—' : shortNameOf(station)}
                          </span>
                        </Button>
                        {group.members.map(caverRow)}
                      </div>
                    );
                  })
                : cavers.map(caverRow)}
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
              {t('caveview.tracking.cardPosition')}
            </span>
            <span className="caveview-tracking-card-value">{fullPlace(open.position)}</span>
          </div>
          {/* <b>When the place above was reported, which is not the same question as the row under
              it.</b> The last word moves whenever anybody says anything at all about somebody — a
              radio check, a note, coming out — and none of those says where they are. The station
              keeps the moment of the report that named it, and the two are routinely hours apart: a
              card that showed only the first would date a station heard at noon as eight minutes
              old, on the surface somebody reads while deciding whether a team is overdue.

              <b>The pair is adjacent and the other moment is below both, which is the layout doing
              the work the wording cannot do alone.</b> A time drawn immediately above a station is
              read as dating it whatever its label says, so the moment that dates nothing sits at
              the foot of the card rather than over the place — and the two rows that do belong
              together, the place and the moment that placed it, are neighbours.

              A moment is taken only where a place was actually drawn. A position nobody reported
              and one this reader may not be told both read as silence here, and neither is filled
              in from the last word — the repair that would put this defect straight back. */}
          <div className="caveview-tracking-card-row">
            <span className="caveview-tracking-card-label">
              {t('caveview.tracking.cardPositionAt')}
            </span>
            <span
              className="caveview-tracking-card-value"
              data-testid="caveview-caver-card-position-at"
            >
              {when(
                open.position.kind === 'station' || open.position.kind === 'depth'
                  ? open.positionAt
                  : null,
              )}
            </span>
          </div>
          {/* <b>The last word, named for what it is rather than for being the latest thing.</b>
              "Last update" is the phrasing a reader binds to whatever is beside it, and what was
              beside it was the station — so the card kept saying, in the one place a coordinator
              looks to check how fresh a position is, the very sentence the position's own moment
              exists to stop. It is worded here as the tab and the followed page word it, because
              those two and this card are three drawings of one watch and a reader who moves between
              them should not have to work out that three labels mean one thing. */}
          <div className="caveview-tracking-card-row">
            <span className="caveview-tracking-card-label">
              {t('caveview.tracking.cardLastHeard')}
            </span>
            <span
              className="caveview-tracking-card-value"
              data-testid="caveview-caver-card-last-heard"
            >
              {when(open.lastRecordedAt)}
            </span>
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
          {/* Said at length for the same reason the withholding is: the short tag names the
              condition, and the card is where there is room to say that the report exists, that the
              survey it was made in is not the one on screen, and therefore why no marker is drawn
              for somebody whose place somebody does know. */}
          {open.position.kind === 'otherModel' && (
            <Typography.Text type="secondary" style={{ fontSize: 11 }}>
              {t('caveview.tracking.positionOtherModelDetail')}
            </Typography.Text>
          )}
        </div>
      )}
    </div>
  );
}
