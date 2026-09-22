// SPDX-License-Identifier: AGPL-3.0-or-later
import { lazy, Suspense, useEffect, useMemo, useState, type CSSProperties, type ReactNode } from 'react';
import {
  EyeInvisibleOutlined,
  HistoryOutlined,
  QuestionCircleOutlined,
  WarningOutlined,
} from '@ant-design/icons';
import { Alert, Card, Collapse, Flex, Result, Skeleton, Spin, Tabs, Tag, Typography, theme } from 'antd';
import { useTranslation } from 'react-i18next';
import { useParams, useSearchParams } from 'react-router-dom';
import {
  usePublicPastTrips,
  usePublicTrip,
  type PublicTripParticipant,
} from '../../api/hooks.ts';
import CaveViewPanel, {
  type CaveViewFocusRequest,
} from '../../components/caveview/CaveViewPanel.tsx';
import { noStationsMissing } from '../../caveview/placedOnModel.ts';
import { envelopeCrsLookup, publicTrackedCavers } from '../../caveview/publicTrackedCavers.ts';
import { usePublishedStationMedia } from '../../caveview/useStationMedia.ts';
import { unnamedViewerFileName } from '../../caveview/viewerFileName.ts';
import { useIsMobile } from '../../hooks/useIsMobile.ts';
import { usePublishedSheets } from '../../rastermap/publishedSheets.ts';
import { VIEW_KIND_ICONS } from '../../rastermap/viewKindIcons.tsx';
import { followedStation } from './pastTrackReplay.ts';
import { usePinnedModelUrl } from './pinnedModelUrl.ts';
import PublicPastBar from './PublicPastBar.tsx';
import PublicPastTripList from './PublicPastTripList.tsx';
import {
  partyByTeam,
  partyStandings,
  positionAgeInWords,
  sinceInWords,
  standingOf,
  tripDateRange,
} from './publicTripParty.ts';
import { usePastTripPlayback } from './usePastTripPlayback.ts';
import { readPastLink, writePastLink } from './pastTripLink.ts';
import './PublicTripPage.css';

/**
 * Loaded when a trip actually carries sheets, not with the page: the sheet pane pulls in
 * OpenLayers, which nothing else on the public bundle needs, and the ordinary published trip
 * has no maps — its readers should not download a map engine to watch a party on a 3D drawing.
 */
const PublicTripSheetPane = lazy(() => import('./PublicTripSheetPane.tsx'));

/** The 3D pane's tab key — every sheet's key is derived from a URL and cannot collide with it. */
const TAB_3D = '3d';

/**
 * A tracked trip as somebody without an account follows it.
 *
 * <b>Nothing on this page belongs to the application around it.</b> No navigation, no sign-in
 * offer, no link of any kind — a visitor here was handed one address by somebody they know, and
 * every other address in this installation refuses them. A page that showed them the way in would
 * be showing them a door that is locked, and a page that carried the workspace's chrome would
 * imply they are inside something they are not.
 *
 * <b>Designed at 360px.</b> A live-trip link is opened on a phone, usually by somebody who is not
 * a caver and is checking whether the party is out yet; the desk layout is what the widths above
 * 760px get, and it is the exception here rather than the starting point.
 *
 * <b>Every unusable token is one page.</b> Malformed, unknown, revoked, a cave somebody has since
 * protected, a trip that is gone: the server answers all of them identically, on purpose, because
 * telling them apart is telling a stranger which tokens exist. So there is one failure state here
 * and nothing inspects the refusal to find out which it was.
 *
 * <b>The three states of a party are drawn as three.</b> Underground, out, and nobody has said
 * anything yet — because the person reading this is waiting for the second one, and drawing the
 * third as "not underground" would tell them the party is back before it has set off.
 */
export default function PublicTripPage() {
  const { t, i18n } = useTranslation();
  const { token } = useParams<{ token: string }>();
  const narrow = useIsMobile();
  const { token: antdToken } = theme.useToken();
  const { data, isPending, error } = usePublicTrip(token);

  /**
   * The cave's past, and which of it this reader has asked to see.
   *
   * Reads nothing until a trip is chosen — see the hook, where the reason that is a rule rather
   * than a tuning is written down.
   */
  const past = usePastTripPlayback(token);
  const [search, setSearch] = useSearchParams();

  /**
   * What the page is actually showing: the party now, or a past trip wound back to a moment.
   *
   * <b>One shape, and that is what keeps this honest.</b> A moment of a past trip is folded into
   * the very envelope the live read produces, so everything below — the standings, the teams, the
   * four reasons a place cannot be drawn, the markers, the sheets, the pictures — is the same code
   * drawing the same shape. There is no second set of rules for the past to fall out of step with.
   *
   * <b>Undefined while a chosen track is still in flight, and deliberately not the live party.</b>
   * Falling back would draw the party who are underground right now under a banner saying this is
   * the past, which is the one sentence this whole feature must never produce.
   */
  const view = past.engaged ? (past.envelope ?? undefined) : data;

  // A link in somebody's prose, opened in a fresh tab: the address carries which past trip to play
  // and, where it says so, whom to keep the camera on and where to start. Applied when the address
  // changes and never afterwards, so a reader who presses "back to now" is not sent straight back
  // into the past by their own URL.
  const openPast = past.open;
  useEffect(() => {
    const asked = readPastLink(search);
    if (asked !== null) {
      openPast(asked.tripLogId, { at: asked.at, follow: asked.follow });
    }
  }, [search, openPast]);

  // The address the viewer is given: held still while it is the same survey, replaced when the
  // survey itself changes. Both halves matter and the reasoning for each lives with the rule,
  // beside the page that shares it.
  const model = view?.model ?? null;
  // Pinned per drawing rather than per token, because the past is a second drawing reached from
  // the same address: a past trip's survey is its own, often a superseded one, and a pin held
  // across the switch would draw one survey's geometry under another survey's station names.
  const pinnedModelUrl = usePinnedModelUrl(
    model?.modelUrl,
    past.tripLogId === null ? token : `${token}:${past.tripLogId}`,
  );

  // The tab a link opens in says which trip it is. Worth doing here and nowhere else in this
  // application: everything else is opened from inside a workspace whose tab is already named,
  // and this is a single page somebody keeps open beside a dozen others while they wait.
  useEffect(() => {
    if (data !== undefined) {
      document.title = data.title;
    }
  }, [data]);

  const crsLookup = useMemo(() => envelopeCrsLookup(model), [model]);

  /**
   * The pictures the envelope carried, shaped for the viewer.
   *
   * <b>Deliberately not pinned the way the model URL above is.</b> The model is pinned because the
   * viewer re-downloads and re-parses it when its address changes, and the camera goes back to the
   * opening view with it — a minute's poll must not do that to somebody watching. A picture is
   * fetched only when a finger lands on the station holding it, which is what makes pinning them
   * the wrong way round: the model URL is spent the instant it arrives and never again, while a
   * picture URL may not be spent for half an hour, by which time a signature good for ten minutes
   * has expired. A pinned set would be a page of broken thumbnails on somebody's website.
   *
   * <b>So they are kept fresh, and kept fresh without disturbing anybody.</b> The re-read that
   * re-signs them is arranged in the query itself, which goes on asking — more slowly — while a
   * published page carries pictures; and what the fresh signatures land on is the map the viewer is
   * already holding rather than a new one, so a strip standing open under somebody's thumb is not
   * closed by a poll they did not make. Both halves live in one place for the two pages that need
   * them.
   */
  const stationMedia = usePublishedStationMedia(model?.pictures);

  /**
   * The scanned map sheets the envelope carried, one tab each beside the 3D drawing.
   *
   * Kept as one array while it is the same sheets and restamped with each poll's fresh
   * signatures — the pictures' arrangement, in the module the two public pages share — so a
   * minute's poll neither rebuilds the tab strip nor reloads a sheet somebody is reading,
   * while a tab first opened late still fetches its picture on a live signature.
   */
  const sheets = usePublishedSheets(model?.rasterMaps);
  const [activeTab, setActiveTab] = useState(TAB_3D);

  // A tab whose sheet left the envelope cannot stay active: the pane it named is gone from
  // the strip, and a Tabs left pointing at nothing shows nothing.
  useEffect(() => {
    if (activeTab !== TAB_3D && !sheets.some((sheet) => sheet.key === activeTab)) {
      setActiveTab(TAB_3D);
    }
  }, [sheets, activeTab]);

  const cavers = useMemo(
    () =>
      view === undefined
        ? []
        : publicTrackedCavers(view, (ordinal) => t('publicTrip.caverOrdinal', { ordinal })),
    [view, t],
  );

  /**
   * Keeping the camera on a followed team or caver as the replay plays.
   *
   * <b>Asked for when the station changes and at no other time.</b> A request on every tick would
   * take the model away from a reader who has turned it to look at something else, five times a
   * second; a request when the followed party actually moves is the thing that was asked for. A
   * moment where the follow has nowhere honest to point — nobody reported yet, a place withheld, a
   * place measured on another survey — asks for nothing at all rather than guessing.
   */
  const followStation = followedStation(cavers, past.follow);
  const [focusRequest, setFocusRequest] = useState<CaveViewFocusRequest | undefined>();
  useEffect(() => {
    if (followStation !== null) {
      setFocusRequest({ kind: 'station', ref: followStation });
    }
  }, [followStation]);

  /**
   * Whether the archive section stands open, and therefore whether its list has been read at all.
   *
   * Opened by a reader, and opened for them when a link brought them here already playing something
   * — arriving in the past with the list that produced it shut would leave no visible way back to
   * the other trips of the cave.
   */
  const [pastOpen, setPastOpen] = useState(false);
  useEffect(() => {
    if (past.engaged) {
      setPastOpen(true);
    }
  }, [past.engaged]);
  const pastTrips = usePublicPastTrips(token, pastOpen);

  /** Choosing a trip: play it, and write it into the address so the view can be sent to somebody. */
  const play = (tripLogId: string) => {
    past.open(tripLogId, { follow: null });
    setSearch(writePastLink(search, tripLogId, null), { replace: true });
  };

  /**
   * Leaving the past, address included.
   *
   * <b>The address has to be cleared with the view, not after it.</b> A reader who presses the way
   * back and then copies what is in the bar would otherwise be sending somebody a link into a past
   * trip while believing they were sending the live page — the address would still name a trip the
   * page had stopped showing. `replace` rather than a new entry: leaving a replay is not a place in
   * the reader's history to go back to.
   */
  const leavePast = () => {
    past.backToNow();
    setSearch(writePastLink(search, null, null), { replace: true });
  };

  /**
   * The stations the drawing on this page turns out not to hold.
   *
   * <b>Read from the viewer and from nowhere else, and it is the one thing on this page the server
   * cannot answer.</b> Everything else here was decided before the envelope was sent: which places
   * may be shown at all, which were measured in another survey. Whether the file this browser
   * parsed contains a station of the name it was given is known only after it is parsed, in this
   * browser, by the viewer that parsed it. Empty until a drawing is loaded, so a phone that has not
   * downloaded the model yet claims nothing about it.
   *
   * Station paths, as the viewer answers them: a name either is one of the drawing's nodes or it is
   * not, and two followers' places that share a name share the answer.
   */
  const [unplacedStations, setUnplacedStations] = useState<ReadonlySet<string>>(noStationsMissing);
  /**
   * Whether this page was given a station and the drawing above holds no node of that name.
   *
   * Two questions in order, exactly as the coordinator's own table asks them. A place measured in
   * another survey is not drawn here at all, so it can never also be missing from a drawing it was
   * never going to be on — and a follower told both things about one person is being told two
   * different stories at once.
   */
  const offModel = (participant: PublicTripParticipant) =>
    !participant.positionOnOtherModel
    && participant.stationName !== null
    && participant.stationName.length > 0
    && unplacedStations.has(participant.stationName);

  // Antd's tokens reach the stylesheet as custom properties on the page's own root, so the
  // rules below stay readable and the colours still come from the one place they are decided.
  // Antd's tokens reach the stylesheet as custom properties on the page's own root, so the rules
  // there stay readable and the colours still come from the one place they are decided.
  const palette = {
    '--silexgis-public-bg': antdToken.colorBgContainer,
    '--silexgis-public-border': antdToken.colorBorderSecondary,
    '--silexgis-public-accent': antdToken.colorPrimary,
    // What the past is marked in, so the one signal that costs no height on a small frame reads
    // the same as the banner that costs one.
    '--silexgis-public-warning': antdToken.colorWarning,
  } as CSSProperties;

  if (isPending) {
    return (
      <Flex align="center" justify="center" style={{ minHeight: '100dvh' }}>
        <Spin size="large" />
      </Flex>
    );
  }

  // Only when there is nothing to show at all.
  //
  // The distinction this page has to keep is between "this link opens nothing" and "this phone
  // could not ask just now". A follower on a train loses signal for a few seconds, the minute's
  // poll fails, and a page that took the refusal as the answer would replace the party a family
  // is watching with a statement that the link may never have existed — for a minute, until the
  // next poll, and on evidence that says no such thing. The envelope already in hand is still
  // the last true word about where everybody was; it stays on screen, under a notice saying it
  // has stopped being refreshed.
  if (data === undefined) {
    return (
      <Flex align="center" justify="center" style={{ minHeight: '100dvh', padding: 16 }}>
        <Card style={{ maxWidth: 520, width: '100%' }}>
          <Result
            icon={<QuestionCircleOutlined />}
            status="warning"
            title={t('publicTrip.notFoundTitle')}
            subTitle={t('publicTrip.notFoundBody')}
            data-testid="public-trip-not-found"
          />
        </Card>
      </Flex>
    );
  }

  const counts = partyStandings(view?.participants ?? []);
  const groups = partyByTeam(view?.participants ?? [], view?.teams ?? []);
  /**
   * What "ago" is measured from.
   *
   * The wall clock while the live party is on screen, because that is the question a family is
   * asking. The moment on the scrubber while a past trip is playing, because there the question is
   * how long the party had been out of contact <em>then</em> — measured against today it would say
   * "6 years ago" of every position on a trip from 2019, which is true, useless, and identical for
   * the first report and the last.
   */
  const now = past.engaged ? (past.at ?? Date.now()) : Date.now();

  const when = (value: string | null) =>
    value === null ? '—' : new Date(value).toLocaleString(i18n.language);

  /**
   * Where somebody was last reported, said as strongly as this page is actually told it, and the
   * moment that placed them there where there is one.
   *
   * <b>Only the branches that draw a place carry a moment.</b> A position nobody reported and one
   * this page may not be told arrive the same way — with no moment at all — and neither may gain
   * an age from anything written later, because there is no age in scope where no place was drawn.
   * The one repair that must never be made is filling that gap from the last word, which would
   * date a station from a radio note made hours after it.
   */
  const positionOf = (
    participant: PublicTripParticipant,
  ): { shown: ReactNode; placedAt: string | null } => {
    // Read before the two absences below, because both of them would be wrong about it and one of
    // them would be the worst sentence this page can produce.
    //
    // <b>Somebody has reported where this person is.</b> The place was measured in a different
    // survey of the cave than the one drawn here — a watch re-pointed at a corrected survey while
    // the party is underground leaves every earlier report naming the survey it was made in — so
    // the server sends the station and the depth as absences, deliberately, and raises this bit to
    // say which kind of absence it is. Without reading it the page falls through to "No position
    // reported" and tells the family of somebody underground that nobody knows where they are,
    // which is false. The drawing beside it already says "on another survey"; this is the same
    // page's other half, and it is the half read on a phone.
    if (participant.positionOnOtherModel) {
      return {
        shown: (
          <Tag icon={<QuestionCircleOutlined />} data-testid="public-trip-position-other-model">
            {t('publicTrip.positionOtherModel')}
          </Tag>
        ),
        // No moment either, and for the reason the server sends none: an hour beside a place this
        // page cannot show would date something a reader can only read as the place beside it.
        placedAt: null,
      };
    }
    // A station this page was given, and the drawing above holds no station of that name.
    //
    // <b>The name stays and is marked, rather than being replaced by a tag.</b> A family reading
    // this is being told where somebody was reported, and that is still true — it is the drawing
    // that cannot show it, because the survey has been re-exported with its stations renamed since
    // the report was made. Dropping the name would take away the one thing this page is for;
    // leaving it unmarked beside a drawing showing nobody would let a reader search that drawing
    // for a dot that was never going to be there.
    if (offModel(participant)) {
      return {
        shown: (
          <>
            {participant.stationName}
            <Tag
              icon={<WarningOutlined />}
              color="warning"
              className="public-trip-position-mark"
              data-testid={`public-trip-position-not-on-model-${participant.ordinal}`}
            >
              {t('publicTrip.positionNotOnModel')}
            </Tag>
          </>
        ),
        placedAt: participant.positionRecordedAt,
      };
    }
    if (participant.stationName !== null && participant.stationName.length > 0) {
      return { shown: participant.stationName, placedAt: participant.positionRecordedAt };
    }
    if (participant.depthM !== null) {
      return {
        shown: t('trips.metres', { value: participant.depthM }),
        placedAt: participant.positionRecordedAt,
      };
    }
    // The absence that can only be a withholding cannot be identified on this surface — the
    // envelope carries no report kind — so the weaker of the two phrasings is the only one used
    // here. Over-claiming would tell a stranger something is being kept from them at the moment
    // a party has merely not set off.
    if (participant.lastRecordedAt !== null && view?.positionsWithheld === true) {
      return {
        shown: (
          <Tag icon={<EyeInvisibleOutlined />} data-testid="public-trip-position-withheld">
            {t('publicTrip.positionMaybeWithheld')}
          </Tag>
        ),
        placedAt: null,
      };
    }
    return { shown: t('publicTrip.positionUnreported'), placedAt: null };
  };

  /**
   * The place, with how long ago it was reported under it.
   *
   * <b>Two ages on this card and they must not read as one.</b> A family follows this page to know
   * whether the party has moved, and "last heard" answers something else entirely — a radio check
   * saying everyone is fine moves it and moves nobody. So the position keeps its own age, inside
   * its own fact, worded rather than bare: a number under "Last reported at" and another under
   * "Last heard" would be two figures on a phone screen that somebody worried is reading quickly.
   */
  const positionFact = (participant: PublicTripParticipant): ReactNode => {
    const { shown, placedAt } = positionOf(participant);
    const since = positionAgeInWords(placedAt, now, i18n.language);
    return (
      <>
        {shown}
        {since !== null && (
          <Typography.Text
            type="secondary"
            className="public-trip-position-age"
            title={when(placedAt)}
            data-testid={`public-trip-position-age-${participant.ordinal}`}
          >
            {t('publicTrip.positionSince', { since })}
          </Typography.Text>
        )}
      </>
    );
  };

  const standingTag = (participant: PublicTripParticipant) => {
    const standing = standingOf(participant);
    return (
      <Tag
        color={standing === 'underground' ? 'blue' : standing === 'out' ? 'green' : 'default'}
        data-testid={`public-trip-standing-${standing}`}
      >
        {t(`publicTrip.standing.${standing}`)}
      </Tag>
    );
  };

  const fact = (label: string, value: ReactNode) => (
    <div key={label}>
      <Typography.Text type="secondary" className="public-trip-fact-label">
        {label}
      </Typography.Text>
      <span className="public-trip-fact-value">{value}</span>
    </div>
  );

  /**
   * Whose title and state the header prints — and nobody's while a chosen past trip is still
   * being read, or could not be.
   *
   * <b>Falling back to the live trip here is the same mistake the body was fixed for.</b> While a
   * track is in flight the banner below already reads "You are looking at a past trip — Reading
   * this trip…"; a header that went on printing the link's own trip would set the live title and
   * its blue "underground now" tag directly under that sentence, and a reader on a slow connection
   * — or one who followed a link to a trip that has since passed this installation's retention,
   * where the state is permanent — would be told at once that a party is underground and that this
   * is the past. So the header says what it honestly has: that this is a past trip, without a name
   * for it until the name arrives. The browser tab keeps the link's own trip, because the tab is
   * the link.
   */
  const head = view ?? (past.engaged ? null : data);
  const dates =
    head === null ? null : tripDateRange(head.tripDate, head.tripDateEnd, i18n.language);

  return (
    <div className="public-trip" style={palette} data-testid="public-trip">
      {/* The title and the dates are of whatever is on screen — a past trip has its own, and a
          header still naming the live one over a replay of another trip would be the page telling
          two stories at once. The tab keeps the link's own trip, because the tab is the link. */}
      <header className="public-trip-head">
        <h1 className="public-trip-title" data-testid="public-trip-title">
          {head === null ? t('publicTrip.past.unknownTrip') : head.title}
        </h1>
        {/* The dates and the standing belong to a trip; with no trip in hand there is neither to
            print, and a state tag is the one thing on this page that must never be guessed. */}
        {head !== null && (
          <div className="public-trip-subtitle">
            <Typography.Text type="secondary">{dates}</Typography.Text>
            <Tag
              color={head.state === 'armed' ? 'blue' : 'default'}
              data-testid={`public-trip-state-${head.state}`}
            >
              {t(`publicTrip.state.${head.state}`)}
            </Tag>
          </div>
        )}
      </header>

      <main className="public-trip-body">
        {/* First thing under the title, and it stays there for as long as the past is on screen. */}
        {past.engaged && (
          <PublicPastBar
            playback={{ ...past, backToNow: leavePast }}
            liveState={data.state}
            cavers={cavers}
          />
        )}

        {view !== undefined && (
          <div className="public-trip-standing" data-testid="public-trip-counts">
            {(['underground', 'out', 'unheard'] as const).map((standing) => (
              <div className="public-trip-standing-cell" key={standing}>
                <span className="public-trip-standing-count" data-testid={`public-trip-count-${standing}`}>
                  {counts[standing]}
                </span>
                <Typography.Text type="secondary" className="public-trip-standing-label">
                  {t(`publicTrip.standing.${standing}`)}
                </Typography.Text>
              </div>
            ))}
          </div>
        )}

        {/* A poll that failed is a fact about the live read, so it is said while the live read is
            what is on screen. A reader watching a trip from 2019 does not need to be told that
            this minute's refresh of a different trip did not land. */}
        {error != null && !past.engaged && (
          <Alert
            type="warning"
            showIcon
            title={t('publicTrip.staleTitle')}
            description={t('publicTrip.staleBody')}
            data-testid="public-trip-stale"
          />
        )}

        {/* Superseded by the past banner while one is up: two notices saying "this is finished"
            would compete, and only one of them says which trip. */}
        {data.state !== 'armed' && !past.engaged && (
          <Alert
            type="info"
            showIcon
            title={t('publicTrip.closedTitle')}
            description={t('publicTrip.closedBody')}
            data-testid="public-trip-closed"
          />
        )}

        {view?.positionsWithheld === true && (
          <Alert
            type="info"
            showIcon
            title={t('publicTrip.withheldTitle')}
            description={t('publicTrip.withheldBody')}
            data-testid="public-trip-withheld"
          />
        )}

        {/* Said once for the page as well as once per person, because the tag beside a name is a
            label and this is the explanation of it — and the reader is somebody waiting for a
            party to come out, who needs to know that a place they cannot see is not a place
            nobody knows.

            `title`, like its three siblings above. This one passed `message` under a comment saying
            the library's alert had no `title` prop and that the other three were therefore losing
            their headings to a browser tooltip. That was true of the version it was written
            against and is not true of the one installed: `title` is the prop and `message` is the
            deprecated spelling of it, which this alert was announcing on the console of every
            phone that opened a trip with a place on another survey. */}
        {view?.participants.some((participant) => participant.positionOnOtherModel) === true && (
          <Alert
            type="info"
            showIcon
            title={t('publicTrip.otherModelTitle')}
            description={t('publicTrip.otherModelBody')}
            data-testid="public-trip-other-model"
          />
        )}

        {/* And the one the server could not have warned about, because it is not a fact about the
            report at all: the drawing itself has no station of the name that was reported. Said
            once for the page as well as beside each name, for the same reason its neighbour above
            is — the mark beside a name is a label, and a reader waiting for a party to come out
            needs to be told that a place the drawing cannot show is not a place nobody knows. */}
        {view?.participants.some(offModel) === true && (
          <Alert
            type="warning"
            showIcon
            title={t('publicTrip.notOnModelTitle')}
            description={t('publicTrip.notOnModelBody')}
            data-testid="public-trip-not-on-model"
          />
        )}

        {model !== null && pinnedModelUrl !== null && (
          <div className="public-trip-model">
            {/* The drawing strip: the 3D scene, and one tab per sheet the envelope carried.
                With no sheets the bar is hidden while the Tabs element itself stays in the
                tree, so the 3D pane keeps its identity — its parsed model and its WebGL
                context — across the moment a poll first brings a declaration in. Inactive
                panes stay mounted (antd's default): a sheet's picture is fetched when its tab
                is first opened and a tab switch never refetches anything. */}
            <Tabs
              activeKey={sheets.length === 0 ? TAB_3D : activeTab}
              onChange={setActiveTab}
              tabBarStyle={sheets.length === 0 ? { display: 'none' } : undefined}
              items={[
                {
                  key: TAB_3D,
                  label: t('rastermap.tab3d'),
                  children: (
                    <CaveViewPanel
                      fileUrl={pinnedModelUrl}
                      fileName={unnamedViewerFileName(model.format)}
                      // Which stations the viewer could not place a marker at, so the list of people
                      // below this one says it in words. The list is the half read on a phone.
                      onUnplacedStationsChange={setUnplacedStations}
                      // The same share of the screen the signed-in panel reserves, and for the same
                      // reason: the viewer takes every gesture that begins inside it, so it must never
                      // be the only thing under a thumb. dvh because a phone's address bar collapses.
                      height={`min(${narrow ? 300 : 440}px, 60dvh)`}
                      trackedCavers={cavers}
                      crsLookup={crsLookup}
                      // Where a followed team or caver is, when a replay is keeping up with one.
                      focusRequest={focusRequest}
                      toolbar
                      // The pictures come from the envelope and from nothing else. The hook the signed-in
                      // surfaces share reads the model's links, and that route takes an account — the
                      // visitor holding this one token is refused it, as they are refused every other
                      // address here — so reaching for it would fire a request that answers 401 where no
                      // console is being watched, and then draw exactly what a cave with no pictures
                      // draws. What the server sends is already decided: only photographs somebody
                      // published, with URLs that reach a rendering and never an upload. Nothing is
                      // filtered on the way through, because nothing here could be trusted to.
                      stationMedia={stationMedia}
                    />
                  ),
                },
                ...sheets.map((sheet) => ({
                  key: sheet.key,
                  label: (
                    <span>
                      {VIEW_KIND_ICONS[sheet.viewKind]} {sheet.title ?? t('rastermap.untitledMap')}
                    </span>
                  ),
                  children: (
                    // The pane and its map engine arrive when a sheet does; the skeleton is
                    // the moment between pressing a first map tab and the chunk landing.
                    <Suspense fallback={<Skeleton active />}>
                      <PublicTripSheetPane
                        sheet={sheet}
                        // The same fold the 3D pane draws, so the two drawings can never
                        // disagree about the party they are both showing.
                        cavers={cavers}
                        active={activeTab === sheet.key}
                        height={`min(${narrow ? 300 : 440}px, 60dvh)`}
                        token={token}
                        // The sheets follow the same team the 3D scene does: one replay, two
                        // drawings, and a reader switching tabs finds the same party in view.
                        followStation={followStation}
                      />
                    </Suspense>
                  ),
                })),
              ]}
            />
          </div>
        )}

        {view !== undefined && (
        <section data-testid="public-trip-party">
          {groups.map((group) => (
            <div key={group.teamId ?? 'no-team'}>
              <h2 className="public-trip-team-title">
                {group.title ?? t('publicTrip.noTeam')}
              </h2>
              <div className="public-trip-people">
                {group.members.map((participant) => (
                  <div
                    className="public-trip-person"
                    key={participant.ordinal}
                    data-testid={`public-trip-caver-${participant.ordinal}`}
                  >
                    <div className="public-trip-person-head">
                      <span className="public-trip-person-name">
                        {participant.label ??
                          t('publicTrip.caverOrdinal', { ordinal: participant.ordinal })}
                      </span>
                      {standingTag(participant)}
                    </div>
                    <div className="public-trip-person-facts">
                      {fact(t('publicTrip.columnPosition'), positionFact(participant))}
                      {/* The other moment, kept as its own fact under its own label: this one
                          moves when anybody says anything at all about somebody, including a note
                          that says nothing about where they are. */}
                      {fact(
                        t('publicTrip.columnLastHeard'),
                        participant.lastRecordedAt === null ? (
                          '—'
                        ) : (
                          <Typography.Text title={when(participant.lastRecordedAt)}>
                            {sinceInWords(participant.lastRecordedAt, now, i18n.language)}
                          </Typography.Text>
                        ),
                      )}
                    </div>
                  </div>
                ))}
              </div>
            </div>
          ))}
        </section>
        )}

        {/* The archive, at the bottom and shut until it is asked for.
            <b>Shut for a reason and not for tidiness.</b> This page is opened by families while a
            party is underground, on phones, in numbers nobody can see, and its one job is to say
            whether they are out. Reading a cave's whole history on every one of those opens would
            double the cost of the cheapest surface here to answer a question nobody asked — so the
            list is fetched on the press that opens the section, and a reader who never presses it
            costs exactly what they cost before this existed. */}
        <section className="public-trip-past" data-testid="public-trip-past">
          <Collapse
            ghost
            activeKey={pastOpen ? ['past'] : []}
            onChange={(keys) => setPastOpen(keys.length > 0)}
            items={[
              {
                key: 'past',
                label: (
                  <span className="public-trip-past-label">
                    <HistoryOutlined /> {t('publicTrip.past.sectionTitle')}
                  </span>
                ),
                children: (
                  <PublicPastTripList
                    trips={pastTrips.data?.trips}
                    more={pastTrips.data?.more ?? false}
                    loading={pastTrips.isPending}
                    failed={pastTrips.isError}
                    playingId={past.tripLogId}
                    onPlay={play}
                  />
                ),
              },
            ]}
          />
        </section>
      </main>

      <footer className="public-trip-foot">
        <Typography.Text type="secondary">{t('app.name')}</Typography.Text>
      </footer>
    </div>
  );
}

