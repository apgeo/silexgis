// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useMemo, useState } from 'react';
import {
  AimOutlined,
  CameraOutlined,
  DeleteOutlined,
  DownloadOutlined,
  EditOutlined,
  FontColorsOutlined,
  ImportOutlined,
} from '@ant-design/icons';
import {
  Alert,
  App,
  Dropdown,
  Flex,
  Form,
  Input,
  Modal,
  Popconfirm,
  Select,
  Spin,
  Table,
  Tabs,
  Tag,
  Tooltip,
  Typography,
} from 'antd';
import Button from '../../components/Button.tsx';
import UploadDropzone from '../../components/geodata/UploadDropzone.tsx';
import CavingGroupPicker from '../../components/cavingGroups/CavingGroupPicker.tsx';
import { refusalText } from '../../components/geodata/refusalText.ts';
import { isConcurrencyConflict } from '../../api/client.ts';
import { useTranslation } from 'react-i18next';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { downloadFile, geofileListExportUrl } from '../../api/download.ts';
import {
  useCan,
  useDeleteGeofile,
  useGeofileForEdit,
  useGeofiles,
  useUpdateGeofile,
  useUploadGeofile,
  type GeofileInfo,
} from '../../api/hooks.ts';
import { useDebouncedValue } from '../../hooks/useDebouncedValue.ts';
import { fitGeoJsonGeometry } from '../../map/mapContext.ts';
import { useWorkspaceStore } from '../../stores/workspaceStore.ts';
import WorldFilterBar from '../../components/filters/WorldFilterBar.tsx';
import { readWorldFilter, worldToken, writeWorldFilter, type WorldFilter } from '../../filters/worldList.ts';
import GeofileEncodingDialog from './GeofileEncodingDialog.tsx';
import ImportBatchesTab from './ImportBatchesTab.tsx';
import RasterMapsTab from './RasterMapsTab.tsx';

/** The filter world the vector list is about. */
const GEOFILE_WORLD = 'geofile';

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
  cavingGroupId?: string | null;
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
  // The conditions live in the address, as the gallery's do, so a link carries them. They belong to
  // the vector tab: the other tabs read none, and switching tabs leaves them behind.
  const tab = searchParams.get('tab') ?? 'vector';
  const geofileFilter = useMemo(
    (): WorldFilter => (tab === 'vector' ? readWorldFilter(searchParams, GEOFILE_WORLD) : { where: null, unreadableLink: false }),
    [searchParams, tab],
  );
  const { data, isFetching, isError } = useGeofiles(
    {
      page,
      pageSize,
      search: search || undefined,
      filter: worldToken(GEOFILE_WORLD, geofileFilter.where),
    },
    /* pollWhileImporting */ true,
  );
  const upload = useUploadGeofile();
  const update = useUpdateGeofile();
  const remove = useDeleteGeofile();
  const setGeofileVisible = useWorkspaceStore((s) => s.setGeofileVisible);
  const [editing, setEditing] = useState<GeofileInfo | null>(null);
  const [encodingOf, setEncodingOf] = useState<GeofileInfo | null>(null);
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

  // The form starts from the file as the server holds it now, not from the list row: the update
  // replaces every field, so a style or a group changed since the list was read would otherwise
  // be written back over. The read's version goes with the save, so a change made meanwhile is
  // refused rather than overwritten.
  const editRead = useGeofileForEdit(editing?.id);
  const [filled, setFilled] = useState<{ id: string; style: GeofileInfo['style']; version?: string } | null>(null);
  const editVisibility = Form.useWatch('visibility', form);

  useEffect(() => {
    const read = editRead.data;
    if (!editing || !read || read.value.id !== editing.id || filled?.id === editing.id) {
      return;
    }
    form.setFieldsValue({
      name: read.value.name,
      description: read.value.description ?? undefined,
      visibility: read.value.visibility,
      cavingGroupId: read.value.cavingGroupId ?? undefined,
    });
    setFilled({ id: read.value.id, style: read.value.style, version: read.version });
  }, [editing, editRead.data, filled, form]);

  const onEditOpen = (geofile: GeofileInfo) => {
    setFilled(null);
    form.resetFields();
    setEditing(geofile);
  };

  const onEditClose = () => {
    setEditing(null);
    setFilled(null);
  };

  const onEditSubmit = async () => {
    if (!editing || filled?.id !== editing.id) {
      return;
    }
    let values: EditFormValues;
    try {
      values = await form.validateFields();
    } catch {
      // The form shows what is missing beside the field itself.
      return;
    }
    try {
      await update.mutateAsync({
        id: editing.id,
        version: filled.version,
        body: {
          name: values.name,
          description: values.description ?? null,
          style: filled.style ?? null,
          cavingGroupId: values.cavingGroupId ?? null,
          visibility: values.visibility,
        },
      });
      onEditClose();
      message.success(t('common.saved'));
    } catch (error) {
      // A version that moved is told by the application-wide notice that it changed since it was
      // opened; a second message here would say it twice.
      if (!isConcurrencyConflict(error)) {
        message.error(refusalText(error, t, 'common.saveFailed'));
      }
    }
  };

  const onDelete = async (id: string) => {
    try {
      await remove.mutateAsync(id);
      setGeofileVisible(id, false);
      message.success(t('common.deleted'));
    } catch (error) {
      message.error(refusalText(error, t, 'geodata.deleteFailed'));
    }
  };

  const onExport = (geofile: GeofileInfo, format: string) => {
    downloadFile(`/api/v1/geofiles/${geofile.id}/export?format=${format}`).catch((error: unknown) =>
      message.error(refusalText(error, t, 'geodata.exportFailed')),
    );
  };

  const onExportList = (format: string) => {
    downloadFile(
      geofileListExportUrl(format, {
        search: search || undefined,
        filter: worldToken(GEOFILE_WORLD, geofileFilter.where),
      }),
    ).catch(() => message.error(t('geodata.exportListFailed')));
  };

  const vectorTab = (
    <>
      {canUpload && (
        <UploadDropzone
          accept=".gpx,.kml,.kmz,.geojson,.json,.zip,.csv,.tsv,.txt,.wkt,.wkb"
          prompt={t('geodata.uploadPrompt')}
          hint={t('geodata.uploadHint')}
          upload={(file, onProgress) => upload.mutateAsync({ file, onProgress })}
          doneText={(name) => t('geodata.uploadQueued', { name })}
        />
      )}

      <WorldFilterBar
        world={GEOFILE_WORLD}
        testIdPrefix="geodata"
        titleKey="worldFilter.geofileTitle"
        where={geofileFilter.where}
        savedFilterId={geofileFilter.savedFilterId}
        unreadableLink={geofileFilter.unreadableLink}
        refused={isError}
        onChange={(where, savedFilterId) => {
          setSearchParams(writeWorldFilter(searchParams, GEOFILE_WORLD, { where, savedFilterId }), { replace: true });
          setPage(1);
        }}
      />

      <Flex gap={8} style={{ marginBottom: 12 }}>
        <Input.Search
          placeholder={t('geodata.searchPlaceholder')}
          allowClear
          style={{ maxWidth: 320 }}
          onChange={(e) => {
            // A new search starts on its first page: the page the old list was on can lie past
            // the end of a narrower answer and read as "nothing matches".
            setSearchInput(e.target.value);
            setPage(1);
          }}
        />
        <Dropdown
          menu={{
            items: exportFormats.map((format) => ({
              key: format,
              label: format.toUpperCase(),
              onClick: () => onExportList(format),
            })),
          }}
        >
          <Button icon={<DownloadOutlined />} data-testid="geodata-export-list">
            {t('geodata.exportList')}
          </Button>
        </Dropdown>
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
            width: 200,
            render: (_, record) => (
              <Flex gap={4}>
                <Tooltip title={t('geodata.showOnMap')}>
                  <Button
                    size="small"
                    icon={<AimOutlined />}
                    aria-label={t('geodata.showOnMap')}
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
                  <Button size="small" icon={<DownloadOutlined />} aria-label={t('iconButtons.download')} />
                </Dropdown>
                {canEdit && (
                  <>
                    <Tooltip title={t('geodata.edit')}>
                      <Button size="small" icon={<EditOutlined />} aria-label={t('geodata.edit')} onClick={() => onEditOpen(record)} />
                    </Tooltip>
                    {/* Only a delimited table is decoded from bytes by this application; every
                        other format states its own encoding. A file still queued or being read
                        cannot be re-read yet, and the server would refuse it. */}
                    {record.format === 'csv' && (
                      <Tooltip
                        title={
                          record.importStatus === 'uploaded' || record.importStatus === 'importing'
                            ? t('geodata.encoding.busy')
                            : t('geodata.encoding.action')
                        }
                      >
                        <Button
                          size="small"
                          icon={<FontColorsOutlined />}
                          aria-label={t('geodata.encoding.action')}
                          disabled={record.importStatus === 'uploaded' || record.importStatus === 'importing'}
                          data-testid={`geodata-encoding-open-${record.id}`}
                          onClick={() => setEncodingOf(record)}
                        />
                      </Tooltip>
                    )}
                    <Popconfirm
                      title={t('geodata.deleteConfirm')}
                      onConfirm={() => void onDelete(record.id)}
                      okButtonProps={{ danger: true }}
                    >
                      <Button size="small" icon={<DeleteOutlined />} aria-label={t('iconButtons.delete')} danger />
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
        onCancel={onEditClose}
        onOk={() => void onEditSubmit()}
        confirmLoading={update.isPending}
        okButtonProps={{ disabled: filled === null || filled.id !== editing?.id }}
        forceRender
      >
        {editRead.isError && (
          <Alert
            type="error"
            showIcon
            style={{ marginBottom: 16 }}
            title={refusalText(editRead.error, t, 'geodata.editLoadFailed')}
            action={
              <Button size="small" onClick={() => void editRead.refetch()}>
                {t('common.retry')}
              </Button>
            }
          />
        )}
        <Spin spinning={editRead.isLoading}>
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
          {/* Caving-group visibility admits the members of one group, so it needs the group
              named; without one the server refuses the save. */}
          <Form.Item
            name="cavingGroupId"
            label={t('geodata.cavingGroup')}
            rules={[{ required: editVisibility === 'cavingGroup', message: t('geodata.cavingGroupRequired') }]}
          >
            <CavingGroupPicker />
          </Form.Item>
        </Form>
        </Spin>
      </Modal>

      <GeofileEncodingDialog geofile={encodingOf} onClose={() => setEncodingOf(null)} />
    </>
  );

  return (
    <div style={{ padding: 24 }}>
      <Flex justify="space-between" align="center" style={{ marginBottom: 16 }} gap={12} wrap>
        <Typography.Title level={3} style={{ margin: 0 }}>
          {t('geodata.title')}
        </Typography.Title>
        <Button
          icon={<CameraOutlined />}
          onClick={() => navigate('/geodata/photo-import')}
          data-testid="photo-import-open"
        >
          {t('photoImport.openFromGeodata')}
        </Button>
      </Flex>
      <Tabs
        // The tab is in the address so a confirmation can send the importer straight to the
        // list of what it did — and so that list is a place somebody can link to.
        activeKey={tab}
        onChange={(key) => setSearchParams(key === 'vector' ? {} : { tab: key }, { replace: true })}
        items={[
          { key: 'vector', label: t('geodata.tabVector'), children: vectorTab },
          { key: 'raster', label: t('geodata.tabRaster'), children: tab === 'raster' ? <RasterMapsTab /> : null },
          { key: 'batches', label: t('geodata.tabImports'), children: <ImportBatchesTab /> },
        ]}
      />
    </div>
  );
}
