// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import {
  AimOutlined,
  DeleteOutlined,
  DownloadOutlined,
  EditOutlined,
  ImportOutlined,
  InboxOutlined,
} from '@ant-design/icons';
import {
  App,
  Button,
  Dropdown,
  Flex,
  Form,
  Input,
  Modal,
  Popconfirm,
  Select,
  Table,
  Tabs,
  Tag,
  Tooltip,
  Typography,
  Upload,
} from 'antd';
import { useTranslation } from 'react-i18next';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { downloadFile } from '../../api/download.ts';
import {
  useCan,
  useDeleteGeofile,
  useGeofiles,
  useUpdateGeofile,
  useUploadGeofile,
  type GeofileInfo,
} from '../../api/hooks.ts';
import { useDebouncedValue } from '../../hooks/useDebouncedValue.ts';
import { fitGeoJsonGeometry } from '../../map/mapContext.ts';
import { useWorkspaceStore } from '../../stores/workspaceStore.ts';
import ImportBatchesTab from './ImportBatchesTab.tsx';
import RasterMapsTab from './RasterMapsTab.tsx';

const exportFormats = ['geojson', 'gpx', 'kml', 'csv', 'shapefile'] as const;

const statusColor: Record<string, string> = {
  uploaded: 'default',
  importing: 'processing',
  imported: 'success',
  failed: 'error',
};

interface EditFormValues {
  name: string;
  description?: string;
  visibility: GeofileInfo['visibility'];
}

/** Uploaded vector datasets: upload, import status, map toggles, export, manage. */
export default function GeodataPage() {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const [searchParams, setSearchParams] = useSearchParams();
  const { message } = App.useApp();
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [searchInput, setSearchInput] = useState('');
  const search = useDebouncedValue(searchInput);
  const { data, isFetching } = useGeofiles(
    { page, pageSize, search: search || undefined },
    /* pollWhileImporting */ true,
  );
  const upload = useUploadGeofile();
  const update = useUpdateGeofile();
  const remove = useDeleteGeofile();
  const setGeofileVisible = useWorkspaceStore((s) => s.setGeofileVisible);
  const [editing, setEditing] = useState<GeofileInfo | null>(null);
  const [form] = Form.useForm<EditFormValues>();

  const canEdit = useCan('geofiles', 'write');
  const canUpload = useCan('geofiles', 'create');
  // The review is offered to whoever could act on it. Reading a file and reviewing it are the
  // same permission; creating from it is the one that matters, and it is checked again on
  // confirmation — this only decides whether the button is worth showing.
  const canCreateFeatures = useCan('features', 'create');

  const showOnMap = (geofile: GeofileInfo) => {
    setGeofileVisible(geofile.id, true);
    if (geofile.bbox) {
      fitGeoJsonGeometry(geofile.bbox);
    }
    navigate('/map');
  };

  const onEditOpen = (geofile: GeofileInfo) => {
    setEditing(geofile);
    form.setFieldsValue({
      name: geofile.name,
      description: geofile.description ?? undefined,
      visibility: geofile.visibility,
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
          style: editing.style ?? null,
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
      setGeofileVisible(id, false);
      message.success(t('common.deleted'));
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  const onExport = (geofile: GeofileInfo, format: string) => {
    downloadFile(`/api/v1/geofiles/${geofile.id}/export?format=${format}`).catch(() =>
      message.error(t('common.saveFailed')),
    );
  };

  const vectorTab = (
    <>
      {canUpload && (
        <Upload.Dragger
          multiple
          accept=".gpx,.kml,.kmz,.geojson,.json,.zip,.csv,.tsv,.txt,.wkt,.wkb"
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
          <p className="ant-upload-text">{t('geodata.uploadPrompt')}</p>
          <p className="ant-upload-hint">{t('geodata.uploadHint')}</p>
        </Upload.Dragger>
      )}

      <Flex gap={8} style={{ marginBottom: 12 }}>
        <Input.Search
          placeholder={t('geodata.searchPlaceholder')}
          allowClear
          style={{ maxWidth: 320 }}
          onChange={(e) => setSearchInput(e.target.value)}
        />
      </Flex>

      <Table<GeofileInfo>
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
            title: t('geodata.format'),
            dataIndex: 'format',
            width: 110,
            render: (format: string) => <Tag>{format.toUpperCase()}</Tag>,
          },
          {
            title: t('geodata.status'),
            dataIndex: 'importStatus',
            width: 140,
            render: (status: string, record) => (
              <Tooltip title={record.importError}>
                <Tag color={statusColor[status]}>{t(`geodata.statusValues.${status}`)}</Tag>
              </Tooltip>
            ),
          },
          {
            title: t('geodata.features'),
            dataIndex: 'featureCount',
            width: 100,
            align: 'right',
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
            width: 170,
            render: (_, record) => (
              <Flex gap={4}>
                <Tooltip title={t('geodata.showOnMap')}>
                  <Button
                    size="small"
                    icon={<AimOutlined />}
                    disabled={record.importStatus !== 'imported'}
                    onClick={() => showOnMap(record)}
                  />
                </Tooltip>
                {canCreateFeatures && (
                  <Tooltip title={t('vectorImport.reviewAndImport')}>
                    <Button
                      size="small"
                      icon={<ImportOutlined />}
                      // An icon-only button's name would otherwise be the icon's own, which
                      // says "import" and not what this one imports into. The tooltip is a
                      // hover affordance and is nobody's accessible name.
                      aria-label={t('vectorImport.reviewAndImport')}
                      disabled={record.importStatus !== 'imported'}
                      data-testid={`geodata-review-${record.id}`}
                      onClick={() => navigate(`/geodata/${record.id}/import`)}
                    />
                  </Tooltip>
                )}
                <Dropdown
                  disabled={record.importStatus !== 'imported'}
                  menu={{
                    items: exportFormats.map((format) => ({
                      key: format,
                      label: format.toUpperCase(),
                      onClick: () => onExport(record, format),
                    })),
                  }}
                >
                  <Button size="small" icon={<DownloadOutlined />} />
                </Dropdown>
                {canEdit && (
                  <>
                    <Tooltip title={t('geodata.edit')}>
                      <Button size="small" icon={<EditOutlined />} onClick={() => onEditOpen(record)} />
                    </Tooltip>
                    <Popconfirm
                      title={t('geodata.deleteConfirm')}
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
        title={t('geodata.edit')}
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
          <Form.Item name="description" label={t('features.description')}>
            <Input.TextArea rows={3} />
          </Form.Item>
          <Form.Item name="visibility" label={t('features.visibility')} rules={[{ required: true }]}>
            <Select
              options={(['private', 'cavingGroup', 'authenticated', 'public'] as const).map((v) => ({
                value: v,
                label: t(`caves.visibilityValues.${v}`),
              }))}
            />
          </Form.Item>
        </Form>
      </Modal>
    </>
  );

  return (
    <div style={{ padding: 24 }}>
      <Flex justify="space-between" align="center" style={{ marginBottom: 16 }}>
        <Typography.Title level={3} style={{ margin: 0 }}>
          {t('geodata.title')}
        </Typography.Title>
      </Flex>
      <Tabs
        // The tab is in the address so a confirmation can send the importer straight to the
        // list of what it did — and so that list is a place somebody can link to.
        activeKey={searchParams.get('tab') ?? 'vector'}
        onChange={(key) => setSearchParams(key === 'vector' ? {} : { tab: key }, { replace: true })}
        items={[
          { key: 'vector', label: t('geodata.tabVector'), children: vectorTab },
          { key: 'raster', label: t('geodata.tabRaster'), children: <RasterMapsTab /> },
          { key: 'batches', label: t('geodata.tabImports'), children: <ImportBatchesTab /> },
        ]}
      />
    </div>
  );
}
