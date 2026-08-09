// SPDX-License-Identifier: AGPL-3.0-or-later
import { useCallback, useEffect, useRef, useState } from 'react';
import {
  CloseOutlined, DownloadOutlined, InfoCircleOutlined, LeftOutlined, RightOutlined,
  ZoomInOutlined, ZoomOutOutlined,
} from '@ant-design/icons';
import { Button, Descriptions, Drawer, Flex, Tooltip, Typography } from 'antd';
import { useTranslation } from 'react-i18next';

/** What the viewer needs of a picture. The public gallery supplies a subset. */
export interface LightboxPhoto {
  documentId: string;
  title: string;
  previewUrl: string;
  caption?: string | null;
  photographerName?: string | null;
  licenceCode?: string | null;
  placeName?: string | null;
  originalName?: string;
  contentUrl?: string;
  mayDownloadOriginal?: boolean;
  photo?: {
    cameraMake?: string | null;
    cameraModel?: string | null;
    lens?: string | null;
    exposureSeconds?: number | null;
    fNumber?: number | null;
    iso?: number | null;
    focalLengthMm?: number | null;
  } | null;
}

export interface LightboxProps {
  photos: readonly LightboxPhoto[];
  /** Index into `photos`, or null when nothing is open. */
  index: number | null;
  onClose: () => void;
  onIndexChange: (index: number) => void;
}

/** How far in one step of the zoom control goes, and where it stops. */
const ZoomStep = 0.5;
const MinZoom = 1;
const MaxZoom = 6;

/**
 * The full-screen viewer.
 *
 * <p>
 * Keyboard first, because looking through four hundred photographs is a keyboard task: arrows
 * move, Escape closes, +/− zoom, 0 resets. The mouse does the same things, and dragging pans
 * once the picture is larger than the frame.
 * </p>
 * <p>
 * What it shows is the large <em>rendering</em>, not the upload. That is what makes zooming
 * affordable at all — a 60-megapixel panorama is not something to send down a phone connection
 * because somebody clicked a thumbnail — and it is what keeps the viewer usable for a picture
 * whose stored bytes this reader may not have. The download menu is the one place the upload is
 * offered, and it is disabled with a reason when the server says it will not hand it over.
 * </p>
 */
export default function Lightbox({ photos, index, onClose, onIndexChange }: LightboxProps) {
  const { t } = useTranslation();
  const [zoom, setZoom] = useState(1);
  const [pan, setPan] = useState({ x: 0, y: 0 });
  const [facts, setFacts] = useState(false);
  const dragging = useRef<{ x: number; y: number } | null>(null);

  const open = index !== null && index >= 0 && index < photos.length;
  const photo = open ? photos[index] : undefined;

  // A new picture starts unzoomed and centred. Carrying the previous one's zoom over means
  // arrowing through an album lands halfway into every second photograph.
  useEffect(() => {
    setZoom(1);
    setPan({ x: 0, y: 0 });
  }, [index]);

  const move = useCallback(
    (by: number) => {
      if (index === null || photos.length === 0) {
        return;
      }

      // Wraps, because a gallery is a loop to somebody flicking through it and stopping dead at
      // the last picture reads as the viewer having broken.
      onIndexChange((index + by + photos.length) % photos.length);
    },
    [index, photos.length, onIndexChange],
  );

  useEffect(() => {
    if (!open) {
      return undefined;
    }

    const onKey = (event: KeyboardEvent) => {
      switch (event.key) {
        case 'Escape':
          onClose();
          break;
        case 'ArrowLeft':
          move(-1);
          break;
        case 'ArrowRight':
          move(1);
          break;
        case '+':
        case '=':
          setZoom((z) => Math.min(MaxZoom, z + ZoomStep));
          break;
        case '-':
          setZoom((z) => Math.max(MinZoom, z - ZoomStep));
          break;
        case '0':
          setZoom(1);
          setPan({ x: 0, y: 0 });
          break;
        default:
          return;
      }

      // Only for keys actually handled: swallowing everything would eat typing in the facts
      // panel beside it.
      event.preventDefault();
    };

    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [open, move, onClose]);

  if (!open || !photo) {
    return null;
  }

  return (
    <div
      data-testid="lightbox"
      role="dialog"
      aria-modal="true"
      aria-label={photo.caption ?? photo.title}
      style={{
        position: 'fixed',
        inset: 0,
        zIndex: 1100,
        background: 'rgba(0, 0, 0, 0.92)',
        display: 'flex',
        flexDirection: 'column',
      }}
    >
      <Flex justify="space-between" align="center" style={{ padding: 12 }}>
        <Typography.Text style={{ color: '#fff' }} ellipsis>
          {photo.caption ?? photo.title}
        </Typography.Text>
        <Flex gap={4}>
          <Button
            type="text"
            icon={<ZoomOutOutlined />}
            aria-label={t('gallery.zoomOut')}
            onClick={() => setZoom((z) => Math.max(MinZoom, z - ZoomStep))}
            style={{ color: '#fff' }}
          />
          <Button
            type="text"
            icon={<ZoomInOutlined />}
            aria-label={t('gallery.zoomIn')}
            onClick={() => setZoom((z) => Math.min(MaxZoom, z + ZoomStep))}
            style={{ color: '#fff' }}
          />
          <Button
            type="text"
            icon={<InfoCircleOutlined />}
            aria-label={t('gallery.facts')}
            onClick={() => setFacts(true)}
            style={{ color: '#fff' }}
          />
          {/* Offered only where the server says it will actually hand the bytes over. A
              photograph whose subject this reader may not place is delivered as renderings, and
              following the link would answer as a missing file — so the control says why
              instead of failing when used. */}
          <Tooltip title={photo.mayDownloadOriginal ? undefined : t('attachments.originalWithheld')}>
            <Button
              type="text"
              icon={<DownloadOutlined />}
              aria-label={t('gallery.download')}
              disabled={!photo.mayDownloadOriginal}
              href={photo.mayDownloadOriginal ? photo.contentUrl : undefined}
              download={photo.originalName}
              style={{ color: '#fff' }}
            />
          </Tooltip>
          <Button
            type="text"
            icon={<CloseOutlined />}
            aria-label={t('common.close')}
            onClick={onClose}
            style={{ color: '#fff' }}
          />
        </Flex>
      </Flex>

      <Flex align="center" justify="center" style={{ flex: 1, overflow: 'hidden', position: 'relative' }}>
        <Button
          type="text"
          icon={<LeftOutlined />}
          aria-label={t('gallery.previous')}
          onClick={() => move(-1)}
          style={{ position: 'absolute', left: 8, color: '#fff', zIndex: 1 }}
        />
        <img
          src={photo.previewUrl}
          alt={photo.caption ?? photo.title}
          draggable={false}
          onMouseDown={(e) => {
            dragging.current = { x: e.clientX - pan.x, y: e.clientY - pan.y };
          }}
          onMouseMove={(e) => {
            // Panning only means anything once the picture is larger than the frame.
            if (dragging.current && zoom > 1) {
              setPan({ x: e.clientX - dragging.current.x, y: e.clientY - dragging.current.y });
            }
          }}
          onMouseUp={() => {
            dragging.current = null;
          }}
          onMouseLeave={() => {
            dragging.current = null;
          }}
          style={{
            maxWidth: '100%',
            maxHeight: '100%',
            transform: `translate(${pan.x}px, ${pan.y}px) scale(${zoom})`,
            transformOrigin: 'center',
            cursor: zoom > 1 ? 'grab' : 'default',
            userSelect: 'none',
          }}
        />
        <Button
          type="text"
          icon={<RightOutlined />}
          aria-label={t('gallery.next')}
          onClick={() => move(1)}
          style={{ position: 'absolute', right: 8, color: '#fff', zIndex: 1 }}
        />
      </Flex>

      <Drawer
        open={facts}
        onClose={() => setFacts(false)}
        placement="right"
        title={t('gallery.facts')}
        destroyOnHidden
      >
        <Descriptions column={1} size="small">
          {photo.photographerName && (
            <Descriptions.Item label={t('gallery.photographer')}>
              {photo.photographerName}
            </Descriptions.Item>
          )}
          {photo.placeName && (
            <Descriptions.Item label={t('gallery.place')}>{photo.placeName}</Descriptions.Item>
          )}
          {photo.licenceCode && (
            <Descriptions.Item label={t('gallery.licence')}>
              {t(`gallery.licences.${photo.licenceCode}`)}
            </Descriptions.Item>
          )}
          {/* How a photograph was taken is not a position and is not protected — which is why
              it is here and the coordinates are not. */}
          {photo.photo?.cameraModel && (
            <Descriptions.Item label={t('photoFacts.camera')}>
              {[photo.photo.cameraMake, photo.photo.cameraModel].filter(Boolean).join(' ')}
            </Descriptions.Item>
          )}
          {photo.photo?.lens && (
            <Descriptions.Item label={t('photoFacts.lens')}>{photo.photo.lens}</Descriptions.Item>
          )}
          {/* One line for how it was exposed rather than four labels for four numbers: this
              is what a photographer reads at a glance, and it is how every camera writes it. */}
          {(photo.photo?.exposureSeconds || photo.photo?.fNumber || photo.photo?.iso
            || photo.photo?.focalLengthMm) && (
            <Descriptions.Item label={t('photoFacts.exposure')}>
              {[
                photo.photo?.focalLengthMm ? `${photo.photo.focalLengthMm} mm` : null,
                photo.photo?.fNumber ? `f/${photo.photo.fNumber}` : null,
                photo.photo?.exposureSeconds ? `${photo.photo.exposureSeconds} s` : null,
                photo.photo?.iso ? `ISO ${photo.photo.iso}` : null,
              ]
                .filter(Boolean)
                .join(' · ')}
            </Descriptions.Item>
          )}
        </Descriptions>
      </Drawer>
    </div>
  );
}
