// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo, useState } from 'react';
import {
  DeleteOutlined,
  EditOutlined,
  EyeInvisibleOutlined,
  PictureOutlined,
  WarningOutlined,
} from '@ant-design/icons';
import {
  Alert,
  App,
  Button,
  Checkbox,
  Flex,
  Popconfirm,
  Skeleton,
  Space,
  Table,
  Tag,
  Tooltip,
  Typography,
} from 'antd';
import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { isSettledRefusal } from '../../api/client.ts';
import {
  useDeleteTrackingEvent,
  useTrackingDepthReadings,
  useTripMomentPictureLinks,
  useTripTracking,
  useTripTrackingEvents,
  type TrackingEvent,
  type TrackingParticipant,
  type TripLogInfo,
} from '../../api/hooks.ts';
import { replayPictures } from '../../caveview/trackingReplay.ts';
import TrackingConfigCard from '../../components/trips/TrackingConfigCard.tsx';
import TrackingModelPanel from '../../components/trips/TrackingModelPanel.tsx';
import TrackingMomentPictures from '../../components/trips/TrackingMomentPictures.tsx';
import TrackingPicturesDialog from '../../components/trips/TrackingPicturesDialog.tsx';
import TrackingPublicNameDialog from '../../components/trips/TrackingPublicNameDialog.tsx';
import TrackingReportForm from '../../components/trips/TrackingReportForm.tsx';
import TrackingSharePanel from '../../components/trips/TrackingSharePanel.tsx';
import {
  trackingProblemMessage,
  trackingReadRefusalMessage,
} from '../../components/trips/trackingProblems.ts';
import {
  recordedDepthGap,
  type TrackingDepthGap,
} from '../../components/trips/trackingDepthGap.ts';
import { publicNamingOf } from '../../components/trips/trackingPublicName.ts';
import {
  lastHeardInWords,
  trackingStandingOf,
  trackingStandings,
  type TrackingStanding,
} from '../../components/trips/trackingWatch.ts';
import { drawableOn } from '../../caveview/drawableOn.ts';
import { noStationsMissing } from '../../caveview/placedOnModel.ts';
import { useCoarsePointer } from '../../hooks/useCoarsePointer.ts';
import { useIsMobile } from '../../hooks/useIsMobile.ts';
// The position's age is worded by the followed page's own rule, called rather than copied — the
// same reason the standing and the "last heard" age above are. A coordinator and a family read the
// same position, and two roundings of one gap would have them disagreeing about it.
import { positionAgeInWords } from '../public/publicTripParty.ts';
import './TripTrackingTab.css';

/** How many reports the log shows without being asked for more. */
const RECENT_EVENTS = 20;

/**
 * Where the party is, as far as anybody above ground has been told.
 *
 * Three rules shape this surface and each of them is a defect if it is softened:
 *
 * **A position that was withheld is said to have been withheld.** Station names and depths are
 * location data and are kept from a reader without the right to place the cave — they arrive as
 * absences, exactly as they do for somebody nobody has reported yet. Drawing both as "unknown"
 * would tell a rescue co-ordinator that nobody knows where a caver is, when what is true is that
 * *they* are not being told. So where the answer says something was withheld, an absence against a
 * person who *has* been reported is drawn as a withholding.
 *
 * **A position is the latest report that claimed a place, and the last report is whatever came
 * last.** A note or an exit says something happened, not where — so the two columns disagree on
 * purpose, and a surface that folded them together would move somebody back to an entrance because
 * their last word was a radio check. <b>Their two ages disagree for the same reason and are drawn
 * as two.</b> The place carries the moment it was reported and the "last heard" column carries the
 * moment of the last word of any kind: a station heard four hours ago under a last word eight
 * minutes old is not a station eight minutes old, and until the place was dated from its own
 * report that is exactly what this table said. The gap between the two is information — it is what
 * tells a coordinator that nobody has said where a party is since noon although somebody has said
 * *something* since — so the two are never collapsed into one figure or one label.
 *
 * **A wrong report is deleted, never edited.** What is on the log is what somebody said at a
 * moment; rewriting one in place would leave a record indistinguishable from one nobody corrected.
 *
 * **A failed read is a notice, not a demolition.** The watch is re-read every half minute while a
 * party is underground, so a refusal here is the ordinary consequence of a dropped connection
 * rather than news about the trip. The watch already in hand stays on screen under a warning —
 * along with the model somebody loaded, the selection they made and the report they are half-way
 * through typing off a phone call.
 */
export default function TripTrackingTab({
  trip,
  canEdit,
}: {
  trip: TripLogInfo;
  canEdit: boolean;
}) {
  const { t, i18n } = useTranslation();
  const { message } = App.useApp();
  // Chosen on the pointer, never on the width: a phone held in landscape has a desk's worth of
  // room across and still nothing on it that can hit a fourteen-pixel icon.
  const coarse = useCoarsePointer();
  // And chosen on the width, never on the pointer: how much room there is across is what decides
  // whether five columns can stand side by side, and a tablet with a trackpad has the room.
  const narrow = useIsMobile();
  const { data, isPending, isFetching, error, refetch } = useTripTracking(trip.id);
  const events = useTripTrackingEvents(trip.id, { pageSize: RECENT_EVENTS });
  const deleteEvent = useDeleteTrackingEvent();
  const [selected, setSelected] = useState<ReadonlySet<string>>(new Set());
  /** Whose caption on the published page is being set, or null while nobody's is. */
  const [naming, setNaming] = useState<TrackingParticipant | null>(null);
  /**
   * The stations the model below turns out not to hold, as the viewer in it answers.
   *
   * <b>Kept here so the table can say it, which is the whole point of carrying it this far.</b>
   * This is the surface somebody reads to answer "where is everybody"; the model is opened
   * deliberately and costs a download. A station name printed here with a freshness age under it
   * reads as a current place on the survey in force — and when the drawing holds no station of
   * that name, that is exactly the sentence that has to be stopped.
   *
   * <b>Stations, not the people at them, because the panel below is not always drawing this
   * table's party.</b> Its replay hands the viewer the watch as it stood at some earlier moment
   * while this table goes on showing the watch as it stands, so an answer naming people would be
   * an answer about the wrong ones. A station name is the same name in both.
   *
   * Empty while the model is closed, because it is answered by a viewer and there is none: what is
   * said is "the drawing opened below does not hold this", never "this station does not exist".
   */
  const [unplacedStations, setUnplacedStations] = useState<ReadonlySet<string>>(noStationsMissing);
  /**
   * The moment photographs are being hung on, and who they are about, or null while nothing is.
   *
   * <b>A moment and a subject, never a report.</b> The offer on a log row opens this at the instant
   * that row was reported at — which is the natural way to say "the party was at the pitch head
   * then, here is the photograph" — and what is stored is that instant. Deleting the row it was
   * read off changes nothing about the picture, which is the whole design; the row is a convenient
   * clock and not the thing being attached to.
   */
  const [attaching, setAttaching] = useState<{ at: number; caverId: string | null } | null>(null);

  /**
   * Whether the server has answered this read for good, rather than failing to answer it.
   *
   * <b>Two failures wear the same shape here and a coordinator has to act on them differently.</b>
   * A dropped connection, a server restarting, a phone in a valley: the poll comes round again in
   * thirty seconds and fixes itself, and the right thing to say is "hold on". A trip that has been
   * deleted, or one this account may no longer read, or a session that lapsed behind the reader:
   * the same poll will refuse for ever, and saying "it starts refreshing again by itself" over a
   * party table that will never refresh — with a retry that fails on every press — is the surface
   * telling somebody watching a party underground that the stale figures in front of them are
   * about to become current. They are not. The rule that separates the two is the one the retry
   * policy already uses, called rather than written a second time.
   */
  const refused = isSettledRefusal(error);

  /**
   * Every depth anybody typed that this screen could measure, each one once.
   *
   * <b>Collected here because a reported depth and the station it was recorded at are two facts,
   * and only one of them is on the answer.</b> What a row carries is the station the server
   * resolved to and the number somebody typed; how far apart those two are is nowhere in the
   * record, by decision — no residual is stored — so the screen works it out when it draws it.
   *
   * <b>Narrowed to reports made against the model the watch is on now</b>, which is not an
   * optimisation: a depth measured against a datum that has since been replaced is not a depth in
   * the model in use, so re-resolving it answers a question about a different cave. Those rows are
   * never measured, so their numbers are never asked for either — a request whose answer could only
   * be discarded is a request not worth making.
   *
   * Deduplicated because the answer is a property of the number and the trip rather than of the
   * row: a party of six reported at 120 m is six rows carrying one question. Sorted so the list is
   * stable between renders rather than reshuffling with the log, and bounded by what the log itself
   * is bounded by — one page of recent reports, plus the party, which is a couple of dozen numbers
   * at the very worst and usually a handful.
   */
  const askedDepths = useMemo(() => {
    const watchModelId = data?.surveyModelId ?? null;
    const typed = new Set<number>();
    if (watchModelId !== null) {
      for (const participant of data?.participants ?? []) {
        if (participant.depthM !== null && participant.positionSurveyModelId === watchModelId) {
          typed.add(participant.depthM);
        }
      }
      for (const row of events.data?.items ?? []) {
        if (row.depthEnteredM !== null && row.surveyModelId === watchModelId) {
          typed.add(row.depthEnteredM);
        }
      }
    }
    return [...typed].sort((a, b) => a - b);
  }, [data?.surveyModelId, data?.participants, events.data?.items]);

  /**
   * What each of those depths means, asked of the server that resolves them.
   *
   * <b>Asked rather than worked out here, and that is not a style preference.</b> Which altitude a
   * depth is measured from, which spellings of a station name the trip's filter matches, how ties
   * break — all of that is one rule with one home, and a browser that re-derived it would be a
   * second opinion that drifts silently and is believed. So the client asks the same question the
   * preview beside the report form asks, and reads the distance off the answer.
   *
   * Only where the question can be answered: resolving a depth needs a model, and the route is
   * open to accounts that may write to this log. A reader without that sees the two facts side by
   * side and labelled — which is the part that was actually misleading — and no distance between
   * them.
   */
  const depthReadings = useTrackingDepthReadings(
    trip.id,
    askedDepths,
    canEdit && data?.surveyModelId != null,
  );

  /**
   * The photographs hung on this trip's moments.
   *
   * <b>Read on the tab rather than inside the survey panel, and that is the fix rather than a
   * convenience.</b> A moment picture belongs to an instant of this trip and not to a place in a
   * cave, so everything about it — that it exists, when it was taken, who it is about — is legible
   * to a reader with no model on screen, and to one who may read the trip but may never be told
   * where the cave is. Asked for only where there is something to ask about: a trip nobody ever
   * watched has no moments, so it can have no pictures on them and the request would be spent
   * learning that.
   */
  const pictureLinks = useTripMomentPictureLinks(trip.id, data?.armedAt != null);
  const momentPictures = useMemo(
    () => replayPictures(pictureLinks.data?.items ?? [], trip.id),
    [pictureLinks.data, trip.id],
  );

  if (isPending) {
    return <Skeleton active />;
  }

  /**
   * Only when there is no watch at all.
   *
   * <b>A failed poll is not a failed tab, and the difference is everything this surface holds.</b>
   * The read refreshes itself every thirty seconds while a party is underground, and a query that
   * fails a refresh reports the failure while still holding the answer it had. Treating that as
   * "there is nothing here" tore the whole watch down every time a connection blinked: the
   * configuration, the share panel, the table, the log, the report being typed off a phone call,
   * and the survey model — whose viewer is keyed on the file it was given, so the recovery was to
   * download and parse the entire survey again. None of that was news about the trip; it was one
   * request that did not come back. So this is the genuine empty case only, and the failure is said
   * further down, beside everything it did not destroy.
   */
  if (!data) {
    return (
      <Alert
        type="error"
        showIcon
        title={t('trips.tracking.unavailable')}
        // Why, where the server settled it in words. A first read that merely failed to arrive is
        // left at the title alone: naming a reason nobody was given would be a guess drawn as an
        // explanation on the surface least able to afford one.
        description={refused ? trackingReadRefusalMessage(error, t) : undefined}
      />
    );
  }

  const names = new Map(trip.participants.map((person) => [person.caverId, person.name]));
  const teamTitles = new Map(data.teams.map((team) => [team.id, team.title]));
  const named = (caverId: string) => names.get(caverId) ?? t('trips.tracking.unknownCaver');
  const when = (value: string | null) =>
    value ? new Date(value).toLocaleString(i18n.language) : '—';
  // Taken once per render rather than per row, so every age on the screen is measured from one
  // moment: two rows a millisecond apart rounding to different minutes would be a table disagreeing
  // with itself about how long it has been.
  const now = Date.now();
  const standings = trackingStandings(data.participants);

  /**
   * The latest moment anybody was reported at, off the page of reports this tab is holding.
   *
   * The largest rather than the first: reports are listed by the moment they were <em>said</em>,
   * which a coordinator can backdate, and only the largest is the latest whatever the page's order
   * turns out to be. Null on a watch that has been armed and has heard nothing yet.
   */
  const lastReportAt = (events.data?.items ?? []).reduce<number | null>((latest, row) => {
    const at = Date.parse(row.recordedAt);
    return Number.isFinite(at) && (latest === null || at > latest) ? at : latest;
  }, null);

  /**
   * Where the photographs card opens its dialog when no particular report was pressed.
   *
   * A camera's own file usually says when each picture was taken and that is what gets stored, so
   * this is only the fallback for the ones whose file says nothing — and "some time during this
   * trip" is a better guess for those than "now", which is whenever somebody happened to sit down
   * with the memory card.
   */
  const defaultPictureMoment =
    lastReportAt ?? (data.armedAt === null ? now : Date.parse(data.armedAt));

  /**
   * How long ago somebody was last heard from, with the clock time kept for whoever wants it.
   *
   * <b>A watch is read for the gap, not for the clock.</b> The question this screen exists to
   * answer is "has anybody heard from them lately", and a timestamp makes the person asking it
   * subtract two times in their head, during a callout, having been awake since five. The exact
   * moment is still there on hover and in the log below, which is the record.
   *
   * Silence is drawn as silence rather than as a dash: an empty cell in a column of ages reads as a
   * rendering gap, and the one row on this table that nobody has said a word about is the row that
   * must not be mistaken for a missing value.
   */
  const lastHeard = (participant: TrackingParticipant) => {
    const words = lastHeardInWords(participant, now, i18n.language);
    if (words === null) {
      return (
        <Typography.Text type="secondary" data-testid="trip-tracking-never-heard">
          {t('trips.tracking.neverHeard')}
        </Typography.Text>
      );
    }
    return <Typography.Text title={when(participant.lastRecordedAt)}>{words}</Typography.Text>;
  };

  /**
   * Where one person stands, as one of three and never as two.
   *
   * Underground, out, and nobody has said anything at all. The third is the one that goes missing
   * when a surface asks "are they out?" and draws the answer as a pair — and on this tab it is the
   * state the whole watch exists to notice, because a caver nobody has reported is not the same
   * news as a caver who has been reported safely out.
   */
  const standingTag = (participant: TrackingParticipant) => {
    const standing = trackingStandingOf(participant);
    // Written out rather than assembled from the standing: the check that every key the code asks
    // for exists reads them out of the source text, and a key built at the call is a key it cannot
    // see — on wording that says whether somebody is still in a cave.
    const label: Record<TrackingStanding, string> = {
      underground: t('trips.tracking.standing.underground'),
      out: t('trips.tracking.standing.out'),
      unheard: t('trips.tracking.standing.unheard'),
    };
    return (
      <Tag
        color={standing === 'underground' ? 'blue' : standing === 'out' ? 'green' : 'default'}
        data-testid={`trip-tracking-standing-${standing}`}
      >
        {label[standing]}
      </Tag>
    );
  };

  /**
   * What the trip's published page will call one person — answered before a link is minted rather
   * than after somebody's family has read it.
   *
   * <b>The name drawn for the "no caption" case is the one this application holds, and it is not a
   * promise about the exact string.</b> A published page names people from the roster's own record,
   * while every signed-in surface — this table included — calls somebody by the display name their
   * account chose, where they have one. The two are the same person and usually the same words, and
   * where they differ the published page is the plainer of the two. What is exact is the part that
   * matters here: whether that page prints a name at all, and what a caption makes it print
   * instead. That limit is said on the page as well as here — see the paragraph above the table,
   * and the sentence this cell's own tooltip ends with. A reader who is told the string and not
   * told its one qualification has been given a promise this surface cannot keep.
   */
  const publicNameOf = (participant: TrackingParticipant) => {
    const kind = publicNamingOf(participant, data.publishesRealNames);
    const testId = `trip-tracking-public-name-${participant.caverId}`;
    if (kind === 'caption') {
      return (
        <Tooltip title={t('trips.tracking.publicName.captionDetail')}>
          <Tag color="purple" data-testid={testId}>
            {participant.label}
          </Tag>
        </Tooltip>
      );
    }
    if (kind === 'realName') {
      return (
        <Tooltip title={t('trips.tracking.publicName.realNameDetail')}>
          {/* The server's own answer where it has one, and this table's name for the person only as
              a fallback for a build that does not send it. They differ exactly where a member has
              chosen a display name: every signed-in screen calls them by it and the published page
              prints the roster's name. Preferring the server's here is what makes this cell the
              answer to "what will a follow link print" rather than an approximation of it — and the
              answer has to be exact, because somebody reading it is deciding whether to ask for a
              caption. */}
          <span data-testid={testId}>{participant.publishedAs ?? named(participant.caverId)}</span>
        </Tooltip>
      );
    }
    return (
      <Tooltip title={t('trips.tracking.publicName.placeInPartyDetail')}>
        <Typography.Text type="secondary" data-testid={testId}>
          {t('trips.tracking.publicName.placeInParty')}
        </Typography.Text>
      </Tooltip>
    );
  };

  /**
   * An absence where a position would be, said as strongly as it is actually known.
   *
   * `certain` is for the absence that can only be a withholding — a report whose kind always
   * carries a place, arriving without one. Everything else is the absence this reader genuinely
   * cannot resolve: a caver whose last word was a note may have been placed an hour ago and be
   * having that position kept back, or may never have been placed at all. Saying "this position
   * exists and you may not be told it" there would be a claim about the world made from a gap in
   * what was sent, which is the same mistake as drawing a withheld position as nobody knowing.
   */
  const withheldTag = (certain: boolean) => (
    <Tooltip
      title={t(
        certain
          ? 'trips.tracking.positionWithheldDetail'
          : 'trips.tracking.positionMaybeWithheldDetail',
      )}
    >
      <Tag
        icon={<EyeInvisibleOutlined />}
        data-testid={
          certain ? 'trip-tracking-position-withheld' : 'trip-tracking-position-maybe-withheld'
        }
      >
        {t(certain ? 'trips.tracking.positionWithheld' : 'trips.tracking.positionMaybeWithheld')}
      </Tag>
    </Tooltip>
  );

  /**
   * A place that was reported and was not measured in the survey this watch now uses.
   *
   * <b>The name stays on screen and is marked, rather than being taken off it.</b> This table is
   * the coordinator's own record of what was said: `cave.deep.3` is a true account of a report
   * somebody made, and blanking it would hide the trip's history from the person keeping it. What
   * is not true is that it says where this person is *now* — a station path means whatever the
   * survey it was measured in says it means, so the same path in the corrected survey may be
   * another chamber or nowhere at all. Unmarked, with a freshness age under it, the cell reads as
   * a current place on the survey in use, which is the sentence this mark exists to prevent.
   *
   * <b>Marked here as well as on the model, because this is the surface that is actually read.</b>
   * The 3D panel says it too, and says it well — but it is opened deliberately and costs a model
   * download, while this table is the first thing on the tab and is where somebody looks to answer
   * "where is everybody". One of the two saying so is not enough.
   *
   * It covers the report whose survey has been deleted for the same reason and by the same rule:
   * the station outlives the survey it was measured in, so it names a place nothing on this server
   * can resolve any more.
   */
  const otherModelTag = (caverId: string) => (
    <Tooltip title={t('trips.tracking.positionOtherModelDetail')}>
      <Tag
        color="warning"
        icon={<WarningOutlined />}
        className="tracking-position-other-model"
        data-testid={`trip-tracking-position-other-model-${caverId}`}
      >
        {t('trips.tracking.positionOtherModel')}
      </Tag>
    </Tooltip>
  );

  /**
   * A station that was reported against the survey in force, and that the drawing of it cannot
   * show.
   *
   * <b>Not the tag above it, and the difference is the one this mark exists for.</b> That one says
   * the report was measured in a <em>different</em> survey — two recorded identifiers that do not
   * match, which this page can work out for itself from the row. This one says the identifiers
   * match perfectly and the drawing still holds no station of that name: a survey re-exported with
   * its stations renamed does that to every place reported before it, under the same model id,
   * with nobody having touched the watch. Nothing in a row can reveal it. The viewer in the panel
   * below is asked, and this is its answer.
   *
   * <b>The station stays on screen and is marked, for the same reason the other one is.</b> It is
   * a true record of what was said and it is what would be read out over a phone; what is not true
   * is that anybody can be found at it on the drawing below.
   */
  const notOnModelTag = (caverId: string) => (
    <Tooltip title={t('trips.tracking.positionNotOnModelDetail')}>
      <Tag
        color="warning"
        icon={<WarningOutlined />}
        className="tracking-position-not-on-model"
        data-testid={`trip-tracking-position-not-on-model-${caverId}`}
      >
        {t('trips.tracking.positionNotOnModel')}
      </Tag>
    </Tooltip>
  );

  /**
   * How far the station a depth was recorded at sits from the depth itself, where that is knowable.
   *
   * The model each report was made against is carried through rather than assumed, because the
   * watch can be re-pointed while the party is underground and the rule that decides whether a
   * stored place is still comparable lives in one place beside its reasoning.
   */
  const gapOf = (report: {
    stationName: string | null;
    askedDepthM: number | null;
    surveyModelId: string | null;
  }): TrackingDepthGap | null =>
    recordedDepthGap(
      report,
      data?.surveyModelId ?? null,
      report.askedDepthM === null ? undefined : depthReadings.get(report.askedDepthM),
    );

  /**
   * A place, drawn as the two different facts it is made of.
   *
   * <b>This used to be one string with a middle dot in it, and the dot was the defect.</b> A depth
   * report carries two numbers that are not two readings of one thing: the station the server
   * resolved to, which is what is stored and what the model draws, and the depth somebody typed,
   * which is what was asked for. Joined, they read as one description — "P3 · 120 m" says that P3
   * is at 120 m. Nothing guarantees it. Resolution takes whichever station of the trip's filter is
   * nearest with no tolerance beneath it, so a coordinator who types 1200 for 120 against a 140 m
   * cave gets the bottom of the system, and the row then says that station is at 1200 m. It is not;
   * it is 1060 m away from it, and the joined row is the sentence that hides that.
   *
   * So the two are drawn as two. The station stands as the answer, because it is what was recorded
   * and what everything downstream acts on; the depth goes underneath it, in words that say it is
   * what was reported rather than what it came out as.
   *
   * <b>And the distance between them is said only when it is worth saying.</b> A party reported at
   * 120 m and placed at a station 0.4 m away needs no remark — the survey simply has no station at
   * exactly 120 m, which is the ordinary case, and a row that cried wolf on every depth report
   * would teach a coordinator to read past the one that mattered. What counts as far enough is a
   * caving judgement and lives in one place, next to its reasoning.
   */
  const place = (stationName: string | null, depthM: number | null, gap: TrackingDepthGap | null) => {
    if (depthM === null) {
      return stationName;
    }
    return (
      <>
        {/* An element of its own rather than a loose text node, so that the station is something a
            reader — and a test — can point at as one thing separate from the depth beside it. */}
        <span className="tracking-position-station">{stationName}</span>
        {/* Never on the same line as the station, in either layout. The whole failure being
            repaired is two numbers reading as one fact, and a phone would wrap them back together
            the moment a station name got long. */}
        <Typography.Text type="secondary" className="tracking-position-asked">
          {t('trips.tracking.positionAsked', { depth: depthM })}
        </Typography.Text>
        {gap?.wide === true && (
          <Tooltip
            title={t('trips.tracking.positionGapDetail', {
              asked: Math.abs(depthM),
              station: gap.stationName,
              depth: gap.stationDepthM,
              gap: gap.gapM,
            })}
          >
            <Tag
              color="warning"
              icon={<WarningOutlined />}
              className="tracking-position-gap"
              data-testid="trip-tracking-position-gap"
            >
              {t('trips.tracking.positionGap', { gap: gap.gapM })}
            </Tag>
          </Tooltip>
        )}
      </>
    );
  };

  /**
   * One person's last known place, and the moment that placed them there where there is one.
   *
   * Nothing at all is drawn for somebody nobody has reported yet — there is no position to
   * withhold from anybody, so saying "withheld" there would invent a secret.
   *
   * For everybody else the strength of the claim follows what the last report was. Going in,
   * coming out and a radio note carry no place at all, so they are the ordinary early-trip state
   * and the commonest reason a row has no position — calling those withheld would tell a
   * co-ordinator, five minutes after the party went in, that the page is hiding every position on
   * it. A station or a depth report always carries a place, so an empty one is a withholding and
   * can be nothing else, and that is the only case said as a fact.
   *
   * <b>Only the branch that draws a place hands back a moment, and that is the whole guard.</b> A
   * withheld position and one nobody ever reported arrive identically — the server sends no moment
   * for either, deliberately, because which of the two it is is itself something a reader without
   * the right to place the cave may not learn. Answering the moment here rather than beside the
   * drawing means an absence cannot acquire an age by anything written later: there is no moment
   * in scope to draw. What is refused above all is the obvious repair — filling the gap from the
   * last word — which is the very sentence this whole change exists to stop the table saying.
   */
  const positionOf = (
    participant: TrackingParticipant,
  ): { shown: ReactNode; placedAt: string | null } => {
    if (participant.stationName !== null || participant.depthM !== null) {
      const shown = place(
        participant.stationName,
        participant.depthM,
        gapOf({
          stationName: participant.stationName,
          askedDepthM: participant.depthM,
          surveyModelId: participant.positionSurveyModelId,
        }),
      );
      // Two questions in order, and they are not the same question. The first is which survey this
      // place was measured in, which two stored ids answer. The second is whether the drawing of
      // that survey actually holds the station — which only the viewer below can answer, and which
      // it is asked about a place that passed the first test. A report measured elsewhere is not
      // drawn at all, so it is never also marked as missing from a drawing it was never on.
      const here = drawableOn(participant.positionSurveyModelId, data.surveyModelId);
      // Looked up by the station's own name, which is also what keeps a depth report out of this:
      // a row placed by metres alone names no station, so there is nothing to find and nothing
      // about the drawing to say.
      const missing =
        here && participant.stationName !== null && unplacedStations.has(participant.stationName);
      return {
        shown:
          here && !missing ? (
            shown
          ) : (
            <>
              {shown}
              {here ? notOnModelTag(participant.caverId) : otherModelTag(participant.caverId)}
            </>
          ),
        placedAt: participant.positionRecordedAt,
      };
    }
    if (participant.lastRecordedAt === null || !data.positionsWithheld) {
      return { shown: '—', placedAt: null };
    }
    return {
      shown: withheldTag(
        participant.lastKind === 'atStation' || participant.lastKind === 'atDepth',
      ),
      placedAt: null,
    };
  };

  /**
   * The "where" cell: the place, with how long ago it was reported under it.
   *
   * <b>Under the place rather than in a column of its own, and worded rather than bare.</b> The age
   * belongs to the place — it is the answer to "how old is this station", not to a fifth question —
   * so it is drawn inside the same cell, where it cannot be read against the wrong heading. And it
   * says "reported" rather than standing as a bare figure: two bare ages one column apart, under
   * "Where" and under "Last heard", are two numbers a tired reader at four in the morning will
   * eventually read as the same fact. The exact moment stays on hover, as it does for the last
   * word, for whoever wants the clock rather than the gap.
   */
  const positionCell = (participant: TrackingParticipant) => {
    const { shown, placedAt } = positionOf(participant);
    const since = positionAgeInWords(placedAt, now, i18n.language);
    return (
      <>
        {shown}
        {since !== null && (
          <Typography.Text
            type="secondary"
            className="tracking-position-age"
            title={when(placedAt)}
            data-testid={`trip-tracking-position-age-${participant.caverId}`}
          >
            {t('trips.tracking.positionSince', { since })}
          </Typography.Text>
        )}
      </>
    );
  };

  /**
   * What one report says about a place. A station report and a depth report always carry one, so
   * an empty one on either of those kinds is a withholding and nothing else — there is no second
   * reading of it, unlike the folded position above.
   */
  const eventPlace = (row: TrackingEvent) => {
    if (row.stationName !== null || row.depthEnteredM !== null) {
      return place(
        row.stationName,
        row.depthEnteredM,
        gapOf({
          stationName: row.stationName,
          askedDepthM: row.depthEnteredM,
          surveyModelId: row.surveyModelId,
        }),
      );
    }
    return row.kind === 'atStation' || row.kind === 'atDepth' ? withheldTag(true) : '—';
  };

  const teamOf = (teamId: string | null) =>
    teamId && teamTitles.has(teamId) ? <Tag>{teamTitles.get(teamId)}</Tag> : '—';

  const kindOf = (kind: TrackingParticipant['lastKind'], out: boolean) =>
    kind ? <Tag color={out ? 'default' : 'blue'}>{t(`trips.tracking.kinds.${kind}`)}</Tag> : '—';

  /**
   * One field of a row, said as a label and an answer stacked under each other.
   *
   * <b>The column headings become these labels, and that is the whole of why this layout exists.</b>
   * Five columns of a watch do not fit across a phone, and a table told to keep its own overflow
   * keeps it by scrolling sideways — which is not the same as showing it. Measured at 412px with
   * both tables at rest: the participants' "Where" began 139px past the right edge and the log's
   * delete control 457px past it, so the answer to "where is everybody", and the only way to take a
   * wrong report off the log, were both reachable only by a horizontal drag inside a table that
   * gives no sign it has more to the right. Stacked, every field of every row is on screen at rest
   * and the page scrolls the way a page scrolls.
   */
  const fact = (label: string, value: ReactNode) => (
    <div className="tracking-stacked-fact" key={label}>
      {/* Said with the library's own secondary text rather than a colour of this stylesheet's own:
          the label has to recede from its answer in both themes, and a colour written here would
          have to be written twice and kept in step by hand. */}
      <Typography.Text type="secondary" className="tracking-stacked-label">
        {label}
      </Typography.Text>
      <span className="tracking-stacked-value">{value}</span>
    </div>
  );

  /**
   * How big everything somebody presses on this surface is drawn.
   *
   * `small` on a desk is what these controls have always been; `large` is where the forty pixels
   * come from, built by antd out of `controlHeightLG` — the touch target the rest of this
   * application uses. Asked for by size rather than set as a height, so the padding, line height
   * and icon inside each control are built for the size the control believes it is.
   */
  const controlSize: 'large' | 'small' = coarse ? 'large' : 'small';
  /** A confirmation is two more things to press, and they are pressed by the same finger. */
  const confirmSizes = { okButtonProps: { size: controlSize }, cancelButtonProps: { size: controlSize } };

  const onDeleteEvent = async (eventId: string) => {
    try {
      await deleteEvent.mutateAsync({ tripLogId: trip.id, eventId });
      message.success(t('trips.tracking.eventDeleted'));
    } catch (failure) {
      message.error(trackingProblemMessage(failure, t));
    }
  };

  /** The one control that takes a report off a log nothing can edit — the same one in both layouts. */
  const deleteControl = (row: TrackingEvent) => (
    <Popconfirm
      title={t('trips.tracking.eventDeleteConfirm')}
      onConfirm={() => void onDeleteEvent(row.id)}
      {...confirmSizes}
    >
      {/* A button rather than a bare icon. The icon on its own was 14px square — the smallest
          thing on the page, and the only way to take a wrong report off a log that cannot be
          edited. */}
      <Button
        type="text"
        size={controlSize}
        icon={<DeleteOutlined />}
        aria-label={t('trips.tracking.eventDelete')}
        data-testid={`trip-tracking-event-delete-${row.id}`}
      />
    </Popconfirm>
  );

  /**
   * The offer to hang photographs on the moment one report was made at.
   *
   * <b>On the log row because the log is where a trip is turned into a report.</b> Somebody
   * emptying a memory card a week later reads down the rows — "here is where we were at 14:05" —
   * and this is the one press between that row and the photographs of it. It carries the row's
   * caver across as the subject, since a report is about somebody and the picture beside it almost
   * always is too.
   *
   * <b>What it attaches to is the instant, not the row.</b> The row supplies a clock and nothing
   * else: deleting it — which is how this log is corrected — leaves the photographs exactly where
   * they are.
   */
  const attachControl = (row: TrackingEvent) => {
    const at = Date.parse(row.recordedAt);
    if (!Number.isFinite(at)) {
      return null;
    }
    return (
      <Button
        type="text"
        size={controlSize}
        icon={<PictureOutlined />}
        aria-label={t('trips.tracking.pictures.attachAtReport')}
        onClick={() => setAttaching({ at, caverId: row.caverId })}
        data-testid={`trip-tracking-event-picture-${row.id}`}
      />
    );
  };

  /**
   * What the published page calls somebody, and the way to change it — one cell, in both layouts.
   *
   * The reading and the writing are together on purpose. A caption is set because a person asked to
   * be kept off a public page, and the act of setting one is inseparable from checking what that
   * page would otherwise have said about them: put in a card of its own, the two would be a list of
   * names in one place and a list of what they publish to in another, which is exactly the pairing
   * somebody gets wrong at speed.
   */
  const publicNameCell = (participant: TrackingParticipant) => (
    <Flex gap={4} align="center" wrap>
      {publicNameOf(participant)}
      {canEdit && (
        <Button
          type="text"
          size={controlSize}
          icon={<EditOutlined />}
          aria-label={t('trips.tracking.publicName.edit')}
          onClick={() => setNaming(participant)}
          data-testid={`trip-tracking-public-name-edit-${participant.caverId}`}
        />
      )}
    </Flex>
  );

  return (
    <Space orientation="vertical" size="middle" style={{ width: '100%' }}>
      {/* The watch could not be re-read, said above everything it did not take away rather than in
          place of it — see the guard above. Which of the two things it means is not the reader's to
          work out: a refusal the server settled ends the watch until somebody acts, and a poll that
          did not get through ends nothing. */}
      {error != null &&
        (refused ? (
          /* No retry offered, deliberately. The same request will be refused every time it is made,
             so a button promising otherwise would cost a coordinator presses and time during a
             callout and teach them the page is broken rather than that the answer has changed.
             What replaces it is the server's own reason, which is the only thing that can be acted
             on. */
          <Alert
            type="error"
            showIcon
            title={t('trips.tracking.refusedTitle')}
            description={t('trips.tracking.refusedBody', {
              reason: trackingReadRefusalMessage(error, t),
            })}
            data-testid="trip-tracking-refused"
          />
        ) : (
          /* The retry is the read this panel already holds: the poll comes round again on its own,
             and a coordinator who does not want to wait thirty seconds for it should not have to. */
          <Alert
            type="warning"
            showIcon
            title={t('trips.tracking.staleTitle')}
            description={t('trips.tracking.staleBody')}
            data-testid="trip-tracking-stale"
            action={
              <Button
                size={controlSize}
                loading={isFetching}
                onClick={() => void refetch()}
                data-testid="trip-tracking-retry"
              >
                {t('common.retry')}
              </Button>
            }
          />
        ))}

      {/* Said once, at the top, as well as marked on every row it applies to: a reader who is
          being shown fewer positions than exist has to learn that from the page rather than from
          the shape of what is missing. */}
      {data.positionsWithheld && (
        <Alert
          type="info"
          showIcon
          title={t('trips.tracking.positionsWithheldTitle')}
          description={t('trips.tracking.positionsWithheldBody')}
          data-testid="trip-tracking-positions-withheld"
        />
      )}

      <TrackingConfigCard
        tripLogId={trip.id}
        caveIds={trip.caveIds}
        tracking={data}
        canEdit={canEdit}
        // Whether this trip has an arrangement to notice the party has not come back. Handed down
        // to be read, never written: the card states plainly that a watch raises no alarm and
        // names the callout as the thing that does, and that sentence has to be true of this trip
        // rather than of the product — on a trip with no callout arranged it would otherwise send
        // a reader up the page to a panel that drew nothing.
        calloutState={trip.calloutState}
        onStale={() => void refetch()}
      />

      {/* <b>That this trip is published, to everybody who can read the trip.</b>
          Deliberately outside the publishing panel below, which is drawn only for somebody who may
          run the watch: until this existed, a caver whose real name was on a public page — by the
          installation's default, decided by somebody else — had no way at all to find out. The
          list of follow links takes write access and carries tokens; this is the fact and none of
          the capability, which is why it names no address and offers no way to open one.

          Drawn as a warning rather than as information, and the colour is the argument: this is
          the state in which what the rows below say about people is readable by anybody holding a
          link, and a reader who skims past it has missed the one thing on this tab that is about
          them rather than about the trip. */}
      {data.publishedAt != null && (
        <Alert
          type="warning"
          showIcon
          style={{ marginBottom: 16 }}
          title={t('trips.tracking.published.title')}
          data-testid="trip-tracking-published"
          description={
            <Flex vertical gap={4} style={{ marginTop: 4 }}>
              <span data-testid="trip-tracking-published-since">
                {t('trips.tracking.published.since', { when: when(data.publishedAt) })}
              </span>
              {/* Said only when the server answered it. The two arrive together, so this is not a
                  case that happens — but a date rendered from a missing value would read as a real
                  promise about when the page stops, which is the one sentence here that must not be
                  invented. */}
              {data.publishedUntil != null && (
                <span data-testid="trip-tracking-published-until">
                  {t('trips.tracking.published.until', { when: when(data.publishedUntil) })}
                </span>
              )}
              {/* Points at the column that already answers "and what does it call me", rather than
                  answering it again here for one reader: this tab does not know which row is the
                  person reading, and the list names everybody including them. */}
              <span data-testid="trip-tracking-published-where">
                {t('trips.tracking.published.whereToLook')}
              </span>
            </Flex>
          }
        />
      )}

      {/* Under the setup rather than at the foot of the tab: publishing is a decision about the
          watch as it is configured — which cave, which survey — and the card above it is where
          that configuration is read. A link handed out before a model is chosen is refused. */}
      <TrackingSharePanel
        tripLogId={trip.id}
        tripTitle={trip.title}
        canEdit={canEdit}
        // What the published page will call the party, which is the installation's setting and
        // arrives on the trip's own read rather than being asked for a second time.
        publishesRealNames={data.publishesRealNames}
        // Whether anything is published at this moment — the same answer the banner above is drawn
        // from, so the panel's rows and that banner cannot say different things about one trip. The
        // list of links cannot work this out for itself: a publication also ends when the watch
        // closes and when the cave stops being publishable, and neither is written on a row.
        published={data.publishedAt != null}
      />

      <div>
        {/* <b>How the party divides, in three figures, because the third one is the one nobody was
            counting.</b> Underground and out were readable off the rows; "nobody has said a word
            about them" was not counted anywhere, and it is the figure a coordinator is actually
            watching — a party of eight with six underground and one out has somebody unaccounted
            for, and until this line existed the only way to notice was to read every row.

            Drawn as three equal cells rather than as a sentence so the three stay side by side and
            comparable at 360px, and so the silent count cannot be mistaken for a footnote to the
            other two. */}
        <div className="tracking-standings" data-testid="trip-tracking-counts">
          {/* Written out one by one rather than mapped over the three names: the check that every
              translation key the code asks for exists reads literal calls out of the source, and
              these are the words that say whether anybody is still in a cave. */}
          <div className="tracking-standing-cell">
            <span className="tracking-standing-count" data-testid="trip-tracking-count-underground">
              {standings.underground}
            </span>
            <Typography.Text type="secondary" className="tracking-standing-label">
              {t('trips.tracking.standing.underground')}
            </Typography.Text>
          </div>
          <div className="tracking-standing-cell">
            <span className="tracking-standing-count" data-testid="trip-tracking-count-out">
              {standings.out}
            </span>
            <Typography.Text type="secondary" className="tracking-standing-label">
              {t('trips.tracking.standing.out')}
            </Typography.Text>
          </div>
          <div className="tracking-standing-cell">
            <span className="tracking-standing-count" data-testid="trip-tracking-count-unheard">
              {standings.unheard}
            </span>
            <Typography.Text type="secondary" className="tracking-standing-label">
              {t('trips.tracking.standing.unheard')}
            </Typography.Text>
          </div>
        </div>

        {/* <b>What the published page will call this party, said before a link is minted rather
            than discovered after one has been handed out.</b> Which of the two sentences is true is
            an installation's setting rather than a property of this trip, so it is read from the
            server and never guessed; the caption beside each name is what overrides it, in both
            directions, for one person at a time. Both halves have to be on this tab, because the
            people named are not the person reading — they are the rest of the club. */}
        <Typography.Paragraph type="secondary" className="tracking-public-names">
          <span data-testid="trip-tracking-names-setting">
            {/* Two whole calls over two literal keys rather than one over a chosen key, for the
                reason given at the counts above. Anything but an explicit "no" is worded as the
                naming case — see the rule this reads through. */}
            {data.publishesRealNames !== false
              ? t('trips.tracking.publicName.settingRealNames')
              : t('trips.tracking.publicName.settingPlaces')}
          </span>{' '}
          {t('trips.tracking.publicName.columnExplain')}
          {/* <b>The one thing on this column that is not exact, said on the page rather than only
              in a comment.</b> Every signed-in screen here calls somebody by the display name their
              account chose, where they have one; the published page prints the name the club's
              roster holds. The two are the same person and start out the same words, and they part
              company the moment a member sets a display name — so a column whose entire job is to
              answer "what will a follow link print" must not read as a promise about the exact
              string. Said only where names are published at all: on an installation that numbers
              its party, no name of any kind goes out and the caveat would be a worry about
              nothing. */}
          {data.publishesRealNames !== false && (
            <>
              {' '}
              <span data-testid="trip-tracking-roster-name">
                {t('trips.tracking.publicName.rosterName')}
              </span>
            </>
          )}
        </Typography.Paragraph>

        {/* <b>Selecting everybody is a control of this page's own, and not the checkbox antd puts
            in the table's header.</b> Two reasons, and either would be enough on its own.

            The first is that on a phone there is no header to put it in: the rows are stacked, so
            the one act this page starts with would have had nowhere to live. The second is that
            antd's header checkbox cannot be asked to appear only once. A table told to keep its own
            overflow gets a measure row, and the measure row *clones every column's title* into a
            hidden cell — so the header's live "Select all" checkbox was minted a second time inside
            a `height: 0` box marked `aria-hidden`. Measured on a desk at 1600x1000: two checkboxes
            named "Select all", the second reachable by Tab, with no visible focus anywhere on the
            page, and Space on it silently selected the whole party on the surface that decides who
            a report is about. Said here instead, it is one control, it carries its own words, and
            it is large enough to press. */}
        {canEdit && data.participants.length > 0 && (
          <Checkbox
            className="tracking-select-all"
            checked={selected.size === data.participants.length}
            indeterminate={selected.size > 0 && selected.size < data.participants.length}
            onChange={(event) =>
              setSelected(
                event.target.checked
                  ? new Set(data.participants.map((person) => person.caverId))
                  : new Set(),
              )
            }
            data-testid="trip-tracking-select-all"
          >
            {t('trips.tracking.selectEverybody', { count: data.participants.length })}
          </Checkbox>
        )}

        {/* <b>Where there is room across, five columns; where there is not, one row per caver with
            its columns stacked inside it.</b> Wide, the table is told to keep its own overflow:
            without that the inner table simply bursts out of its card — measured at 538px inside a
            364px container — and because nothing clips it the whole page gains that width, so
            reading where somebody is and pressing Save became two views of the page 334px apart.
            Narrow, there is no sideways overflow to keep, because nothing stands side by side. */}
        <Table<TrackingParticipant>
          rowKey="caverId"
          size="small"
          pagination={false}
          showHeader={!narrow}
          scroll={narrow ? undefined : { x: 'max-content' }}
          className={`tracking-table${narrow ? ' tracking-table-stacked' : ''}`}
          dataSource={data.participants}
          data-testid="trip-tracking-participants"
          locale={{ emptyText: t('trips.tracking.participantsNone') }}
          rowSelection={
            canEdit
              ? {
                  selectedRowKeys: [...selected],
                  onChange: (keys) => setSelected(new Set(keys as string[])),
                  // Said above the table instead — see the note on that control.
                  hideSelectAll: true,
                  // The column is what the tap target is made of — see the stylesheet, which gives
                  // the label the whole cell. antd's own 32px would make that cell narrower than the
                  // finger it is for.
                  columnWidth: coarse ? 48 : undefined,
                }
              : undefined
          }
          columns={
            narrow
              ? [
                  {
                    title: t('trips.tracking.columnCaver'),
                    key: 'caver',
                    render: (_value, row) => (
                      <div className="tracking-stacked">
                        <div className="tracking-stacked-head">
                          <Typography.Text strong>{named(row.caverId)}</Typography.Text>
                          {/* Beside the name rather than down among the fields: where somebody
                              stands is what the row is read for, and on a phone the fields below
                              are read only after one of these has said which row to read. */}
                          {standingTag(row)}
                        </div>
                        <div className="tracking-stacked-facts">
                          {fact(t('trips.tracking.publicName.column'), publicNameCell(row))}
                          {fact(t('trips.tracking.columnTeam'), teamOf(row.teamId))}
                          {fact(t('trips.tracking.columnLastKind'), kindOf(row.lastKind, row.out))}
                          {fact(t('trips.tracking.columnLastHeard'), lastHeard(row))}
                          {fact(t('trips.tracking.columnPosition'), positionCell(row))}
                        </div>
                      </div>
                    ),
                  },
                ]
              : [
                  {
                    title: t('trips.tracking.columnCaver'),
                    key: 'caver',
                    render: (_value, row) => (
                      <Flex gap={8} align="center" wrap>
                        <span>{named(row.caverId)}</span>
                        {standingTag(row)}
                      </Flex>
                    ),
                  },
                  {
                    title: t('trips.tracking.publicName.column'),
                    key: 'publicName',
                    render: (_value, row) => publicNameCell(row),
                  },
                  {
                    title: t('trips.tracking.columnTeam'),
                    dataIndex: 'teamId',
                    render: (teamId: string | null) => teamOf(teamId),
                  },
                  {
                    title: t('trips.tracking.columnLastKind'),
                    dataIndex: 'lastKind',
                    render: (kind: TrackingParticipant['lastKind'], row) => kindOf(kind, row.out),
                  },
                  {
                    // Named for the last word and carrying the age of the last word. The position
                    // beside it carries its own, inside its own cell — the two answer different
                    // questions and a column heading that covered both would be the wrong sentence
                    // rather than a missing one.
                    title: t('trips.tracking.columnLastHeard'),
                    key: 'lastHeard',
                    render: (_value, row) => lastHeard(row),
                  },
                  {
                    title: t('trips.tracking.columnPosition'),
                    key: 'position',
                    render: (_value, row) => positionCell(row),
                  },
                ]
          }
        />

        {/* Mounted only while somebody is being named, so the field starts empty of the last
            person's caption whatever the dialog's own lifecycle does. */}
        {naming !== null && (
          <TrackingPublicNameDialog
            open
            tripLogId={trip.id}
            // Taken from the watch rather than from the row that was pressed, so a caption saved by
            // a second coordinator between the press and the write is the one this dialog shows.
            participant={
              data.participants.find((person) => person.caverId === naming.caverId) ?? naming
            }
            caverName={named(naming.caverId)}
            publishesRealNames={data.publishesRealNames}
            onClose={() => setNaming(null)}
          />
        )}
      </div>

      {/* The same watch on the survey it is resolved against, for whoever knows the cave well
          enough for a place to mean more than its name. Drawn under the table rather than over it:
          a position that was withheld has no point to put on a model, so the reading that can say
          so in words comes first. */}
      <TrackingModelPanel
        tripLogId={trip.id}
        tracking={data}
        participants={trip.participants}
        events={events.data?.items}
        canEdit={canEdit}
        // The same selection the card below records for. Handed down as an offer rather than as a
        // requirement: pressing a station on the model opens a dialog that asks who it is about,
        // with these already chosen, so the fast path never depends on having ticked anybody.
        selectedCaverIds={[...selected]}
        onRecorded={() => setSelected(new Set())}
        // Which stations the viewer in it could not place a marker at, so the table above says it
        // too. Answered empty while the model is closed, which is what keeps this table from
        // marking a row against a drawing nobody has opened.
        onUnplacedStationsChange={setUnplacedStations}
      />

      {canEdit && (
        <TrackingReportForm
          tripLogId={trip.id}
          armed={data.state === 'armed'}
          caverIds={[...selected]}
          teams={data.teams}
          onRecorded={() => setSelected(new Set())}
        />
      )}

      <div>
        <Typography.Text strong>{t('trips.tracking.events')}</Typography.Text>
        <Typography.Paragraph type="secondary" style={{ marginTop: 4 }}>
          {t('trips.tracking.eventsCorrection')}
        </Typography.Paragraph>
        {/* A log that could not be read is not an empty log. Left to the table's own empty text,
            a refused or dropped request would say "nothing has been reported yet" under a
            participants table showing cavers at stations — a failure to learn something drawn as
            a fact about the world, on the one surface that is the record of what came in over the
            radio. The rows already held are kept on screen; what is said about them changes. */}
        {events.error != null && (
          <Alert
            type="error"
            showIcon
            title={t('trips.tracking.eventsUnavailable')}
            style={{ marginBottom: 8 }}
            data-testid="trip-tracking-events-unavailable"
          />
        )}
        <Table<TrackingEvent>
          rowKey="id"
          size="small"
          loading={events.isPending}
          pagination={false}
          showHeader={!narrow}
          scroll={narrow ? undefined : { x: 'max-content' }}
          className={`tracking-table${narrow ? ' tracking-table-stacked' : ''}`}
          dataSource={events.data?.items ?? []}
          data-testid="trip-tracking-events"
          locale={{
            emptyText:
              events.error != null
                ? t('trips.tracking.eventsUnavailable')
                : t('trips.tracking.eventsNone'),
          }}
          columns={
            narrow
              ? [
                  {
                    title: t('trips.tracking.columnLastRecordedAt'),
                    key: 'report',
                    render: (_value, row) => (
                      <div className="tracking-stacked">
                        <div className="tracking-stacked-head">
                          <Typography.Text strong>{when(row.recordedAt)}</Typography.Text>
                          {/* On the row it corrects rather than in a column of its own. That
                              column was the last of six, so on a phone it began 457px past the
                              right edge of a scroller 364px wide — the only way to take a wrong
                              report off a log nothing can edit, three screens sideways. */}
                          {canEdit && attachControl(row)}
                          {canEdit && deleteControl(row)}
                        </div>
                        <div className="tracking-stacked-facts">
                          {fact(t('trips.tracking.columnCaver'), named(row.caverId))}
                          {fact(
                            t('trips.tracking.columnLastKind'),
                            <Tag>{t(`trips.tracking.kinds.${row.kind}`)}</Tag>,
                          )}
                          {fact(t('trips.tracking.columnPosition'), eventPlace(row))}
                          {fact(t('trips.tracking.columnNote'), row.note ?? '—')}
                        </div>
                      </div>
                    ),
                  },
                ]
              : [
                  {
                    title: t('trips.tracking.columnLastRecordedAt'),
                    dataIndex: 'recordedAt',
                    render: (value: string) => when(value),
                  },
                  {
                    title: t('trips.tracking.columnCaver'),
                    dataIndex: 'caverId',
                    render: (caverId: string) => named(caverId),
                  },
                  {
                    title: t('trips.tracking.columnLastKind'),
                    dataIndex: 'kind',
                    render: (kind: TrackingEvent['kind']) => (
                      <Tag>{t(`trips.tracking.kinds.${kind}`)}</Tag>
                    ),
                  },
                  {
                    title: t('trips.tracking.columnPosition'),
                    key: 'position',
                    render: (_value, row) => eventPlace(row),
                  },
                  {
                    title: t('trips.tracking.columnNote'),
                    dataIndex: 'note',
                    render: (note: string | null) => note ?? '—',
                  },
                  ...(canEdit
                    ? [
                        {
                          title: '',
                          key: 'actions',
                          render: (_value: unknown, row: TrackingEvent) => (
                            <Flex gap={4} align="center">
                              {attachControl(row)}
                              {deleteControl(row)}
                            </Flex>
                          ),
                        },
                      ]
                    : []),
                ]
          }
        />
      </div>

      {/* The trip's photographs, under the log they belong beside and outside the survey panel
          entirely — see the panel's own note for why that placement is the point of it. Offered
          only for a trip that was actually watched: a moment of a trip nobody watched is not a
          thing, and an empty card promising one would be an invitation to nothing. */}
      {data.armedAt !== null && (
        <TrackingMomentPictures
          tripLogId={trip.id}
          pictures={momentPictures}
          loading={pictureLinks.isPending}
          failed={pictureLinks.error != null}
          canEdit={canEdit}
          nameOf={named}
          // The moment this opens at is the last word on the log, falling back to when the watch
          // was armed — see where that is worked out. The subject is whoever is ticked on the
          // table above, offered rather than imposed: the dialog asks, and its chooser is where
          // the answer is actually settled.
          onAttach={() =>
            setAttaching({ at: defaultPictureMoment, caverId: [...selected][0] ?? null })
          }
        />
      )}

      {/* Mounted only while something is being attached, so the dialog's fields start from the
          moment that was actually pressed rather than from the last one. */}
      {canEdit && attaching !== null && (
        <TrackingPicturesDialog
          open
          tripLogId={trip.id}
          defaultAt={attaching.at}
          defaultCaverId={attaching.caverId}
          cavers={trip.participants.map((person) => ({
            caverId: person.caverId,
            name: person.name,
          }))}
          onClose={() => setAttaching(null)}
        />
      )}
    </Space>
  );
}
