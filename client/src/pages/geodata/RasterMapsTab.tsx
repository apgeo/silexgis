// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo, useState } from 'react';
import { AimOutlined, DeleteOutlined, EditOutlined, InboxOutlined } from '@ant-design/icons';
import {
  App,
  Flex,
  Form,
  Input,
  InputNumber,
  Modal,
  Popconfirm,
  Select,
  Table,
  Tag,
  Tooltip,
  Upload,
} from 'antd';
import Button from '../../components/Button.tsx';
import { useTranslation } from 'react-i18next';
import { useNavigate, useSearchParams } from 'react-router-dom';
import {
  useCan,
  useDeleteRasterMap,
  useRasterMaps,
  useUpdateRasterMap,
  useUploadRasterMap,
  type RasterMapInfo,
} from '../../api/hooks.ts';
import WorldFilterBar from '../../components/filters/WorldFilterBar.tsx';
import { readWorldFilter, worldToken, writeWorldFilter } from '../../filters/worldList.ts';
import ObjectSelector from '../../components/selector/ObjectSelector.tsx';
import type { SelectorScope } from '../../filters/selection.ts';
import { fitGeoJsonGeometry } from '../../map/mapContext.ts';
import { useWorkspaceStore } from '../../stores/workspaceStore.ts';

const statusColor: Record<string, string> = {
  uploaded: 'default',
  processing: 'processing',
  ready: 'success',
  failed: 'error',
};

/** The filter world this list is about. */
const RASTER_WORLD = 'georeferencedMap';

/** A georeferenced map is linked to a cave, never to an entrance or any other feature. */
const LINKED_CAVE_SCOPES: readonly SelectorScope[] = [
  { id: 'caves', labelKey: 'filters.scopes.caves', world: 'feature', where: { field: 'kind', value: 'cave' } },
];

interface EditFormValues {
  name: string;
  description?: string;
  mapKind: RasterMapInfo['mapKind'];
  attribution?: string;
  defaultOpacity: number;
  caveFeatureId?: string | null;
  visibility: RasterMapInfo['visibility'];
}

/** Georeferenced raster maps: upload GeoTIFFs, watch COG processing, manage, show on map. */
export default function RasterMapsTab() {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const { message } = App.useApp();
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [searchParams, setSearchParams] = useSearchParams();
  // The tab owns the conditions in the address while it is the active one (the page mounts it only
  // then), so a link to this tab carries them.
  const rasterFilter = useMemo(() => readWorldFilter(searchParams, RASTER_WORLD), [searchParams]);
  const { data, isFetching, isError } = useRasterMaps(
    { page, pageSize, filter: worldToken(RASTER_WORLD, rasterFilter.where) },
    /* pollWhileProcessing */ true,
  );
  const upload = useUploadRasterMap();
  const update = useUpdateRasterMap();
  const remove = useDeleteRasterMap();
  const setRasterVisible = useWorkspaceStore((s) => s.setRasterVisible);
  const [editing, setEditing] = useState<RasterMapInfo | null>(null);
  const [form] = Form.useForm<EditFormValues>();

  const canEdit = useCan('georeferencedMaps', 'write');
  const canUpload = useCan('georeferencedMaps', 'create');

  const showOnMap = (raster: RasterMapInfo) => {
    setRasterVisible(raster.id, true);
    if (raster.bbox) {
      fitGeoJsonGeometry(raster.bbox);
    }
    navigate('/map');
  };

  const onEditOpen = (raster: RasterMapInfo) => {
    setEditing(raster);
    form.setFieldsValue({
      name: raster.name,
      description: raster.description ?? undefined,
      mapKind: raster.mapKind,
      attribution: raster.attribution ?? undefined,
      defaultOpacity: Number(raster.defaultOpacity),
      caveFeatureId: raster.caveFeatureId ?? undefined,
      visibility: raster.visibility,
    });
  };

  const onEditSubmit = async () => {
    if (!editing) {
      return;
    }
    const values = await form.validateFields();
    try {
      await update.mutateAsync({
        id: editing.id,
        body: {
          name: values.name,
          description: values.description ?? null,
          mapKind: values.mapKind,
          minZoom: editing.minZoom,
          maxZoom: editing.maxZoom,
          attribution: values.attribution ?? null,
          defaultOpacity: values.defaultOpacity,
          caveFeatureId: values.caveFeatureId ?? null,
          cavingGroupId: editing.cavingGroupId,
          visibility: values.visibility,
        },
      });
      setEditing(null);
      message.success(t('common.saved'));
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  const onDelete = async (id: string) => {
    try {
      await remove.mutateAsync(id);
      setRasterVisible(id, false);
      message.success(t('common.deleted'));
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  return (
    <>
      {canUpload && (
        <Upload.Dragger
          multiple
          accept=".tif,.tiff"
          showUploadList={false}
          customRequest={({ file, onSuccess, onError }) => {
            upload
              .mutateAsync(file as File)
              .then((created) => {
                onSuccess?.(created);
                message.success(t('geodata.uploadQueued', { name: created.name }));
              })
              .catch((e: Error) => {
                onError?.(e);
                message.error(t('geodata.uploadFailed'));
              });
          }}
          style={{ marginBottom: 16 }}
        >
          <p className="ant-upload-drag-icon">
            <InboxOutlined />
          </p>
          <p className="ant-upload-text">{t('geodata.rasterUploadPrompt')}</p>
          <p className="ant-upload-hint">{t('geodata.rasterUploadHint')}</p>
        </Upload.Dragger>
      )}

      <WorldFilterBar
        world={RASTER_WORLD}
        testIdPrefix="raster"
        titleKey="worldFilter.rasterTitle"
        where={rasterFilter.where}
        savedFilterId={rasterFilter.savedFilterId}
        unreadableLink={rasterFilter.unreadableLink}
        refused={isError}
        onChange={(where, savedFilterId) => {
          setSearchParams(writeWorldFilter(searchParams, RASTER_WORLD, { where, savedFilterId }), { replace: true });
          setPage(1);
        }}
      />

      <Table<RasterMapInfo>
        scroll={{ x: 'max-content' }}
        rowKey="id"
        size="middle"
        loading={isFetching && !data}
        dataSource={data?.items}
        onChange={(pagination) => {
          setPage(pagination.current ?? 1);
          setPageSize(pagination.pageSize ?? 20);
        }}
        pagination={{
          current: data?.page,
          pageSize: data?.pageSize,
          total: data?.totalItems,
          showSizeChanger: true,
        }}
        columns={[
          { title: t('geodata.name'), dataIndex: 'name' },
          {
            title: t('geodata.mapKind'),
            dataIndex: 'mapKind',
            width: 140,
            render: (kind: string) => <Tag>{t(`geodata.mapKinds.${kind}`)}</Tag>,
          },
          {
            title: t('geodata.status'),
            dataIndex: 'status',
            width: 140,
            render: (status: string, record) => (
              <Tooltip title={record.processingError}>
                <Tag color={statusColor[status]}>{t(`geodata.rasterStatusValues.${status}`)}</Tag>
              </Tooltip>
            ),
          },
          {
            title: t('features.visibility'),
            dataIndex: 'visibility',
            width: 130,
            render: (value: string) => <Tag>{t(`caves.visibilityValues.${value}`)}</Tag>,
          },
          {
            title: t('features.updatedAt'),
            dataIndex: 'updatedAt',
            width: 170,
            render: (value: string) => new Date(value).toLocaleString(i18n.resolvedLanguage),
          },
          {
            title: '',
            key: 'actions',
            width: 140,
            render: (_, record) => (
              <Flex gap={4}>
                <Tooltip title={t('geodata.showOnMap')}>
                  <Button
                    size="small"
                    icon={<AimOutlined />}
                    disabled={record.status !== 'ready'}
                    onClick={() => showOnMap(record)}
                  />
                </Tooltip>
                {canEdit && (
                  <>
                    <Tooltip title={t('geodata.editRaster')}>
                      <Button size="small" icon={<EditOutlined />} onClick={() => onEditOpen(record)} />
                    </Tooltip>
                    <Popconfirm
                      title={t('geodata.deleteRasterConfirm')}
                      onConfirm={() => void onDelete(record.id)}
                      okButtonProps={{ danger: true }}
                    >
                      <Button size="small" icon={<DeleteOutlined />} danger />
                    </Popconfirm>
                  </>
                )}
              </Flex>
            ),
          },
        ]}
      />

      <Modal
        title={t('geodata.editRaster')}
        open={editing !== null}
        onCancel={() => setEditing(null)}
        onOk={() => void onEditSubmit()}
        confirmLoading={update.isPending}
        destroyOnHidden
      >
        <Form<EditFormValues> form={form} layout="vertical">
          <Form.Item name="name" label={t('geodata.name')} rules={[{ required: true }]}>
            <Input maxLength={200} />
          </Form.Item>
          <Form.Item name="mapKind" label={t('geodata.mapKind')} rules={[{ required: true }]}>
            <Select
              options={(['geological', 'topographic', 'tourist', 'caveMap', 'other'] as const).map((k) => ({
                value: k,
                label: t(`geodata.mapKinds.${k}`),
              }))}
            />
          </Form.Item>
          <Form.Item name="visibility" label={t('features.visibility')} rules={[{ required: true }]}>
            <Select
              options={(['private', 'cavingGroup', 'authenticated', 'public'] as const).map((v) => ({
                value: v,
                label: t(`caves.visibilityValues.${v}`),
              }))}
            />
          </Form.Item>
          <Form.Item name="caveFeatureId" label={t('features.linkedCave')}>
            {/* The linked cave is named by the filter model's resolve, so a cave this account may no
                longer read shows as "not shown" rather than as its identifier. Whether the map may
                be linked to the cave chosen is decided by the server when the form is saved. */}
            <ObjectSelector
              scopes={LINKED_CAVE_SCOPES}
              presentation="dropdown"
              allowClear
              pageSize={8}
              sorts={[]}
              placeholder={t('features.linkedCavePlaceholder')}
              aria-label={t('features.linkedCave')}
              data-testid="raster-map-cave-picker"
            />
          </Form.Item>
          <Form.Item name="defaultOpacity" label={t('geodata.defaultOpacity')} rules={[{ required: true }]}>
            <InputNumber min={0} max={1} step={0.05} style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item name="attribution" label={t('geodata.attribution')}>
            <Input maxLength={300} />
          </Form.Item>
          <Form.Item name="description" label={t('features.description')}>
            <Input.TextArea rows={2} />
          </Form.Item>
        </Form>
      </Modal>
    </>
  );
}
