// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useMemo, useState, type ReactNode } from 'react';
import {
  CaretRightOutlined,
  HistoryOutlined,
  LeftOutlined,
  PauseOutlined,
  PictureOutlined,
  RightOutlined,
} from '@ant-design/icons';
import { Alert, Button, ConfigProvider, Flex, Segmented, Slider, Spin, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import type { TrackingEvent, TrackingState } from '../../api/hooks.ts';
import {
  noteAfter,
  noteAt,
  noteBefore,
  picturesAt,
  replayNotes,
  replayWindow,
  type ReplayPicture,
} from '../../caveview/trackingReplay.ts';
import {
  COARSE_SLIDER,
  REPLAY_SPEEDS,
  useReplayClock,
} from '../../caveview/useReplayClock.ts';
import { useCoarsePointer } from '../../hooks/useCoarsePointer.ts';
import { useIsMobile } from '../../hooks/useIsMobile.ts';
import './TrackingReplayBar.css';

export interface TrackingReplayBarProps {
  /** The watch: the stretch of time it spans, and what it says about positions being withheld. */
  tracking: TrackingState;
  /** The <b>whole</b> log, newest first, or undefined while it is still being followed. */
  events: readonly TrackingEvent[] | undefined;
  /** True while the pages are still being followed. Nothing is replayed over a partial log. */
  loading: boolean;
  /** Set when the log could not be read at all. */
  failed: boolean;
  /** Whether the panel is showing a replayed moment instead of the live watch. */
  engaged: boolean;
  onEngagedChange(engaged: boolean): void;
  /** The moment being replayed, or null until one has been settled on. */
  at: number | null;
  onAtChange(at: number): void;
  /** What the trip's roster calls a caver, for the name against a note. */
  nameOf(caverId: string): string;
  /**
   * The photographs hung on this trip's moments, oldest first. They widen the window as well as
   * marking it: a camera's clock is nobody's clock, so a picture routinely falls outside the
   * stretch the reports cover, and a scrubber that could not reach it would be a replay with a
   * photograph missing from it.
   */
  pictures?: readonly ReplayPicture[];
  /** Offers to hang a picture on the moment on screen, where the reader may write to the log. */
  onAttachHere?(at: number): void;
}

/**
 * The trip, played back over its own log.
 *
 * <b>It answers a question the live watch cannot.</b> The watch is where everybody is now, folded by
 * the server and holding nothing of how they got there; the log is the only record that anybody was
 * ever anywhere else. So a coordinator who joins late, or anybody reading a trip afterwards, can
 * wind the party back through the places that were actually radioed out — the markers slide between
 * reported stations, which is exactly what they do live, because it is the same viewer being handed
 * the same shape.
 *
 * <b>Nothing here writes anything.</b> A replay is a second reading of reports already recorded; it
 * cannot record, delete or correct one, and leaving it puts the live watch back on the model
 * untouched.
 *
 * <b>A partial log is refused rather than replayed.</b> The reports arrive newest first, so a first
 * page on its own is the end of the trip: a replay over it would open with the party already
 * underground at places nothing on screen says they walked to, and would animate that fiction
 * confidently. Until every page is in, this offers a wait; if the read fails, it says so and offers
 * nothing.
 */
export default function TrackingReplayBar({
  tracking,
  events,
  loading,
  failed,
  engaged,
  onEngagedChange,
  at,
  onAtChange,
  nameOf,
  pictures = [],
  onAttachHere,
}: TrackingReplayBarProps) {
  const { t, i18n } = useTranslation();
  const narrow = useIsMobile();
  const coarse = useCoarsePointer();
  // Where a live trip's window ends. Taken once, when the replay is opened, rather than read on
  // every render: an end that crept forward with the real clock would move under the handle
  // somebody is dragging, and would make the last second of a live trip unreachable by definition.
  const [openedAt, setOpenedAt] = useState(() => Date.now());

  const span = useMemo(
    () => (events === undefined ? null : replayWindow(tracking, events, openedAt, pictures)),
    [tracking, events, openedAt, pictures],
  );
  const notes = useMemo(
    () => (events === undefined || span === null ? [] : replayNotes(events, span)),
    [events, span],
  );
  const marks = useMemo(() => {
    const built: Record<number, { label: ReactNode }> = {};
    for (const note of notes) {
      // Drawn as a dot and nothing else. A timestamp per mark is unreadable on a phone the moment
      // there are three of them; what a reader lands on a mark *with* is the pair of step buttons
      // below, which are labelled, reachable from a keyboard and sized for a finger.
      built[note.at] = { label: <span className="tracking-replay-mark" aria-hidden="true" /> };
    }
    for (const picture of pictures) {
      // Only the ones the rail actually covers. The window holds every picture whose clock is
      // plausibly this trip's, and deliberately refuses to stretch itself around one whose camera
      // clock was never set — a mark for such a picture would be drawn off the end of the rail,
      // where it is a dot the handle can never be brought to. The trip's own list of photographs
      // is where every one of them is reachable, whatever its file claims.
      if (span === null || picture.at < span.from || picture.at > span.to) {
        continue;
      }
      // A moment carrying both words and a photograph gets the picture's mark, because the picture
      // is the rarer thing and the one a reader is scrubbing to find. The note is still read out
      // under the rail when the handle lands there, so nothing is lost by the mark saying the less
      // common of the two.
      built[picture.at] = {
        label: <span className="tracking-replay-mark tracking-replay-mark-picture" aria-hidden="true" />,
      };
    }
    return built;
  }, [notes, pictures, span]);

  // Held still across renders on purpose: a fresh object here is a fresh theme five times a second
  // while the replay plays, and every one of those has the whole slider's styles derived again.
  const sliderTheme = useMemo(
    () => ({ components: { Slider: coarse ? COARSE_SLIDER : {} } }),
    [coarse],
  );

  // The clock itself, shared with the published archive's own replay: one engine, two strips.
  const transport = useReplayClock({ span, at, onAtChange, engaged });

  // The window a live trip is replayed over ends when somebody asked for the replay, not when the
  // model was opened — those can be hours apart on a tab left standing.
  useEffect(() => {
    if (engaged) {
      setOpenedAt(Date.now());
    }
  }, [engaged]);

  // A replay opens at the start of the trip: that is the only moment that needs no explanation.
  useEffect(() => {
    if (engaged && at === null && span !== null) {
      onAtChange(span.from);
    }
  }, [engaged, at, span, onAtChange]);

  // A watch that was never armed has no stretch of time to play, so nothing is offered for one.
  if (tracking.armedAt === null) {
    return null;
  }

  /**
   * How big every control on this strip is drawn, chosen on the pointer and on nothing else.
   *
   * Each of them — play, the speed, the way out, the steps between notes — is a thing somebody
   * presses, and not one of them depends on how much room there is: a phone in landscape has the
   * width of a desk and still nothing on it that can hit a twenty-four-pixel target. So the strip
   * branches once, here, rather than control by control — a branch made per control is how a strip
   * ends up with its two note arrows sized for a finger and the button pressed most often left at
   * the size a mouse needs.
   *
   * `large` is where the forty pixels come from — antd builds it out of `controlHeightLG`, the
   * touch target the rest of this application uses — and asking for it by size rather than by
   * height means the padding, line height and icon inside each control are built for the size the
   * control believes it is.
   */
  const controlSize = coarse ? 'large' : 'small';

  const leave = (
    <Button
      size={controlSize}
      onClick={() => onEngagedChange(false)}
      data-testid="trip-tracking-replay-leave"
    >
      {t('trips.tracking.replay.leave')}
    </Button>
  );

  if (!engaged) {
    return (
      <div className="tracking-replay">
        <div>
          <Button
            size={controlSize}
            icon={<HistoryOutlined />}
            onClick={() => onEngagedChange(true)}
            data-testid="trip-tracking-replay-open"
          >
            {t('trips.tracking.replay.open')}
          </Button>
        </div>
      </div>
    );
  }

  if (failed) {
    return (
      <div className="tracking-replay">
        <Alert
          type="error"
          showIcon
          message={t('trips.tracking.replay.logUnavailable')}
          description={t('trips.tracking.replay.logUnavailableBody')}
          action={leave}
          data-testid="trip-tracking-replay-unavailable"
        />
      </div>
    );
  }

  if (loading || events === undefined) {
    return (
      <div className="tracking-replay">
        <Flex gap="small" align="center" wrap>
          <Spin size="small" />
          <Typography.Text type="secondary" data-testid="trip-tracking-replay-loading">
            {t('trips.tracking.replay.reading')}
          </Typography.Text>
          {leave}
        </Flex>
      </div>
    );
  }

  if (span === null) {
    return (
      <div className="tracking-replay">
        <Alert
          type="info"
          showIcon
          message={t('trips.tracking.replay.nothingToReplay')}
          action={leave}
          data-testid="trip-tracking-replay-empty"
        />
      </div>
    );
  }

  const moment = at ?? span.from;
  const standing = noteAt(notes, moment);
  // The pictures in force at this moment — the same reading the note has, for the same reason: a
  // scrubber stops at a thousand places across a window hours long and never lands on the instant
  // a camera recorded.
  const shown = picturesAt(pictures, moment);
  const previous = noteBefore(notes, moment);
  const next = noteAfter(notes, moment);

  /** The moment in words: the whole stamp where there is room for it, the time of day where not. */
  const clock = (value: number) =>
    new Date(value).toLocaleString(
      i18n.language,
      narrow
        ? { hour: '2-digit', minute: '2-digit' }
        : { dateStyle: 'short', timeStyle: 'short' },
    );

  return (
    <div className="tracking-replay" data-testid="trip-tracking-replay">
      <Flex gap="small" align="center" wrap>
        <Button
          type="primary"
          size={controlSize}
          icon={transport.playing ? <PauseOutlined /> : <CaretRightOutlined />}
          aria-label={t(transport.playing ? 'trips.tracking.replay.pause' : 'trips.tracking.replay.play')}
          onClick={transport.toggle}
          data-testid="trip-tracking-replay-play"
        />
        <Typography.Text strong data-testid="trip-tracking-replay-clock">
          {clock(moment)}
        </Typography.Text>
        <Segmented
          size={controlSize}
          value={transport.speed}
          onChange={(value) => transport.setSpeed(Number(value))}
          options={REPLAY_SPEEDS.map((value) => ({
            value,
            label: t('trips.tracking.replay.speed', { value }),
          }))}
          data-testid="trip-tracking-replay-speed"
        />
        {leave}
      </Flex>

      {/* The one control that has to be driven by a finger on a moving page. The stylesheet gives
          the track `touch-action: pan-y`, which hands the browser the axis it scrolls on and keeps
          the axis this scrubs on — without it the page either cannot be scrolled past the replay or
          steals the drag that was meant to move the handle. */}
      <div className="tracking-replay-scrub" data-testid="trip-tracking-replay-scrub">
        <ConfigProvider theme={sliderTheme}>
          <Slider
            min={span.from}
            max={span.to}
            step={transport.step}
            value={moment}
            marks={marks}
            onChange={transport.scrubTo}
            tooltip={{ formatter: (value) => (typeof value === 'number' ? clock(value) : '') }}
            // Named on the handle rather than on the control: the handle is the element that
            // carries the slider role, and a label on the wrapper reaches nothing.
            ariaLabelForHandle={t('trips.tracking.replay.scrub')}
          />
        </ConfigProvider>
      </div>

      {notes.length > 0 && (
        <Flex gap="small" align="center" className="tracking-replay-note">
          <Button
            size={controlSize}
            icon={<LeftOutlined />}
            disabled={previous === null}
            aria-label={t('trips.tracking.replay.notePrevious')}
            onClick={() => previous !== null && transport.scrubTo(previous.at)}
            data-testid="trip-tracking-replay-note-previous"
          />
          <Typography.Text
            type={standing === null ? 'secondary' : undefined}
            className="tracking-replay-note-text"
            data-testid="trip-tracking-replay-note"
          >
            {standing === null
              ? t('trips.tracking.replay.noteNone')
              : t('trips.tracking.replay.note', {
                  when: clock(standing.at),
                  who: nameOf(standing.caverId),
                  note: standing.note,
                })}
          </Typography.Text>
          <Button
            size={controlSize}
            icon={<RightOutlined />}
            disabled={next === null}
            aria-label={t('trips.tracking.replay.noteNext')}
            onClick={() => next !== null && transport.scrubTo(next.at)}
            data-testid="trip-tracking-replay-note-next"
          />
        </Flex>
      )}

      {/* The photographs of the moment on screen, and the offer to add one to it.
          <b>Drawn here rather than on the model, and both of them deliberately.</b> A picture of a
          moment belongs to a time, and the party's own places are what the model draws; a
          photograph taken by somebody whose position nobody reported has no station to hang under
          and would simply never appear. This strip shows every one of them. */}
      {(shown.length > 0 || onAttachHere !== undefined) && (
        <Flex gap="small" align="center" wrap className="tracking-replay-pictures">
          {shown.map((picture) => (
            <a
              key={`${picture.at}-${picture.documentId}`}
              href={picture.entry.url}
              target="_blank"
              rel="noreferrer"
              data-testid="trip-tracking-replay-picture"
            >
              <img
                src={picture.entry.thumbnailUrl ?? picture.entry.url}
                alt={picture.entry.caption ?? t('trips.tracking.pictures.thumbnailAlt')}
                className="tracking-replay-picture"
              />
            </a>
          ))}
          {onAttachHere !== undefined && (
            <Button
              size={controlSize}
              icon={<PictureOutlined />}
              onClick={() => onAttachHere(moment)}
              data-testid="trip-tracking-replay-attach-picture"
            >
              {t('trips.tracking.pictures.attachHere')}
            </Button>
          )}
        </Flex>
      )}
    </div>
  );
}
