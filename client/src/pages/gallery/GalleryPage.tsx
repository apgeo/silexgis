// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo, useState } from 'react';
import { CopyOutlined, DeleteOutlined, RotateRightOutlined } from '@ant-design/icons';
import {
  Alert, App, Button, DatePicker, Empty, Flex, Input, Pagination, Popconfirm, Select, Space, Spin,
  Typography,
} from 'antd';
import dayjs from 'dayjs';
import { useTranslation } from 'react-i18next';
import { useSearchParams } from 'react-router-dom';
import {
  hasAccessAction,
  useAlbums,
  useCapabilities,
  useCavers,
  usePhotoBulk,
  usePhotos,
  useTags,
  type PhotoQueryParams,
} from '../../api/hooks.ts';
import Lightbox from '../../components/gallery/Lightbox.tsx';
import PhotoGrid from '../../components/gallery/PhotoGrid.tsx';

/** Pictures per page. Large enough that scrolling is the main gesture, small enough to load. */
const PageSize = 60;

/**
 * Every photograph in the installation, browsable in one place.
 *
 * <p>
 * The filters are the point of it. A club archive is tens of thousands of pictures, and
 * "everything, newest first" stops being useful at about four hundred — so the same grid
 * answers "the entrance shots of this cave", "everything Ana photographed", "what came back
 * from that trip" and "what has no position yet".
 * </p>
 * <p>
 * The map extent arrives in the address, which is what makes clicking a cluster on the map open
 * the gallery for it: the two surfaces agree because they are asking the server the same
 * question, and the server withholds a picture's position from both in the same way.
 * </p>
 */
export default function GalleryPage() {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const [params, setParams] = useSearchParams();
  const { data: capabilities } = useCapabilities();
  const canRead = hasAccessAction(capabilities?.domains.documents, 'read');
  const canWrite = hasAccessAction(capabilities?.domains.documents, 'write');

  const [page, setPage] = useState(1);
  const [selected, setSelected] = useState<string[]>([]);
  const [openIndex, setOpenIndex] = useState<number | null>(null);

  const { data: cavers } = useCavers();
  const { data: tags } = useTags('');
  const { data: albums } = useAlbums({ pageSize: 100 }, canRead);
  const bulk = usePhotoBulk();

  // Read from the address rather than from state, so a gallery is a link somebody can send —
  // and so the map can open one for an extent by navigating.
  const query = useMemo<PhotoQueryParams>(
    () => ({
      caveId: params.get('caveId') ?? undefined,
      tripLogId: params.get('tripLogId') ?? undefined,
      caverId: params.get('caverId') ?? undefined,
      tagId: params.get('tagId') ? Number(params.get('tagId')) : undefined,
      albumId: params.get('albumId') ?? undefined,
      camera: params.get('camera') ?? undefined,
      bbox: params.get('bbox') ?? undefined,
      unplaced: params.get('unplaced') === 'true' ? true : undefined,
      from: params.get('from') ?? undefined,
      to: params.get('to') ?? undefined,
      search: params.get('search') ?? undefined,
      page,
      pageSize: PageSize,
    }),
    [params, page],
  );

  const { data, isFetching } = usePhotos(query, canRead);
  const photos = data?.items ?? [];

  if (!capabilities) {
    return <Spin style={{ display: 'block', marginTop: '20vh' }} />;
  }

  if (!canRead) {
    return <Alert type="error" showIcon title={t('admin.forbidden')} style={{ margin: 16 }} />;
  }

  const setFilter = (key: string, value: string | undefined) => {
    const next = new URLSearchParams(params);
    if (value === undefined || value === '') {
      next.delete(key);
    } else {
      next.set(key, value);
    }

    setParams(next, { replace: true });
    setPage(1);
    setSelected([]);
  };

  const toggle = (documentId: string) =>
    setSelected((current) =>
      current.includes(documentId)
        ? current.filter((id) => id !== documentId)
        : [...current, documentId],
    );

  const applyBulk = async (body: Parameters<typeof bulk.mutateAsync>[0], done: string) => {
    try {
      const result = await bulk.mutateAsync(body);
      const refused = Object.keys(result.refused).length;
      // A selection of two hundred will, on a real archive, contain one somebody else owns —
      // so the answer is a partial result and is reported as one.
      if (refused === 0) {
        message.success(t(done, { count: result.changed.length }));
      } else {
        message.warning(t('gallery.bulkPartly', { changed: result.changed.length, refused }));
      }

      setSelected([]);
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  return (
    <div style={{ padding: 24, height: '100%', overflow: 'auto' }}>
      <Typography.Title level={3} style={{ marginTop: 0 }}>
        {t('gallery.title')}
      </Typography.Title>

      <Flex wrap gap={8} style={{ marginBottom: 16 }}>
        <Input.Search
          allowClear
          placeholder={t('gallery.search')}
          defaultValue={params.get('search') ?? undefined}
          onSearch={(value) => setFilter('search', value || undefined)}
          style={{ width: 220 }}
        />
        <Select
          allowClear
          showSearch
          optionFilterProp="label"
          placeholder={t('gallery.photographer')}
          value={params.get('caverId') ?? undefined}
          onChange={(value) => setFilter('caverId', value)}
          options={(cavers ?? []).map((c) => ({ value: c.id, label: c.name }))}
          style={{ width: 200 }}
        />
        <Select
          allowClear
          showSearch
          optionFilterProp="label"
          placeholder={t('tags.title')}
          value={params.get('tagId') ?? undefined}
          onChange={(value) => setFilter('tagId', value)}
          options={(tags ?? []).map((tag) => ({ value: String(tag.id), label: tag.name }))}
          style={{ width: 180 }}
        />
        <Select
          allowClear
          showSearch
          optionFilterProp="label"
          placeholder={t('gallery.album')}
          value={params.get('albumId') ?? undefined}
          onChange={(value) => setFilter('albumId', value)}
          options={(albums?.items ?? []).map((a) => ({ value: a.id, label: a.title }))}
          style={{ width: 200 }}
        />
        <DatePicker.RangePicker
          allowEmpty={[true, true]}
          value={[
            params.get('from') ? dayjs(params.get('from')) : null,
            params.get('to') ? dayjs(params.get('to')) : null,
          ]}
          onChange={(range) => {
            setFilter('from', range?.[0]?.format('YYYY-MM-DD'));
            setFilter('to', range?.[1]?.format('YYYY-MM-DD'));
          }}
        />
        <Select
          allowClear
          placeholder={t('gallery.placed')}
          value={params.get('unplaced') ?? undefined}
          onChange={(value) => setFilter('unplaced', value)}
          options={[{ value: 'true', label: t('gallery.unplaced') }]}
          style={{ width: 160 }}
        />
      </Flex>

      {params.get('bbox') && (
        <Alert
          type="info"
          showIcon
          closable
          onClose={() => setFilter('bbox', undefined)}
          style={{ marginBottom: 12 }}
          title={t('gallery.mapExtent')}
        />
      )}

      {canWrite && selected.length > 0 && (
        <Flex align="center" gap={12} style={{ marginBottom: 12 }} data-testid="photo-bulk-bar">
          <Typography.Text strong>{t('gallery.selected', { count: selected.length })}</Typography.Text>
          <Space>
            <Button
              icon={<RotateRightOutlined />}
              onClick={() =>
                void applyBulk(
                  { documentIds: selected, rotateQuarterTurns: 1 },
                  'gallery.rotated',
                )
              }
            >
              {t('gallery.rotate')}
            </Button>
            <Select
              placeholder={t('gallery.addToAlbum')}
              style={{ width: 200 }}
              value={null}
              onChange={(albumId: string) =>
                void applyBulk(
                  { documentIds: selected, addToAlbumId: albumId },
                  'gallery.addedToAlbum',
                )
              }
              options={(albums?.items ?? []).map((a) => ({ value: a.id, label: a.title }))}
              suffixIcon={<CopyOutlined />}
            />
            <Popconfirm
              title={t('gallery.deleteConfirm')}
              description={t('gallery.deleteHint')}
              okButtonProps={{ danger: true }}
              onConfirm={() =>
                void applyBulk({ documentIds: selected, delete: true }, 'gallery.deleted')
              }
            >
              <Button danger icon={<DeleteOutlined />}>
                {t('gallery.delete')}
              </Button>
            </Popconfirm>
          </Space>
        </Flex>
      )}

      {isFetching && !data ? (
        <Spin style={{ display: 'block', marginTop: '15vh' }} />
      ) : photos.length === 0 ? (
        <Empty description={t('gallery.empty')} style={{ marginTop: '15vh' }} />
      ) : (
        <>
          <PhotoGrid
            photos={photos}
            onOpen={(documentId) =>
              setOpenIndex(photos.findIndex((p) => p.documentId === documentId))
            }
            selected={canWrite ? selected : undefined}
            onToggle={canWrite ? toggle : undefined}
          />

          <Pagination
            style={{ marginTop: 16 }}
            current={data?.page}
            pageSize={data?.pageSize}
            total={data?.totalItems}
            showSizeChanger={false}
            onChange={(next) => {
              setPage(next);
              setSelected([]);
            }}
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
