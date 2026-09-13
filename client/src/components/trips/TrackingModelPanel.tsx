// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo, useState } from 'react';
import { Button, Card, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import {
  surveyModelReadableByViewer,
  useSurveyModel,
  type TrackingEvent,
  type TrackingState,
  type TripParticipant,
} from '../../api/hooks.ts';
import CaveViewPanel from '../caveview/CaveViewPanel.tsx';
import { trackedCaversFrom } from '../../caveview/trackedCavers.ts';
import { viewerFileName } from '../../caveview/viewerFileName.ts';
import { useIsMobile } from '../../hooks/useIsMobile.ts';

export interface TrackingModelPanelProps {
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
 */
export default function TrackingModelPanel({
  tracking,
  participants,
  events,
}: TrackingModelPanelProps) {
  const { t } = useTranslation();
  const narrow = useIsMobile();
  const [open, setOpen] = useState(false);
  const { data: model } = useSurveyModel(tracking.surveyModelId ?? undefined);

  const names = useMemo(
    () => new Map(participants.map((person) => [person.caverId, person.name])),
    [participants],
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
          name: names.get(caverId) ?? t('trips.tracking.unknownCaver'),
          enteredAt: enteredAt.get(caverId) ?? null,
        }),
        model?.id,
      ),
    [tracking, names, enteredAt, model?.id, t],
  );

  if (model === undefined || model.status !== 'ready' || !surveyModelReadableByViewer(model)) {
    return null;
  }

  return (
    <Card
      size="small"
      title={t('trips.tracking.modelTitle')}
      data-testid="trip-tracking-model"
      extra={
        <Button size="small" onClick={() => setOpen(!open)} data-testid="trip-tracking-model-toggle">
          {t(open ? 'trips.tracking.modelHide' : 'trips.tracking.modelShow')}
        </Button>
      }
    >
      {open ? (
        <CaveViewPanel
          fileUrl={model.modelUrl}
          fileName={viewerFileName(model)}
          height={narrow ? NARROW_HEIGHT : HEIGHT}
          surveyModelId={model.id}
          trackedCavers={cavers}
          // The viewer's own controls: this is a model shown to be read rather than one shown
          // beside chrome competing for the same corner.
          toolbar
        />
      ) : (
        <Typography.Text type="secondary">
          {t('trips.tracking.modelHint', { name: model.name })}
        </Typography.Text>
      )}
    </Card>
  );
}
