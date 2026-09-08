// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { Alert, Descriptions, Drawer, Flex, Skeleton, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { ApiError } from '../../api/client.ts';
import {
  usePhotoLibraryPhotograph,
  type LibraryPhotographDetail,
  type LibraryPhotoSource,
} from '../../api/hooks.ts';
import {
  libraryPictureFailed,
  libraryPictureUrl,
  markLibraryPictureFailed,
} from '../../photolibrary/pictureUrl.ts';

export interface LibraryPhotoDrawerProps {
  source: LibraryPhotoSource | undefined;
  /** Which photograph, or null when nothing is open. */
  photographId: string | null;
  onClose: () => void;
}

/**
 * One photograph from a neighbouring library, opened from the grid.
 *
 * <p>
 * It shows the larger rendering and what the library knows about it in words. What the library did
 * not say is left out rather than shown empty: the two products describe a photograph differently
 * and neither describes one completely, so a row that is not there means nobody answered it, and a
 * row printed with a dash would say the picture has no camera where the true statement is only
 * that this library did not name one.
 * </p>
 * <p>
 * <b>There is no position and no link into the library's own interface.</b> The first because this
 * whole surface has none — the map is where a library's photographs are placed. The second because
 * a link that does not resolve is worse than no link: what shape a permalink into either product
 * takes is not established here, and that interface is often not published to a browser at all.
 * </p>
 */
export default function LibraryPhotoDrawer({
  source,
  photographId,
  onClose,
}: LibraryPhotoDrawerProps) {
  const { t } = useTranslation();
  const { data, isPending, error } = usePhotoLibraryPhotograph(source, photographId);

  return (
    <Drawer
      open={photographId !== null}
      onClose={onClose}
      width={520}
      title={t('libraryPhotos.detail.title')}
      data-testid="library-photo-drawer"
    >
      {photographId === null ? null : error ? (
        <Alert
          type={gone(error) ? 'info' : 'warning'}
          showIcon
          message={
            gone(error) ? t('libraryPhotos.detail.gone') : t('libraryPhotos.detail.silent')
          }
        />
      ) : isPending || !data ? (
        <Skeleton active paragraph={{ rows: 6 }} />
      ) : (
        <PhotographDetail detail={data} />
      )}
    </Drawer>
  );
}

/**
 * Whether the library answered that it no longer holds the photograph, as opposed to not
 * answering at all.
 *
 * Read from the refusal's own code rather than matched out of its sentence, which is prose written
 * for a person and may be reworded without anything here noticing. The two send a reader somewhere
 * completely different: one means the list was read a moment ago and the library has changed since,
 * and the other means somebody has to go and look at a container.
 */
function gone(error: unknown): boolean {
  return error instanceof ApiError && error.code === 'photo_library.photograph_not_found';
}

function PhotographDetail({ detail }: { detail: LibraryPhotographDetail }) {
  const { t, i18n } = useTranslation();
  const [failed, setFailed] = useState(() =>
    libraryPictureFailed(detail.source, detail.reference),
  );

  const picture = failed
    ? undefined
    : libraryPictureUrl(detail.pictureUrlTemplate, detail.reference, 'large');

  const takenAt = detail.takenAt ? new Date(detail.takenAt) : null;

  // Every row is here only if the library answered it. Built as a list rather than as fixed markup
  // so that "the library did not say" is expressed by a row's absence in one place instead of by a
  // conditional around each one.
  const rows: { key: string; label: string; value: string }[] = [];
  const add = (key: string, label: string, value: string | number | null) => {
    if (value !== null && value !== '') {
      rows.push({ key, label, value: String(value) });
    }
  };

  add(
    'takenAt',
    t('libraryPhotos.detail.takenAt'),
    takenAt ? takenAt.toLocaleString(i18n.resolvedLanguage) : null,
  );
  add(
    'camera',
    t('libraryPhotos.detail.camera'),
    [detail.cameraMake, detail.cameraModel].filter(Boolean).join(' ') || null,
  );
  add('lens', t('libraryPhotos.detail.lens'), detail.lens);
  add(
    'aperture',
    t('libraryPhotos.detail.aperture'),
    detail.aperture === null ? null : `f/${detail.aperture}`,
  );
  add('shutter', t('libraryPhotos.detail.exposure'), detail.shutterSpeed);
  add('iso', t('libraryPhotos.detail.iso'), detail.iso);
  add(
    'focalLength',
    t('libraryPhotos.detail.focalLength'),
    detail.focalLengthMm === null ? null : `${detail.focalLengthMm} mm`,
  );
  add('library', t('libraryPhotos.detail.library'), detail.libraryName);

  return (
    <Flex vertical gap={12}>
      {picture ? (
        <img
          src={picture}
          alt={detail.title ?? t('libraryPhotos.noTitle')}
          decoding="async"
          onError={() => {
            // Asked once and never again, for the reason the grid's tiles are: against one of the
            // two products a picture request whose original cannot be resolved is what marks the
            // file missing over there.
            markLibraryPictureFailed(detail.source, detail.reference);
            setFailed(true);
          }}
          style={{ width: '100%', height: 'auto', borderRadius: 4 }}
          data-testid="library-photo-detail-picture"
        />
      ) : (
        <Alert type="info" showIcon message={t('libraryPhotos.thumbnailFailed')} />
      )}

      <Typography.Title level={5} style={{ margin: 0 }}>
        {detail.title ?? t('libraryPhotos.noTitle')}
      </Typography.Title>

      {/* Said out loud, because what is shown above is a still: a reader who cannot tell a
          photograph from the first frame of a video is being told something untrue by the
          picture alone. Absent for a photograph whose kind the library never stated, which is
          not the same as one it called an image. */}
      {detail.kind === 'video' && (
        <Alert type="info" showIcon message={t('libraryPhotos.detail.isVideo')} />
      )}

      {detail.description && (
        <Typography.Paragraph style={{ margin: 0 }}>{detail.description}</Typography.Paragraph>
      )}

      <Descriptions
        column={1}
        size="small"
        bordered
        items={rows.map((row) => ({ key: row.key, label: row.label, children: row.value }))}
      />
    </Flex>
  );
}
