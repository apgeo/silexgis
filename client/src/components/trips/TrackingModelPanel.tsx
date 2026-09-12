// SPDX-License-Identifier: AGPL-3.0-or-later
import { useCallback, useMemo, useState } from 'react';
import { Button, Card, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import {
  surveyModelReadableByViewer,
  useSurveyModel,
  useTripTrackingEventLog,
  type TrackingEvent,
  type TrackingState,
  type TripParticipant,
} from '../../api/hooks.ts';
import CaveViewPanel from '../caveview/CaveViewPanel.tsx';
import { trackedCaversFrom } from '../../caveview/trackedCavers.ts';
import { trackedCaversAt } from '../../caveview/trackingReplay.ts';
import { viewerFileName } from '../../caveview/viewerFileName.ts';
import { useCoarsePointer } from '../../hooks/useCoarsePointer.ts';
import { useIsMobile } from '../../hooks/useIsMobile.ts';
import TrackingReplayBar from './TrackingReplayBar.tsx';

export interface TrackingModelPanelProps {
  /** Whose watch this is — the replay reads the trip's whole log for itself. */
  tripLogId: string;
  /** The watch as it stands, including which model it resolves positions against. */
  tracking: TrackingState;
  /** The trip's roster. The watch carries caver ids and nothing on it knows what anybody is called. */
  participants: readonly TripParticipant[];
  /** The reports on screen, newest first — where the moment somebody went in is read from. */
  events: readonly TrackingEvent[] | undefined;
}

/** Taller than a phone can spare, shorter than a desk screen would waste. */
const HEIGHT = 460;
const NARROW_HEIGHT = 320;

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

/** The two numbers above, each held under what the screen can actually spare. */
const modelHeight = (narrow: boolean) =>
  `min(${narrow ? NARROW_HEIGHT : HEIGHT}px, ${VIEWPORT_SHARE})`;

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
}: TrackingModelPanelProps) {
  const { t } = useTranslation();
  const narrow = useIsMobile();
  // The one control this panel owns is the button that opens the model, and how big it has to be
  // depends on what is pressing it and on nothing else — the same rule, and the same hook, as the
  // replay strip inside it.
  const coarse = useCoarsePointer();
  const [open, setOpen] = useState(false);
  /** Whether the panel is showing a moment of the trip rather than the watch as it stands. */
  const [replaying, setReplaying] = useState(false);
  /** The moment being replayed, or null while there is no replay or none has been settled on. */
  const [replayAt, setReplayAt] = useState<number | null>(null);
  const { data: model } = useSurveyModel(tracking.surveyModelId ?? undefined);

  // Asked for only once somebody wants a replay, and only while the model is on screen: it is
  // several requests on a long trip, and this tab is opened routinely by somebody who wants to
  // record that the party went in and nothing else.
  const log = useTripTrackingEventLog(tripLogId, open && replaying);

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

  if (model === undefined || model.status !== 'ready' || !surveyModelReadableByViewer(model)) {
    return null;
  }

  const shown = replaying && replayCavers !== null ? replayCavers : cavers;
  const controlSize: 'large' | 'small' = coarse ? 'large' : 'small';

  /** Hiding the model puts the live watch back: a replay of a model nobody is looking at is state. */
  const onToggle = () => {
    if (open) {
      setReplaying(false);
      setReplayAt(null);
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
        <Button
          size={controlSize}
          onClick={onToggle}
          data-testid="trip-tracking-model-toggle"
        >
          {t(open ? 'trips.tracking.modelHide' : 'trips.tracking.modelShow')}
        </Button>
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
          />
          <CaveViewPanel
            fileUrl={model.modelUrl}
            fileName={viewerFileName(model)}
            height={modelHeight(narrow)}
            surveyModelId={model.id}
            trackedCavers={shown}
            // The viewer's own controls: this is a model shown to be read rather than one shown
            // beside chrome competing for the same corner.
            toolbar
          />
        </>
      ) : (
        <Typography.Text type="secondary">
          {t('trips.tracking.modelHint', { name: model.name })}
        </Typography.Text>
      )}
    </Card>
  );
}
