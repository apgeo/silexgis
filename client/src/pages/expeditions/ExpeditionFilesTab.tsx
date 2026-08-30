// SPDX-License-Identifier: AGPL-3.0-or-later
import { PictureOutlined, PlusOutlined } from '@ant-design/icons';
import { App, Button, Card, Empty, Flex, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate } from 'react-router-dom';
import {
  hasAccessAction,
  useAlbums,
  useCapabilities,
  useCreateAlbum,
} from '../../api/hooks.ts';
import AttachmentSection from '../../components/attachments/AttachmentSection.tsx';

/**
 * What is filed against the camp: the permit, the plan, the photographs somebody attached to the
 * camp itself, and the albums made about it.
 *
 * There is no grid of the camp's pictures here, and that is not an omission. A photograph filed on
 * a camp is protected by the caves the camp's member trips name, and the gallery answers about a
 * trip rather than a camp — so a grid built here would be assembled from the wrong question. The
 * attachments below come through the camp's own reading, which already carries that protection.
 */
export default function ExpeditionFilesTab({
  expeditionId,
  expeditionName,
  canEdit,
}: {
  expeditionId: string;
  expeditionName: string;
  canEdit: boolean;
}) {
  return (
    <div data-testid="expedition-files-tab">
      <AttachmentSection entityType="expedition" entityId={expeditionId} canEdit={canEdit} />
      <ExpeditionAlbums expeditionId={expeditionId} expeditionName={expeditionName} />
    </div>
  );
}

/**
 * The albums that are about this camp.
 *
 * The right to make one is the right to write documents, not the right to write the camp: an album
 * is separately governed content that happens to have a camp as its subject. So somebody who may
 * edit the camp and may not write documents is shown the albums and not the button, and the server
 * would refuse them anyway.
 */
function ExpeditionAlbums({
  expeditionId,
  expeditionName,
}: {
  expeditionId: string;
  expeditionName: string;
}) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const navigate = useNavigate();
  const { data: capabilities } = useCapabilities();
  const canRead = hasAccessAction(capabilities?.domains.documents, 'read');
  const canWrite = hasAccessAction(capabilities?.domains.documents, 'write');
  const albumsQuery = useAlbums({ subjectEntityId: expeditionId, pageSize: 100 }, canRead);
  const createAlbum = useCreateAlbum();

  if (!canRead) {
    return null;
  }

  const albums = albumsQuery.data?.items ?? [];

  // A new album starts private and named after the camp. Private because an album's audience is
  // its own decision and inheriting the camp's would widen one silently; named after the camp
  // because the alternative is a dialog asking for the one thing the page already knows.
  const onCreate = async () => {
    try {
      const album = await createAlbum.mutateAsync({
        title: expeditionName,
        visibility: 'private',
        subjectEntityType: 'expedition',
        subjectEntityId: expeditionId,
      });
      navigate(`/albums/${album.id}`);
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  return (
    <Card
      size="small"
      title={t('expeditions.albums')}
      style={{ marginTop: 16 }}
      data-testid="expedition-albums"
      extra={
        canWrite && (
          <Button
            size="small"
            icon={<PlusOutlined />}
            loading={createAlbum.isPending}
            onClick={() => void onCreate()}
            data-testid="expedition-new-album"
          >
            {t('gallery.newAlbum')}
          </Button>
        )
      }
    >
      {albums.length === 0 ? (
        <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('expeditions.noAlbums')} />
      ) : (
        <Flex wrap gap={12}>
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
    </Card>
  );
}
