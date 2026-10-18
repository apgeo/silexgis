// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo, useState } from 'react';
import { CopyOutlined, DeleteOutlined, FilterOutlined, RotateRightOutlined } from '@ant-design/icons';
import {
  Alert, App, Button, DatePicker, Drawer, Empty, Flex, Input, Pagination, Popconfirm, Select, Space, Spin,
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
  useTagsBySlug,
  type PhotoQueryParams,
} from '../../api/hooks.ts';
import Lightbox from '../../components/gallery/Lightbox.tsx';
import PhotoCreditDrawer from '../../components/gallery/PhotoCreditDrawer.tsx';
import PhotoGrid from '../../components/gallery/PhotoGrid.tsx';
import { viewPhoto } from '../../components/gallery/photoView.ts';
import FilterEditor from '../../components/filters/FilterEditor.tsx';
import {
  galleryEditorDocument,
  galleryToken,
  galleryWhere,
  PHOTO_WORLD,
  readGalleryControls,
  readGalleryDocument,
  readGalleryFilter,
  writeGalleryFilter,
  type GalleryControls,
} from '../../filters/gallery.ts';
import type { FilterNode } from '../../filters/types.ts';
import GallerySavedFilters from './GallerySavedFilters.tsx';

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
  const [editing, setEditing] = useState<string | null>(null);
  const [advancedOpen, setAdvancedOpen] = useState(false);

  const { data: cavers } = useCavers();
  const { data: tags } = useTags('');
  const { data: albums } = useAlbums({ pageSize: 100 }, canRead);
  const bulk = usePhotoBulk();

  // The photograph world's conditions, the search box's, the tag's and the date range's among
  // them: a link carries them and a saved gallery view keeps them. The other controls are
  // parameters of this screen beside the document.
  const galleryFilter = useMemo(() => readGalleryFilter(params), [params]);
  const { controls, extra } = useMemo(() => readGalleryControls(galleryFilter.where), [galleryFilter]);
  // A chosen tag is looked up by itself: the name listing answers only its first hundred.
  const { data: chosenTags } = useTagsBySlug(controls.tag ? [controls.tag] : []);
  const tagOptions = useMemo(() => {
    const options = (tags ?? []).map((tag) => ({ value: tag.slug, label: tag.name }));
    const chosen = chosenTags?.find((tag) => tag.slug === controls.tag);
    return chosen && !options.some((o) => o.value === chosen.slug)
      ? [...options, { value: chosen.slug, label: chosen.name }]
      : options;
  }, [tags, chosenTags, controls.tag]);

  // Read from the address rather than from state, so a gallery is a link somebody can send —
  // and so the map can open one for an extent by navigating.
  const query = useMemo<PhotoQueryParams>(
    () => ({
      filter: galleryToken(galleryFilter.where),
      caveId: params.get('caveId') ?? undefined,
      tripLogId: params.get('tripLogId') ?? undefined,
      expeditionId: params.get('expeditionId') ?? undefined,
      caverId: params.get('caverId') ?? undefined,
      albumId: params.get('albumId') ?? undefined,
      camera: params.get('camera') ?? undefined,
      bbox: params.get('bbox') ?? undefined,
      unplaced: params.get('unplaced') === 'true' ? true : undefined,
      page,
      pageSize: PageSize,
    }),
    [params, page, galleryFilter],
  );

  const { data, isFetching, isError } = usePhotos(query, canRead);
  // Narrowed to what the grid and the viewer take, so a caption edited here shows here: the
  // listing nests it under a credit and both components read the flat shape.
  const photos = useMemo(() => (data?.items ?? []).map(viewPhoto), [data]);

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

  const setGalleryFilter = (where: FilterNode | null, savedFilterId: string | undefined) => {
    setParams(writeGalleryFilter(params, { where, savedFilterId }), { replace: true });
    setPage(1);
    setSelected([]);
  };

  // A control moves its own condition in the document; the rest of the document stays as it was.
  const setControl = (update: GalleryControls) =>
    setGalleryFilter(galleryWhere({ ...controls, ...update }, extra), galleryFilter.savedFilterId);

  // What a saved view does not keep, said only while one of them is narrowing the screen.
  const unsavedSet = ['caverId', 'albumId', 'unplaced', 'caveId', 'featureId', 'tripLogId', 'expeditionId', 'camera', 'bbox']
    .some((key) => params.has(key));

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
          // Re-seeded when the document's search changes under it — an applied view, the back button.
          key={controls.search ?? ''}
          allowClear
          placeholder={t('gallery.search')}
          aria-label={t('gallery.search')}
          data-testid="gallery-search"
          defaultValue={controls.search}
          onSearch={(value) => setControl({ search: value.trim() || undefined })}
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
          aria-label={t('tags.title')}
          data-testid="gallery-tag"
          value={controls.tag}
          onChange={(value?: string) => setControl({ tag: value || undefined })}
          options={tagOptions}
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
            controls.from ? dayjs(controls.from) : null,
            controls.to ? dayjs(controls.to) : null,
          ]}
          onChange={(range) =>
            setControl({
              from: range?.[0]?.format('YYYY-MM-DD'),
              to: range?.[1]?.format('YYYY-MM-DD'),
            })}
        />
        <Select
          allowClear
          placeholder={t('gallery.placed')}
          value={params.get('unplaced') ?? undefined}
          onChange={(value) => setFilter('unplaced', value)}
          options={[{ value: 'true', label: t('gallery.unplaced') }]}
          style={{ width: 160 }}
        />
        <Button
          icon={<FilterOutlined />}
          onClick={() => setAdvancedOpen(true)}
          aria-haspopup="dialog"
          data-testid="gallery-advanced"
        >
          {t('gallery.filter.advanced')}
          {extra.length > 0 && ` · ${t('gallery.filter.on')}`}
        </Button>
        <GallerySavedFilters
          where={galleryFilter.where}
          savedFilterId={galleryFilter.savedFilterId}
          onApply={(where, savedFilterId) => setGalleryFilter(where, savedFilterId)}
          onSaved={(savedFilterId) => setGalleryFilter(galleryFilter.where, savedFilterId)}
          onForget={() => setGalleryFilter(galleryFilter.where, undefined)}
        />
      </Flex>

      {unsavedSet && (
        <Typography.Paragraph type="secondary" style={{ marginTop: -8 }} data-testid="gallery-unsaved-controls">
          {t('gallery.filter.unsavedControls')}
        </Typography.Paragraph>
      )}

      <Drawer
        open={advancedOpen}
        title={t('gallery.filter.advancedTitle')}
        size="large"
        destroyOnHidden
        onClose={() => setAdvancedOpen(false)}
      >
        {/* Keyed by the question it opens on, so an address that moves while it is open re-seeds it
            rather than leaving a draft of an older question to be applied over the newer one. */}
        <FilterEditor
          key={JSON.stringify(galleryFilter.where)}
          world={PHOTO_WORLD}
          document={galleryEditorDocument(galleryFilter.where)}
          onCancel={() => setAdvancedOpen(false)}
          onApply={(edited) => {
            setGalleryFilter(readGalleryDocument(edited) ?? null, galleryFilter.savedFilterId);
            setAdvancedOpen(false);
          }}
        />
      </Drawer>

      {galleryFilter.unreadableLink && (
        <Alert
          type="warning"
          showIcon
          style={{ marginBottom: 12 }}
          data-testid="gallery-unreadable-link"
          title={t('gallery.filter.unreadableLink')}
        />
      )}

      {isError && (
        <Alert
          type="error"
          showIcon
          style={{ marginBottom: 12 }}
          data-testid="gallery-filter-refused"
          title={t('gallery.filter.refused')}
        />
      )}

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

      {/* The extent held more photographs than one search carries, so the grid is a share of
          them; without this a dense extent reads as complete. */}
      {params.get('bbox') && data?.extentCutShort && (
        <Alert
          type="info"
          showIcon
          style={{ marginBottom: 12 }}
          data-testid="gallery-extent-cut-short"
          title={t('gallery.mapExtentCutShort')}
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
        onEdit={canWrite ? setEditing : undefined}
      />

      {/* Mounted beside the viewer rather than inside it, so closing the viewer with the credit
          drawer open does not take the form away mid-edit. */}
      <PhotoCreditDrawer documentId={editing} onClose={() => setEditing(null)} />
    </div>
  );
}
