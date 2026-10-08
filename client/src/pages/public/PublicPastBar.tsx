// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo, useState } from 'react';
import {
  CaretRightOutlined,
  ControlOutlined,
  HistoryOutlined,
  LeftOutlined,
  LinkOutlined,
  PauseOutlined,
  PlayCircleOutlined,
  RightOutlined,
  WarningOutlined,
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
  Tag,
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
  /**
   * Which of its shapes the strip is drawn in. The page's, where none is named.
   *
   * <b>`frame` is the whole strip inside somebody's article</b>, for a frame with room for it: the
   * page's strip with the type brought down and the statement in its short form, everything on
   * screen at once.
   *
   * <b>`line` and `sheet` are the two halves of one strip, for a frame without that room.</b> A
   * frame a club pasted is 260px tall on a phone, and the whole strip is taller than that: capped
   * and scrolling inside itself it left the drawing 106px, every one of them under the viewer's
   * own controls, with the play button below the fold. So that frame keeps one line — that this is
   * the past, which trip, the clock, play and the way back — and everything a reader reaches for
   * less often (the speed, the steps between reports, whom to follow, the rail, and the statement
   * in full) is drawn by the same component a press away, in the frame's sheet.
   *
   * The two are never the same control twice: what the line draws the sheet does not, so a frame
   * showing both holds one play button, one clock and one way back.
   */
  layout?: 'page' | 'frame' | 'line' | 'sheet';
  /**
   * Opens what the strip has no room for. On the line that is the other half of the strip, and
   * its last button is drawn only where this is supplied — a line with nothing behind it would be
   * offering controls it cannot show. On the whole strip in a frame it is the cave's lists, which
   * a frame keeps in a sheet.
   */
  onMore?: () => void;
  /**
   * The address of the moment on the clock — and, asked for playing, of that moment set going.
   *
   * <b>Supplied by the page that has an address of its own to give, and by nothing else.</b> With
   * it the strip offers two buttons that copy such a link; without it, none. The frame inside an
   * article supplies none: a framed page is refused the clipboard unless the framing site said
   * otherwise, and the address a reader would want from there is the article's, which this page
   * does not know.
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
  layout = 'page',
  onMore,
  momentAddress,
}: PublicPastBarProps) {
  const { t, i18n } = useTranslation();
  const [copied, setCopied] = useState<Copied | null>(null);
  const coarse = useCoarsePointer();
  const narrow = useIsMobile();
  // The clock belongs to the playback, not to this strip: the drawing beside it reads whether it
  // is playing to decide how the markers move, in the same render the moment changes.
  const { span, at, moments, followedMoments, track, transport } = playback;

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
   * actually moves from one report to the next.
   *
   * <b>The marks are everybody's, whoever is followed.</b> The arrows narrow to the followed
   * party's own reports; the rail does not, because it is the picture of the whole trip — where it
   * was busy, where it went quiet — and a reader following one team still wants to see that the
   * others were reporting while theirs was not.
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
  const leaveInFull = t(live ? 'publicTrip.past.backToNow' : 'publicTrip.past.backToThisTrip');
  /**
   * The way back, said in full on the page and in one word on the frame's line.
   *
   * The sentence is two hundred pixels of a line that is 328 wide in a phone's article, so the
   * line shows its last word — "now" where a party is underground, "back" where none is — and
   * keeps the sentence as the button's name and its hover text. The word shown is part of that
   * name, so somebody driving the page by voice can still say what they see.
   */
  const leave = (
    <Button
      size={controlSize}
      type={live ? 'primary' : 'default'}
      onClick={playback.backToNow}
      aria-label={layout === 'line' ? leaveInFull : undefined}
      title={layout === 'line' ? leaveInFull : undefined}
      data-testid="public-past-back"
    >
      {layout === 'line'
        ? t(live ? 'publicTrip.past.backToNowShort' : 'publicTrip.past.backToThisTripShort')
        : leaveInFull}
    </Button>
  );

  const dates =
    track === undefined ? null : tripDateRange(track.tripDate, track.tripDateEnd, i18n.language);

  // The sheet stands in the same document as the frame's line, which already carries the names a
  // reader and a test find the statement by; its copy of the statement answers to its own.
  const inSheet = layout === 'sheet';
  const inFrame = layout === 'frame';
  const banner = (
    <Alert
      type="warning"
      showIcon
      banner={inSheet || inFrame}
      className="public-past-banner"
      title={t('publicTrip.past.bannerTitle')}
      description={
        <div className="public-past-banner-body">
          <div data-testid={inSheet ? 'public-past-statement-what' : 'public-past-banner-what'}>
            {/* A trip that could not be read has no name to give and is not being read either:
                the body below says it failed, so this line says only that it is a past trip —
                "reading this trip" above "this trip could not be read" is a strip that both is
                and is not reading it, and for a trip past retention that state is permanent. */}
            {track !== undefined
              ? t(inFrame ? 'publicTrip.past.bannerWhatShort' : 'publicTrip.past.bannerWhat', {
                  title: track.title,
                  dates,
                })
              : playback.failed
                ? t('publicTrip.past.unknownTrip')
                : t('publicTrip.past.bannerLoading')}
          </div>
          {/* Which camp the trip was part of, where the server names one — the same words the
              list it was picked from gathers it under, so a reader who arrived by a link and
              never saw that list is told as much as one who pressed a row. */}
          {!inFrame && track?.expedition != null && (
            <div className="public-past-banner-camp" data-testid="public-past-banner-camp">
              {t('publicTrip.camp', { name: track.expedition.name })}
            </div>
          )}
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
            <div data-testid={inSheet ? 'public-past-statement-truncated' : 'public-past-truncated'}>
              {t('publicTrip.past.truncated')}
            </div>
          )}
          {/* The half of the request that is easiest to get wrong. There is no party to go back to,
              so the page says that rather than offering a word it cannot honour. */}
          {!live && (
            <div data-testid="public-past-no-live">{t('publicTrip.past.noLiveParty')}</div>
          )}
          {/* The sheet has no way back of its own: it is drawn over a line that has one. */}
          {!inSheet && (
            <div className="public-past-banner-action">
              {leave}
              {/* A frame keeps the cave's lists in a sheet, and this is the way to it from a
                  replay: beside the way back, so it is there for a trip that could not be read
                  too — the reader most likely to want another one. */}
              {inFrame && onMore !== undefined && (
                <Button
                  size={controlSize}
                  icon={<HistoryOutlined aria-hidden />}
                  onClick={onMore}
                  data-testid="public-past-lists-open"
                >
                  {t('publicTrip.past.sectionTitle')}
                </Button>
              )}
            </div>
          )}
        </div>
      }
      data-testid={inSheet ? 'public-past-statement' : 'public-past-banner'}
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
      narrow || layout !== 'page'
        ? multiDay
          ? { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' }
          : { hour: '2-digit', minute: '2-digit' }
        : { dateStyle: 'short', timeStyle: 'short' },
    );

  /**
   * What the strip says where there is no transport to draw, or null where there is one.
   *
   * Three different things, and none of them is a replay: the trip could not be read, it is still
   * being read, or it was read and holds nothing a clock could run over. That last one is an
   * ordinary answer — published, closed, readable — and the picker's `playable` normally keeps a
   * reader from arriving at it; a link in an article can name such a trip directly, so it is
   * still said.
   */
  const without: 'failed' | 'reading' | 'nothing' | null = playback.failed
    ? 'failed'
    : playback.loading || track === undefined
      ? 'reading'
      : span === null || at === null
        ? 'nothing'
        : null;

  /**
   * The transport's controls, each built once and placed by whichever layout is being drawn.
   *
   * The page lays all of them out together; the frame puts the clock and play on its line and
   * the rest in its sheet. Built in one place so that the two surfaces cannot come to disagree
   * about what a control does — only about where it stands.
   */
  const controls = () => {
    if (without !== null || span === null || at === null) {
      return null;
    }
    /**
     * What the two arrows step through: the followed party's own reports where somebody is
     * followed, and everybody's otherwise.
     *
     * A reader keeping up with one team of four presses "next" to see where that team went next,
     * and three times in four the next report of the trip is somebody else's: the clock moves and
     * nothing they are watching does. So a follow narrows the steps to the reports that move the
     * party the camera is on.
     *
     * <b>Not when that party has no report at all</b> — a link naming a team of another trip, or
     * somebody on the roster of whom nothing was ever said. Narrowed to nothing, both arrows would
     * be dead on a trip full of reports, for a reason the reader cannot see; they step through
     * everybody's instead, and are named as doing so.
     */
    const ownSteps = followedMoments.length > 0;
    const steps = ownSteps ? followedMoments : moments;
    const previous = momentBefore(steps, at);
    const next = momentAfter(steps, at);
    // Named for whose reports they step, because an arrow that skips reports is otherwise an
    // arrow that seems to miss them. A followed team the trip carried no title for has reports
    // and no name: it is called what it is, the party being followed.
    const stepName = (direction: 'Previous' | 'Next') =>
      !ownSteps
        ? t(`publicTrip.past.report${direction}`)
        : followName === null
          ? t(`publicTrip.past.report${direction}Followed`)
          : t(`publicTrip.past.report${direction}Of`, { who: followName });
    return {
      at,
      play: (
        <Button
          type="primary"
          size={controlSize}
          icon={transport.playing ? <PauseOutlined /> : <CaretRightOutlined />}
          aria-label={t(transport.playing ? 'publicTrip.past.pause' : 'publicTrip.past.play')}
          onClick={transport.toggle}
          data-testid="public-past-play"
        />
      ),
      clock: (
        <Typography.Text strong className="public-past-clock" data-testid="public-past-clock">
          {clock(at)}
        </Typography.Text>
      ),
      fine: (
        <>
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
              them are moments anybody said anything. Said in the hover text as well as to a
              screen reader: once somebody is followed the arrows pass over other people's
              reports, and a reader who can see the marks they skip is owed the reason. */}
          <Button
            size={controlSize}
            icon={<LeftOutlined />}
            disabled={previous === null}
            aria-label={stepName('Previous')}
            title={stepName('Previous')}
            onClick={() => previous !== null && transport.scrubTo(previous)}
            data-testid="public-past-report-previous"
          />
          <Button
            size={controlSize}
            icon={<RightOutlined />}
            disabled={next === null}
            aria-label={stepName('Next')}
            title={stepName('Next')}
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
        </>
      ),
      // The one control driven by a finger on a moving page. The stylesheet gives the track
      // `touch-action: pan-y`, which hands the browser the axis it scrolls on and keeps the axis
      // this scrubs on — without it the page either cannot be scrolled past the strip or steals
      // the drag that was meant to move the handle.
      rail: (
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
      ),
    };
  };

  /** The page's strip under its statement: everything, laid out together. */
  const body = () => {
    if (without === 'failed') {
      return (
        <Alert
          type="warning"
          showIcon
          title={t('publicTrip.past.trackFailedTitle')}
          description={t('publicTrip.past.trackFailedBody')}
          data-testid="public-past-track-failed"
        />
      );
    }
    if (without === 'reading') {
      return (
        <Flex gap="small" align="center">
          <Spin size="small" />
          <Typography.Text type="secondary" data-testid="public-past-track-loading">
            {t('publicTrip.past.reading')}
          </Typography.Text>
        </Flex>
      );
    }
    const drawn = controls();
    if (drawn === null) {
      return (
        <Alert
          type="info"
          showIcon
          title={t('publicTrip.past.nothingToPlay')}
          data-testid="public-past-nothing"
        />
      );
    }

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
      const when = clock(drawn.at);
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
          {drawn.play}
          {drawn.clock}
          {drawn.fine}
        </Flex>
        {drawn.rail}

        {momentAddress !== undefined && (
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

  if (layout === 'line') {
    const drawn = controls();
    /**
     * What the line says in the place of a trip's name where there is no transport: that the trip
     * could not be read, is being read, or holds nothing to play. Allowed to break onto a second
     * line of type, which a name is not — a name cut short is still the name, and "This trip
     * could not b…" is a frame that does not say what happened to it.
     */
    const said =
      without === 'failed'
        ? { id: 'public-past-track-failed', words: t('publicTrip.past.trackFailedTitle') }
        : without === 'reading'
          ? { id: 'public-past-track-loading', words: t('publicTrip.past.reading') }
          : without === 'nothing'
            ? { id: 'public-past-nothing', words: t('publicTrip.past.nothingToPlay') }
            : null;
    return (
      <div className="public-past-bar public-past-bar-line" data-testid="public-past-bar">
        {/* The statement that this is the past, in the one form that fits a line: a tag in the
            warning colour that never leaves, and the trip's name beside it for as far as the
            line goes. The frame's outline says the same thing at no height at all. */}
        <span className="public-past-line-what" data-testid="public-past-banner">
          <Tag color="warning" className="public-past-line-tag">
            {t('publicTrip.past.tag')}
          </Tag>
          <span
            className={said === null ? 'public-past-line-title' : 'public-past-line-said'}
            title={
              track === undefined || said !== null
                ? undefined
                : t('publicTrip.past.bannerWhatShort', { title: track.title, dates })
            }
            data-testid="public-past-banner-what"
          >
            {said !== null ? <span data-testid={said.id}>{said.words}</span> : track?.title}
          </span>
        </span>
        {drawn?.clock}
        {drawn?.play}
        {leave}
        {onMore !== undefined && (
          <Button
            size={controlSize}
            icon={<ControlOutlined />}
            onClick={onMore}
            aria-label={t('publicTrip.past.controls')}
            title={t('publicTrip.past.controls')}
            data-testid="public-past-controls-open"
          />
        )}
        {/* A record cut short, said on the line itself and not only in the sheet. A reader who
            presses play here watches the clock stop at the last report that arrived, and "the
            replay finished" and "the record ran out" must not be allowed to look alike — least
            of all in the frame, where nothing else is on screen to tell them apart. A row of its
            own under the controls, in few words and allowed to wrap; the sentence in full is its
            hover text and stands in the sheet. */}
        {track?.trackTruncated === true && (
          <span
            className="public-past-line-truncated"
            title={t('publicTrip.past.truncated')}
            data-testid="public-past-truncated"
          >
            <WarningOutlined aria-hidden /> {t('publicTrip.past.truncatedShort')}
          </span>
        )}
      </div>
    );
  }

  if (layout === 'sheet') {
    const drawn = controls();
    return (
      <div className="public-past-bar public-past-bar-sheet" data-testid="public-past-sheet">
        {/* The controls first and the statement after: the line this sheet was opened from has
            already said it is the past, and a sheet over a 260px frame shows about a hundred
            pixels before it has to be scrolled — which is the rail and one row of controls. */}
        {drawn !== null && (
          <>
            {drawn.rail}
            <Flex gap="small" align="center" wrap>
              {drawn.fine}
            </Flex>
          </>
        )}
        {banner}
        {/* Why a trip could not be read, which the line had no room for. */}
        {without === 'failed' && (
          <Typography.Text type="secondary" data-testid="public-past-track-failed-why">
            {t('publicTrip.past.trackFailedBody')}
          </Typography.Text>
        )}
      </div>
    );
  }

  return (
    <div
      className={inFrame ? 'public-past-bar public-past-bar-frame' : 'public-past-bar'}
      data-testid="public-past-bar"
    >
      {banner}
      {body()}
    </div>
  );
}
