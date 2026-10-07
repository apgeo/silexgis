// SPDX-License-Identifier: AGPL-3.0-or-later
import {
  lazy,
  Suspense,
  useEffect,
  useLayoutEffect,
  useMemo,
  useRef,
  useState,
  type CSSProperties,
  type ReactNode,
} from 'react';
import {
  CloseOutlined,
  EyeInvisibleOutlined,
  HistoryOutlined,
  QuestionCircleOutlined,
  TeamOutlined,
  WarningOutlined,
} from '@ant-design/icons';
import { Alert, Button, Card, Collapse, Flex, Result, Skeleton, Spin, Tabs, Tag, Typography, theme } from 'antd';
import { useTranslation } from 'react-i18next';
import { useParams, useSearchParams } from 'react-router-dom';
import { isSettledRefusal } from '../../api/client.ts';
import {
  usePublicLiveTrips,
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
import { useNow } from '../../hooks/useNow.ts';
import { usePublishedSheets } from '../../rastermap/publishedSheets.ts';
import { VIEW_KIND_ICONS } from '../../rastermap/viewKindIcons.tsx';
import { followedStation, type PastFollow } from './pastTrackReplay.ts';
import { usePinnedModelUrl } from './pinnedModelUrl.ts';
import PublicPastBar from './PublicPastBar.tsx';
import PublicLanguageButton from './PublicLanguageButton.tsx';
import PublicTripAbout from './PublicTripAbout.tsx';
import PublicLiveTripList from './PublicLiveTripList.tsx';
import PublicPastTripList from './PublicPastTripList.tsx';
import PublicWatchBar from './PublicWatchBar.tsx';
import { publicTripView, shownReadEnded, watchedParty } from './publicLiveWatch.ts';
import { readNotLanding } from './publicReadFreshness.ts';
import {
  ageInWords,
  clockInWords,
  durationInWords,
  followedSpanInWords,
  momentOrAge,
  partyByTeam,
  partyStandings,
  standingOf,
  tripDateRange,
  watchStartedAt,
} from './publicTripParty.ts';
import { usePastTripPlayback } from './usePastTripPlayback.ts';
import { usePublicLanguage } from './usePublicLanguage.ts';
import { momentLink, PAST_LINK_PARAMS, readPastLink, writePastLink } from './pastTripLink.ts';
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
  // The failure of an attempt still being retried and whether the read is being held back are
  // taken here with the rest, on every render: a page is redrawn only for the members it reads.
  const { data, isPending, error, refetch, dataUpdatedAt, failureReason, isPaused } =
    usePublicTrip(token);
  // The one clock of this page: every gap below is measured from it, and it is what redraws them
  // as time passes on a page that nothing else is redrawing.
  const present = useNow();

  /**
   * The cave's past, and which of it this reader has asked to see.
   *
   * Reads nothing until a trip is chosen — see the hook, where the reason that is a rule rather
   * than a tuning is written down.
   */
  const past = usePastTripPlayback(token);
  const [search, setSearch] = useSearchParams();
  // The language an address names, and the button that changes it. Read before every early
  // return below, so a page that has nothing to show still says so in the language asked for.
  const language = usePublicLanguage();

  /**
   * The other party of the cave this reader asked to see, if any — and its name as it stood when
   * they asked, which is what a notice can still say once the party itself is gone from the list.
   */
  const [watch, setWatch] = useState<{ tripLogId: string; title: string } | null>(null);
  /** The party whose watch ended under the reader, by name, until the notice saying so is closed. */
  const [watchEnded, setWatchEnded] = useState<string | null>(null);
  /**
   * Whether the section of parties being followed now stands open, and so whether their list has
   * been read at all.
   *
   * <b>Shut until a reader opens it, and never opened for them.</b> Who else is in the cave is
   * not what this link was handed over for, and a page that read it on every open would charge
   * every family for a list most of them never look at. Once a party from it is on screen the list
   * goes on being read with the section shut again: it is where that party's positions come from,
   * and where the page learns that the party is no longer being followed.
   */
  const [liveOpen, setLiveOpen] = useState(false);
  /**
   * The link everything a reader asked this page for was asked under.
   *
   * The page is one component across two links — the browser's Back and Forward between two of
   * them, a link on a club's own page — and what a reader chose under the first is not a choice
   * made under the second: see where it is forgotten, below. Until that has happened, nothing is
   * read under the new link on the old link's say-so.
   */
  const [askedUnder, setAskedUnder] = useState(token);
  const sameLink = askedUnder === token;
  const liveTrips = usePublicLiveTrips(token, sameLink && (liveOpen || watch !== null));
  const watchId = watch?.tripLogId ?? null;
  const ownTripLogId = data?.tripLogId;
  const watched = useMemo(
    () => watchedParty(liveTrips.data, watchId, ownTripLogId),
    [liveTrips.data, watchId, ownTripLogId],
  );

  /**
   * What the page is actually showing: the link's own party now, another party of the cave being
   * followed now, or a past trip wound back to a moment.
   *
   * <b>One shape, and that is what keeps this honest.</b> A moment of a past trip is folded into
   * the very envelope the live read produces, and so is a row of the list of parties being
   * followed, so everything below — the standings, the teams, the four reasons a place cannot be
   * drawn, the markers, the sheets, the pictures — is the same code drawing the same shape. There
   * is no second set of rules for either to fall out of step with.
   *
   * <b>Undefined while what was asked for is not in hand, and deliberately not the link's own
   * party.</b> Falling back would draw the party who are underground right now under a banner
   * saying this is the past, or this is somebody else — the one sentence these views must never
   * produce. Which of the three it is, and that rule, are decided in one place beside this page.
   */
  const pastEngaged = past.engaged;
  const pastEnvelope = past.envelope;
  const { mode, envelope: view } = useMemo(
    () => publicTripView(data, { engaged: pastEngaged, envelope: pastEnvelope }, watched),
    [data, pastEngaged, pastEnvelope, watched],
  );

  /**
   * A watched party that is no longer in the list ends the watch, and the page says so.
   *
   * <b>Never a silent return.</b> The figures on screen change owner at that moment — from the
   * party the reader asked for back to the one this link was published for — and a page that
   * swapped them without a word would leave somebody reading one party's "everybody is out" as
   * the other's. So the watch is dropped and a notice names the party it was.
   */
  useEffect(() => {
    if (watch !== null && watched.kind === 'gone') {
      setWatchEnded(watch.title);
      setWatch(null);
    }
  }, [watch, watched]);
  // Reaching a past trip by any road — a row, a link, the browser's Forward — is the later and
  // more deliberate choice, so the watch is over rather than waiting underneath it: leaving the
  // past returns to the trip this link was published for, as its way back says it does.
  useEffect(() => {
    if (pastEngaged) {
      setWatch(null);
      setWatchEnded(null);
    }
  }, [pastEngaged]);

  // A link in somebody's prose, opened in a fresh tab: the address carries which past trip to play
  // and, where it says so, whom to keep the camera on, where to start and whether to start
  // playing. Applied when the address changes and never afterwards, so a reader who presses "back
  // to now" is not sent straight back into the past by their own URL.
  //
  // "Changes" is what the address says about the past, and nothing else on it. The same address
  // carries the page's language, which the language button rewrites; a link naming a moment and
  // asking to play would otherwise be honoured a second time by that press — the replay wound
  // back to the link's moment and set going again because the reader asked for English.
  //
  // An address that comes to name no past trip is the other half of the same reading, and it is
  // what the browser's Back button produces: picking a trip is a step in the reader's history, so
  // stepping back over it arrives at the address of the party being followed now — and a page still
  // playing the replay under that address would be showing one thing and naming another. Leaving
  // the past where nothing of it is on screen changes nothing, so the first reading of an address
  // that never named a trip is harmless.
  //
  // And read again when the link itself changes under the same words: a replay belongs to the link
  // it was opened under and the playback drops it with that link, so a second link whose address
  // names a past trip exactly as the first one's did has to be opened from its own address — it
  // would otherwise stand on the party now under an address that names a replay.
  const openPast = past.open;
  const leaveThePast = past.backToNow;
  const askedOfThePast = PAST_LINK_PARAMS.map((name) => search.get(name) ?? '\u0000').join('\u0001');
  const searchRef = useRef(search);
  searchRef.current = search;
  useEffect(() => {
    const asked = readPastLink(searchRef.current);
    if (asked !== null) {
      openPast(asked.tripLogId, { at: asked.at, follow: asked.follow, play: asked.play });
    } else {
      leaveThePast();
    }
  }, [askedOfThePast, openPast, leaveThePast, token]);

  // The address the viewer is given: held still while it is the same survey, replaced when the
  // survey itself changes. Both halves matter and the reasoning for each lives with the rule,
  // beside the page that shares it.
  //
  // The survey is the link's own unless a past trip is on screen. Another party of the cave is
  // drawn on the link's survey and on nothing else, and read from the link's own answer rather
  // than through that party's view so that a moment with nobody to draw — its list not in hand
  // yet — still leaves the drawing standing, instead of tearing down a viewer that would be built
  // again a second later.
  const model = (mode === 'past' ? view?.model : data?.model) ?? null;
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
   * A request belongs to the trip it was made on, and is forgotten the moment that trip leaves.
   *
   * <b>Otherwise the last thing a follow asked for is done again on the next drawing.</b> The
   * request above is only ever set, so leaving the past — or opening another trip — left it
   * standing while the survey under it changed: a past trip's survey is its own, so the way back
   * to the live party loads the live survey, and the panel, which performs a request whenever a
   * model finishes loading, flew the fresh drawing to where a party of some other year had been —
   * or reported "not in this model" about a station name that survey never had.
   *
   * Forgotten while rendering rather than in an effect, on purpose: a state change made during a
   * render is applied by rendering again at once, before anything of this render is committed, so
   * the request is gone before the follow above can make the next one. A link that opens a trip
   * and follows a party in one press therefore ends with that party's request and nothing older,
   * whatever order the two were declared in.
   */
  const playingId = past.tripLogId;
  const [requestedOn, setRequestedOn] = useState(playingId);
  if (requestedOn !== playingId) {
    setRequestedOn(playingId);
    setFocusRequest(undefined);
  }

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
  /**
   * The same list, offered a second time directly under the replay's strip — and for which trip.
   *
   * <b>The list is the last thing on this page and the replay is the first.</b> A reader going
   * through a cave's history trip by trip pressed a row at the foot of the page, was carried up to
   * the strip, and then had a party, a drawing and a table to scroll past to reach the next row —
   * on a phone, several screens each way, every time. So while a past trip is on screen the way to
   * another one is one press under the strip.
   *
   * <b>Shut until it is pressed, because what belongs under the strip is the drawing.</b> Held as
   * the trip it was opened under rather than as a switch, so it shuts by itself the moment another
   * trip is on screen or the past is left by any road — the row was the whole point of opening it,
   * and a list left standing open would push each replay's drawing a screen down.
   */
  const [switchOpenFor, setSwitchOpenFor] = useState<string | null>(null);
  const switchOpen = switchOpenFor !== null && switchOpenFor === past.tripLogId;
  // One read for both places: the same link and the same list, so whichever is opened second finds
  // the rows already in hand.
  const pastTrips = usePublicPastTrips(token, sameLink && (pastOpen || switchOpen));
  /**
   * A second link starts as a page nobody has asked anything of.
   *
   * The playback forgets a replay with its link, and the rest of what a reader chose goes the same
   * way: the party of the cave they asked to watch, the notice that a watch ended, and the two
   * sections they opened. Kept, the watch would have the second link's list read though nobody
   * asked under it — and where the two links are of one cave the party would be found there, so
   * the second link's page opened on a party of the first link's choosing; for a link of another
   * cave it would open on a notice naming a party of a cave this page is not about.
   *
   * Forgotten while rendering rather than in an effect, so no render under the new link is ever
   * committed with the old link's choices.
   */
  if (!sameLink) {
    setAskedUnder(token);
    setWatch(null);
    setWatchEnded(null);
    setLiveOpen(false);
    setPastOpen(false);
  }
  /**
   * Whether this link has been refused for good since the page opened. The cave's lists are read
   * with the same link, so while it is refused a refusal of either list may be nothing more than
   * the link being over — and it arrives as exactly the answer an installation with its archive
   * switched off gives. The lists are told, so that neither claims a reason it cannot know.
   */
  const linkEnded = error != null && isSettledRefusal(error);
  /**
   * Whether the list of past trips has failed in a way that leaves nothing to show.
   *
   * The list is read again when a reader comes back to it after a while, and such a re-read can
   * fail with a perfectly good list still in hand. A request that merely did not land must not
   * take that list away from somebody choosing from it — the rows are as true as they were a
   * minute ago. A refusal the server settled is different: every row would be refused the same
   * way when pressed, so the list gives way to the sentence that says so.
   */
  const archiveFailed =
    pastTrips.isError && (pastTrips.data === undefined || isSettledRefusal(pastTrips.error));
  /**
   * The same of the list of parties being followed now, and it matters more there: that list is
   * read again on a clock and on every return to the tab, and its rows carry the buttons a reader
   * changes party with — the way back to this link's own trip among them. A minute's read that
   * did not land must not replace them with an invitation to open the list again.
   */
  const liveFailed =
    liveTrips.isError && (liveTrips.data === undefined || isSettledRefusal(liveTrips.error));

  /**
   * Bringing the statement that this is the past to where the reader is looking.
   *
   * <b>The picker is the last thing on this page and the strip is the first.</b> On a phone, with
   * a party of eight between them, a row is pressed a screen or more below where the banner, the
   * way back and the transport mount — and the only visible answer to the press was the row's own
   * tag turning to "Playing". A reader had to know to scroll up to find out what had happened;
   * a screen reader was told, because the banner is an alert, and a sighted reader was not. So a
   * press in the picker is followed, once the strip is on the page, by the strip being brought
   * to the top of the viewport, where the banner names the trip and the controls are under it.
   *
   * Counted rather than keyed on the trip, because pressing the row already playing is a press
   * too. And only a press: a link that arrives already playing opens at the top of the page in any
   * case, and a page that moved itself whenever the trip changed would fight a reader who is
   * scrubbing.
   *
   * <b>Not at the press, but once the replay has drawn its body.</b> In the render the press
   * causes, the trip's counts, survey and party are not there yet — they need the track and a
   * moment on the clock — so the page is briefly only the title, the strip and the list. Scrolled
   * then, the strip is already at the top of a page too short to scroll, and when the body arrives
   * a moment later the browser's scroll anchoring keeps the pressed row where it was on the screen,
   * which carries the page straight back down to the bottom: the strip ends up out of sight above
   * after all. So the press is remembered, and answered in the first layout where the replay has
   * settled what it draws — a moment to show, a trip that could not be read, or one with nothing
   * to play — and before that layout is painted, so the reader never sees the page jump twice.
   */
  const pastBarRef = useRef<HTMLDivElement>(null);
  const [picked, setPicked] = useState(0);
  /** The trip a press asked to be brought into view, until the page has done so. */
  const scrollOwedFor = useRef<string | null>(null);
  const pastSettled =
    past.engaged && !past.loading && (past.envelope !== null || past.failed || past.span === null);
  useLayoutEffect(() => {
    if (scrollOwedFor.current === null || scrollOwedFor.current !== past.tripLogId || !pastSettled) {
      return;
    }
    scrollOwedFor.current = null;
    // Optional because a renderer that lays nothing out has no such method.
    pastBarRef.current?.scrollIntoView?.({ block: 'start' });
  }, [picked, pastSettled, past.tripLogId]);

  /**
   * Choosing a trip: play it, and write it into the address so the view can be sent to somebody.
   *
   * <b>A new entry in the reader's history, where everything else this page writes replaces the
   * one it is on.</b> Somebody who pressed a row and finds themselves in a trip of years ago
   * reaches for the browser's Back button to undo it, and with the address merely replaced that
   * press took them off the page altogether — out of the party they came to follow. Pressing the
   * row of the trip the address already names is the exception: there is no step to add, and a
   * second identical entry would be a Back press that appears to do nothing.
   *
   * <b>Written into the page at once, not when the router gets round to it.</b> The router hands
   * a new address to the page as a change that may wait — and it does wait, for as long as the
   * browser is busy parsing the survey this very press asked for. The way back above is read from
   * the address *changing* to one that names no trip; a reader who pressed Back inside that wait
   * went from an address the page had not yet been told about to the one it was already on, which
   * is no change at all, and stayed in the replay under the live trip's address. So the pick is
   * committed before the press returns, and there is no such wait to press Back in.
   */
  const play = (tripLogId: string) => {
    // One party at a time: the past trip replaces whoever was on screen, a watched party included.
    setWatch(null);
    setWatchEnded(null);
    past.open(tripLogId, { follow: null });
    setSearch(writePastLink(search, tripLogId, null), {
      replace: search.get('past') === tripLogId,
      flushSync: true,
    });
    scrollOwedFor.current = tripLogId;
    setPicked((count) => count + 1);
  };

  /**
   * Leaving the past, address included.
   *
   * <b>The address has to be cleared with the view, not after it.</b> A reader who presses the way
   * back and then copies what is in the bar would otherwise be sending somebody a link into a past
   * trip while believing they were sending the live page — the address would still name a trip the
   * page had stopped showing. `replace` rather than a new entry: leaving a replay is not a place in
   * the reader's history to go back to. And at once, for the reason a pick is: whether the next
   * pick is a new step in the reader's history is decided against the address this leaves behind.
   */
  const leavePast = () => {
    scrollOwedFor.current = null;
    past.backToNow();
    setSearch(writePastLink(search, null, null), { replace: true, flushSync: true });
  };

  /**
   * Bringing the statement that another party is on screen to where the reader is looking.
   *
   * The list is at the foot of the page and the statement at its head, exactly as with a past
   * trip, so a press on a row is answered by the strip being brought to the top of the viewport.
   * Nothing has to be waited for here — the party pressed is already in hand and is drawn in the
   * very render the press causes — so the layout that follows the press is the one to scroll in.
   */
  const watchBarRef = useRef<HTMLDivElement>(null);
  const [watchPicked, setWatchPicked] = useState(0);
  useLayoutEffect(() => {
    if (watchPicked > 0) {
      // Optional because a renderer that lays nothing out has no such method.
      watchBarRef.current?.scrollIntoView?.({ block: 'start' });
    }
  }, [watchPicked]);

  /** Back to the trip this link was published for, from another party of the cave. */
  const leaveWatch = () => {
    setWatch(null);
    setWatchEnded(null);
  };

  /**
   * Asking for a party of the cave to be drawn — or, given this link's own trip, for the way back.
   *
   * <b>Only a party the list in hand actually carries.</b> The press comes from a row, so there is
   * one; asking for anything else is ignored rather than left standing as a watch of nobody.
   *
   * <b>Not written into the address.</b> Everything else this page draws can be sent on as a link,
   * and this cannot, on purpose: a link that named a party to watch would have the page read the
   * cave's list for every reader who opened it, where the rule is that the list is read when a
   * reader asks for it. What the address still says — this link's trip — is also where the page
   * returns when the watch ends, so a copied address never opens somebody else's party.
   *
   * A replay on screen is left first, address included: one party at a time, and the press asked
   * for this one.
   */
  const watchParty = (tripLogId: string) => {
    if (tripLogId === ownTripLogId) {
      leaveWatch();
      return;
    }
    const row = liveTrips.data?.trips.find((trip) => trip.tripLogId === tripLogId);
    if (row === undefined) {
      return;
    }
    if (past.engaged) {
      leavePast();
    }
    setWatchEnded(null);
    setWatch({ tripLogId, title: row.title });
    setWatchPicked((count) => count + 1);
  };

  /**
   * Choosing whom to keep up with, address included.
   *
   * <b>The address is what a reader copies to send the view on, so it has to say who is followed
   * as well as which trip.</b> A reader who follows the survey team and pastes the bar into a
   * message otherwise sends a link that opens the trip following nobody — while the same link with
   * the team written on it, which this page reads, does what they meant. `replace`, because a
   * change of whom to follow is a change of the view and not a place in the reader's history.
   *
   * The address being rewritten sends the page back through its own link reading, which asks for
   * the same follow of the same trip again; the playback answers a follow it already holds by
   * doing nothing more, so the round trip moves neither the clock nor the camera.
   */
  const followPast = (follow: PastFollow | null) => {
    past.setFollow(follow);
    if (past.tripLogId !== null) {
      setSearch(writePastLink(search, past.tripLogId, follow), { replace: true });
    }
  };

  /**
   * The address of the moment on the replay's clock, for the two copy buttons on its strip.
   *
   * Built from where the reader is — this page's own address, with the trip, whom the replay
   * follows and the moment written onto it — so whatever else the address carries, the page's
   * language above all, travels with the link. Read when a button is pressed, never while the clock
   * runs: the address bar itself is still never given a moment.
   */
  const momentAddress = (playing: boolean): string => {
    const query =
      past.tripLogId === null
        ? search
        : momentLink(search, past.tripLogId, past.follow, past.at, playing);
    return `${window.location.origin}${window.location.pathname}?${query.toString()}`;
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

  /**
   * A first read the browser never sent, because it knows it has no connection.
   *
   * Nothing failed — nothing was attempted — so the read stays "pending" for as long as the phone
   * is offline, and a page that drew pending as a spinner would spin at a family in a car park
   * until they gave up. It is the same fact as a first read that went out and died, and is said
   * the same way; the read goes out by itself the moment the connection is back.
   */
  const neverSent = data === undefined && isPaused === true;
  /**
   * A first read the server asked to have back later, and which is being held until then.
   *
   * The wait it names can be minutes, and is honoured before each further attempt — so "pending"
   * is again not "about to arrive", and a spinner for that long is a page that looks broken to the
   * very readers who arrive when the server is busiest. Said the way the other two are; the read
   * is made again by itself when the wait is over, and the button asks at once.
   */
  const firstReadHeld =
    neverSent
    || (data === undefined && readNotLanding({ error: null, failureReason, isPaused }) != null);

  if (isPending && !firstReadHeld) {
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
  //
  // And with nothing in hand the same distinction still has to be kept. A first read that failed
  // because the phone had no signal is not an answer about the link, and a page asserting that the
  // link "may never have existed" on that evidence would be telling a family something false. So
  // a fault that may clear is said as one, with the way to try again; only a refusal the server
  // actually gave — and every unusable link gives the same one — is drawn as nothing to show.
  if (data === undefined && (firstReadHeld || (error != null && !isSettledRefusal(error)))) {
    return (
      <Flex align="center" justify="center" style={{ minHeight: '100dvh', padding: 16 }}>
        <Card style={{ maxWidth: 520, width: '100%' }}>
          <Result
            status="warning"
            title={t('publicTrip.unreachableTitle')}
            subTitle={t('publicTrip.unreachableBody')}
            extra={
              <Flex vertical align="center" gap={8}>
                <Button
                  type="primary"
                  onClick={() => void refetch()}
                  data-testid="public-trip-retry"
                >
                  {t('publicTrip.retry')}
                </Button>
                <PublicLanguageButton control={language} />
              </Flex>
            }
            data-testid="public-trip-unreachable"
          />
        </Card>
      </Flex>
    );
  }
  if (data === undefined) {
    return (
      <Flex align="center" justify="center" style={{ minHeight: '100dvh', padding: 16 }}>
        <Card style={{ maxWidth: 520, width: '100%' }}>
          <Result
            icon={<QuestionCircleOutlined />}
            status="warning"
            title={t('publicTrip.notFoundTitle')}
            subTitle={t('publicTrip.notFoundBody')}
            // The one sentence on this page is the one a reader most needs to be able to read.
            extra={<PublicLanguageButton control={language} />}
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
  const now = past.engaged ? (past.at ?? present) : present;

  /**
   * Whether what is on screen has stopped changing for good: the watch is closed, or the read it
   * is drawn from has been given its final answer.
   *
   * From then on a moment is said as its hour rather than as a gap. A gap is right while the page
   * keeps up with it; on a page nothing will change again it either stands still and is wrong
   * within the minute, or grows all night under a party who came out in the afternoon. A read
   * that merely failed is not this — it may clear, the gaps are still true of the clock, and the
   * notice above them says since when they have not been refreshed. Nor is a replay, which
   * measures from the moment on its own scrubber.
   */
  const shownState = mode === 'own' ? data.state : view?.state;
  /**
   * Whether the read feeding the screen has been refused for good — which, while another party of
   * the cave is on screen, is a question about the list and not about this link's own read.
   */
  const shownEnded = shownReadEnded(mode, error, liveTrips.error);
  const settled =
    mode !== 'past' && ((shownState !== undefined && shownState !== 'armed') || shownEnded);

  /**
   * When this page last heard from the server, or null where it cannot say.
   *
   * The moment the last read that succeeded arrived — a read that fails leaves it standing, which
   * is exactly what makes it the answer to "how old is what I am looking at".
   */
  const shownUpdatedAt = mode === 'watched' ? liveTrips.dataUpdatedAt : dataUpdatedAt;
  const readAt = Number.isFinite(shownUpdatedAt) && shownUpdatedAt > 0 ? shownUpdatedAt : null;
  /**
   * The failure of the read that feeds what is on screen, or null while that read is landing.
   *
   * Another party of the cave arrives by the list, not by the link's own read, so while one is on
   * screen it is the list failing that makes the page stale — and the link's own read failing
   * that does not, for as long as the list still lands.
   *
   * "Failure" in the wide sense a reader means it: a read held back because the browser knows it
   * is offline, and one waiting out a pause the server asked for, are not being refreshed either.
   */
  const readError =
    mode === 'watched'
      ? readNotLanding({
          error: liveTrips.isError ? liveTrips.error : null,
          failureReason: liveTrips.failureReason,
          isPaused: liveTrips.isPaused,
        })
      : readNotLanding({ error, failureReason, isPaused });

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
    const placed = momentOrAge(placedAt, now, i18n.language, settled);
    return (
      <>
        {shown}
        {placed !== null && (
          <Typography.Text
            type="secondary"
            className="public-trip-position-age"
            title={when(placedAt)}
            data-testid={`public-trip-position-age-${participant.ordinal}`}
          >
            {settled
              ? t('publicTrip.positionAt', { clock: placed })
              : t('publicTrip.positionSince', { since: placed })}
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
   *
   * Another party of the cave is the same case with a different name on it: the header is that
   * party's while it is on screen, and nobody's — never the link's own — for a moment in which it
   * was asked for and is not in hand.
   */
  const head = view ?? (mode === 'own' ? data : null);
  const dates =
    head === null ? null : tripDateRange(head.tripDate, head.tripDateEnd, i18n.language);

  /**
   * Since when — or from when to when — the trip in the header was followed.
   *
   * <b>Of whatever trip the header names, and worded as what it is.</b> The moment is when the
   * watch was started, so the line says "followed" and never "underground": a watch is often
   * started in the car park, or an hour after the party went in. While the watch runs it is the
   * hour and how long that has been, which is the figure somebody at home is counting; once it is
   * closed, and on every replay, it is the span, measured against today's real date however far
   * back the scrubber stands — the hour a trip of last year started is not "today" because the
   * replay is at that hour. Nothing at all where the trip carries no such moment.
   */
  const followedLine = ((): string | null => {
    if (head === null) {
      return null;
    }
    const startedAt = watchStartedAt(head);
    if (startedAt === null) {
      return null;
    }
    if (head.state === 'armed') {
      const duration = durationInWords(startedAt, present, i18n.language);
      const clock = clockInWords(startedAt, present, i18n.language);
      return duration === null
        ? t('publicTrip.since.started', { clock })
        : t('publicTrip.since.running', { clock, duration });
    }
    const span = followedSpanInWords(head, present, i18n.language);
    return span === null
      ? t('publicTrip.since.started', { clock: clockInWords(startedAt, present, i18n.language) })
      : t('publicTrip.since.span', { span });
  })();

  return (
    <div className="public-trip" style={palette} data-testid="public-trip">
      {/* The title and the dates are of whatever is on screen — a past trip has its own, and a
          header still naming the live one over a replay of another trip would be the page telling
          two stories at once. The tab keeps the link's own trip, because the tab is the link. */}
      <header className="public-trip-head">
        <h1 className="public-trip-title" data-testid="public-trip-title">
          {head !== null
            ? head.title
            : mode === 'watched' && watch !== null
              ? watch.title
              : t('publicTrip.past.unknownTrip')}
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
            {followedLine !== null && (
              <Typography.Text
                type="secondary"
                className="public-trip-since"
                data-testid="public-trip-since"
              >
                {followedLine}
              </Typography.Text>
            )}
          </div>
        )}
      </header>

      <main className="public-trip-body">
        {/* First thing under the title, and it stays there for as long as the past is on screen.
            Wrapped so a press in the picker at the bottom of the page has something to scroll to. */}
        {past.engaged && (
          <div ref={pastBarRef}>
            <PublicPastBar
              playback={{ ...past, backToNow: leavePast, setFollow: followPast }}
              liveState={data.state}
              cavers={cavers}
              momentAddress={momentAddress}
            />
            {/* The cave's other past trips, one press from the replay. Its rows are not on the
                page at all while it is shut — taken out by this page in the same render, not left
                to the panel's closing animation: the same list stands open at the foot of the page
                for as long as a past trip is on screen, and two copies of every row would be two
                controls for one act wherever the page is searched or read aloud. */}
            <Collapse
              ghost
              activeKey={switchOpen ? ['switch'] : []}
              onChange={(keys) =>
                setSwitchOpenFor(keys.includes('switch') ? past.tripLogId : null)
              }
              items={[
                {
                  key: 'switch',
                  label: (
                    <span className="public-trip-past-label" data-testid="public-past-switch">
                      <HistoryOutlined /> {t('publicTrip.past.switchTrip')}
                    </span>
                  ),
                  children: switchOpen && (
                    <div data-testid="public-past-switch-list">
                      <PublicPastTripList
                        trips={pastTrips.data?.trips}
                        more={pastTrips.data?.more ?? false}
                        loading={pastTrips.isPending}
                        failed={archiveFailed}
                        refused={isSettledRefusal(pastTrips.error)}
                        linkEnded={linkEnded}
                        playingId={past.tripLogId}
                        onPlay={play}
                      />
                    </div>
                  ),
                },
              ]}
            />
          </div>
        )}

        {/* The same place, for the same reason, while another party of the cave is on screen: whose
            figures these are is said before any of them. */}
        {mode === 'watched' && (
          <div ref={watchBarRef}>
            <PublicWatchBar
              party={view === undefined || dates === null ? null : { title: view.title, dates }}
              onBack={leaveWatch}
            />
          </div>
        )}

        {/* The watch that ended under the reader. Said where the banner stood, and kept until it is
            closed or something else is asked for: the figures below changed owner without a press,
            and that is not something to say for a few seconds and then take away. */}
        {watchEnded !== null && mode === 'own' && (
          <Alert
            type="warning"
            showIcon
            // The icon is named although it is the library's own default: its alert draws a close
            // button for an options object only when that object carries one.
            closable={{
              closeIcon: <CloseOutlined />,
              onClose: () => setWatchEnded(null),
              'aria-label': t('publicTrip.live.watchEndedClose'),
            }}
            title={t('publicTrip.live.watchEndedTitle')}
            description={t('publicTrip.live.watchEndedBody', { title: watchEnded })}
            data-testid="public-watch-ended"
          />
        )}

        {view !== undefined && (
          <div
            className="public-trip-standing"
            data-testid="public-trip-counts"
            // Announced when it changes — somebody coming out is the one event this page is kept
            // open for — but only for the live party: a replay changes these counts as it plays,
            // and a reader moving a scrubber is not waiting to be told.
            role={past.engaged ? undefined : 'status'}
            aria-live={past.engaged ? undefined : 'polite'}
          >
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

        {/* How old what is on screen is, said standing and not only when something goes wrong: a
            reader should not have to wonder whether a quiet page is a quiet cave or a page that
            stopped asking. A gap while the page is keeping up, the hour once it never will again.
            About the live read, so not said over a replay. Not a live region — a figure that
            changes twice a minute would be read out twice a minute. */}
        {readAt !== null && !past.engaged && (
          <Typography.Text
            type="secondary"
            className="public-trip-updated"
            title={new Date(readAt).toLocaleString(i18n.language)}
            data-testid="public-trip-updated"
          >
            {settled
              ? t('publicTrip.updated.settled', { clock: clockInWords(readAt, present, i18n.language) })
              : t('publicTrip.updated.running', {
                  since: ageInWords(readAt, present, i18n.language),
                })}
          </Typography.Text>
        )}

        {/* A poll that failed is a fact about the live read, so it is said while the live read is
            what is on screen. A reader watching a trip from 2019 does not need to be told that
            this minute's refresh of a different trip did not land.

            Both are facts about the read feeding what is on screen, and for another party of the
            cave that is the list of parties being followed, not this link's own read. The two do
            not end together: the link's own read is refused as soon as its trip is no longer
            published, while the list may go on answering — so a final word about this link's own
            trip is not said over a party that is still being refreshed every minute, and is said
            when the reader goes back to that trip. And a list refused for good is final for the
            party on screen even where the link's own read, no longer repeated, never heard it. */}
        {mode !== 'past' && (shownEnded || readError != null) && (
          shownEnded ? (
            // The server's own answer, and a final one: the link has been taken back or the
            // publication has run out. Said in those words, and without the promise that the
            // page will refresh — the poll has stopped for good, and a reader told "it starts
            // refreshing again by itself" would be waiting for something that cannot happen.
            <Alert
              type="warning"
              showIcon
              title={t('publicTrip.endedTitle')}
              description={
                readAt === null
                  ? t('publicTrip.endedBody')
                  : t('publicTrip.endedBodyAt', {
                      clock: clockInWords(readAt, present, i18n.language),
                    })
              }
              data-testid="public-trip-ended"
            />
          ) : (
            <Alert
              type="warning"
              showIcon
              title={t('publicTrip.staleTitle')}
              // How stale, and not only that it is: "stopped refreshing" reads the same at one
              // minute and at five hours, and they are different things to somebody waiting.
              description={
                readAt === null
                  ? t('publicTrip.staleBody')
                  : t('publicTrip.staleBodySince', {
                      since: ageInWords(readAt, present, i18n.language),
                    })
              }
              data-testid="public-trip-stale"
            />
          )
        )}

        {/* Superseded by the past banner while one is up: two notices saying "this is finished"
            would compete, and only one of them says which trip.

            About the party on screen, never about the link's own trip under somebody else's: a
            reader watching a party that is still underground from a link whose own trip closed
            this morning must not be told "this trip is over" above people who are not out. And a
            watched party whose watch has closed is told in words of its own, because the page
            does still change for it — it is about to leave the list. */}
        {mode !== 'past' && shownState !== undefined && shownState !== 'armed' && (
          <Alert
            type="info"
            showIcon
            title={t(mode === 'watched' ? 'publicTrip.live.watchClosedTitle' : 'publicTrip.closedTitle')}
            description={t(
              mode === 'watched' ? 'publicTrip.live.watchClosedBody' : 'publicTrip.closedBody',
            )}
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
                      // Placed at each moment of a replay while it is dragged or stepped, sliding one tick's
                      // worth while it plays — the viewer's own slide on the live party, as always.
                      markerMoveMs={past.markerMoveMs}
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

        {/* Directly over the names it explains, on the live page and on a replay alike. */}
        {view !== undefined && <PublicTripAbout />}

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
                          <Typography.Text
                            title={when(participant.lastRecordedAt)}
                            data-testid={`public-trip-last-heard-${participant.ordinal}`}
                          >
                            {momentOrAge(participant.lastRecordedAt, now, i18n.language, settled)
                              ?? '—'}
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

        {/* The rest of the cave, at the bottom and shut until it is asked for: who else is being
            followed in it now, and its past trips.
            <b>Shut for a reason and not for tidiness.</b> This page is opened by families while a
            party is underground, on phones, in numbers nobody can see, and its one job is to say
            whether they are out. Reading a cave's other parties and its whole history on every
            one of those opens would multiply the cost of the cheapest surface here to answer
            questions nobody asked — so each list is fetched on the press that opens its own
            section, and a reader who never presses either costs exactly what they cost before
            these existed.
            <b>Two sections, each with one list.</b> They answer different questions and are read
            from different routes, one of which an installation may have switched off: the parties
            in the cave now are offered whether or not the cave's history is. */}
        <section className="public-trip-past" data-testid="public-trip-past">
          <Collapse
            ghost
            activeKey={[...(liveOpen ? ['live'] : []), ...(pastOpen ? ['past'] : [])]}
            onChange={(keys) => {
              setLiveOpen(keys.includes('live'));
              setPastOpen(keys.includes('past'));
            }}
            items={[
              {
                key: 'live',
                label: (
                  <span className="public-trip-past-label" data-testid="public-trip-live-section">
                    <TeamOutlined /> {t('publicTrip.live.sectionTitle')}
                  </span>
                ),
                children: (
                  <PublicLiveTripList
                    trips={liveTrips.data?.trips}
                    more={liveTrips.data?.more ?? false}
                    loading={liveTrips.isPending}
                    failed={liveFailed}
                    refused={isSettledRefusal(liveTrips.error)}
                    // The list's own final refusal counts while it is what the screen is drawn
                    // from: the notice above has just said this link stopped answering, and the
                    // list must not invite another try beneath it.
                    linkEnded={linkEnded || shownEnded}
                    ownTripLogId={data.tripLogId}
                    onWatch={watchParty}
                    watchingId={mode === 'watched' ? watchId : null}
                  />
                ),
              },
              {
                key: 'past',
                label: (
                  <span className="public-trip-past-label" data-testid="public-trip-past-section">
                    <HistoryOutlined /> {t('publicTrip.past.sectionTitle')}
                  </span>
                ),
                children: (
                  <PublicPastTripList
                    trips={pastTrips.data?.trips}
                    more={pastTrips.data?.more ?? false}
                    loading={pastTrips.isPending}
                    failed={archiveFailed}
                    refused={isSettledRefusal(pastTrips.error)}
                    linkEnded={linkEnded}
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
        <PublicLanguageButton control={language} />
      </footer>
    </div>
  );
}

