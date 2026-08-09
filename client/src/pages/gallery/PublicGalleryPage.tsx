// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { Alert, Empty, Pagination, Spin, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { useParams } from 'react-router-dom';
import { usePublicPhotos, useSharedAlbum } from '../../api/hooks.ts';
import Lightbox from '../../components/gallery/Lightbox.tsx';
import PhotoGrid from '../../components/gallery/PhotoGrid.tsx';

/**
 * The installation's curated public gallery — the one page an anonymous visitor sees.
 *
 * <p>
 * It shows what an administrator published and nothing else. Not what a visibility band admits:
 * making a picture readable by every account here is an editorial act, and putting it on the
 * internet is a different decision taken by a different person.
 * </p>
 * <p>
 * Everything here is a rendering. The server mints delivery tokens for renderings only, so the
 * upload and the capture position cannot leave by this door whatever a client does with the
 * URLs — the response has no field for either.
 * </p>
 */
export default function PublicGalleryPage() {
  const { t } = useTranslation();
  const [page, setPage] = useState(1);
  const [openIndex, setOpenIndex] = useState<number | null>(null);
  const { data, isPending } = usePublicPhotos(page);

  const photos = data?.items ?? [];

  return (
    <div style={{ padding: 24, height: '100%', overflow: 'auto' }}>
      <Typography.Title level={3} style={{ marginTop: 0 }}>
        {t('gallery.publicTitle')}
      </Typography.Title>

      {isPending ? (
        <Spin style={{ display: 'block', marginTop: '15vh' }} />
      ) : photos.length === 0 ? (
        <Empty description={t('gallery.publicEmpty')} style={{ marginTop: '15vh' }} />
      ) : (
        <>
          <PhotoGrid photos={photos} onOpen={(id) => setOpenIndex(photos.findIndex((p) => p.documentId === id))} />
          <Pagination
            style={{ marginTop: 16 }}
            current={data?.page}
            pageSize={data?.pageSize}
            total={data?.totalItems}
            showSizeChanger={false}
            onChange={setPage}
          />
        </>
      )}

      <Lightbox
        photos={photos}
        index={openIndex}
        onClose={() => setOpenIndex(null)}
        onIndexChange={setOpenIndex}
      />
    </div>
  );
}

/**
 * One album, opened by the link somebody was given.
 *
 * <p>
 * A revoked link, an unknown one and a sign-in-only one all read the same here, because the
 * server answers them the same way: a token is the whole of the caller's claim, and telling
 * them apart would say which tokens exist.
 * </p>
 */
export function SharedAlbumPage() {
  const { t } = useTranslation();
  const { token } = useParams<{ token: string }>();
  const [openIndex, setOpenIndex] = useState<number | null>(null);
  const { data, isPending, isError } = useSharedAlbum(token);

  if (isPending) {
    return <Spin style={{ display: 'block', marginTop: '20vh' }} />;
  }

  if (isError || !data) {
    return <Alert type="error" showIcon title={t('gallery.linkNotFound')} style={{ margin: 24 }} />;
  }

  return (
    <div style={{ padding: 24, height: '100%', overflow: 'auto' }}>
      <Typography.Title level={3} style={{ marginTop: 0 }}>
        {data.title}
      </Typography.Title>
      {data.description && (
        <Typography.Paragraph type="secondary">{data.description}</Typography.Paragraph>
      )}

      <PhotoGrid
        photos={data.photos}
        onOpen={(id) => setOpenIndex(data.photos.findIndex((p) => p.documentId === id))}
        emptyText={t('gallery.publicEmpty')}
      />

      <Lightbox
        photos={data.photos}
        index={openIndex}
        onClose={() => setOpenIndex(null)}
        onIndexChange={setOpenIndex}
      />
    </div>
  );
}
