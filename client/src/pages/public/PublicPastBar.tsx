// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo, useState } from 'react';
import {
  CaretRightOutlined,
  LeftOutlined,
  LinkOutlined,
  PauseOutlined,
  PlayCircleOutlined,
  RightOutlined,
} from '@ant-design/icons';
import {
  Alert,
  Button,
  ConfigProvider,
  Flex,
  Input,
  Segmented,
  Select,
  Slider,
  Spin,
  Typography,
} from 'antd';
import { useTranslation } from 'react-i18next';
import type { TripTrackingState } from '../../api/hooks.ts';
import { trackedCaverTeams, type TrackedCaver } from '../../caveview/trackedCavers.ts';
import { COARSE_SLIDER, REPLAY_SPEEDS } from '../../caveview/useReplayClock.ts';
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
   * Handed in rather than folded again here, and read for one thing: whether anybody is outside
   * every team at this moment, which is the only time "not in a team" is a party the camera can
   * be asked to keep up with. Everything else the follow control offers comes from the trip's own
   * roster, so what it says matches the banner and the markers by construction.
   */
  cavers: readonly TrackedCaver[];
  /** The frame inside somebody's article, where every line costs a share of a small box. */
  compact?: boolean;
  /**
   * The address of the moment on the clock — and, asked for playing, of that moment set going.
   *
   * <b>Supplied by the page that has an address of its own to give, and by nothing else.</b> With
   * it the strip offers two buttons that copy such a link; without it, none. The frame inside an
   * article supplies none: it has no room for them, a framed page is refused the clipboard unless
   * the framing site said otherwise, and the address a reader would want from there is the
   * article's, which this page does not know.
   */
  momentAddress?: (playing: boolean) => string;
}

/** What the last press of a copy button came to, for the line that says so. */
type Copied =
  | { outcome: 'copied'; playing: boolean; when: string }
  | { outcome: 'refused'; address: string };

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
  momentAddress,
}: PublicPastBarProps) {
  const { t, i18n } = useTranslation();
  const [copied, setCopied] = useState<Copied | null>(null);
  const coarse = useCoarsePointer();
  const narrow = useIsMobile();
  // The clock belongs to the playback, not to this strip: the drawing beside it reads whether it
  // is playing to decide how the markers move, in the same render the moment changes.
  const { span, at, moments, track, transport } = playback;

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

  const followValue =
    playback.follow === null
      ? 'none'
      : `${playback.follow.kind}:${playback.follow.id ?? ''}`;

  // The words for whoever is being followed, read off the trip's own roster rather than off the
  // moment — see the function, where the difference is the reason it is written that way.
  const followName =
    track === undefined
      ? null
      : followedName(track, playback.follow, {
          unteamed: t('publicTrip.noTeam'),
          unnamed: (ordinal) => t('publicTrip.caverOrdinal', { ordinal }),
        });

  /**
   * Whom the replay can be asked to keep up with: the whole party, one of its teams, or one
   * person.
   *
   * <b>Built from the trip's own roster, not from the party at the moment on screen.</b> A replay
   * opens at the trip's beginning, where nobody has been reported into any team yet; a control
   * that offered only the parties reported by that moment would offer nobody at all, on the one
   * screen whose reason to exist is "show me the survey team". Choosing a party that has not
   * appeared yet is the ordinary case, and `setFollow` answers it by moving the clock to where
   * they first appear. Every team and every person the trip had is therefore always offered, in
   * the words the banner and the markers use for them.
   *
   * <b>And whoever is being followed is always among the options, named.</b> A link can name a
   * team before any report placed it, or a person beyond the roster; the control draws a value it
   * has no option for as the raw value, which for a team is its identifier. So a follow the roster
   * does not offer is appended as one more option, under its name where the roster knows one and
   * under a plain "not in this trip" where it does not — never as an identifier on a phone.
   */
  const followOptions = useMemo(() => {
    const teams = (track?.teams ?? []).map((team) => ({
      value: `team:${team.id}`,
      label: team.title,
    }));
    // "Not in a team" is a party only where somebody is in it at the moment on screen: the roster
    // has no entry for it, and offered always it would be a choice that follows nobody most of
    // the time.
    if (trackedCaverTeams(cavers).some((team) => team.teamId === null)) {
      teams.push({ value: 'team:', label: t('publicTrip.noTeam') });
    }
    const people = (track?.participants ?? []).map((participant) => ({
      value: `caver:${participant.ordinal}`,
      label: participant.label ?? t('publicTrip.caverOrdinal', { ordinal: participant.ordinal }),
    }));
    const options = [
      { value: 'none', label: t('publicTrip.past.followNobody') },
      ...teams,
      ...people,
    ];
    if (!options.some((option) => option.value === followValue)) {
      options.push({
        value: followValue,
        label: followName ?? t('publicTrip.past.followNotInTrip'),
      });
    }
    return options;
  }, [track, cavers, followValue, followName, t]);

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
                paragraph is the drawing's height.

                A trip that could not be read has no name to give and is not being read either:
                the body below says it failed, so this line says only that it is a past trip —
                "reading this trip" above "this trip could not be read" is a strip that both is
                and is not reading it, and for a trip past retention that state is permanent. */}
            {track !== undefined
              ? t(compact ? 'publicTrip.past.bannerWhatShort' : 'publicTrip.past.bannerWhat', {
                  title: track.title,
                  dates,
                })
              : playback.failed
                ? t('publicTrip.past.unknownTrip')
                : t('publicTrip.past.bannerLoading')}
          </div>
          {/* Said only where it is true, and never as the reason the view is in the past: a reader
              can follow nobody and still be looking at a past trip. */}
          {followName !== null && (
            <div data-testid="public-past-banner-following">
              {t('publicTrip.past.following', { who: followName })}{' '}
              {/* A link in a line of prose, and still a target a finger has to hit: sized on the
                  pointer like every other control on this strip. */}
              <Button
                type="link"
                size={controlSize}
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

  /**
   * Whether the stretch being played runs past a local midnight.
   *
   * A camp trip is measured over two or three days, and on one of those a time of day alone on
   * the clock says nothing about which day the party is being shown on: noon on the first day and
   * noon on the second print identically, and the "reported 3 hours ago" beside every name is
   * measured from that ambiguous moment. Decided on the calendar day the reader's browser is in,
   * since that is the day the clock prints.
   */
  const multiDay =
    span !== null
    && new Date(span.from).toLocaleDateString(i18n.language)
      !== new Date(span.to).toLocaleDateString(i18n.language);

  /**
   * The moment in words: the whole stamp where there is room for it, the time of day where not —
   * and the day beside it, however little room there is, once the trip spans more than one.
   */
  const clock = (value: number) =>
    new Date(value).toLocaleString(
      i18n.language,
      narrow || compact
        ? multiDay
          ? { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' }
          : { hour: '2-digit', minute: '2-digit' }
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

    /**
     * Copies the address of the moment on the clock, and says what was copied.
     *
     * <b>The moment is taken at the press, with the words that name it.</b> The link carries an
     * instant in UTC and the strip prints local time, so the confirmation repeats the time the
     * reader was looking at when they pressed: on a replay that is playing, "copied" alone would
     * leave them guessing which of the last few seconds went into the link.
     *
     * <b>A refusal still hands the link over.</b> A browser may decline the clipboard — an old
     * one, a page opened over plain HTTP, a permission somebody denied — and the reader pressed
     * because they want the address. So it is put in front of them, selected on focus, to copy by
     * hand.
     */
    const copy = async (playing: boolean) => {
      if (momentAddress === undefined) {
        return;
      }
      const address = momentAddress(playing);
      const when = clock(at);
      try {
        await navigator.clipboard.writeText(address);
        setCopied({ outcome: 'copied', playing, when });
      } catch {
        setCopied({ outcome: 'refused', address });
      }
    };

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

        {momentAddress !== undefined && !compact && (
          <div className="public-past-links" data-testid="public-past-links">
            <Flex gap="small" align="center" wrap>
              {/* Buttons, and worded ones: what each copies is the difference between them, and
                  an icon cannot say "and starts playing". Their text is their accessible name —
                  the whole of it, which is why the pictures beside it are hidden from one: the
                  icon set names each picture, and "link Copy link to this moment" is what a
                  screen reader would otherwise say. */}
              <Button
                size={controlSize}
                icon={<LinkOutlined aria-hidden />}
                onClick={() => void copy(false)}
                data-testid="public-past-copy-moment"
              >
                {t('publicTrip.past.copyMoment')}
              </Button>
              <Button
                size={controlSize}
                icon={<PlayCircleOutlined aria-hidden />}
                onClick={() => void copy(true)}
                data-testid="public-past-copy-playing"
              >
                {t('publicTrip.past.copyPlaying')}
              </Button>
              {/* Always in the page, empty until a press: a status that appears together with its
                  first words is one a screen reader has not been listening to yet. */}
              <Typography.Text type="secondary" role="status" data-testid="public-past-copied">
                {copied === null
                  ? ''
                  : copied.outcome === 'refused'
                    ? t('publicTrip.past.copyRefused')
                    : t(
                        copied.playing
                          ? 'publicTrip.past.copiedPlaying'
                          : 'publicTrip.past.copiedMoment',
                        { when: copied.when },
                      )}
              </Typography.Text>
            </Flex>
            {copied?.outcome === 'refused' && (
              <Input
                readOnly
                size={controlSize}
                value={copied.address}
                onFocus={(event) => event.target.select()}
                aria-label={t('publicTrip.past.copyByHand')}
                data-testid="public-past-copy-by-hand"
              />
            )}
          </div>
        )}
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
