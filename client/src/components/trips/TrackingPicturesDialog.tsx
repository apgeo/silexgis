// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo, useState } from 'react';
import {
  Alert,
  App,
  Checkbox,
  DatePicker,
  Empty,
  Flex,
  InputNumber,
  Modal,
  Select,
  Spin,
  Tag,
  Typography,
} from 'antd';
import dayjs, { type Dayjs } from 'dayjs';
import { useTranslation } from 'react-i18next';
import {
  useAttachTrackingPictures,
  usePhotos,
  useTripTrackingEventLog,
  type PhotoInfo,
  type TrackingPictureInput,
} from '../../api/hooks.ts';
import { picturePlacementAt, type PicturePlacement } from '../../caveview/trackingReplay.ts';
import { trackingProblemCodeMessage, trackingProblemMessage } from './trackingProblems.ts';

export interface TrackingPicturesDialogProps {
  open: boolean;
  tripLogId: string;
  /**
   * The moment the dialog opens at, as epoch milliseconds — the row's report, the scrubber's
   * instant, or the present. Used for photographs whose own file says nothing about when they were
   * taken, and never silently: those are flagged.
   */
  defaultAt: number;
  /** Who the moment is about by default, or null for a picture of the party. */
  defaultCaverId: string | null;
  /** The trip's roster, for the subject choosers. */
  cavers: readonly { caverId: string; name: string }[];
  /**
   * The survey the watch is on, as this reader is told it, or null where they are told none.
   *
   * What a chosen photograph's place is previewed against. It is the watch's own survey and not
   * whichever one a panel elsewhere happens to be showing: that is the survey a replay opens on.
   */
  surveyModelId: string | null;
  onClose(): void;
  onAttached?(): void;
}

/** How many of the trip's photographs the chooser lists. A trip's own gallery, not an archive. */
const PAGE_SIZE = 60;

/**
 * How far from the trip a moment read off a file may be before it is worth remarking on.
 *
 * <b>Wide on purpose, because the failure it catches is not a near miss.</b> A camera whose battery
 * died reports 1970 or 2000 — the clock was never set, not set wrongly — and one such file is
 * enough to make a replay's scrubber useless, since a window stretched to hold it moves in steps of
 * weeks. The scrubber refuses to be stretched that far, so nothing is destroyed any more; what is
 * left is a photograph filed at a moment nobody meant, which is worth saying <em>before</em> it is
 * stored rather than leaving somebody to find it on the trip's list afterwards.
 *
 * A month, so that no real trip is ever flagged. A camp lasting a week, a clock out by a time zone,
 * a date that rolled over at midnight: all of them are the ordinary case and all of them pass
 * without a word. Nothing here refuses — the moment can be right and this surface cannot know.
 */
const STRAY_MOMENT_MS = 30 * 24 * 60 * 60_000;

/**
 * Hangs photographs on the moments of a tracked trip.
 *
 * <b>Written for the act that actually happens, which is not the one the feature sounds like.</b>
 * Nobody uploads from underground: these arrive days later, when somebody empties a memory card and
 * turns a finished log into a trip report. So the ordinary use is thirty pictures at thirty
 * different instants, and the thing that makes that bearable is that the instants are already in
 * the files — the gallery hands over what each picture says about when it was taken, and this
 * prefills every moment from it with nothing typed.
 *
 * <b>The camera-clock offset is the one control that earns its place.</b> A camera underground is
 * whatever it was last set to, and it is routinely minutes or hours out. Correcting each picture
 * one at a time is the work this dialog exists to remove, so the correction is stated once and
 * applied to every prefilled instant. It is arithmetic in this dialog and no concept at all on the
 * server: what is stored is the moment, already corrected.
 *
 * <b>A picture whose file says nothing about when it was taken is flagged rather than guessed at.</b>
 * It falls back to the moment the dialog was opened with, which is right often enough to offer and
 * wrong often enough that saving it silently would be how a photograph ends up dated to whenever
 * somebody happened to press a button.
 *
 * <b>Where each photograph will be drawn is shown before it is stored, and it is not worked out
 * here.</b> A photograph's station is never recorded — the replay folds it out of the log at the
 * photograph's own moment, for the person it is about — so a wrong clock or a wrong name shows up
 * only afterwards, as a picture under a station nobody photographed, or on no station at all. The
 * preview asks the replay's own rule the same question for each chosen photograph and asks it
 * again whenever the clock correction or the person changes; it is told the whole log or it says
 * nothing, because a first page of a log answers "nobody had reported them yet" for most of a trip.
 * A reader from whom a place is kept is told that it is kept, never a station.
 */
export default function TrackingPicturesDialog({
  open,
  tripLogId,
  defaultAt,
  defaultCaverId,
  cavers,
  surveyModelId,
  onClose,
  onAttached,
}: TrackingPicturesDialogProps) {
  const { t } = useTranslation();
  // The application's own message surface, not the library's static one: that one is drawn outside
  // the theme and warns about it on the console of every browser that attaches a photograph.
  const { message } = App.useApp();
  const [selected, setSelected] = useState<readonly string[]>([]);
  /** Minutes the camera's clock was ahead of the truth; negative when it was behind. */
  const [offsetMinutes, setOffsetMinutes] = useState<number>(0);
  const [fallback, setFallback] = useState<Dayjs>(() => dayjs(defaultAt));
  /** Who the chosen photographs are about, unless one of them says otherwise below. */
  const [caverId, setCaverId] = useState<string | null>(defaultCaverId);
  /**
   * The photographs that are about somebody other than everybody else's subject, by document.
   *
   * Held as the exceptions rather than as a subject per photograph, so that the chooser above goes
   * on meaning "all of them": choosing there clears this, which is the whole of "apply to all".
   */
  const [subjects, setSubjects] = useState<ReadonlyMap<string, string | null>>(() => new Map());

  const photos = usePhotos({ tripLogId, pageSize: PAGE_SIZE }, open);
  // The whole log, read once for the preview. Not asked for where there is no survey to place
  // anything on: every answer is then the same and none of them needs a report.
  const log = useTripTrackingEventLog(tripLogId, open && surveyModelId !== null);
  const attach = useAttachTrackingPictures();

  const items = useMemo(() => photos.data?.items ?? [], [photos.data]);

  /** The moment each chosen picture would be filed at, and whether it was guessed. */
  const moments = useMemo(() => {
    const byId = new Map(items.map((photo) => [photo.documentId, photo]));
    return selected.map((documentId) => {
      const photo = byId.get(documentId);
      const taken = photo?.takenAt == null ? Number.NaN : Date.parse(photo.takenAt);
      const known = Number.isFinite(taken);
      return {
        documentId,
        title: photo?.title ?? documentId,
        // The offset corrects a clock that ran fast, so it is taken off what the clock said. It is
        // never applied to the fallback: that moment came from this application's own clock and was
        // never read off a camera.
        at: known ? taken - offsetMinutes * 60_000 : fallback.valueOf(),
        guessed: !known,
      };
    });
  }, [items, selected, offsetMinutes, fallback]);

  const guessedCount = moments.filter((moment) => moment.guessed).length;

  const subjectOf = (documentId: string): string | null =>
    subjects.has(documentId) ? (subjects.get(documentId) ?? null) : caverId;

  /**
   * Where one chosen photograph would be drawn, or that it cannot be said yet.
   *
   * A photograph about nobody, and one with no survey to stand on, need no log to answer; every
   * other answer waits for the whole of it and is not given from part of it.
   */
  const placementOf = (documentId: string, at: number): PicturePlacement | 'reading' | 'unread' => {
    const subject = subjectOf(documentId);
    if (subject !== null && surveyModelId !== null && log.data === undefined) {
      return log.isError ? 'unread' : 'reading';
    }
    return picturePlacementAt(log.data ?? [], at, subject, surveyModelId ?? undefined);
  };

  const placementText = (placement: PicturePlacement | 'reading' | 'unread'): string => {
    if (placement === 'reading') {
      return t('trips.tracking.pictures.placeReading');
    }
    if (placement === 'unread') {
      return t('trips.tracking.pictures.placeUnread');
    }
    switch (placement.kind) {
      case 'station':
        return t('trips.tracking.pictures.willDrawAt', { station: placement.station });
      case 'otherModel':
        return t('trips.tracking.pictures.onOtherSurvey');
      case 'withheld':
        return t('trips.tracking.pictures.placeWithheld');
      case 'unplaced':
        return t('trips.tracking.pictures.noPlaceThen');
      case 'noModel':
        return t('trips.tracking.pictures.noSurvey');
      case 'party':
        return t('trips.tracking.pictures.timelineOnly');
    }
  };

  /**
   * The ones whose file puts them nowhere near this trip — measured against the moment this dialog
   * was opened at, which is a report of the trip or the instant on the scrubber. Never counted
   * against a guessed moment: that one came from this application's own clock and is by
   * construction the moment being compared to.
   */
  const stray = moments.filter(
    (moment) => !moment.guessed && Math.abs(moment.at - defaultAt) > STRAY_MOMENT_MS,
  );

  const submit = () => {
    if (moments.length === 0) {
      return;
    }
    const payload: TrackingPictureInput[] = moments.map((moment) => ({
      documentId: moment.documentId,
      at: new Date(moment.at).toISOString(),
      // Each photograph's own person: a memory card holds pictures of several people, and the
      // station each is drawn at is the station of whoever it is about.
      caverId: subjectOf(moment.documentId),
      caption: null,
    }));
    attach.mutate(
      { tripLogId, items: payload },
      {
        onSuccess: (result) => {
          const refusedCount = Object.keys(result.refused ?? {}).length;
          // Both halves are said. A write that attached nine of ten and reported only the nine
          // would be the surface deciding which half of its own answer the reader is owed.
          void message.success(
            t('trips.tracking.pictures.attached', {
              count: result.attached.length,
              refused: refusedCount,
            }),
          );
          // And why, once per reason rather than once per photograph: thirty pictures filed an
          // hour into the future are one mistake with one remedy. A reason this application has no
          // words for adds no line — the count above has already said that something was left out.
          const reasons = new Set(
            Object.values(result.refused ?? {})
              .map((code) => trackingProblemCodeMessage(code, t))
              .filter((sentence) => sentence !== undefined),
          );
          for (const sentence of reasons) {
            void message.warning(sentence);
          }
          setSelected([]);
          setSubjects(new Map());
          onAttached?.();
          onClose();
        },
        // A refusal of the whole request in its own words where it has any — a trip that was never
        // followed is put right by a different act than an installation that cannot store the link.
        onError: (error) =>
          void message.error(
            trackingProblemMessage(error, t, t('trips.tracking.pictures.attachFailed')),
          ),
      },
    );
  };

  return (
    <Modal
      open={open}
      title={t('trips.tracking.pictures.title')}
      okText={t('trips.tracking.pictures.attach', { count: moments.length })}
      okButtonProps={{ disabled: moments.length === 0, loading: attach.isPending }}
      onOk={submit}
      onCancel={onClose}
      destroyOnHidden
      data-testid="trip-tracking-pictures-dialog"
    >
      <Typography.Paragraph type="secondary">
        {t('trips.tracking.pictures.hint')}
      </Typography.Paragraph>

      <Flex gap="small" wrap align="center" style={{ marginBottom: 12 }}>
        <Typography.Text>{t('trips.tracking.pictures.subject')}</Typography.Text>
        <Select
          allowClear
          style={{ minWidth: 200 }}
          value={caverId}
          onChange={(value) => {
            // Chosen here it is everybody's, so whatever was said about one photograph alone goes.
            setCaverId(value ?? null);
            setSubjects(new Map());
          }}
          placeholder={t('trips.tracking.pictures.subjectNobody')}
          options={cavers.map((caver) => ({ value: caver.caverId, label: caver.name }))}
          data-testid="trip-tracking-pictures-subject"
        />
      </Flex>
      <Typography.Paragraph type="secondary" style={{ marginTop: -8 }}>
        {t('trips.tracking.pictures.subjectHint')} {t('trips.tracking.pictures.subjectApplyAll')}
      </Typography.Paragraph>

      <Flex gap="small" wrap align="center" style={{ marginBottom: 12 }}>
        <Typography.Text>{t('trips.tracking.pictures.clockOffset')}</Typography.Text>
        <InputNumber
          value={offsetMinutes}
          onChange={(value) => setOffsetMinutes(typeof value === 'number' ? value : 0)}
          step={1}
          aria-label={t('trips.tracking.pictures.clockOffset')}
          data-testid="trip-tracking-pictures-offset"
        />
        <Typography.Text type="secondary">
          {t('trips.tracking.pictures.clockOffsetUnit')}
        </Typography.Text>
      </Flex>

      {photos.isPending && open ? (
        <Spin data-testid="trip-tracking-pictures-loading" />
      ) : items.length === 0 ? (
        <Empty description={t('trips.tracking.pictures.none')} />
      ) : (
        <Flex
          vertical
          gap="small"
          style={{ maxHeight: 320, overflowY: 'auto' }}
          data-testid="trip-tracking-pictures-list"
        >
          {items.map((photo: PhotoInfo) => (
            <Checkbox
              key={photo.documentId}
              checked={selected.includes(photo.documentId)}
              onChange={(event) =>
                setSelected((held) =>
                  event.target.checked
                    ? [...held, photo.documentId]
                    : held.filter((id) => id !== photo.documentId),
                )
              }
              data-testid={`trip-tracking-pictures-pick-${photo.documentId}`}
            >
              <Flex gap="small" align="center" wrap>
                <span>{photo.title}</span>
                {photo.takenAt === null ? (
                  <Tag color="warning">{t('trips.tracking.pictures.noTakenAt')}</Tag>
                ) : (
                  <Typography.Text type="secondary">
                    {t('trips.tracking.pictures.takenAt', { at: new Date(photo.takenAt) })}
                  </Typography.Text>
                )}
              </Flex>
            </Checkbox>
          ))}
        </Flex>
      )}

      {moments.length > 0 && (
        <Flex
          vertical
          gap="small"
          style={{ marginTop: 12 }}
          data-testid="trip-tracking-pictures-preview"
        >
          <Typography.Text strong>{t('trips.tracking.pictures.previewTitle')}</Typography.Text>
          <Typography.Text type="secondary">
            {t('trips.tracking.pictures.previewHint')}
          </Typography.Text>
          {moments.map((moment) => {
            const placement = placementOf(moment.documentId, moment.at);
            return (
              <Flex
                key={moment.documentId}
                gap="small"
                align="center"
                wrap
                data-testid={`trip-tracking-pictures-row-${moment.documentId}`}
              >
                <Typography.Text>{moment.title}</Typography.Text>
                <Typography.Text type="secondary">
                  {t('trips.tracking.pictures.filedAt', { at: new Date(moment.at) })}
                </Typography.Text>
                <Select
                  allowClear
                  size="small"
                  style={{ minWidth: 160 }}
                  value={subjectOf(moment.documentId)}
                  onChange={(value) =>
                    setSubjects((held) => new Map(held).set(moment.documentId, value ?? null))
                  }
                  placeholder={t('trips.tracking.pictures.subjectNobody')}
                  options={cavers.map((caver) => ({ value: caver.caverId, label: caver.name }))}
                  aria-label={t('trips.tracking.pictures.rowSubject', { title: moment.title })}
                  data-testid={`trip-tracking-pictures-row-subject-${moment.documentId}`}
                />
                <Typography.Text
                  type={
                    typeof placement === 'object' && placement.kind === 'station'
                      ? undefined
                      : 'secondary'
                  }
                  data-placement={typeof placement === 'object' ? placement.kind : placement}
                  data-testid={`trip-tracking-pictures-row-place-${moment.documentId}`}
                >
                  {placementText(placement)}
                </Typography.Text>
              </Flex>
            );
          })}
        </Flex>
      )}

      {guessedCount > 0 && (
        <Alert
          type="warning"
          showIcon
          style={{ marginTop: 12 }}
          title={t('trips.tracking.pictures.guessedTitle', { count: guessedCount })}
          description={
            <Flex gap="small" align="center" wrap>
              <span>{t('trips.tracking.pictures.guessedBody')}</span>
              <DatePicker
                showTime
                value={fallback}
                onChange={(value) => value !== null && setFallback(value)}
                aria-label={t('trips.tracking.pictures.fallbackMoment')}
                data-testid="trip-tracking-pictures-fallback"
              />
            </Flex>
          }
          data-testid="trip-tracking-pictures-guessed"
        />
      )}

      {/* Said rather than refused, and said here rather than on the server. The moment can be
          right — a picture relayed out of the cave, a trip whose own dates are odd — and refusing
          would throw away the record of a photograph over a number this person can correct with
          the offset above. What this prevents is storing one silently. */}
      {stray.length > 0 && (
        <Alert
          type="warning"
          showIcon
          style={{ marginTop: 12 }}
          title={t('trips.tracking.pictures.strayTitle', { count: stray.length })}
          description={t('trips.tracking.pictures.strayBody', {
            at: new Date(stray[0].at),
          })}
          data-testid="trip-tracking-pictures-stray"
        />
      )}
    </Modal>
  );
}
