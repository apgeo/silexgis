// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo } from 'react';
import {
  CaretRightOutlined,
  LeftOutlined,
  PauseOutlined,
  RightOutlined,
} from '@ant-design/icons';
import {
  Alert,
  Button,
  ConfigProvider,
  Flex,
  Segmented,
  Select,
  Slider,
  Spin,
  Typography,
} from 'antd';
import { useTranslation } from 'react-i18next';
import type { TripTrackingState } from '../../api/hooks.ts';
import { trackedCaverTeams, type TrackedCaver } from '../../caveview/trackedCavers.ts';
import {
  COARSE_SLIDER,
  REPLAY_SPEEDS,
  useReplayClock,
} from '../../caveview/useReplayClock.ts';
import { useCoarsePointer } from '../../hooks/useCoarsePointer.ts';
import { useIsMobile } from '../../hooks/useIsMobile.ts';
import { followedName, momentAfter, momentBefore, type PastFollow } from './pastTrackReplay.ts';
import { tripDateRange } from './publicTripParty.ts';
import type { PastTripPlayback } from './usePastTripPlayback.ts';

/**
 * The most report marks the rail is drawn with before it stops drawing them.
 *
 * The mark is a 6px dot, so beyond roughly this many the widest rail this control is given — a
 * desk browser's, some hundreds of pixels — carries them edge to edge, and a rail of touching dots
 * says nothing a plain rail does not. A response carries up to two thousand reports, which is the
 * case this bound exists for: two thousand absolutely-positioned elements re-laid-out by the
 * library, on a phone, for a smear.
 */
const MAX_SCRUB_MARKS = 120;

export interface PublicPastBarProps {
  playback: PastTripPlayback;
  /**
   * What this link's <em>own</em> trip is doing, which is the whole of the back-to-now question.
   *
   * A party underground right now is something to go back <em>to</em>; a link whose trip finished
   * last winter has no such thing, and a button promising one would be a button that goes nowhere.
   */
  liveState: TripTrackingState;
  /**
   * The party at the moment on the clock, as the drawings are holding it.
   *
   * Handed in rather than folded again here: the words this strip prints for a followed team or
   * caver have to be the very words on the marker beside them, and the only way to guarantee that
   * is to read the same array.
   */
  cavers: readonly TrackedCaver[];
  /** The frame inside somebody's article, where every line costs a share of a small box. */
  compact?: boolean;
}

/**
 * The strip that says a reader is looking at the past, and the controls that move them through it.
 *
 * <b>This is the part that must not be subtle, and it is drawn as an alert for that reason.</b>
 * Everything else on these two surfaces is about a party underground <em>now</em>; the same
 * drawing, the same markers and the same list are used to show a trip that finished years ago, and
 * nothing in the drawing itself could tell a reader which of those they are looking at. So the
 * statement is persistent, sits above the controls rather than inside them, names the trip and says
 * when it was — and it never goes away while the past is on screen.
 *
 * <b>The way back is two different offers, and telling them apart is the point.</b> Where this
 * link's own trip is still being followed, there is a live party to return to and the button says
 * so. Where it is not, there is no "now" on this page at all — so the strip says that in words and
 * offers the neutral way out instead, back to the trip the link was published for. A button
 * labelled "back to now" over a cave nobody is in would be this page inventing a party.
 */
export default function PublicPastBar({
  playback,
  liveState,
  cavers,
  compact = false,
}: PublicPastBarProps) {
  const { t, i18n } = useTranslation();
  const coarse = useCoarsePointer();
  const narrow = useIsMobile();
  const { span, at, moments, track } = playback;

  const transport = useReplayClock({
    span,
    at,
    onAtChange: playback.setAt,
    engaged: playback.engaged,
  });

  // Held still across renders: a fresh object here is a fresh theme five times a second while the
  // replay plays, and every one of those has the whole slider's styles derived again.
  const sliderTheme = useMemo(
    () => ({ components: { Slider: coarse ? COARSE_SLIDER : {} } }),
    [coarse],
  );

  /**
   * Where the reports sit on the rail — built once per trip rather than five times a second.
   *
   * <b>Held still for the reason the theme above is.</b> A mark is an absolutely-positioned element
   * the library lays out, and the clock delivers a new moment every 200ms for the whole of a
   * playback; rebuilt inline, the filter, the object and every one of those elements were derived
   * again on each of those renders, on the one surface here explicitly designed for a phone.
   *
   * <b>And past a point they stop being marks at all.</b> The dot is 6px and a response carries up
   * to two thousand reports, so a busy trip's rail is a solid bar of them — which says less than a
   * bare rail does, at the cost of two thousand elements. Above the cap they are simply not drawn:
   * nothing is hidden by that, because the two step arrows beside the handle are how a reader
   * actually moves from one report to the next, and they step over every one of them.
   */
  const marks = useMemo(() => {
    if (span === null) {
      return undefined;
    }
    const onRail = moments.filter((moment) => moment >= span.from && moment <= span.to);
    if (onRail.length > MAX_SCRUB_MARKS) {
      return undefined;
    }
    return Object.fromEntries(
      onRail.map((moment) => [
        moment,
        { label: <span className="public-past-mark" aria-hidden="true" /> },
      ]),
    );
  }, [moments, span]);

  /**
   * Every control on this strip is sized on the pointer and on nothing else.
   *
   * A phone in landscape has the width of a desk and still nothing on it that can hit a
   * twenty-four-pixel target, so the branch is made once here rather than control by control —
   * which is how a strip ends up with its two step arrows sized for a finger and the button pressed
   * most often left at the size a mouse needs.
   */
  const controlSize = coarse ? 'large' : 'small';

  /**
   * Whom the replay can be asked to keep up with: the whole party, one of its teams, or one
   * person.
   *
   * <b>Built from the party at the moment on screen, which is the only party there is.</b> A team
   * nobody had been put into yet simply is not offered at that moment, and appears the moment a
   * report names it — which is the same truth the drawing beside this is telling. Offering the
   * trip's eventual teams from the start would be offering a reader a team that did not exist yet.
   */
  const followOptions = useMemo(() => {
    const teams = trackedCaverTeams(cavers).map((team) => ({
      value: `team:${team.teamId ?? ''}`,
      label: team.title ?? t('publicTrip.noTeam'),
    }));
    const people = cavers.map((caver) => ({
      value: `caver:${caver.caverId}`,
      label: caver.name,
    }));
    return [
      { value: 'none', label: t('publicTrip.past.followNobody') },
      ...teams,
      ...people,
    ];
  }, [cavers, t]);

  const followValue =
    playback.follow === null
      ? 'none'
      : `${playback.follow.kind}:${playback.follow.id ?? ''}`;

  const onFollowChange = (value: string) => {
    if (value === 'none') {
      playback.setFollow(null);
      return;
    }
    const [kind, id] = [value.slice(0, value.indexOf(':')), value.slice(value.indexOf(':') + 1)];
    const follow: PastFollow =
      kind === 'team'
        ? { kind: 'team', id: id === '' ? null : id }
        : { kind: 'caver', id };
    playback.setFollow(follow);
  };

  // The words for whoever is being followed, read off the trip's own roster rather than off the
  // moment — see the function, where the difference is the reason it is written that way.
  const followName =
    track === undefined
      ? null
      : followedName(track, playback.follow, {
          unteamed: t('publicTrip.noTeam'),
          unnamed: (ordinal) => t('publicTrip.caverOrdinal', { ordinal }),
        });

  const live = liveState === 'armed';
  const leave = (
    <Button
      size={controlSize}
      type={live ? 'primary' : 'default'}
      onClick={playback.backToNow}
      data-testid="public-past-back"
    >
      {t(live ? 'publicTrip.past.backToNow' : 'publicTrip.past.backToThisTrip')}
    </Button>
  );

  const dates =
    track === undefined ? null : tripDateRange(track.tripDate, track.tripDateEnd, i18n.language);

  const banner = (
    <Alert
      type="warning"
      showIcon
      banner={compact}
      className="public-past-banner"
      title={t('publicTrip.past.bannerTitle')}
      description={
        <div className="public-past-banner-body">
          <div data-testid="public-past-banner-what">
            {/* Shorter inside a frame, and shorter only in the part that explains rather than
                states: which trip and when it was are what the reader must not be able to miss,
                and they are said in full on both surfaces. What the frame drops is the paragraph
                spelling out what a replay is — in a box a club may have made 260px tall, that
                paragraph is the drawing's height. */}
            {track === undefined
              ? t('publicTrip.past.bannerLoading')
              : t(compact ? 'publicTrip.past.bannerWhatShort' : 'publicTrip.past.bannerWhat', {
                  title: track.title,
                  dates,
                })}
          </div>
          {/* Said only where it is true, and never as the reason the view is in the past: a reader
              can follow nobody and still be looking at a past trip. */}
          {followName !== null && (
            <div data-testid="public-past-banner-following">
              {t('publicTrip.past.following', { who: followName })}{' '}
              <Button
                type="link"
                size="small"
                onClick={() => playback.setFollow(null)}
                data-testid="public-past-unfollow"
              >
                {t('publicTrip.past.stopFollowing')}
              </Button>
            </div>
          )}
          {/* A record longer than one response carries, said where it cannot be missed.
              The rail already stops at the last report that arrived rather than at the trip's
              close — see `pastReplayWindow` — so what is left to say is why it stops there. Said
              in the banner and not only beside the transport: a reader who reaches the end of the
              rail is entitled to know that the end of the rail is not the end of the trip, and
              "the replay finished" and "the record ran out" are the two readings that must not be
              allowed to look alike. */}
          {track?.trackTruncated === true && (
            <div data-testid="public-past-truncated">{t('publicTrip.past.truncated')}</div>
          )}
          {/* The half of the request that is easiest to get wrong. There is no party to go back to,
              so the page says that rather than offering a word it cannot honour. */}
          {!live && (
            <div data-testid="public-past-no-live">{t('publicTrip.past.noLiveParty')}</div>
          )}
          <div className="public-past-banner-action">{leave}</div>
        </div>
      }
      data-testid="public-past-banner"
    />
  );

  /** The moment in words: the whole stamp where there is room for it, the time of day where not. */
  const clock = (value: number) =>
    new Date(value).toLocaleString(
      i18n.language,
      narrow || compact
        ? { hour: '2-digit', minute: '2-digit' }
        : { dateStyle: 'short', timeStyle: 'short' },
    );

  const body = () => {
    if (playback.failed) {
      return (
        <Alert
          type="warning"
          showIcon
          banner={compact}
          title={t('publicTrip.past.trackFailedTitle')}
          description={t('publicTrip.past.trackFailedBody')}
          data-testid="public-past-track-failed"
        />
      );
    }
    if (playback.loading || track === undefined) {
      return (
        <Flex gap="small" align="center">
          <Spin size="small" />
          <Typography.Text type="secondary" data-testid="public-past-track-loading">
            {t('publicTrip.past.reading')}
          </Typography.Text>
        </Flex>
      );
    }
    if (span === null || at === null) {
      // Published, closed, readable — and nothing was ever recorded that a clock could run over.
      // An ordinary answer, and the picker's `playable` normally keeps a reader from arriving
      // here at all; a link in an article can name such a trip directly, so it is still said.
      return (
        <Alert
          type="info"
          showIcon
          banner={compact}
          title={t('publicTrip.past.nothingToPlay')}
          data-testid="public-past-nothing"
        />
      );
    }

    const previous = momentBefore(moments, at);
    const next = momentAfter(moments, at);

    return (
      <>
        <Flex gap="small" align="center" wrap>
          <Button
            type="primary"
            size={controlSize}
            icon={transport.playing ? <PauseOutlined /> : <CaretRightOutlined />}
            aria-label={t(
              transport.playing ? 'publicTrip.past.pause' : 'publicTrip.past.play',
            )}
            onClick={transport.toggle}
            data-testid="public-past-play"
          />
          <Typography.Text strong data-testid="public-past-clock">
            {clock(at)}
          </Typography.Text>
          <Segmented
            size={controlSize}
            value={transport.speed}
            onChange={(value) => transport.setSpeed(Number(value))}
            options={REPLAY_SPEEDS.map((value) => ({
              value,
              label: t('publicTrip.past.speed', { value }),
            }))}
            data-testid="public-past-speed"
          />
          {/* Stepping between the reports themselves, because that is what a reader scrubbing a
              trip is hunting for: a handle can stop at a thousand places and only a few dozen of
              them are moments anybody said anything. */}
          <Button
            size={controlSize}
            icon={<LeftOutlined />}
            disabled={previous === null}
            aria-label={t('publicTrip.past.reportPrevious')}
            onClick={() => previous !== null && transport.scrubTo(previous)}
            data-testid="public-past-report-previous"
          />
          <Button
            size={controlSize}
            icon={<RightOutlined />}
            disabled={next === null}
            aria-label={t('publicTrip.past.reportNext')}
            onClick={() => next !== null && transport.scrubTo(next)}
            data-testid="public-past-report-next"
          />
          {/* "A past trip / team", in the owner's own words: one control chooses which of the party
              the camera keeps up with as the clock runs. A team and a single caver are the same
              question asked at two scales, so they are one list rather than two controls. */}
          <Select
            size={controlSize}
            value={followValue}
            onChange={onFollowChange}
            options={followOptions}
            aria-label={t('publicTrip.past.followLabel')}
            className="public-past-follow"
            data-testid="public-past-follow"
          />
        </Flex>

        {/* The one control driven by a finger on a moving page. The stylesheet gives the track
            `touch-action: pan-y`, which hands the browser the axis it scrolls on and keeps the axis
            this scrubs on — without it the page either cannot be scrolled past the strip or steals
            the drag that was meant to move the handle. */}
        <div className="public-past-scrub" data-testid="public-past-scrub">
          <ConfigProvider theme={sliderTheme}>
            <Slider
              min={span.from}
              max={span.to}
              step={transport.step}
              value={at}
              marks={marks}
              onChange={transport.scrubTo}
              tooltip={{ formatter: (value) => (typeof value === 'number' ? clock(value) : '') }}
              // Named on the handle rather than on the control: the handle is the element that
              // carries the slider role, and a label on the wrapper reaches nothing.
              ariaLabelForHandle={t('publicTrip.past.scrub')}
            />
          </ConfigProvider>
        </div>
      </>
    );
  };

  return (
    <div
      className={compact ? 'public-past-bar public-past-bar-compact' : 'public-past-bar'}
      data-testid="public-past-bar"
    >
      {banner}
      {body()}
    </div>
  );
}
