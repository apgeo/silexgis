// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { PictureOutlined, VideoCameraOutlined } from '@ant-design/icons';
import { Empty, Flex, Typography, theme } from 'antd';
import { useTranslation } from 'react-i18next';
import type { LibraryPhotograph, LibraryPhotoSource } from '../../api/hooks.ts';
import {
  libraryPictureFailed,
  libraryPictureUrl,
  markLibraryPictureFailed,
} from '../../photolibrary/pictureUrl.ts';

export interface LibraryPhotoGridProps {
  source: LibraryPhotoSource;
  photographs: readonly LibraryPhotograph[];
  /**
   * Where one picture is fetched from, or null when this library's pictures are stopped. Null is
   * not an error to render as broken: the listing is fine and every tile falls back to its mark.
   */
  pictureUrlTemplate: string | null;
  onOpen: (photographId: string) => void;
  emptyText: string;
}

/** One tile's side, in pixels. Fixed, because neither product states a picture's proportions here. */
const TileSize = 160;

/**
 * A page of a neighbouring library's photographs, as tiles.
 *
 * <p>
 * Every tile loads lazily, so a page of sixty is sixty requests the browser makes as they scroll
 * into view rather than all at once — and every one of those requests is a call through this
 * application into somebody else's container, which is the reason it matters more here than it
 * does for this installation's own gallery.
 * </p>
 * <p>
 * The picture is always a rendering, never an original: a page of sixty photographs at full size is
 * hundreds of megabytes, and the small rendering is what a grid is for.
 * </p>
 */
export default function LibraryPhotoGrid({
  source,
  photographs,
  pictureUrlTemplate,
  onOpen,
  emptyText,
}: LibraryPhotoGridProps) {
  const { token } = theme.useToken();

  if (photographs.length === 0) {
    return (
      <Empty
        description={emptyText}
        image={Empty.PRESENTED_IMAGE_SIMPLE}
        style={{ marginTop: '10vh' }}
      />
    );
  }

  return (
    <Flex wrap gap={12} data-testid="library-photo-grid">
      {photographs.map((photograph) => (
        <LibraryPhotoTile
          key={photograph.photographId}
          source={source}
          photograph={photograph}
          pictureUrlTemplate={pictureUrlTemplate}
          onOpen={onOpen}
          borderColor={token.colorBorderSecondary}
          background={token.colorFillQuaternary}
          radius={token.borderRadius}
          mask={token.colorBgMask}
          onMask={token.colorTextLightSolid}
        />
      ))}
    </Flex>
  );
}

interface LibraryPhotoTileProps {
  source: LibraryPhotoSource;
  photograph: LibraryPhotograph;
  pictureUrlTemplate: string | null;
  onOpen: (photographId: string) => void;
  borderColor: string;
  background: string;
  radius: number;
  /** Behind the mark on a tile that is not a photograph. Over a picture, so never a page colour. */
  mask: string;
  /** On that mark. Fixed against the mask rather than against the page, for the same reason. */
  onMask: string;
}

/**
 * One photograph.
 *
 * <p>
 * <b>A picture that did not arrive falls back to a mark and is never asked for again.</b> The
 * ledger that remembers which those are lives outside this component, and outside React, for a
 * reason that is not tidiness: against one of the two products, a picture request whose original
 * cannot be resolved is itself what marks the file missing over there and can drop the photograph
 * from that library's index. A tile that retried on every render — or a grid that forgot on every
 * remount and asked again — would be a deletion loop rather than a slow page. So the fallback is
 * read from the ledger on the way in as well as written to it on failure.
 * </p>
 * <p>
 * The fallback is a mark and a title rather than a hole: a photograph whose picture failed is still
 * a photograph the library holds, and it can still be opened.
 * </p>
 */
function LibraryPhotoTile({
  source,
  photograph,
  pictureUrlTemplate,
  onOpen,
  borderColor,
  background,
  radius,
  mask,
  onMask,
}: LibraryPhotoTileProps) {
  const { t, i18n } = useTranslation();
  const [failed, setFailed] = useState(() =>
    libraryPictureFailed(source, photograph.reference),
  );

  const picture = failed
    ? undefined
    : libraryPictureUrl(pictureUrlTemplate, photograph.reference, 'small');

  const title = photograph.title ?? t('libraryPhotos.noTitle');
  const takenAt = photograph.takenAt ? new Date(photograph.takenAt) : null;

  // What the library said this is, and only what it said. A kind it never stated stays unstated:
  // most of these tiles are photographs and drawing one with a picture mark is an admitted guess
  // about a shape, but calling something a photograph in words is a claim, and this application
  // makes it only where the library did.
  //
  // A film drawn as an ordinary tile is that claim made silently. What the tile shows is a still,
  // so without this mark a page counted and captioned as photographs would be showing something
  // that is not one, with nothing on it saying so.
  const isVideo = photograph.kind === 'video';
  const label = isVideo ? `${title} (${t('libraryPhotos.video')})` : title;

  return (
    <figure style={{ margin: 0 }} data-testid="library-photo-tile">
      <button
        type="button"
        onClick={() => onOpen(photograph.photographId)}
        aria-label={label}
        style={{
          padding: 0,
          position: 'relative',
          border: `1px solid ${borderColor}`,
          borderRadius: radius,
          overflow: 'hidden',
          cursor: 'pointer',
          background,
          width: TileSize,
          height: TileSize,
          display: 'block',
        }}
      >
        {picture ? (
          <img
            src={picture}
            alt={label}
            data-testid="library-photo-tile-picture"
            // Native lazy loading rather than an observer of our own: the browser already knows
            // what is on screen, and it keeps working while the tab is in the background.
            loading="lazy"
            decoding="async"
            width={TileSize}
            height={TileSize}
            onError={() => {
              markLibraryPictureFailed(source, photograph.reference);
              setFailed(true);
            }}
            style={{ objectFit: 'cover', display: 'block', width: '100%', height: '100%' }}
          />
        ) : (
          <Flex
            align="center"
            justify="center"
            style={{ width: '100%', height: '100%' }}
            data-testid="library-photo-tile-fallback"
          >
            <PictureOutlined
              aria-label={t('libraryPhotos.thumbnailFailed')}
              style={{ fontSize: 28, opacity: 0.45 }}
            />
          </Flex>
        )}

        {isVideo && (
          <span
            data-testid="library-photo-tile-video"
            style={{
              position: 'absolute',
              left: 6,
              bottom: 6,
              display: 'inline-flex',
              alignItems: 'center',
              gap: 4,
              padding: '0 6px',
              borderRadius: radius,
              background: mask,
              color: onMask,
              fontSize: 11,
              lineHeight: '18px',
            }}
          >
            {/* The word carries it; the icon is decoration beside the word, and the button's own
                label already says it for anything not reading the picture. */}
            <VideoCameraOutlined aria-hidden />
            {t('libraryPhotos.video')}
          </span>
        )}
      </button>

      <figcaption style={{ maxWidth: TileSize }}>
        <Typography.Paragraph
          ellipsis={{ rows: 2 }}
          style={{ fontSize: 12, margin: '4px 0 0' }}
        >
          {title}
        </Typography.Paragraph>
        {takenAt && (
          <Typography.Text type="secondary" style={{ fontSize: 12 }}>
            {takenAt.toLocaleDateString(i18n.resolvedLanguage)}
          </Typography.Text>
        )}
      </figcaption>
    </figure>
  );
}
