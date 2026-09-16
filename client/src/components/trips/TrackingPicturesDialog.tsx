// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo, useState } from 'react';
import {
  Alert,
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
  message,
} from 'antd';
import dayjs, { type Dayjs } from 'dayjs';
import { useTranslation } from 'react-i18next';
import {
  useAttachTrackingPictures,
  usePhotos,
  type PhotoInfo,
  type TrackingPictureInput,
} from '../../api/hooks.ts';

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
  /** The trip's roster, for the subject chooser. */
  cavers: readonly { caverId: string; name: string }[];
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
 */
export default function TrackingPicturesDialog({
  open,
  tripLogId,
  defaultAt,
  defaultCaverId,
  cavers,
  onClose,
  onAttached,
}: TrackingPicturesDialogProps) {
  const { t } = useTranslation();
  const [selected, setSelected] = useState<readonly string[]>([]);
  /** Minutes the camera's clock was ahead of the truth; negative when it was behind. */
  const [offsetMinutes, setOffsetMinutes] = useState<number>(0);
  const [fallback, setFallback] = useState<Dayjs>(() => dayjs(defaultAt));
  const [caverId, setCaverId] = useState<string | null>(defaultCaverId);

  const photos = usePhotos({ tripLogId, pageSize: PAGE_SIZE }, open);
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
      caverId,
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
          setSelected([]);
          onAttached?.();
          onClose();
        },
        onError: () => void message.error(t('trips.tracking.pictures.attachFailed')),
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
          onChange={(value) => setCaverId(value ?? null)}
          placeholder={t('trips.tracking.pictures.subjectNobody')}
          options={cavers.map((caver) => ({ value: caver.caverId, label: caver.name }))}
          data-testid="trip-tracking-pictures-subject"
        />
      </Flex>
      <Typography.Paragraph type="secondary" style={{ marginTop: -8 }}>
        {t('trips.tracking.pictures.subjectHint')}
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

      {guessedCount > 0 && (
        <Alert
          type="warning"
          showIcon
          style={{ marginTop: 12 }}
          message={t('trips.tracking.pictures.guessedTitle', { count: guessedCount })}
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
          message={t('trips.tracking.pictures.strayTitle', { count: stray.length })}
          description={t('trips.tracking.pictures.strayBody', {
            at: new Date(stray[0].at),
          })}
          data-testid="trip-tracking-pictures-stray"
        />
      )}
    </Modal>
  );
}
