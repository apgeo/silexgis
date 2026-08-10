// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ReactNode } from 'react';
import { CheckCircleFilled, LeftOutlined, PictureOutlined, RightOutlined } from '@ant-design/icons';
import { Button, Empty, Flex, Typography, theme } from 'antd';
import { useTranslation } from 'react-i18next';

/** The least a tile needs. Both the signed-in gallery and the public one supply it. */
export interface GridPhoto {
  documentId: string;
  title: string;
  thumbnailUrl: string;
  width?: number | null;
  height?: number | null;
  caption?: string | null;
}

/**
 * Rearranging, when the grid is an album rather than a listing.
 *
 * <p>
 * Both gestures are offered, and the second is not a nicety: dragging is a mouse gesture, and an
 * album of two hundred pictures has to be arrangeable by somebody who is not using a mouse. They
 * are the same operation — a step is a drop onto the neighbour — so there is one ordering rule
 * rather than two that can disagree.
 * </p>
 */
export interface PhotoGridArrange {
  /** A picture was dropped onto another. Where it lands is the caller's rule, not the grid's. */
  onDrop: (movedId: string, targetId: string) => void;
  onStep: (documentId: string, direction: -1 | 1) => void;
}

export interface PhotoGridProps {
  photos: readonly GridPhoto[];
  onOpen: (documentId: string) => void;
  /** When given, tiles become selectable and show a mark. */
  selected?: readonly string[];
  onToggle?: (documentId: string) => void;
  emptyText?: string;
  arrange?: PhotoGridArrange;
  /** Per-tile controls the page hangs on — the album page puts "cover" and "remove" here. */
  renderExtra?: (photo: GridPhoto) => ReactNode;
}

/**
 * The grid.
 *
 * <p>
 * Two things it does that a naive grid does not, and both come from the archive being large.
 * Every tile loads lazily, so a page of a hundred is a hundred requests the browser makes as
 * they scroll into view rather than all at once. And every tile is sized from the dimensions
 * the server states — with any recorded rotation already applied — so the grid has its final
 * shape before a single image arrives. A grid that reflows as pictures load is the thing lazy
 * loading is supposed to make bearable, not something to add on top of it.
 * </p>
 * <p>
 * The image is always a rendering. The gallery never loads an upload: a page of sixty
 * photographs at full size is hundreds of megabytes, and for a picture whose subject the
 * reader may not place the upload is not theirs to have at all.
 * </p>
 */
export default function PhotoGrid({
  photos,
  onOpen,
  selected,
  onToggle,
  emptyText,
  arrange,
  renderExtra,
}: PhotoGridProps) {
  const { t } = useTranslation();
  const { token } = theme.useToken();

  if (photos.length === 0) {
    return (
      <Empty
        description={emptyText ?? t('gallery.empty')}
        image={Empty.PRESENTED_IMAGE_SIMPLE}
        style={{ marginTop: '10vh' }}
      />
    );
  }

  const selectable = selected !== undefined && onToggle !== undefined;

  return (
    <Flex wrap gap={12} data-testid="photo-grid">
      {photos.map((photo, position) => {
        const isSelected = selected?.includes(photo.documentId) ?? false;

        // The tile keeps the picture's own proportions inside a fixed height, so a row of
        // portraits and landscapes lines up along the bottom rather than stepping.
        const ratio =
          photo.width && photo.height && photo.height > 0 ? photo.width / photo.height : 4 / 3;
        const height = 160;

        return (
          <figure
            key={photo.documentId}
            style={{ margin: 0, position: 'relative' }}
            data-testid="photo-tile"
            data-document-id={photo.documentId}
            draggable={arrange !== undefined}
            onDragStart={(event) => {
              event.dataTransfer.setData('text/plain', photo.documentId);
              event.dataTransfer.effectAllowed = 'move';
            }}
            onDragOver={(event) => {
              // Without this the browser refuses the drop outright — the default for most
              // elements is "nothing may be dropped here".
              if (arrange) {
                event.preventDefault();
                event.dataTransfer.dropEffect = 'move';
              }
            }}
            onDrop={(event) => {
              if (!arrange) {
                return;
              }

              event.preventDefault();
              const moved = event.dataTransfer.getData('text/plain');
              if (moved) {
                arrange.onDrop(moved, photo.documentId);
              }
            }}
          >
            <button
              type="button"
              onClick={() => (selectable && isSelected ? onToggle!(photo.documentId) : onOpen(photo.documentId))}
              aria-label={photo.caption ?? photo.title}
              style={{
                padding: 0,
                border: isSelected
                  ? `2px solid ${token.colorPrimary}`
                  : `1px solid ${token.colorBorderSecondary}`,
                borderRadius: token.borderRadius,
                overflow: 'hidden',
                cursor: 'pointer',
                background: token.colorFillQuaternary,
                width: Math.round(height * ratio),
                height,
                display: 'block',
              }}
            >
              <img
                src={photo.thumbnailUrl}
                alt={photo.caption ?? photo.title}
                // Native lazy loading rather than an observer of our own: the browser already
                // knows what is on screen, and it keeps working while the tab is in the
                // background.
                loading="lazy"
                decoding="async"
                width={Math.round(height * ratio)}
                height={height}
                style={{ objectFit: 'cover', display: 'block', width: '100%', height: '100%' }}
              />
            </button>

            {selectable && (
              <button
                type="button"
                onClick={() => onToggle!(photo.documentId)}
                aria-label={t('gallery.select')}
                data-testid="photo-select"
                style={{
                  position: 'absolute',
                  top: 6,
                  left: 6,
                  border: 'none',
                  background: 'transparent',
                  cursor: 'pointer',
                  padding: 0,
                  lineHeight: 0,
                }}
              >
                {isSelected ? (
                  <CheckCircleFilled style={{ color: token.colorPrimary, fontSize: 20 }} />
                ) : (
                  <PictureOutlined style={{ color: token.colorTextLightSolid, fontSize: 18 }} />
                )}
              </button>
            )}

            {renderExtra && (
              <Flex
                gap={2}
                style={{ position: 'absolute', top: 4, right: 4 }}
                data-testid="photo-tile-extra"
              >
                {renderExtra(photo)}
              </Flex>
            )}

            {arrange && (
              <Flex justify="space-between" style={{ maxWidth: Math.round(height * ratio) }}>
                <Button
                  size="small"
                  type="text"
                  icon={<LeftOutlined />}
                  aria-label={t('gallery.moveEarlier')}
                  disabled={position === 0}
                  onClick={() => arrange.onStep(photo.documentId, -1)}
                />
                <Button
                  size="small"
                  type="text"
                  icon={<RightOutlined />}
                  aria-label={t('gallery.moveLater')}
                  disabled={position === photos.length - 1}
                  onClick={() => arrange.onStep(photo.documentId, 1)}
                />
              </Flex>
            )}

            <figcaption style={{ maxWidth: Math.round(height * ratio) }}>
              <Typography.Text type="secondary" style={{ fontSize: 12 }} ellipsis>
                {photo.caption ?? photo.title}
              </Typography.Text>
            </figcaption>
          </figure>
        );
      })}
    </Flex>
  );
}
