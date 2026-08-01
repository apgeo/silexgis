// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo, useState } from 'react';
import { AimOutlined, DeleteOutlined, EditOutlined, InboxOutlined } from '@ant-design/icons';
import {
  App,
  Button,
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
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import {
  useCave,
  useDeleteRasterMap,
  useMe,
  useRasterMaps,
  useSearch,
  useUpdateRasterMap,
  useUploadRasterMap,
  type RasterMapInfo,
} from '../../api/hooks.ts';
import { useDebouncedValue } from '../../hooks/useDebouncedValue.ts';
import { fitGeoJsonGeometry } from '../../map/mapContext.ts';
import { useWorkspaceStore } from '../../stores/workspaceStore.ts';

const statusColor: Record<string, string> = {
  uploaded: 'default',
  processing: 'processing',
  ready: 'success',
  failed: 'error',
};

interface EditFormValues {
  name: string;
  description?: string;
  mapKind: RasterMapInfo['mapKind'];
  attribution?: string;
  defaultOpacity: number;
  caveFeatureId?: string;
  visibility: RasterMapInfo['visibility'];
}

/** Georeferenced raster maps: upload GeoTIFFs, watch COG processing, manage, show on map. */
export default function RasterMapsTab() {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const { message } = App.useApp();
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const { data, isFetching } = useRasterMaps({ page, pageSize }, /* pollWhileProcessing */ true);
  const { data: me } = useMe();
  const upload = useUploadRasterMap();
  const update = useUpdateRasterMap();
  const remove = useDeleteRasterMap();
  const setRasterVisible = useWorkspaceStore((s) => s.setRasterVisible);
  const [editing, setEditing] = useState<RasterMapInfo | null>(null);
  const [form] = Form.useForm<EditFormValues>();

  // Cave link: remote-search select (same pattern as the feature editor). The search spans
  // every feature kind, so only cave results become options here.
  const [caveQuery, setCaveQuery] = useState('');
  const debouncedCaveQuery = useDebouncedValue(caveQuery);
  // Ask the server for caves: its hit budget is shared across kinds, so filtering a mixed
  // answer here can leave the picker empty while a matching cave exists.
  const { data: searchResults } = useSearch(debouncedCaveQuery, 'cave');
  const { data: linkedCave } = useCave(editing?.caveFeatureId ?? undefined);
  const caveOptions = useMemo(() => {
    const options = (searchResults?.features ?? [])
      .map((cave) => ({ value: cave.id, label: cave.name ?? cave.id }));
    if (linkedCave && !options.some((o) => o.value === linkedCave.id)) {
      options.unshift({ value: linkedCave.id, label: linkedCave.name });
    }
    return options;
  }, [searchResults, linkedCave]);

  const canEdit = me?.roles.some((r) => ['Admin', 'Manager', 'Editor'].includes(r)) ?? false;

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
    setCaveQuery('');
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
      {canEdit && (
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
            <Select
              allowClear
              showSearch
              filterOption={false}
              onSearch={setCaveQuery}
              placeholder={t('features.linkedCavePlaceholder')}
              options={caveOptions}
              notFoundContent={null}
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
