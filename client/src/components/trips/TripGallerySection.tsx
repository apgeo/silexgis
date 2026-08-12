// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { PictureOutlined, PlusOutlined } from '@ant-design/icons';
import { Alert, App, Button, Card, Empty, Flex, Spin, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate } from 'react-router-dom';
import {
  hasAccessAction,
  useAlbums,
  useCapabilities,
  useCreateAlbum,
  usePhotos,
} from '../../api/hooks.ts';
import Lightbox from '../gallery/Lightbox.tsx';
import PhotoGrid from '../gallery/PhotoGrid.tsx';
import { viewPhoto } from '../gallery/photoView.ts';

/**
 * How many of the trip's photographs are drawn here.
 *
 * A trip is not an archive: a hundred is already more than a page of a trip write-up wants, and
 * the whole set is one click away in the gallery. Kept well under the server's page ceiling so
 * this section never becomes the surface where somebody arranges a set they cannot all see.
 */
const PageSize = 60;

/**
 * The trip's photographs, and the albums that are about it.
 *
 * <p>
 * Two different things, deliberately side by side. The grid is every picture filed against this
 * trip — it follows from attaching a photograph and needs no curation. An album is something
 * somebody made from them: an order that means something, a cover it is recognised by, and a
 * link that can be handed to people who have no account here. Neither is a copy of the other.
 * </p>
 * <p>
 * Nothing is arranged from this page. Ordering and the cover belong to the album, and the album
 * has a page where both already live — a second arranging surface over the same rows is how two
 * orders come to disagree about which is the album's.
 * </p>
 * <p>
 * The right to make an album is the right to write documents, not the right to write this trip:
 * an album is separately governed content that happens to be about a trip. So somebody who may
 * edit the trip and may not write documents is shown the pictures and not the button, and the
 * server would refuse them anyway.
 * </p>
 */
export default function TripGallerySection({
  tripId,
  tripTitle,
}: {
  tripId: string;
  tripTitle: string;
}) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const navigate = useNavigate();
  const { data: capabilities } = useCapabilities();
  const canRead = hasAccessAction(capabilities?.domains.documents, 'read');
  const canWrite = hasAccessAction(capabilities?.domains.documents, 'write');

  const photosQuery = usePhotos({ tripLogId: tripId, pageSize: PageSize }, canRead);
  const albumsQuery = useAlbums({ subjectEntityId: tripId, pageSize: 100 }, canRead);
  const createAlbum = useCreateAlbum();
  const [openIndex, setOpenIndex] = useState<number | null>(null);

  if (!canRead) {
    return null;
  }

  const photos = (photosQuery.data?.items ?? []).map(viewPhoto);
  const albums = albumsQuery.data?.items ?? [];

  // A new album starts private and named after the trip. Private because an album's audience is
  // its own decision and inheriting the trip's would widen one silently; named after the trip
  // because the alternative is a modal asking for the one thing the page already knows.
  const onCreate = async () => {
    try {
      const album = await createAlbum.mutateAsync({
        title: tripTitle,
        visibility: 'private',
        subjectEntityType: 'tripLog',
        subjectEntityId: tripId,
      });
      navigate(`/albums/${album.id}`);
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  return (
    <Card
      size="small"
      title={t('trips.gallery')}
      style={{ marginTop: 16 }}
      data-testid="trip-gallery"
      extra={
        <Flex gap={8}>
          <Link to={`/gallery?tripLogId=${tripId}`}>
            <Button size="small">{t('gallery.openInGallery')}</Button>
          </Link>
          {canWrite && (
            <Button
              size="small"
              icon={<PlusOutlined />}
              loading={createAlbum.isPending}
              onClick={() => void onCreate()}
              data-testid="trip-new-album"
            >
              {t('gallery.newAlbum')}
            </Button>
          )}
        </Flex>
      }
    >
      {albums.length > 0 && (
        <Flex wrap gap={12} style={{ marginBottom: 12 }} data-testid="trip-albums">
          {albums.map((album) => (
            <Card
              key={album.id}
              size="small"
              style={{ width: 180 }}
              cover={
                album.coverThumbnailUrl ? (
                  <img
                    src={album.coverThumbnailUrl}
                    alt={album.title}
                    loading="lazy"
                    style={{ height: 100, objectFit: 'cover' }}
                  />
                ) : (
                  <Flex
                    align="center"
                    justify="center"
                    style={{ height: 100, background: 'rgba(0,0,0,0.04)' }}
                  >
                    <PictureOutlined style={{ fontSize: 24, opacity: 0.4 }} />
                  </Flex>
                )
              }
            >
              <Card.Meta
                title={<Link to={`/albums/${album.id}`}>{album.title}</Link>}
                description={
                  <Typography.Text type="secondary">
                    {t('gallery.albumCount', { count: album.photoCount })}
                  </Typography.Text>
                }
              />
            </Card>
          ))}
        </Flex>
      )}

      {photosQuery.isPending ? (
        <Spin />
      ) : photos.length === 0 ? (
        <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('trips.noPhotographs')} />
      ) : (
        <>
          {/* What is drawn is what this caller may read, which is not necessarily what is filed
              against the trip — said plainly, because a reader comparing their screen with
              somebody else's otherwise reads the difference as a fault. */}
          <Alert
            type="info"
            showIcon
            title={t('trips.galleryVisibleToYou')}
            style={{ marginBottom: 12 }}
          />
          <PhotoGrid
            photos={photos}
            onOpen={(documentId) => setOpenIndex(photos.findIndex((p) => p.documentId === documentId))}
          />
        </>
      )}

      {openIndex !== null && photos[openIndex] && (
        <Lightbox
          photos={photos}
          index={openIndex}
          onClose={() => setOpenIndex(null)}
          onIndexChange={setOpenIndex}
        />
      )}
    </Card>
  );
}
