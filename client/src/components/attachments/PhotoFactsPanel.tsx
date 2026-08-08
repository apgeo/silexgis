// SPDX-License-Identifier: AGPL-3.0-or-later
import { CameraOutlined } from '@ant-design/icons';
import { Button, Descriptions, Empty, Popover, Typography } from 'antd';
import dayjs from 'dayjs';
import { useTranslation } from 'react-i18next';
import type { FileInfo } from '../../api/hooks.ts';
import PositionSourceTag from '../import/PositionSourceTag.tsx';

/**
 * What a photograph says about itself: the camera, the lens, the exposure, when it was taken —
 * and, for whoever may be given it, where.
 *
 * The two halves come from different fields and are shown under different conditions, which is
 * the whole reason they are separate on the wire. How a picture was taken is not a position and
 * is shown to anyone who may read the file; where it was taken is a position, and the server
 * only sends it to a caller it would also hand the original to. Nothing here decides that — a
 * missing position simply is not drawn.
 */
export default function PhotoFactsPanel({ file }: { file: FileInfo }) {
  const { t } = useTranslation();
  const exif = file.photo;
  const position = file.position;

  const items: { key: string; label: string; children: React.ReactNode }[] = [];

  if (file.contentCreatedAt) {
    items.push({
      key: 'taken',
      label: t('photoFacts.takenAt'),
      // Formatted with its offset rather than in the viewer's zone: the camera recorded a
      // moment in the photographer's own time, and shifting it would misreport a trip.
      children: dayjs(file.contentCreatedAt).format('YYYY-MM-DD HH:mm:ss Z'),
    });
  }

  if (exif?.cameraMake || exif?.cameraModel) {
    items.push({
      key: 'camera',
      label: t('photoFacts.camera'),
      children: [exif.cameraMake, exif.cameraModel].filter(Boolean).join(' '),
    });
  }

  if (exif?.lens) {
    items.push({ key: 'lens', label: t('photoFacts.lens'), children: exif.lens });
  }

  if (exif?.focalLengthMm || exif?.fNumber || exif?.exposureSeconds || exif?.iso) {
    items.push({
      key: 'exposure',
      label: t('photoFacts.exposure'),
      children: [
        exif.focalLengthMm ? `${Math.round(exif.focalLengthMm)} mm` : null,
        exif.fNumber ? `f/${exif.fNumber}` : null,
        exif.exposureSeconds ? formatShutter(exif.exposureSeconds) : null,
        exif.iso ? `ISO ${exif.iso}` : null,
      ]
        .filter(Boolean)
        .join(' · '),
    });
  }

  if (exif?.widthPixels && exif?.heightPixels) {
    items.push({
      key: 'size',
      label: t('photoFacts.dimensions'),
      children: `${exif.widthPixels} × ${exif.heightPixels}`,
    });
  }

  if (exif?.orientation) {
    items.push({
      key: 'orientation',
      label: t('photoFacts.orientation'),
      children: t(`photoFacts.orientations.${exif.orientation}`),
    });
  }

  if (position?.geom?.type === 'Point') {
    const [lon, lat] = position.geom.coordinates as number[];
    items.push({
      key: 'position',
      label: t('photoFacts.position'),
      children: (
        <span>
          <Typography.Text copyable={{ text: `${lat.toFixed(6)}, ${lon.toFixed(6)}` }}>
            {lat.toFixed(6)}, {lon.toFixed(6)}
          </Typography.Text>
          <br />
          <PositionSourceTag source={position.positionSource} confidence={position.confidence} />
        </span>
      ),
    });

    if (position.altitudeMeters !== null) {
      items.push({
        key: 'altitude',
        label: t('photoFacts.altitude'),
        children: `${Math.round(position.altitudeMeters)} m`,
      });
    }

    if (position.directionDegrees !== null) {
      items.push({
        key: 'direction',
        label: t('photoFacts.direction'),
        children: t(
          position.directionIsMagnetic ? 'photoImport.bearingMagnetic' : 'photoImport.bearing',
          { value: Math.round(position.directionDegrees) },
        ),
      });
    }
  }

  const content =
    items.length === 0 ? (
      <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('photoFacts.nothingRecorded')} />
    ) : (
      <Descriptions size="small" column={1} items={items} style={{ width: 320 }} />
    );

  return (
    <Popover content={content} title={t('photoFacts.title')} trigger="click">
      <Button size="small" type="text" icon={<CameraOutlined />} aria-label={t('photoFacts.title')} />
    </Popover>
  );
}

/** A shutter time the way a camera writes it: a fraction below a second, seconds above it. */
function formatShutter(seconds: number): string {
  return seconds >= 1 ? `${Math.round(seconds * 10) / 10}s` : `1/${Math.round(1 / seconds)}`;
}
