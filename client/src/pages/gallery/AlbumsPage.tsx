// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import {
  CopyOutlined, DeleteOutlined, DisconnectOutlined, EditOutlined, LinkOutlined, PictureOutlined, PlusOutlined,
  ShareAltOutlined,
} from '@ant-design/icons';
import {
  Alert, App, Card, Empty, Flex, Form, Input, Modal, Popconfirm, Select, Space, Spin, Tooltip,
  Typography, theme,
} from 'antd';
import Button from '../../components/Button.tsx';
import { KEEP_OPEN_ON_OUTSIDE_CLICK, useDiscardGuard, useFormEdits } from '../../components/useDiscardGuard.ts';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate } from 'react-router-dom';
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
  type Visibility,
} from '../../api/hooks.ts';
import { visibilityOptions } from '../../components/attachments/visibilityChoices.ts';
import { albumShareProblem } from './albumProblems.ts';

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
  const { token } = theme.useToken();
  const { message, modal } = App.useApp();
  const { data: capabilities } = useCapabilities();
  const canRead = hasAccessAction(capabilities?.domains.documents, 'read');
  // Editing and deleting follow the documents domain's write right. The share link is the
  // album's own business: minting and revoking it take the Share right on that album, which the
  // server works out per album and sends as `canShare`.
  const canWrite = hasAccessAction(capabilities?.domains.documents, 'write');

  const albumsQuery = useAlbums({ pageSize: 100 }, canRead);
  const { data, isPending } = albumsQuery;
  const createAlbum = useCreateAlbum();
  const navigate = useNavigate();
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
        // A new album is empty, and what comes next is filling it: it is opened at once.
        const created = await createAlbum.mutateAsync(body);
        setEditing(null);
        message.success(t('gallery.albumCreated'));
        void navigate(`/albums/${created.id}`);
        return;
      }
      if (editing) {
        await updateAlbum.mutateAsync({ id: editing.id, ...body });
      }

      setEditing(null);
      message.success(t('common.saved'));
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  const copyLink = async (url: string) => {
    try {
      await navigator.clipboard.writeText(url);
      message.success(t('gallery.linkCopied'));
    } catch {
      message.warning(t('gallery.copyFailed'));
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
          <Space orientation="vertical" style={{ width: '100%' }}>
            <Typography.Paragraph type="secondary">{t('gallery.shareOnce')}</Typography.Paragraph>
            <Space.Compact style={{ width: '100%' }}>
              <Input readOnly value={url} onFocus={(e) => e.target.select()} />
              <Button icon={<CopyOutlined />} onClick={() => void copyLink(url)}>
                {t('gallery.copyLink')}
              </Button>
            </Space.Compact>
          </Space>
        ),
        width: 560,
      });
    } catch (error) {
      message.error(albumShareProblem(error, t));
    }
  };

  const revokeLink = async (album: AlbumInfo) => {
    try {
      await revoke.mutateAsync(album.id);
      message.success(t('gallery.shareRevoked'));
    } catch (error) {
      message.error(albumShareProblem(error, t));
    }
  };

  const removeAlbum = async (album: AlbumInfo) => {
    try {
      await deleteAlbum.mutateAsync(album.id);
      message.success(t('common.deleted'));
    } catch {
      message.error(t('common.deleteFailed'));
    }
  };

  const cardActions = (album: AlbumInfo) => {
    const actions = [];
    if (canWrite) {
      actions.push(
        <Tooltip key="edit" title={t('gallery.editAlbum')}>
          <Button
            type="text"
            size="small"
            icon={<EditOutlined />}
            aria-label={t('gallery.editAlbum')}
            onClick={() => setEditing(album)}
          />
        </Tooltip>,
      );
    }
    if (album.hasActiveShare && album.canShare) {
      actions.push(
        <Popconfirm key="revoke" title={t('gallery.revokeConfirm')} onConfirm={() => revokeLink(album)}>
          <Tooltip title={t('gallery.revokeShare')}>
            {/* A glyph of its own: the share glyph means "make a link", never "take it back". */}
            <Button
              type="text"
              size="small"
              icon={<DisconnectOutlined />}
              aria-label={t('gallery.revokeShare')}
            />
          </Tooltip>
        </Popconfirm>,
      );
    } else if (!album.hasActiveShare && album.canShare) {
      actions.push(
        <Tooltip key="share" title={t('gallery.shareAlbum')}>
          <Button
            type="text"
            size="small"
            icon={<ShareAltOutlined />}
            aria-label={t('gallery.shareAlbum')}
            onClick={() => void mintLink(album)}
          />
        </Tooltip>,
      );
    }
    if (canWrite) {
      actions.push(
        <Popconfirm
          key="delete"
          title={t('gallery.deleteAlbumConfirm')}
          description={t('gallery.deleteAlbumHint')}
          onConfirm={() => removeAlbum(album)}
        >
          <Tooltip title={t('gallery.deleteAlbum')}>
            <Button type="text" size="small" icon={<DeleteOutlined />} aria-label={t('gallery.deleteAlbum')} />
          </Tooltip>
        </Popconfirm>,
      );
    }
    return actions.length > 0 ? actions : undefined;
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

      {albumsQuery.isError ? (
        <Alert
          type="error"
          showIcon
          title={t('gallery.albumsLoadFailed')}
          action={
            <Button size="small" onClick={() => void albumsQuery.refetch()}>
              {t('common.retry')}
            </Button>
          }
        />
      ) : isPending ? (
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
                  // Wrapped, because the card styles its cover's own child as a block, which
                  // undid the centring and left the glyph in the corner like a broken picture.
                  <div>
                    <Flex
                      vertical
                      align="center"
                      justify="center"
                      gap={4}
                      style={{ height: 150, background: token.colorFillQuaternary }}
                      data-testid="album-cover-placeholder"
                    >
                      <PictureOutlined style={{ fontSize: 28, opacity: 0.4 }} />
                      {album.photoCount === 0 && (
                        <Typography.Text type="secondary">{t('gallery.albumEmpty')}</Typography.Text>
                      )}
                    </Flex>
                  </div>
                )
              }
              actions={cardActions(album)}
            >
              <Card.Meta
                title={<Link to={`/albums/${album.id}`}>{album.title}</Link>}
                description={
                  <Space orientation="vertical" size={0}>
                    <span>{t('gallery.albumCount', { count: album.photoCount })}</span>
                    {album.hasActiveShare && (
                      <Typography.Text type="success">
                        <LinkOutlined /> {t('gallery.sharedByLink')}
                      </Typography.Text>
                    )}
                  </Space>
                }
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
  const [form] = Form.useForm<{ title: string; description?: string; visibility: Visibility }>();
  const requestDiscard = useDiscardGuard();
  const edits = useFormEdits();

  const save = async () => {
    let values: { title: string; description?: string; visibility: Visibility };
    try {
      values = await form.validateFields();
    } catch {
      // The fields say what is wrong with them; there is nothing to send.
      return;
    }
    await onSubmit({
      title: values.title,
      description: values.description ?? null,
      visibility: values.visibility,
    });
  };

  return (
    <Modal
      open
      title={editing === 'new' ? t('gallery.newAlbum') : t('gallery.editAlbum')}
      okText={editing === 'new' ? t('common.create') : t('common.save')}
      onCancel={() => requestDiscard(edits.edited, onCancel)}
      mask={KEEP_OPEN_ON_OUTSIDE_CLICK}
      onOk={() => void save()}
      confirmLoading={busy}
      destroyOnHidden
    >
      <Form
        form={form}
        layout="vertical"
        onValuesChange={edits.onValuesChange}
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
          <Select options={visibilityOptions(t)} />
        </Form.Item>
      </Form>
    </Modal>
  );
}
