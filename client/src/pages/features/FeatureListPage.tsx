// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { AimOutlined, DeleteOutlined, DownloadOutlined, EyeOutlined } from '@ant-design/icons';
import { App, Button, Dropdown, Flex, Input, Popconfirm, Select, Table, Tag, Tooltip, Typography } from 'antd';
import type { TablePaginationConfig } from 'antd';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import { downloadFile } from '../../api/download.ts';
import {
  useCan,
  useDeleteFeature,
  useFeatureTypes,
  useFeatures,
  useTags,
  type FeatureCategory,
  type FeatureKind,
  type FeatureListItem,
  type FeatureListParams,
} from '../../api/hooks.ts';
import { useDebouncedValue } from '../../hooks/useDebouncedValue.ts';
import { fitGeoJsonGeometry } from '../../map/mapContext.ts';
import { useWorkspaceStore } from '../../stores/workspaceStore.ts';
import { surfaceFeaturesChanged } from '../../workspace/surfaceFeatureRefresh.ts';

const exportFormats = ['csv', 'geojson', 'gpx', 'kml', 'shapefile'] as const;
const featureKinds: FeatureKind[] = ['generic', 'cave', 'caveEntrance', 'centerline'];
const featureCategories: FeatureCategory[] = ['surface', 'underground', 'area', 'structure'];

export default function FeatureListPage() {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const { message } = App.useApp();
  const [params, setParams] = useState<FeatureListParams>({ page: 1, pageSize: 20 });
  const [searchInput, setSearchInput] = useState('');
  const search = useDebouncedValue(searchInput);
  const { data, isFetching } = useFeatures({ ...params, search: search || undefined });
  const { data: featureTypes } = useFeatureTypes();
  const { data: tags } = useTags('');
  const setSelection = useWorkspaceStore((s) => s.setSelection);
  const deleteFeature = useDeleteFeature();

  const canEdit = useCan('features', 'write');
  const typeName = (code: string | null) =>
    code === null ? '' : featureTypes?.find((x) => x.code === code)?.name ?? code;

  const showOnMap = (feature: FeatureListItem) => {
    if (!feature.geometry) {
      return;
    }
    setSelection({ kind: 'feature', featureId: feature.id });
    fitGeoJsonGeometry(feature.geometry);
    navigate('/map');
  };

  const onDelete = async (id: string) => {
    try {
      await deleteFeature.mutateAsync(id);
      surfaceFeaturesChanged();
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
    if (params.kind !== undefined) {
      query.set('kind', params.kind);
    }
    if (params.featureTypeId !== undefined) {
      query.set('featureTypeId', String(params.featureTypeId));
    }
    downloadFile(`/api/v1/export/features?${query}`).catch(() =>
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
      <Flex gap={8} wrap style={{ marginBottom: 12 }}>
        <Input.Search
          placeholder={t('features.searchPlaceholder')}
          allowClear
          style={{ maxWidth: 320 }}
          onChange={(e) => setSearchInput(e.target.value)}
        />
        <Select
          allowClear
          placeholder={t('features.allKinds')}
          style={{ width: 170 }}
          options={featureKinds.map((k) => ({ value: k, label: t(`features.kinds.${k}`) }))}
          onChange={(value?: FeatureKind) => setParams((p) => ({ ...p, page: 1, kind: value }))}
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
          placeholder={t('features.allCategories')}
          style={{ width: 170 }}
          options={featureCategories.map((c) => ({ value: c, label: t(`features.categories.${c}`) }))}
          onChange={(value?: FeatureCategory) => setParams((p) => ({ ...p, page: 1, category: value }))}
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
      <Table<FeatureListItem>
        scroll={{ x: 'max-content' }}
        rowKey="id"
        size="middle"
        loading={isFetching}
        dataSource={data?.items}
        onChange={onTableChange}
        onRow={(record) => ({
          onClick: () => navigate(`/features/${record.id}`),
          style: { cursor: 'pointer' },
        })}
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
          {
            title: t('features.kind'),
            dataIndex: 'kind',
            width: 130,
            render: (kind: FeatureKind) => <Tag>{t(`features.kinds.${kind}`)}</Tag>,
          },
          { title: t('features.type'), dataIndex: 'featureTypeCode', width: 200, render: typeName },
          {
            title: t('features.geometry'),
            dataIndex: 'geometry',
            width: 130,
            render: (geometry: FeatureListItem['geometry'], record) =>
              geometry
                ? t(`features.geometryTypes.${geometry.type}`)
                : record.omittedLocation
                  ? t('features.locationWithheld')
                  : '—',
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
              // Buttons must not also trigger the row's navigate-to-detail click.
              <Flex gap={4} onClick={(e) => e.stopPropagation()}>
                <Tooltip title={t('features.showOnMap')}>
                  <Button
                    size="small"
                    icon={<AimOutlined />}
                    disabled={!record.geometry}
                    onClick={() => showOnMap(record)}
                  />
                </Tooltip>
                <Tooltip title={t('features.open')}>
                  <Button
                    size="small"
                    icon={<EyeOutlined />}
                    onClick={() => navigate(`/features/${record.id}`)}
                  />
                </Tooltip>
                {canEdit && (
                  <Popconfirm
                    title={t('features.deleteConfirm')}
                    onConfirm={() => void onDelete(record.id)}
                    okButtonProps={{ danger: true }}
                  >
                    <Button size="small" icon={<DeleteOutlined />} danger />
                  </Popconfirm>
                )}
              </Flex>
            ),
          },
        ]}
      />
    </div>
  );
}
