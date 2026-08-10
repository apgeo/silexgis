// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { DeleteOutlined, EditOutlined, StarFilled, StarOutlined } from '@ant-design/icons';
import { Alert, App, Breadcrumb, Button, Flex, Spin, Tooltip, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { Link, useParams } from 'react-router-dom';
import {
  hasAccessAction,
  useAlbum,
  useCapabilities,
  usePhotos,
  useRemoveAlbumItem,
  useReorderAlbum,
  useSetAlbumCover,
} from '../../api/hooks.ts';
import Lightbox from '../../components/gallery/Lightbox.tsx';
import PhotoCreditDrawer from '../../components/gallery/PhotoCreditDrawer.tsx';
import PhotoGrid from '../../components/gallery/PhotoGrid.tsx';
import {
  placementForDrop, placementForStep, reorder, type Placement,
} from '../../components/gallery/albumOrdering.ts';
import { viewPhoto } from '../../components/gallery/photoView.ts';

/**
 * How many pictures one request returns — the largest page the server will serve.
 *
 * An album may hold more than this, so what is drawn can be the first page of a longer album.
 * That is said on screen rather than left to look like the whole thing: arranging what you can
 * see while the rest is silently absent is how somebody moves a picture to "the end" and finds
 * it in the middle.
 */
const PageSize = 500;

/**
 * One album, in its own order, arrangeable.
 *
 * <p>
 * This is the page an album exists for. The gallery can already be filtered to an album, and that
 * shows the same pictures — but a filtered listing is something you look through, and an album is
 * something somebody made: the order carries meaning, the cover is how it is recognised, and
 * neither is expressible as a filter.
 * </p>
 * <p>
 * Ordering is sent one move at a time rather than as a list of positions, so two people tidying
 * the same album do not overwrite each other wholesale. The grid redraws before the server
 * answers and re-reads afterwards, which is what makes a drag feel like moving a picture rather
 * than like submitting a form.
 * </p>
 */
export default function AlbumDetailPage() {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const { id } = useParams<{ id: string }>();
  const { data: capabilities } = useCapabilities();
  const canRead = hasAccessAction(capabilities?.domains.documents, 'read');
  const canWrite = hasAccessAction(capabilities?.domains.documents, 'write');

  const { data: album, isPending: albumPending } = useAlbum(id);
  const photosQuery = usePhotos({ albumId: id, pageSize: PageSize }, canRead && Boolean(id));
  const reorderAlbum = useReorderAlbum();
  const setCover = useSetAlbumCover();
  const removeItem = useRemoveAlbumItem();

  const [openIndex, setOpenIndex] = useState<number | null>(null);
  const [editing, setEditing] = useState<string | null>(null);
  // The order drawn while a move is in flight. Waiting for the round trip would show the picture
  // snapping back to where it was, which reads as the drag having failed.
  const [pending, setPending] = useState<string[] | null>(null);

  if (!capabilities || albumPending) {
    return <Spin style={{ display: 'block', marginTop: '20vh' }} />;
  }

  if (!canRead || !album) {
    return <Alert type="error" showIcon title={t('admin.forbidden')} style={{ margin: 16 }} />;
  }

  // Narrowed to what the grid and the viewer take: the listing nests a caption under a credit,
  // and both components read the flat shape.
  const serverPhotos = (photosQuery.data?.items ?? []).map(viewPhoto);
  const order = pending ?? serverPhotos.map((photo) => photo.documentId);
  // Sorted by the overlay rather than re-sorted from it: an id in the overlay that is no longer in
  // the album (somebody else removed it) simply finds nothing and drops out.
  const photos = order
    .map((documentId) => serverPhotos.find((photo) => photo.documentId === documentId))
    .filter((photo) => photo !== undefined);

  const move = async (movedId: string, placement: Placement | null) => {
    if (!placement || !id) {
      return;
    }

    setPending(reorder(order, movedId, placement));
    try {
      await reorderAlbum.mutateAsync({
        id,
        documentId: movedId,
        afterDocumentId: placement.afterId,
      });
      // Awaited before the overlay goes, so the grid never flickers through the old order.
      await photosQuery.refetch();
    } catch {
      message.error(t('common.saveFailed'));
    } finally {
      setPending(null);
    }
  };

  const remove = async (documentId: string) => {
    if (!id) {
      return;
    }

    try {
      await removeItem.mutateAsync({ id, documentId });
      message.success(t('gallery.removedFromAlbum'));
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  return (
    <div style={{ padding: 24, height: '100%', overflow: 'auto' }}>
      <Breadcrumb
        style={{ marginBottom: 8 }}
        items={[{ title: <Link to="/albums">{t('gallery.albums')}</Link> }, { title: album.title }]}
      />

      <Flex justify="space-between" align="flex-start" style={{ marginBottom: 16 }}>
        <div>
          <Typography.Title level={3} style={{ marginTop: 0, marginBottom: 4 }}>
            {album.title}
          </Typography.Title>
          {album.description && (
            <Typography.Paragraph type="secondary" style={{ marginBottom: 0 }}>
              {album.description}
            </Typography.Paragraph>
          )}
        </div>
        <Link to={`/gallery?albumId=${album.id}`}>
          <Button>{t('gallery.openInGallery')}</Button>
        </Link>
      </Flex>

      {canWrite && photos.length > 1 && (
        <Alert
          type="info"
          showIcon
          style={{ marginBottom: 12 }}
          title={t('gallery.arrangeHint')}
        />
      )}

      {/* Said rather than implied. Arranging what is on screen while the rest of the album is
          silently absent is how somebody moves a picture to "the end" and finds it in the
          middle. */}
      {(photosQuery.data?.totalItems ?? 0) > photos.length && (
        <Alert
          type="warning"
          showIcon
          style={{ marginBottom: 12 }}
          title={t('gallery.albumPartial', {
            shown: photos.length,
            total: photosQuery.data?.totalItems ?? 0,
          })}
        />
      )}

      {photosQuery.isPending ? (
        <Spin style={{ display: 'block', marginTop: '15vh' }} />
      ) : (
        <PhotoGrid
          photos={photos}
          emptyText={t('gallery.albumEmpty')}
          onOpen={(documentId) => setOpenIndex(photos.findIndex((p) => p.documentId === documentId))}
          arrange={
            canWrite
              ? {
                  onDrop: (movedId, targetId) =>
                    void move(movedId, placementForDrop(order, movedId, targetId)),
                  onStep: (documentId, direction) =>
                    void move(documentId, placementForStep(order, documentId, direction)),
                }
              : undefined
          }
          renderExtra={
            canWrite
              ? (photo) => (
                  <>
                    <Tooltip title={t('gallery.makeCover')}>
                      <Button
                        size="small"
                        type="text"
                        aria-label={t('gallery.makeCover')}
                        icon={
                          album.coverDocumentId === photo.documentId ? (
                            <StarFilled style={{ color: '#faad14' }} />
                          ) : (
                            <StarOutlined />
                          )
                        }
                        onClick={() =>
                          void setCover.mutateAsync({ id: album.id, documentId: photo.documentId })
                        }
                      />
                    </Tooltip>
                    <Tooltip title={t('gallery.editCredit')}>
                      <Button
                        size="small"
                        type="text"
                        aria-label={t('gallery.editCredit')}
                        icon={<EditOutlined />}
                        onClick={() => setEditing(photo.documentId)}
                      />
                    </Tooltip>
                    {/* Takes the picture out of the album and nothing more. Deleting a
                        photograph is a different act, reachable from the gallery. */}
                    <Tooltip title={t('gallery.removeFromAlbum')}>
                      <Button
                        size="small"
                        type="text"
                        aria-label={t('gallery.removeFromAlbum')}
                        icon={<DeleteOutlined />}
                        onClick={() => void remove(photo.documentId)}
                      />
                    </Tooltip>
                  </>
                )
              : undefined
          }
        />
      )}

      <Lightbox
        photos={photos}
        index={openIndex}
        onClose={() => setOpenIndex(null)}
        onIndexChange={setOpenIndex}
        onEdit={canWrite ? setEditing : undefined}
      />

      <PhotoCreditDrawer documentId={editing} onClose={() => setEditing(null)} />
    </div>
  );
}
