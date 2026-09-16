// SPDX-License-Identifier: AGPL-3.0-or-later
import { useCallback, useMemo, useRef, useState } from 'react';
import { CompressOutlined, ExpandOutlined, PushpinOutlined } from '@ant-design/icons';
import { Alert, Button, Card, Flex, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import {
  surveyModelReadableByViewer,
  useSurveyModel,
  useTripMomentPictureLinks,
  useTripTrackingEventLog,
  type TrackingEvent,
  type TrackingState,
  type TripParticipant,
} from '../../api/hooks.ts';
import CaveViewPanel from '../caveview/CaveViewPanel.tsx';
import type { CaveViewMediaEntry } from '../../caveview/loadCaveView.ts';
import { pathOf, type PickedModelPart } from '../../caveview/modelParts.ts';
import { trackedCaversFrom } from '../../caveview/trackedCavers.ts';
import { caveViewToolbarButtons } from '../../caveview/toolbarButtons.ts';
import {
  placedPicturesAt,
  replayPictures,
  trackedCaversAt,
} from '../../caveview/trackingReplay.ts';
import { useStationMedia } from '../../caveview/useStationMedia.ts';
import { viewerFileName } from '../../caveview/viewerFileName.ts';
import { useCoarsePointer } from '../../hooks/useCoarsePointer.ts';
import { useIsMobile } from '../../hooks/useIsMobile.ts';
import TrackingPicturesDialog from './TrackingPicturesDialog.tsx';
import TrackingReplayBar from './TrackingReplayBar.tsx';
import TrackingReportDialog from './TrackingReportDialog.tsx';

export interface TrackingModelPanelProps {
  /** Whose watch this is — the replay reads the trip's whole log for itself. */
  tripLogId: string;
  /** The watch as it stands, including which model it resolves positions against. */
  tracking: TrackingState;
  /** The trip's roster. The watch carries caver ids and nothing on it knows what anybody is called. */
  participants: readonly TripParticipant[];
  /** The reports on screen, newest first — where the moment somebody went in is read from. */
  events: readonly TrackingEvent[] | undefined;
  /** Whether this reader may write to the log at all. */
  canEdit: boolean;
  /**
   * Who is ticked on the table above. Offered as the answer when a station is pressed, never
   * imposed: the dialog asks, because requiring the ticks first would put back the scrolling the
   * whole press-a-station path exists to remove.
   */
  selectedCaverIds: readonly string[];
  /** Called once a report has landed, so the selection that produced it can be let go. */
  onRecorded: () => void;
}

/** Taller than a phone can spare, shorter than a desk screen would waste. */
const HEIGHT = 460;
const NARROW_HEIGHT = 320;

/**
 * The same two numbers for a reader who has asked for more of the screen.
 *
 * <b>Asked for rather than shipped, because the model is not what this tab is for.</b> The tab is
 * opened to record that a party went in, and it is read as a table; the model is the second reading
 * of the same watch. So it opens at the size that leaves the rest of the tab usable, and somebody
 * tracing a route through a two-hundred-station survey says when they want the screen.
 */
const LARGE_HEIGHT = 760;
const NARROW_LARGE_HEIGHT = 560;

/**
 * The share of the screen the model is allowed to take, whatever else says about its size.
 *
 * <b>This is the axis neither of the other two describes, and leaving it out is what made a phone
 * in landscape unusable.</b> Width says how much room there is across, the pointer says how big a
 * thing has to be to press — and neither of them knows that a 863x360 phone on its side is wide
 * enough to be handed the desk layout's 460px model in a viewport 360px tall. Measured there, the
 * model overhung the screen by a hundred pixels with the card's own edges off it too, and because
 * the viewer takes every gesture that starts inside it there was nowhere left to put a finger: a
 * swipe at the middle of the surface and a swipe at the bottom strip of the viewport both moved the
 * page zero pixels.
 *
 * <b>The gesture cannot be handed back, which is why this is the fix.</b> The obvious repair is the
 * one the replay bar's scrubber uses — `touch-action: pan-y`, giving the browser the axis it
 * scrolls on. It does not work here and must not be copied here. That strip can spare the vertical
 * axis because it only scrubs along the horizontal one; a model is turned in both, and one-finger
 * vertical drag is how anybody looks down a pitch. Driven against the live viewer with `pan-y`
 * forced onto the surface, the page still scrolled zero pixels while the scene still moved: the
 * browser claims the gesture — later `touchmove`s arrive with `cancelable` false — but the viewer's
 * container is `overflow: hidden`, so the pan it starts belongs to a box with nothing to scroll and
 * chains no further. The result is the worst of the two, and the honest reading is that the surface
 * is a control that owns its gestures, exactly like the slider's handle.
 *
 * So the model is kept from ever being the only thing under a thumb. At three fifths there is
 * always two fifths of the screen that is ordinary page, in any orientation, and a reader who wants
 * to get past the model never has to fight it for the gesture. `dvh` rather than `vh` because a
 * phone's address bar collapses and `vh` keeps quoting the taller measurement — the fraction is
 * here to guarantee what is left over, so it has to be a fraction of what is actually on screen.
 *
 * It also puts the viewer's own fullscreen button back in working order as a side effect, and that
 * is not a coincidence: the viewer decides whether it is already fullscreen by comparing its height
 * to the window's, and a height that is a fraction of the viewport can never be mistaken for the
 * whole of it.
 */
const VIEWPORT_SHARE = '60dvh';

/**
 * And the share a reader who asked for a bigger model gets.
 *
 * <b>Still a share, and that is the whole of what makes the larger size safe.</b> Everything the
 * comment above says about the gesture is unchanged by somebody asking for more room: the viewer
 * still owns every touch that begins inside it, and a model as tall as the viewport would still
 * leave nowhere to put a finger to get past it. So what the control changes is how much of the
 * screen the model takes, never whether a strip of ordinary page is left — at four fifths there is
 * still a fifth, which on the shortest screen this is designed at is 72px of page, well above what
 * a thumb needs to land on. A hundred is the number that must never be written here.
 *
 * <b>This is the whole of the size answer on this panel</b>, and the viewer's own fullscreen button
 * is deliberately not the other half of it — see below for what pressing it costs here.
 */
const LARGE_VIEWPORT_SHARE = '80dvh';

/**
 * The one control of the viewer's own that this panel does not offer, and why it is this one.
 *
 * <b>Everything this panel draws over the model is outside the element that goes fullscreen.</b>
 * The viewer's fullscreen button puts its own container — the drawing surface — into the browser's
 * top layer, and the top layer paints over the whole document. The list of who is where, the notice
 * that a link named a station this model does not hold, and the offer a station press raises are
 * all siblings of that surface rather than children of it, so all three stop existing the moment
 * the button is pressed.
 *
 * Measured on the live instance at 1440x900: with the surface fullscreen, `document.fullscreenElement`
 * is `.caveview-panel-surface`, a press on a station still fires and still raises the offer at
 * 117,541 — and `document.elementFromPoint` at the centre of its "Record here" button answers
 * `CANVAS`. The same probe at the centre of the party list answers nothing at all. So the press
 * records a station, draws a button, and the button cannot be reached: a path that looks like it is
 * working and is not.
 *
 * <b>Taken out here rather than repaired, because this panel has a better answer already.</b> The
 * size control beside the title gives the model four fifths of the screen, which is the size answer
 * this panel needs, and keeps the fifth of the page a thumb has to be able to land on. The other
 * surfaces that draw a model keep the button: a page whose whole job is the viewer loses nothing by
 * covering itself with it.
 */
const PANEL_TOOLBAR_OMITS = 'fullscreen';

/** The numbers above, each held under what the screen can actually spare. */
const modelHeight = (narrow: boolean, large: boolean) =>
  large
    ? `min(${narrow ? NARROW_LARGE_HEIGHT : LARGE_HEIGHT}px, ${LARGE_VIEWPORT_SHARE})`
    : `min(${narrow ? NARROW_HEIGHT : HEIGHT}px, ${VIEWPORT_SHARE})`;

/**
 * The party on the survey: everybody the watch names, drawn where they were last reported.
 *
 * <b>The table above this says the same thing in words, and this does not replace it.</b> A place
 * on a model is only legible to somebody who knows the cave, while "P12, 84 m, 40 minutes ago" is
 * legible to anybody — and a withheld position has no point to draw at all, which is exactly when
 * a picture is at its most misleading. So this is the second reading of the watch, never the only
 * one, and everybody it cannot place is still listed inside it as unplaced.
 *
 * <b>It is opened rather than mounted.</b> A survey viewer downloads a model and takes one of the
 * handful of drawing contexts a browser will keep alive, and this tab is opened routinely by
 * somebody who only wants to record that the party went in. So the model arrives when it is asked
 * for, which on a phone on a hillside is also the difference between a page that loads and one
 * that does not.
 *
 * Nothing is offered at all unless the watch resolves positions against a model this viewer can
 * read: a watch with no model has no station names to place, and a wall mesh has no stations.
 *
 * <b>The same model answers a second question: where everybody was.</b> The replay below hands the
 * viewer a watch derived from the log at a chosen moment instead of the live one — the same shape,
 * so nothing downstream of it knows the difference — and leaving the replay hands back the live one
 * untouched. It writes nothing: it is a second reading of reports already recorded.
 */
export default function TrackingModelPanel({
  tripLogId,
  tracking,
  participants,
  events,
  canEdit,
  selectedCaverIds,
  onRecorded,
}: TrackingModelPanelProps) {
  const { t } = useTranslation();
  const narrow = useIsMobile();
  // The controls this panel owns are pressed, and how big they have to be depends on what is
  // pressing them and on nothing else — the same rule, and the same hook, as the replay strip
  // inside it.
  const coarse = useCoarsePointer();
  const [open, setOpen] = useState(false);
  /** Whether the reader has asked the model for more of the screen than it opens with. */
  const [large, setLarge] = useState(false);
  /** The station last pressed in the model, while the offer to record there is still standing. */
  const [picked, setPicked] = useState<PickedModelPart | null>(null);
  /** The station the dialog is recording at, or null while it is closed. */
  const [recording, setRecording] = useState<string | null>(null);
  /** Whether the panel is showing a moment of the trip rather than the watch as it stands. */
  const [replaying, setReplaying] = useState(false);
  /** The moment being replayed, or null while there is no replay or none has been settled on. */
  const [replayAt, setReplayAt] = useState<number | null>(null);
  /** The moment the picture dialog is attaching to, or null while it is closed. */
  const [attachingAt, setAttachingAt] = useState<number | null>(null);
  const { data: model } = useSurveyModel(tracking.surveyModelId ?? undefined);

  // The set the panel would have been given, less the one control that would hide the rest of the
  // panel. Subtracted from what the viewer wrapper chooses rather than listed here, so which
  // controls fit a screen stays decided in the one place that has the measurements for it.
  const toolbarButtons = useMemo(
    () => caveViewToolbarButtons({ narrow, coarse }).filter((id) => id !== PANEL_TOOLBAR_OMITS),
    [narrow, coarse],
  );

  // Asked for only once somebody wants a replay, and only while the model is on screen: it is
  // several requests on a long trip, and this tab is opened routinely by somebody who wants to
  // record that the party went in and nothing else.
  const log = useTripTrackingEventLog(tripLogId, open && replaying);

  /**
   * The photographs linked to this model's stations, shown over the model where they were taken.
   *
   * <b>Gated on the model being open, for the same reason the model itself is.</b> This tab is
   * opened routinely by somebody who only wants to record that the party went in, and the pictures
   * are worth exactly nothing until there is a model on screen to draw them over — so the request
   * is not made until the panel is opened, and stops being made when it is closed again.
   *
   * The same source the cave's own survey viewer reads, rather than a second reading of it: what a
   * photograph is anchored to, and how far a reader may reach for it, are one rule and have one home.
   */
  const stationMedia = useStationMedia(model?.id, open);

  const names = useMemo(
    () => new Map(participants.map((person) => [person.caverId, person.name])),
    [participants],
  );
  const nameOf = useCallback(
    (caverId: string) => names.get(caverId) ?? t('trips.tracking.unknownCaver'),
    [names, t],
  );

  /**
   * When each person went in, read off the reports the page is holding.
   *
   * The watch folds every report into one position and one time, so the moment somebody entered
   * survives only on the log — and only as far back as this page has asked for it. A trip whose
   * entries have scrolled off the recent reports shows no time rather than a wrong one, which is
   * why the card says "—" there instead of guessing from the trip's own start.
   *
   * Newest first, first one wins: somebody who came out and went back in is in on the strength of
   * the later entry.
   */
  const enteredAt = useMemo(() => {
    const entries = new Map<string, string>();
    for (const event of events ?? []) {
      if (event.kind === 'entered' && !entries.has(event.caverId)) {
        entries.set(event.caverId, event.recordedAt);
      }
    }
    return entries;
  }, [events]);

  const cavers = useMemo(
    () =>
      trackedCaversFrom(
        tracking,
        (caverId) => ({
          name: nameOf(caverId),
          enteredAt: enteredAt.get(caverId) ?? null,
        }),
        model?.id,
      ),
    [tracking, nameOf, enteredAt, model?.id],
  );

  /**
   * The same watch, as it stood at the replayed moment.
   *
   * Only ever built from a log that is complete. Until every page is in, and whenever the read
   * failed, `replayAt` stays null and the live watch is what the viewer keeps — a replay over the
   * first page alone would be the newest reports only, and would draw the party materialising
   * part-way through the trip as confidently as it draws anything else.
   */
  const replayCavers = useMemo(
    () =>
      replayAt === null || log.data === undefined
        ? null
        : trackedCaversAt(tracking, log.data, replayAt, nameOf, model?.id),
    [tracking, log.data, replayAt, nameOf, model?.id],
  );

  /** The party as a chooser takes it — the watch says who is on it, the roster says their names. */
  const dialogCavers = useMemo(
    () =>
      tracking.participants.map((participant) => ({
        caverId: participant.caverId,
        name: nameOf(participant.caverId),
      })),
    [tracking.participants, nameOf],
  );

  /**
   * The photographs hung on this trip's own moments.
   *
   * <b>A different thing from the station strip above, and the difference is what makes it
   * possible at all.</b> A station picture says "this was taken here" and shows on every replay of
   * every trip through this cave; a moment picture says "this was taken at 14:05 of this trip", and
   * where that is, is folded out of the log at read time — which is why a correction that deletes a
   * report and re-enters it destroys nothing.
   *
   * Read through the trip's links, gated on the model being open exactly as the station strip is.
   */
  const pictureLinks = useTripMomentPictureLinks(tripLogId, open);
  const momentPictures = useMemo(
    () => replayPictures(pictureLinks.data?.items ?? [], tripLogId),
    [pictureLinks.data, tripLogId],
  );

  /**
   * Which station each moment picture is drawn at.
   *
   * A picture is hung at the position of the caver it is about, folded from the very reports the
   * party's own markers are folded from — so a reader who was refused a position has no marker for
   * one to hang on, and the picture stays on the timeline strip where it says a time and no place.
   * A picture about nobody in particular is never placed: a party that has split is in two places,
   * and "the party's station" is not something a derivation may invent.
   *
   * <b>Where the picture is drawn is folded at the picture's own moment, not at the instant on the
   * scrubber</b>, which is why this hands the whole log down rather than the party as it stands at
   * `replayAt` — the derivation's own note says why, and it is the difference between a photograph
   * at the pitch head and the same photograph following its subject into the sump.
   */
  const placedPictures = useMemo(() => {
    if (!replaying || replayAt === null || log.data === undefined) {
      return new Map<string, CaveViewMediaEntry[]>();
    }
    return placedPicturesAt(momentPictures, log.data, replayAt, model?.id);
  }, [replaying, replayAt, log.data, momentPictures, model?.id]);

  /**
   * What the viewer is told about a station's pictures — a <b>function</b>, and one whose identity
   * never changes.
   *
   * <b>Handing the viewer a different source tears down its hover listeners and closes an open
   * strip.</b> A replay re-derives the placed pictures on every tick of its clock, five times a
   * second, so a map rebuilt per moment would be a new source per tick: a reader who had tapped a
   * station would watch the photographs vanish under their thumb having touched nothing. So the
   * source is a stable closure over refs, read at the instant the pointer arrives.
   */
  const stationMediaRef = useRef(stationMedia);
  stationMediaRef.current = stationMedia;
  const placedRef = useRef(placedPictures);
  placedRef.current = placedPictures;
  const mediaSource = useCallback((station: unknown) => {
    const path = pathOf(station);
    if (path === null) {
      return null;
    }
    const held = stationMediaRef.current.get(path) ?? [];
    const placed = placedRef.current.get(path) ?? [];
    return [...held, ...placed];
  }, []);

  /**
   * The survey this watch is armed on is no longer on this server — somebody deleted it.
   *
   * <b>Said, rather than drawn as nothing.</b> Every other reason this panel does not appear is a
   * reason a co-ordinator can wait out or ignore: a model still being read finishes, a model this
   * reader may not open is a permission they either have or do not. This one is neither. The watch
   * goes on saying "Armed", goes on taking entries, exits and notes, and can place nobody — and
   * with the panel simply absent there is nothing on the page that says why the party and the
   * station control have gone, which on a rescue surface is the failure worth spending a box on.
   *
   * Placed above the other refusals because the model genuinely cannot be fetched in this state,
   * so the check below would swallow it into the same silence as everything else.
   */
  if (tracking.surveyModelMissing) {
    return (
      <Alert
        type="error"
        showIcon
        style={{ marginBottom: 16 }}
        data-testid="trip-tracking-model-missing"
        message={t('trips.tracking.modelMissingTitle')}
        description={t('trips.tracking.modelMissingBody')}
      />
    );
  }

  if (model === undefined || model.status !== 'ready' || !surveyModelReadableByViewer(model)) {
    return null;
  }

  const shown = replaying && replayCavers !== null ? replayCavers : cavers;
  const controlSize: 'large' | 'small' = coarse ? 'large' : 'small';
  /**
   * Whether pressing a station is worth offering at all.
   *
   * Both halves are the same rule the card under the watch is drawn by: reports land on an armed
   * watch and on no other, and a reader who cannot write to the log is not shown an offer that
   * would be refused. Nothing is gated on the refusal itself — a press that produced an offer that
   * produced a refusal is three acts spent learning something the page already knew.
   */
  const canRecord = canEdit && tracking.state === 'armed';

  /** Hiding the model puts the live watch back: a replay of a model nobody is looking at is state. */
  const onToggle = () => {
    if (open) {
      setReplaying(false);
      setReplayAt(null);
      // The size and the standing offer belong to a model on screen. Kept, they would decide how
      // much of the screen the next opening takes and put a station name over a model that has not
      // been drawn yet.
      setLarge(false);
      setPicked(null);
      setRecording(null);
      setAttachingAt(null);
    }
    setOpen(!open);
  };

  return (
    <Card
      size="small"
      title={t('trips.tracking.modelTitle')}
      // Named for the panel rather than for the model it holds. The survey chooser in the setup
      // card above is the other `trip-tracking-model`, and two elements answering one name is a
      // locator that matches both and picks neither.
      data-testid="trip-tracking-model-panel"
      extra={
        <Flex gap="small" wrap>
          {open && (
            <Button
              size={controlSize}
              icon={large ? <CompressOutlined /> : <ExpandOutlined />}
              onClick={() => setLarge(!large)}
              // Whether the word is there is a question about room, so it is answered by the
              // width; how big the button is is a question about the finger, so it is answered by
              // the pointer. Narrow, the label moves to the accessible name rather than to a
              // tooltip — a device with no hovering pointer never opens one, and a glyph explained
              // by a tooltip is, there, an unlabelled button.
              aria-label={t(large ? 'trips.tracking.modelSmaller' : 'trips.tracking.modelLarger')}
              data-testid="trip-tracking-model-size"
            >
              {narrow
                ? undefined
                : t(large ? 'trips.tracking.modelSmaller' : 'trips.tracking.modelLarger')}
            </Button>
          )}
          <Button size={controlSize} onClick={onToggle} data-testid="trip-tracking-model-toggle">
            {t(open ? 'trips.tracking.modelHide' : 'trips.tracking.modelShow')}
          </Button>
        </Flex>
      }
    >
      {open ? (
        <>
          {/* Above the model rather than over it. The viewer's own corners are spoken for — the
              toolbar, the list of who is where, a station's pictures — and a control that has to be
              dragged has no business sharing an edge with a scene that is dragged to be turned. */}
          <TrackingReplayBar
            tracking={tracking}
            events={log.data}
            loading={log.isPending}
            failed={log.error != null}
            engaged={replaying}
            onEngagedChange={(engaged) => {
              setReplaying(engaged);
              // Leaving hands the live watch straight back, in the same render.
              if (!engaged) {
                setReplayAt(null);
              }
            }}
            at={replayAt}
            onAtChange={setReplayAt}
            nameOf={nameOf}
            pictures={momentPictures}
            // Offered to whoever may write the log, and to nobody else. Unlike recording a report
            // this is not gated on the watch still being armed: the act it exists for happens after
            // the party is out, when somebody empties a memory card and turns the log into a report.
            onAttachHere={canEdit ? (moment) => setAttachingAt(moment) : undefined}
          />
          {/* <b>The press names a station and offers to record there; it does not open a dialog by
              itself.</b> Looking around a model means pressing things, and a form that appeared on
              every press would make the model unusable as a model — which is the same reasoning the
              survey viewer's own "link this part" offer is built on, and the same shape. */}
          {picked !== null && (
            <Alert
              type="info"
              showIcon
              closable
              onClose={() => setPicked(null)}
              title={t('caveview.picked.modelStation', { name: picked.label })}
              action={
                <Button
                  size={controlSize}
                  type="primary"
                  icon={<PushpinOutlined />}
                  onClick={() => setRecording(picked.anchor.station)}
                  data-testid="trip-tracking-record-here-open"
                >
                  {t('trips.tracking.recordHere')}
                </Button>
              }
              style={{ marginBottom: 8 }}
              data-testid="trip-tracking-picked-station"
            />
          )}
          <CaveViewPanel
            fileUrl={model.modelUrl}
            fileName={viewerFileName(model)}
            height={modelHeight(narrow, large)}
            surveyModelId={model.id}
            trackedCavers={shown}
            // A leg or a splay names no single place to report from, so it clears the offer rather
            // than leaving the last station standing under a press that meant something else.
            onPartPick={
              canRecord
                ? (part) => setPicked(part.anchorKind === 'modelStation' ? part : null)
                : undefined
            }
            // The viewer's own controls: this is a model shown to be read rather than one shown
            // beside chrome competing for the same corner. All but one of them — see above.
            toolbar={{ buttons: toolbarButtons }}
            // A function rather than the map, so that scrubbing — which re-derives the placed
            // pictures five times a second — never hands the viewer a different source and never
            // closes a strip somebody is looking at.
            stationMedia={mediaSource}
          />
          {canRecord && (
            <TrackingReportDialog
              open={recording !== null}
              tripLogId={tripLogId}
              station={recording}
              cavers={dialogCavers}
              teams={tracking.teams}
              defaultCaverIds={selectedCaverIds}
              onClose={() => setRecording(null)}
              onRecorded={() => {
                setPicked(null);
                onRecorded();
              }}
            />
          )}
          {canEdit && attachingAt !== null && (
            <TrackingPicturesDialog
              open
              tripLogId={tripLogId}
              defaultAt={attachingAt}
              defaultCaverId={selectedCaverIds[0] ?? null}
              cavers={dialogCavers}
              onClose={() => setAttachingAt(null)}
            />
          )}
        </>
      ) : (
        <Typography.Text type="secondary">
          {t('trips.tracking.modelHint', { name: model.name })}
        </Typography.Text>
      )}
    </Card>
  );
}
