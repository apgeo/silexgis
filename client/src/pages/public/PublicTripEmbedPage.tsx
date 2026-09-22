// SPDX-License-Identifier: AGPL-3.0-or-later
import {
  lazy,
  Suspense,
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
  type CSSProperties,
} from 'react';
import { HistoryOutlined } from '@ant-design/icons';
import { Button, Drawer, Flex, Skeleton, Spin, Tabs, Typography, theme } from 'antd';
import { useTranslation } from 'react-i18next';
import { useParams } from 'react-router-dom';
import { usePublicPastTrips, usePublicTrip } from '../../api/hooks.ts';
import CaveViewPanel, {
  type CaveViewFocusRequest,
} from '../../components/caveview/CaveViewPanel.tsx';
import { noStationsMissing } from '../../caveview/placedOnModel.ts';
import { envelopeCrsLookup, publicTrackedCavers } from '../../caveview/publicTrackedCavers.ts';
import { usePublishedStationMedia } from '../../caveview/useStationMedia.ts';
import { unnamedViewerFileName } from '../../caveview/viewerFileName.ts';
import { usePublishedSheets } from '../../rastermap/publishedSheets.ts';
import { VIEW_KIND_ICONS } from '../../rastermap/viewKindIcons.tsx';
import { followedStation, type PastFollow } from './pastTrackReplay.ts';
import { usePinnedModelUrl } from './pinnedModelUrl.ts';
import PublicPastBar from './PublicPastBar.tsx';
import PublicPastTripList from './PublicPastTripList.tsx';
import {
  EMBED_CHANNEL,
  EMBED_PROTOCOL,
  EMBED_TRIP_LIVE,
  parseEmbedInbound,
  type EmbedOutboundMessage,
  type EmbedReadyMessage,
} from './publicTripEmbed.ts';
import { instantOf } from './publicTripParty.ts';
import { usePastTripPlayback } from './usePastTripPlayback.ts';
import './PublicTripPage.css';

/**
 * Loaded when a trip actually carries sheets, exactly as on the page next door: the pane
 * pulls in OpenLayers, and an article's iframe about a mapless trip should not download a
 * map engine to show a 3D drawing.
 */
const PublicTripSheetPane = lazy(() => import('./PublicTripSheetPane.tsx'));

/** The 3D pane's tab key — every sheet's key is derived from a URL and cannot collide with it. */
const TAB_3D = '3d';

/**
 * The same published trip as a viewer and nothing else, for an iframe on somebody's website.
 *
 * <b>No chrome at all, and that is the difference from the page next door.</b> A title, a party
 * list and a footer belong to the article this sits inside — a club writes those itself, in its own
 * words and its own layout — so what is offered here is the drawing, the party on it, and the
 * viewer's own controls. It fills whatever box the snippet gave it.
 *
 * <b>The iframe is the boundary and nothing widens it.</b> The host page cannot read anything of
 * this document; what it can do is post a message asking for a place to be shown, and this answers
 * only its own framer and only ever to that framer's origin. Which sites may frame this at all is
 * an operator's setting enforced as `frame-ancestors` by the web server — so the allow-list is
 * where an operator can see and change it, not compiled into a bundle.
 *
 * <b>A caver is a place too.</b> Prose that says "caver 3 is at the sump" can link the number, and
 * this resolves it to wherever that place in the party was last reported — which is the only way a
 * link in an article can stay correct while the party moves.
 */
export default function PublicTripEmbedPage() {
  const { t } = useTranslation();
  const { token } = useParams<{ token: string }>();
  const { token: antdToken } = theme.useToken();
  // The refusal is not read separately here: a first read that failed leaves nothing to frame and
  // is the same empty box as a trip with no drawing, and a later one that failed leaves what is
  // already on screen alone.
  const { data, isPending } = usePublicTrip(token);
  const [focusRequest, setFocusRequest] = useState<CaveViewFocusRequest | undefined>();

  /**
   * A place in the drawing that a link named alongside a trip, held until that trip is on screen.
   *
   * <b>The one target that cannot be answered in the breath it arrives in.</b> "The sump on the
   * 2019 push" is one press and two things: open that trip, then show that place in it. The place
   * belongs to the trip's own survey — often a superseded one — which is not downloaded, not
   * parsed, and not even named until the track lands, so a camera move asked for now would either
   * be performed against the survey still on screen, which is a different cave's worth of station
   * names, or be thrown away. Held here instead, and spent the moment the chosen trip's drawing is
   * up; if the trip cannot be read at all the article is told the target was not found, which is
   * what lets it grey the link out rather than offer one that goes nowhere.
   */
  const [pendingPlace, setPendingPlace] = useState<{
    kind: 'station' | 'survey';
    ref: string;
    settle: (found: boolean) => void;
  } | null>(null);

  /** The cave's past, and which of it this frame has been asked to play. Reads nothing until asked. */
  const past = usePastTripPlayback(token);
  /**
   * The list of past trips, which this frame keeps behind a control.
   *
   * <b>Where the followed page has room for a section, a frame in somebody's article has a box.</b>
   * A list standing open in a 4:3 embed would take a third of the drawing away from every reader,
   * including every reader who came for the drawing — so it opens over the frame on a press and is
   * fetched on that press, which means an article whose readers never open it costs exactly what it
   * cost before the archive existed.
   */
  const [picker, setPicker] = useState(false);
  const pastTrips = usePublicPastTrips(token, picker);

  /**
   * What this frame is showing: the party now, or a past trip wound back to a moment.
   *
   * Undefined while a chosen track is in flight, and deliberately not the live party — drawing the
   * people who are underground right now under a strip saying this is the past is the one sentence
   * this feature must never produce, and it would be produced inside somebody else's article.
   */
  const view = past.engaged ? (past.envelope ?? undefined) : data;

  // Held still while it is the same survey and replaced when it is not, by the one rule the
  // followed page uses — a re-signed address must not re-parse the model and throw the camera
  // back to its opening view every minute, and a survey swapped mid-trip must not leave this
  // drawing the old geometry under the new survey's station names. Keyed by trip as well as by
  // token, because a past trip's survey is its own and is often a superseded one.
  const model = view?.model ?? null;
  const pinnedModelUrl = usePinnedModelUrl(
    model?.modelUrl,
    past.tripLogId === null ? token : `${token}:${past.tripLogId}`,
  );

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
   * Asked for when the followed station changes and at no other time: a request on every tick would
   * take the drawing away from a reader who has turned it, five times a second. A moment where the
   * follow has nowhere honest to point asks for nothing rather than guessing.
   *
   * <b>And the tab is left exactly where the reader put it.</b> A link pressed in the article is a
   * reader asking for something and is answered on the drawing that moves — but a follow moves by
   * itself, at every report of the party being followed, and a frame that re-selected the 3D pane
   * each time would make the scanned sheets unreachable for the whole of a followed replay. The
   * sheets are handed the same followed station, so whichever pane is open is the one following.
   */
  /**
   * The parts of the playback this page's message listener reads, named one by one.
   *
   * <b>Not the object, deliberately.</b> `past` is rebuilt every render, so a listener depending on
   * it would be torn down and stood up again five times a second while a replay plays — and a
   * message arriving in that gap is a link in somebody's article that did nothing. The four
   * callbacks are stable and the span changes once per trip, which is what this listener actually
   * cares about.
   */
  const {
    open: openPast,
    backToNow,
    setFollow,
    setAt: setPastAt,
    span: pastSpan,
    engaged: pastEngaged,
  } = past;

  const followStation = followedStation(cavers, past.follow);
  useEffect(() => {
    if (followStation !== null) {
      setFocusRequest({ kind: 'station', ref: followStation });
    }
  }, [followStation]);

  const crsLookup = useMemo(() => envelopeCrsLookup(model), [model]);

  /**
   * The stations the drawing in this frame turns out not to hold, as the viewer answers it.
   *
   * <b>This frame has no chrome, so everything it knows it knows on somebody else's behalf.</b> The
   * page next door marks such a station in its own list of people; here the list of people is the
   * article around the frame, written by a club against the message below. Read from the viewer and
   * from nowhere else: no server can say whether a parsed file contains a station of a given name.
   *
   * Empty until a drawing has been parsed, so a frame still downloading a model claims nothing
   * about it — which matters more here than anywhere, because the first announcement goes out
   * before the envelope has even landed.
   */
  const [unplacedStations, setUnplacedStations] = useState<ReadonlySet<string>>(noStationsMissing);

  // Kept fresh across re-reads rather than pinned like the model URL, and kept as one object while
  // it is the same photographs — both for the reason the page next door gives at length. The case
  // is sharper here: an embed sits inside an article about a trip that finished months ago, opened
  // by a reader who scrolls to the drawing when they get to it, so its picture URLs are the ones
  // most likely to be spent long after they were minted.
  const stationMedia = usePublishedStationMedia(model?.pictures);

  /**
   * The sheets, restamped across re-reads exactly as on the page next door — sharper here
   * for the same reason the pictures' case is: an embed sits inside an article about a trip
   * that may be long over, and its map tab is opened when the reader gets to it, so the
   * signature it spends is the one the latest poll restamped, not the one the page loaded
   * with.
   */
  const sheets = usePublishedSheets(model?.rasterMaps);
  const [activeTab, setActiveTab] = useState(TAB_3D);

  // A tab whose sheet left the envelope cannot stay active.
  useEffect(() => {
    if (activeTab !== TAB_3D && !sheets.some((sheet) => sheet.key === activeTab)) {
      setActiveTab(TAB_3D);
    }
  }, [sheets, activeTab]);

  /**
   * The party as the framing document is told it: a place, a name, a station or nothing, and which
   * kind of nothing it is.
   *
   * The second half is not a nicety. A station is absent for three unrelated reasons — nobody has
   * reported a place; a place was reported on a different survey than the one in this frame and
   * cannot honestly be drawn on it; or a place was reported naming a station of this survey and the
   * drawing turns out to hold no node of that name — and the article around this viewer writes its
   * prose against what it is told. Told only "no station", it says nobody knows where somebody
   * underground is.
   *
   * <b>The third is the one the frame itself has to discover, and it was the one missing here.</b>
   * The other two are settled before the envelope is sent; this one is known only once this browser
   * has parsed the model, so it can only come from the viewer inside this frame. Without it the
   * message said "Ana is at cave.deep.3", the article printed that and linked it, and the drawing
   * beside the prose showed nobody — with the honest answer arriving only after a reader pressed
   * the link. So the station is withheld the same way a place on another survey is, and the reason
   * is handed over beside it.
   */
  const party = useMemo(
    () =>
      cavers.map((caver) => {
        const station = caver.position.kind === 'station' ? caver.position.station : null;
        const notOnDrawing = station !== null && unplacedStations.has(station);
        return {
          ordinal: Number(caver.caverId),
          name: caver.name,
          station: notOnDrawing ? null : station,
          onOtherSurvey: caver.position.kind === 'otherModel',
          notOnDrawing,
        };
      }),
    [cavers, unplacedStations],
  );
  const loaded = view !== undefined;

  /**
   * Spending a place that was named alongside a trip, once that trip's drawing is up.
   *
   * <b>Or answering for it when there will never be one.</b> A trip that cannot be read — gone,
   * withdrawn, past this installation's retention — leaves the request unanswerable, and an article
   * left waiting for a `focused` that never comes cannot tell that from a link still in flight. So
   * the refusal is sent, in the same words a station the drawing does not hold gets.
   */
  const pastFailed = past.failed;
  useEffect(() => {
    if (pendingPlace === null) {
      return;
    }
    if (pastFailed) {
      setPendingPlace(null);
      pendingPlace.settle(false);
      return;
    }
    if (!loaded) {
      return;
    }
    setPendingPlace(null);
    // A fresh object is what asks the panel to move, and the panel waits for its own model to be
    // parsed before it does — so this is spent against the past trip's survey and never against
    // the one that happened to be on screen when the link was pressed. What it settles with is
    // what the article is told: a place that survey does not hold answers `false`.
    setFocusRequest({
      kind: pendingPlace.kind,
      ref: pendingPlace.ref,
      onSettled: pendingPlace.settle,
    });
    // The camera being flown is the 3D pane's; an answer performed behind a sheet tab would read
    // as a link that did nothing. A reader who pressed a link asked for this, unlike a follow.
    setActiveTab(TAB_3D);
  }, [pendingPlace, loaded, pastFailed]);

  /**
   * Which trip the party above belongs to, said on every announcement.
   *
   * <b>Absent while the live trip is on screen, which is what every announcement used to mean.</b>
   * An article written before the archive existed goes on reading the party exactly as it did; one
   * written after it can print its own words for the past, and must, because the same shape now
   * carries two very different claims about where people are.
   */
  const pastNow = useMemo(
    () =>
      past.tripLogId === null || past.track === undefined || past.at === null
        ? undefined
        : {
            tripLogId: past.tripLogId,
            title: past.track.title,
            at: new Date(past.at).toISOString(),
          },
    [past.tripLogId, past.track, past.at],
  );

  /**
   * The framer's origin, once it has said hello, and what it was last told.
   *
   * Both are held rather than derived because the conversation outlives any one render: a greeting
   * arrives while the envelope is still in flight, so the useful answer is the one sent *after*
   * it lands, and there is no second greeting to hang that on — the host script says hello once
   * per frame and then listens.
   */
  const framerOrigin = useRef<string | null>(null);
  const announced = useRef<string | null>(null);

  /**
   * What the framer has been told, as a value that changes when the <em>statement</em> changes.
   *
   * <b>The clock is deliberately not in it, and that is the whole of the throttle.</b> A replay's
   * moment moves five times a second for the length of a playback. Compared on the moment, every
   * one of those ticks is a change, and the whole party is posted into somebody else's article
   * three hundred times a minute — on a phone, re-running a host page's own handler each time, for
   * a party that has not moved. What an article does with this message is print and link where
   * people are, so what it needs to hear about is a person moving: the trip, whether the view has
   * loaded, and the party itself. The moment still rides along on the message, where it is the
   * instant the statement was true at rather than a stream of its own.
   */
  const statement = useMemo(
    () =>
      JSON.stringify({
        loaded,
        party,
        past:
          pastNow === undefined ? null : { tripLogId: pastNow.tripLogId, title: pastNow.title },
      }),
    [loaded, party, pastNow],
  );

  /**
   * What would go out right now, on a ref rather than closed over.
   *
   * So that {@link announce} never changes identity — the listener below depends on it, and a
   * listener rebuilt whenever the party or the clock moves is torn down and stood up again while a
   * message from the article may be arriving, which is a link in somebody's prose that did nothing.
   */
  const outbound = useRef({ loaded, party, pastNow, statement });
  outbound.current = { loaded, party, pastNow, statement };

  const announce = useCallback((origin: string) => {
    const said = outbound.current;
    announced.current = said.statement;
    // Never `'*'`: the party, and which stations it is standing at, goes to the document that
    // framed this page and to no other listener that happens to be in the chain.
    window.parent.postMessage(
      {
        silexgis: EMBED_CHANNEL,
        v: EMBED_PROTOCOL,
        type: 'ready',
        loaded: said.loaded,
        party: said.party,
        past: said.pastNow,
      } satisfies EmbedReadyMessage,
      origin,
    );
  }, []);

  /**
   * Says the party again whenever it stops being what the framer was told.
   *
   * The whole reason the announcement is not a one-off. The envelope lands after the frame has
   * loaded and been greeted, and afterwards each minute's poll can move somebody or bring them
   * out; a host page that greys out or labels its links from this message has to hear about that,
   * and it has no way to ask. An unchanged party is not re-announced, so a poll that found nothing
   * new costs the page nothing.
   */
  useEffect(() => {
    const origin = framerOrigin.current;
    if (origin !== null && announced.current !== statement) {
      announce(origin);
    }
  }, [announce, statement]);

  /**
   * Where a place named by the host page is in this model, or null when it is nowhere.
   *
   * <b>A station this drawing does not hold is nowhere, and it is answered as nowhere here rather
   * than by flying at it.</b> Handed to the panel it would reject, and the reader — who pressed a
   * link in an article and is looking at a frame with no chrome in it — would get a camera that did
   * not move and a notice inside somebody else's page. The frame answers `found: false` instead, in
   * the same breath as a place nobody has reported, which is what lets the article grey the link out
   * rather than offer one that goes nowhere.
   */
  const stationOfCaver = useCallback(
    (ref: string) => {
      const ordinal = Number.parseInt(ref, 10);
      // Of whatever is on screen: a link naming a place in the party means the party being drawn,
      // and reading it off the live trip while a past one is playing would fly the camera to a
      // station somebody is standing at today under the name of somebody from six years ago.
      const participant = view?.participants.find((person) => person.ordinal === ordinal);
      const station = participant?.stationName ?? null;
      return station === null || unplacedStations.has(station) ? null : station;
    },
    [view, unplacedStations],
  );

  /**
   * Where a team named by the host page is, for a single camera move.
   *
   * <b>What a team link means on a trip nobody is replaying.</b> Over a replay, "the survey team"
   * is somebody to keep up with as the clock runs; there is no clock on the live trip, so it is a
   * place — answered through the very derivation that picks which member speaks for a team on the
   * replay, so the two readings cannot point at different people. Keeping it a follow here instead
   * would re-aim the camera at every position report with no strip on screen to call it off, since
   * the strip that carries the stop-following control is only drawn over the past.
   */
  const stationOfTeam = useCallback(
    (ref: string) => {
      const station = followedStation(cavers, { kind: 'team', id: ref });
      return station === null || unplacedStations.has(station) ? null : station;
    },
    [cavers, unplacedStations],
  );

  useEffect(() => {
    // Nothing to talk to. A viewer opened directly in a tab is a legitimate thing to do — it is
    // how somebody checks a snippet before pasting it — and it simply has no conversation.
    if (window.parent === window) {
      return;
    }
    const parent = window.parent;

    const reply = (origin: string, message: EmbedOutboundMessage) => {
      // Never `'*'`: the party, and which stations it is standing at, goes to the document that
      // framed this page and to no other listener that happens to be in the chain.
      parent.postMessage(message, origin);
    };

    const onMessage = (event: MessageEvent) => {
      // The framer, and only the framer. A page nested deeper, an opener, a worker — none of them
      // are who this page is having a conversation with, and `event.origin` alone would not tell
      // them apart from the one that is.
      if (event.source !== parent) {
        return;
      }
      const inbound = parseEmbedInbound(event.data);
      if (inbound === null) {
        return;
      }
      if (inbound.type === 'hello') {
        // Remembered, because this is the only moment the framer's origin is ever stated and
        // every later announcement has to be addressed to it.
        framerOrigin.current = event.origin;
        announce(event.origin);
        return;
      }

      const { kind, ref } = inbound.target;
      const settled = (found: boolean) =>
        reply(event.origin, {
          silexgis: EMBED_CHANNEL,
          v: EMBED_PROTOCOL,
          type: 'focused',
          target: { kind, ref },
          found,
        });

      /*
       * Which trip the prose is talking about, and where its clock stands.
       *
       * Read before the camera, because both of the answers below depend on it: a link naming a
       * caver of a past trip has to open that trip first, and the place it then names is a place in
       * that trip's party rather than in today's.
       */
      const tripRef = inbound.trip ?? (kind === 'trip' ? ref : null);
      const askedAt = inbound.at ?? (kind === 'moment' ? ref : null);

      if (tripRef === EMBED_TRIP_LIVE) {
        // An article's own way back out of the past — the hyperlink form of the button this frame
        // draws. Always honourable: the trip this link was published for is the page's home state
        // whether or not anybody is underground in it.
        backToNow();
        settled(true);
        return;
      }
      if (tripRef !== null) {
        // A *person* named alongside a trip is whom to keep up with through it, not a camera move:
        // the track has not been read yet, so there is no station to fly to, and the follow aims
        // the camera by itself the moment there is one.
        const follow: PastFollow | null =
          kind === 'team'
            ? { kind: 'team', id: ref }
            : kind === 'caver'
              ? { kind: 'caver', id: ref }
              : null;
        openPast(tripRef, { at: askedAt, follow });
        // A *place* named alongside one is the other half of the same sentence, and it is held
        // rather than dropped: the trip's survey is not on screen yet, so the move is made when it
        // is, and the answer sent then is the drawing's own.
        if (kind === 'station' || kind === 'survey') {
          setPendingPlace({ kind, ref, settle: settled });
          return;
        }
        // Whether this cave has such a trip is not knowable in this breath. What is answered here
        // is that the frame accepted the request; the honest word about what it found arrives on
        // the next `ready`, which names the trip on screen and the party in it.
        settled(true);
        return;
      }
      if (pastEngaged && (kind === 'team' || kind === 'caver')) {
        // Within the trip already on screen. Over a replay, "show me Ana" means keep up with Ana as
        // the clock runs — a camera flown once would be pointing at where she was a minute of
        // playback ago. On the live trip a place in the party stays a single camera move, which is
        // what it has always been and what an article pasted years ago is written against — and a
        // team there is the same answer at the other scale, resolved below.
        setFollow({ kind: kind === 'team' ? 'team' : 'caver', id: ref });
        settled(true);
        return;
      }
      if (askedAt !== null) {
        const asked = instantOf(askedAt);
        // A moment only means something over a replay. On the live trip there is no clock to move,
        // and answering `false` is what lets an article grey such a link out rather than offer one
        // that does nothing.
        if (asked === null || pastSpan === null) {
          settled(false);
          return;
        }
        setPastAt(Math.min(Math.max(asked, pastSpan.from), pastSpan.to));
        if (kind === 'moment') {
          settled(true);
          return;
        }
      }

      const station =
        kind === 'caver' ? stationOfCaver(ref) : kind === 'team' ? stationOfTeam(ref) : ref;
      if (station === null) {
        // A place in the party that nobody has placed. Answered rather than ignored, so an
        // article can grey a link out instead of offering one that does nothing.
        settled(false);
        return;
      }
      // A fresh object every time, which is what asks the panel to move — pressing the same link
      // twice has to fly the camera back, and it would not if the request compared equal.
      setFocusRequest({
        kind: kind === 'survey' ? 'survey' : 'station',
        ref: station,
        onSettled: (found: boolean) => settled(found),
      });
      // The camera being flown is the 3D pane's; an answer performed behind a sheet tab
      // would read as a link that did nothing, so the strip turns to the drawing that moved.
      setActiveTab(TAB_3D);
    };

    window.addEventListener('message', onMessage);
    return () => window.removeEventListener('message', onMessage);
  }, [
    announce,
    stationOfCaver,
    stationOfTeam,
    openPast,
    backToNow,
    setFollow,
    setPastAt,
    pastSpan,
    pastEngaged,
  ]);

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
   * The frame's own box, marked whenever the past is what is in it.
   *
   * One value for every branch below rather than a class written out per return: the amber outline
   * is the signal designed to cost no height at all, for the reader who scrolled the strip out of
   * view, and a branch that spelled its class by hand is a state where that reader is shown a past
   * trip in a frame that looks live.
   */
  const frameClass = past.engaged
    ? 'public-trip-embed public-trip-embed-past'
    : 'public-trip-embed';

  const failure = (message: string) => (
    <div className="public-trip-embed" style={palette} data-testid="public-trip-embed-failure">
      <div className="public-trip-embed-failure">
        <Typography.Text type="secondary">{message}</Typography.Text>
      </div>
    </div>
  );

  if (isPending) {
    return (
      <div className="public-trip-embed" style={palette}>
        <Flex align="center" justify="center" style={{ height: '100%' }}>
          <Spin size="large" />
        </Flex>
      </div>
    );
  }

  // Only when there is nothing to show. A poll that failed while an envelope is already in hand —
  // a phone that went through a tunnel — leaves the drawing and the party exactly where they were
  // rather than replacing somebody's website with an assertion that the link never existed.
  if (data === undefined) {
    return failure(t('publicTrip.notFoundTitle'));
  }

  /**
   * The archive, over the frame rather than beside it.
   *
   * <b>A sheet and not a section, because the box is the whole page here.</b> The frame a club
   * pasted is often 4:3 and sometimes 260px tall; a list standing open inside it would leave the
   * drawing a strip. So it covers the frame while it is being read and gets out of the way after —
   * and it is scrolled inside itself, so a long history never pushes the drawing out of somebody's
   * article.
   */
  const archive = (
    <Drawer
      placement="bottom"
      open={picker}
      onClose={() => setPicker(false)}
      // `size` and not `height`: the library renamed this prop, and the old spelling warns on the
      // console of every reader who opens the list — on somebody else's website, where nobody is
      // watching. A share rather than a number, so the sheet is right in a 260px frame and in a
      // 900px one.
      size="80%"
      title={t('publicTrip.past.sectionTitle')}
      // The frame is the whole document; without this the sheet would be hung on the browser's
      // body and drawn over the host page's own content rather than over this viewer.
      getContainer={false}
      rootClassName="public-past-drawer"
      data-testid="public-past-drawer"
    >
      <PublicPastTripList
        trips={pastTrips.data?.trips}
        more={pastTrips.data?.more ?? false}
        loading={pastTrips.isPending}
        failed={pastTrips.isError}
        playingId={past.tripLogId}
        onPlay={(tripLogId) => {
          past.open(tripLogId, { follow: null });
          setPicker(false);
        }}
      />
    </Drawer>
  );

  /**
   * The one line of chrome this frame has, along its bottom edge.
   *
   * <b>It is a line and not a panel, and that is the whole design of it.</b> The page next door has
   * a title, a party list and a footer because it is a page; this document is a drawing inside
   * somebody else's article and everything it adds is taken from the drawing. So while the live
   * trip is on screen there is one control, saying what it opens; while a past trip is playing the
   * strip becomes the statement that it is the past and the controls that move through it, because
   * at that point the reader has to be told something and there is no cheaper place to tell them.
   */
  const strip = past.engaged ? (
    <PublicPastBar playback={past} liveState={data.state} cavers={cavers} compact />
  ) : (
    <div className="public-trip-embed-strip">
      <Button
        size="small"
        icon={<HistoryOutlined />}
        onClick={() => setPicker(true)}
        data-testid="public-past-open"
      >
        {t('publicTrip.past.sectionTitle')}
      </Button>
    </div>
  );

  /**
   * A chosen past trip that has not arrived, or could not be read.
   *
   * <b>Drawn before the missing-drawing branch below, because it is not that.</b> There is no model
   * in hand for the whole of a track's fetch and permanently when one fails — so the branch below
   * would state, inside somebody's article, that this trip has no survey drawing, beside a strip
   * that is still saying the trip is being read. The frame says nothing about a drawing it has not
   * been given: the strip underneath already carries the sentence that belongs to this moment,
   * whether that is "Reading this trip…" or "This trip could not be read".
   */
  if (past.engaged && view === undefined) {
    return (
      <div className={frameClass} style={palette} data-testid="public-trip-embed">
        <div className="public-trip-embed-pending" data-testid="public-trip-embed-pending" />
        {strip}
        {archive}
      </div>
    );
  }

  if (model === null || pinnedModelUrl === null) {
    // The trip is published and there is no drawing to put in a frame. Said in words rather than
    // left as an empty box, because an empty box on somebody's website reads as a broken embed.
    // The archive is still reachable: a trip with no survey of its own is exactly the case where a
    // reader is best served by the cave's other trips.
    //
    // The frame keeps its past outline here as everywhere else: a past trip that genuinely carries
    // no survey is still a past trip, and the one signal that costs no height is the one a reader
    // who scrolled the strip out of view is left with.
    return (
      <div className={frameClass} style={palette} data-testid="public-trip-embed-failure">
        <div className="public-trip-embed-failure">
          <Typography.Text type="secondary">{t('publicTrip.embedNoModel')}</Typography.Text>
        </div>
        {strip}
        {archive}
      </div>
    );
  }

  return (
    <div className={frameClass} style={palette} data-testid="public-trip-embed">
      {/* The strip inside the frame: the 3D drawing and one tab per sheet, the bar hidden
          while there are no sheets so a mapless trip's frame is exactly the viewer it always
          was. Panes fill whatever box the snippet gave the frame; inactive ones stay mounted
          behind display:none, so switching costs no reload and no reparse. */}
      <Tabs
        className="public-trip-embed-tabs"
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
                // The one mount where the whole height is right: this document IS the frame, its
                // size was chosen by whoever pasted the snippet, and the page that scrolls is
                // theirs.
                height="100%"
                trackedCavers={cavers}
                // The one thing this frame learns for itself. The list of people that would have
                // said it is the article around the frame, so it leaves here on the message
                // instead.
                onUnplacedStationsChange={setUnplacedStations}
                crsLookup={crsLookup}
                focusRequest={focusRequest}
                toolbar
                // From the envelope, exactly as on the page next door and through the same
                // derivation. The links a station's pictures are otherwise read from answer only
                // to an account, and this document is served to a stranger on somebody else's
                // website — so what is drawn here is what the server decided may be published,
                // and this file decides nothing further.
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
              <Suspense fallback={<Skeleton active />}>
                <PublicTripSheetPane
                  sheet={sheet}
                  // The same fold the 3D pane draws — one party, two drawings, no disagreement.
                  cavers={cavers}
                  active={activeTab === sheet.key}
                  height="100%"
                  token={token}
                  // The sheets follow the same team the 3D scene does: one replay, two drawings.
                  followStation={followStation}
                />
              </Suspense>
            ),
          })),
        ]}
      />
      {strip}
      {archive}
    </div>
  );
}
