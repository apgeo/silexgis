// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import {
  DeleteOutlined, EditOutlined, PictureOutlined, PlusOutlined, ShareAltOutlined,
} from '@ant-design/icons';
import {
  Alert, App, Button, Card, Empty, Flex, Form, Input, Modal, Popconfirm, Select, Space, Spin,
  Typography,
} from 'antd';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import {
  hasAccessAction,
  useAlbums,
  useCapabilities,
  useCreateAlbum,
  useDeleteAlbum,
  useRevokeAlbumShare,
  useShareAlbum,
  useUpdateAlbum,
  type AlbumInfo,
  type AlbumWrite,
} from '../../api/hooks.ts';

/**
 * Albums: named, ordered sets of photographs somebody put together.
 *
 * <p>
 * The list is deliberately covers-and-counts rather than a table. An album is a visual object —
 * the cover is what somebody recognises it by — and the count is what they may see in it rather
 * than what is in it, so the number beside a cover and the grid it opens agree.
 * </p>
 */
export default function AlbumsPage() {
  const { t } = useTranslation();
  const { message, modal } = App.useApp();
  const { data: capabilities } = useCapabilities();
  const canRead = hasAccessAction(capabilities?.domains.documents, 'read');
  const canWrite = hasAccessAction(capabilities?.domains.documents, 'write');

  const { data, isPending } = useAlbums({ pageSize: 100 }, canRead);
  const createAlbum = useCreateAlbum();
  const updateAlbum = useUpdateAlbum();
  const deleteAlbum = useDeleteAlbum();
  const share = useShareAlbum();
  const revoke = useRevokeAlbumShare();

  const [editing, setEditing] = useState<AlbumInfo | 'new' | null>(null);

  if (!capabilities) {
    return <Spin style={{ display: 'block', marginTop: '20vh' }} />;
  }

  if (!canRead) {
    return <Alert type="error" showIcon title={t('admin.forbidden')} style={{ margin: 16 }} />;
  }

  const submit = async (body: AlbumWrite) => {
    try {
      if (editing === 'new') {
        await createAlbum.mutateAsync(body);
      } else if (editing) {
        await updateAlbum.mutateAsync({ id: editing.id, ...body });
      }

      setEditing(null);
      message.success(t('common.saved'));
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  const mintLink = async (album: AlbumInfo) => {
    try {
      const minted = await share.mutateAsync(album.id);
      const url = `${window.location.origin}/shared/albums/${minted.token}`;

      // Shown once and never again: only the hash is stored, so the link cannot be recovered
      // from the database — it is revoked and re-minted instead. The dialog says so, because
      // somebody who closes it without copying has to know that.
      modal.info({
        title: t('gallery.shareTitle'),
        content: (
          <Space direction="vertical" style={{ width: '100%' }}>
            <Typography.Paragraph type="secondary">{t('gallery.shareOnce')}</Typography.Paragraph>
            <Input readOnly value={url} onFocus={(e) => e.target.select()} />
          </Space>
        ),
        width: 560,
      });
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  const albums = data?.items ?? [];

  return (
    <div style={{ padding: 24, height: '100%', overflow: 'auto' }}>
      <Flex justify="space-between" align="center" style={{ marginBottom: 16 }}>
        <Typography.Title level={3} style={{ margin: 0 }}>
          {t('gallery.albums')}
        </Typography.Title>
        {canWrite && (
          <Button type="primary" icon={<PlusOutlined />} onClick={() => setEditing('new')}>
            {t('gallery.newAlbum')}
          </Button>
        )}
      </Flex>

      {isPending ? (
        <Spin />
      ) : albums.length === 0 ? (
        <Empty description={t('gallery.noAlbums')} style={{ marginTop: '15vh' }} />
      ) : (
        <Flex wrap gap={16}>
          {albums.map((album) => (
            <Card
              key={album.id}
              size="small"
              style={{ width: 260 }}
              cover={
                album.coverThumbnailUrl ? (
                  <img
                    src={album.coverThumbnailUrl}
                    alt={album.title}
                    loading="lazy"
                    style={{ height: 150, objectFit: 'cover' }}
                  />
                ) : (
                  <Flex
                    align="center"
                    justify="center"
                    style={{ height: 150, background: 'rgba(0,0,0,0.04)' }}
                  >
                    <PictureOutlined style={{ fontSize: 28, opacity: 0.4 }} />
                  </Flex>
                )
              }
              actions={
                canWrite
                  ? [
                      <EditOutlined key="edit" onClick={() => setEditing(album)} />,
                      album.hasActiveShare ? (
                        <Popconfirm
                          key="revoke"
                          title={t('gallery.revokeConfirm')}
                          onConfirm={() => void revoke.mutateAsync(album.id)}
                        >
                          <ShareAltOutlined style={{ color: '#1677ff' }} />
                        </Popconfirm>
                      ) : (
                        <ShareAltOutlined key="share" onClick={() => void mintLink(album)} />
                      ),
                      <Popconfirm
                        key="delete"
                        title={t('gallery.deleteAlbumConfirm')}
                        description={t('gallery.deleteAlbumHint')}
                        onConfirm={() => void deleteAlbum.mutateAsync(album.id)}
                      >
                        <DeleteOutlined />
                      </Popconfirm>,
                    ]
                  : undefined
              }
            >
              <Card.Meta
                title={<Link to={`/albums/${album.id}`}>{album.title}</Link>}
                description={t('gallery.albumCount', { count: album.photoCount })}
              />
            </Card>
          ))}
        </Flex>
      )}

      {editing !== null && (
        <AlbumEditModal
          editing={editing}
          busy={createAlbum.isPending || updateAlbum.isPending}
          onCancel={() => setEditing(null)}
          onSubmit={submit}
        />
      )}
    </div>
  );
}

/**
 * Naming an album.
 *
 * Mounted only while one is being named, so the form store belongs with the fields it drives —
 * the same shape the cabinet editor uses.
 */
function AlbumEditModal({
  editing,
  busy,
  onCancel,
  onSubmit,
}: {
  editing: AlbumInfo | 'new';
  busy: boolean;
  onCancel: () => void;
  onSubmit: (body: AlbumWrite) => Promise<void>;
}) {
  const { t } = useTranslation();
  const [form] = Form.useForm<{ title: string; description?: string; visibility: string }>();

  return (
    <Modal
      open
      title={editing === 'new' ? t('gallery.newAlbum') : t('gallery.editAlbum')}
      onCancel={onCancel}
      onOk={() =>
        void form.validateFields().then((values) =>
          onSubmit({
            title: values.title,
            description: values.description ?? null,
            visibility: values.visibility,
          }),
        )
      }
      confirmLoading={busy}
      destroyOnHidden
    >
      <Form
        form={form}
        layout="vertical"
        requiredMark={false}
        initialValues={
          editing === 'new'
            ? { visibility: 'private' }
            : {
                title: editing.title,
                description: editing.description ?? undefined,
                visibility: editing.visibility,
              }
        }
      >
        <Form.Item name="title" label={t('gallery.albumTitle')} rules={[{ required: true }, { max: 200 }]}>
          <Input />
        </Form.Item>
        <Form.Item name="description" label={t('cabinets.description')} rules={[{ max: 2000 }]}>
          <Input.TextArea rows={2} />
        </Form.Item>
        <Form.Item name="visibility" label={t('documents.visibility')}>
          <Select
            options={(['private', 'cavingGroup', 'internal', 'public'] as const).map((value) => ({
              value,
              label: t(`caves.visibilityValues.${value}`),
            }))}
          />
        </Form.Item>
      </Form>
    </Modal>
  );
}
