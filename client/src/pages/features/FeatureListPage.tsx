// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { AimOutlined, DeleteOutlined, DownloadOutlined, EditOutlined } from '@ant-design/icons';
import { App, Button, Dropdown, Flex, Input, Popconfirm, Select, Table, Tag, Tooltip, Typography } from 'antd';
import type { TablePaginationConfig } from 'antd';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import { downloadFile } from '../../api/download.ts';
import {
  useDeleteSurfaceFeature,
  useFeatureTypes,
  useMe,
  useSurfaceFeatures,
  useTags,
  useUpdateSurfaceFeature,
  type SurfaceFeatureDetail,
  type SurfaceFeatureListParams,
} from '../../api/hooks.ts';
import FeatureEditModal, { type FeatureAttributeValues } from '../../components/features/FeatureEditModal.tsx';
import { useDebouncedValue } from '../../hooks/useDebouncedValue.ts';
import { reloadSurfaceFeatures } from '../../map/featureLayer.ts';
import { fitGeoJsonGeometry } from '../../map/mapContext.ts';
import { useWorkspaceStore } from '../../stores/workspaceStore.ts';

const exportFormats = ['csv', 'geojson', 'gpx', 'kml', 'shapefile'] as const;

export default function FeatureListPage() {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const { message } = App.useApp();
  const [params, setParams] = useState<SurfaceFeatureListParams>({ page: 1, pageSize: 20 });
  const [searchInput, setSearchInput] = useState('');
  const search = useDebouncedValue(searchInput);
  const { data, isFetching } = useSurfaceFeatures({ ...params, search: search || undefined });
  const { data: featureTypes } = useFeatureTypes();
  const { data: tags } = useTags('');
  const { data: me } = useMe();
  const setSelection = useWorkspaceStore((s) => s.setSelection);
  const updateFeature = useUpdateSurfaceFeature();
  const deleteFeature = useDeleteSurfaceFeature();
  const [editing, setEditing] = useState<SurfaceFeatureDetail | null>(null);

  const canEdit = me?.roles.some((r) => ['Admin', 'Manager', 'Editor'].includes(r)) ?? false;
  const typeName = (id: number) => featureTypes?.find((x) => Number(x.id) === id)?.name ?? String(id);

  const showOnMap = (feature: SurfaceFeatureDetail) => {
    setSelection({ kind: 'feature', featureId: feature.id });
    fitGeoJsonGeometry(feature.geometry);
    navigate('/');
  };

  const onEditSubmit = async (values: FeatureAttributeValues) => {
    if (!editing) {
      return;
    }
    try {
      await updateFeature.mutateAsync({
        id: editing.id,
        body: {
          name: values.name,
          featureTypeId: values.featureTypeId,
          geometry: editing.geometry,
          description: values.description,
          properties: values.properties as never,
          caveId: values.caveId,
          teamId: editing.teamId,
          visibility: values.visibility,
        },
      });
      reloadSurfaceFeatures();
      setEditing(null);
      message.success(t('common.saved'));
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  const onDelete = async (id: string) => {
    try {
      await deleteFeature.mutateAsync(id);
      reloadSurfaceFeatures();
      message.success(t('common.deleted'));
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  const onTableChange = (pagination: TablePaginationConfig) => {
    setParams((p) => ({ ...p, page: pagination.current, pageSize: pagination.pageSize }));
  };

  // Exports honor the current filters (not the current page — the server streams all rows).
  const onExport = (format: string) => {
    const query = new URLSearchParams({ format });
    if (search) {
      query.set('search', search);
    }
    if (params.featureTypeId !== undefined) {
      query.set('featureTypeId', String(params.featureTypeId));
    }
    downloadFile(`/api/v1/export/surface-features?${query}`).catch(() =>
      message.error(t('common.saveFailed')),
    );
  };

  return (
    <div style={{ padding: 24 }}>
      <Flex justify="space-between" align="center" style={{ marginBottom: 16 }}>
        <Typography.Title level={3} style={{ margin: 0 }}>
          {t('features.title')}
        </Typography.Title>
        <Dropdown
          menu={{
            items: exportFormats.map((format) => ({
              key: format,
              label: format.toUpperCase(),
              onClick: () => onExport(format),
            })),
          }}
        >
          <Button icon={<DownloadOutlined />}>{t('common.export')}</Button>
        </Dropdown>
      </Flex>
      <Flex gap={8} style={{ marginBottom: 12 }}>
        <Input.Search
          placeholder={t('features.searchPlaceholder')}
          allowClear
          style={{ maxWidth: 320 }}
          onChange={(e) => setSearchInput(e.target.value)}
        />
        <Select
          allowClear
          placeholder={t('features.allTypes')}
          style={{ width: 220 }}
          options={featureTypes?.map((x) => ({ value: Number(x.id), label: x.name }))}
          onChange={(value?: number) => setParams((p) => ({ ...p, page: 1, featureTypeId: value }))}
        />
        <Select
          allowClear
          showSearch
          optionFilterProp="label"
          placeholder={t('tags.filterPlaceholder')}
          style={{ width: 200 }}
          options={tags?.map((x) => ({ value: x.slug, label: x.name }))}
          onChange={(value?: string) => setParams((p) => ({ ...p, page: 1, tag: value }))}
        />
      </Flex>
      <Table<SurfaceFeatureDetail>
        rowKey="id"
        size="middle"
        loading={isFetching}
        dataSource={data?.items}
        onChange={onTableChange}
        pagination={{
          current: data?.page,
          pageSize: data?.pageSize,
          total: data?.totalItems,
          showSizeChanger: true,
        }}
        columns={[
          {
            title: t('features.name'),
            dataIndex: 'name',
            render: (name: string | null) =>
              name ?? (
                <Typography.Text type="secondary" italic>
                  {t('features.unnamed')}
                </Typography.Text>
              ),
          },
          { title: t('features.type'), dataIndex: 'featureTypeId', width: 200, render: typeName },
          {
            title: t('features.geometry'),
            dataIndex: 'geometry',
            width: 120,
            render: (geometry: SurfaceFeatureDetail['geometry']) =>
              t(`features.geometryTypes.${geometry.type}`),
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
            width: 130,
            render: (_, record) => (
              <Flex gap={4}>
                <Tooltip title={t('features.showOnMap')}>
                  <Button size="small" icon={<AimOutlined />} onClick={() => showOnMap(record)} />
                </Tooltip>
                {canEdit && (
                  <>
                    <Tooltip title={t('features.edit')}>
                      <Button size="small" icon={<EditOutlined />} onClick={() => setEditing(record)} />
                    </Tooltip>
                    <Popconfirm
                      title={t('features.deleteConfirm')}
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
      <FeatureEditModal
        open={editing !== null}
        title={t('features.editFeature')}
        geometryType={(editing?.geometry.type ?? 'Point') as 'Point' | 'LineString' | 'Polygon'}
        initial={
          editing
            ? {
                name: editing.name,
                featureTypeId: editing.featureTypeId,
                description: editing.description,
                visibility: editing.visibility,
                caveId: editing.caveId,
                properties: (editing.properties ?? {}) as Record<string, unknown>,
              }
            : {}
        }
        busy={updateFeature.isPending}
        onCancel={() => setEditing(null)}
        onSubmit={(values) => void onEditSubmit(values)}
      />
    </div>
  );
}
