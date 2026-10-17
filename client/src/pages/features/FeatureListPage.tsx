// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useMemo, useRef, useState } from 'react';
import { AimOutlined, DeleteOutlined, DownloadOutlined, EyeOutlined } from '@ant-design/icons';
import { Alert, App, Dropdown, Flex, Input, Popconfirm, Select, Table, Tag, Tooltip, Typography } from 'antd';
import Button from '../../components/Button.tsx';
import type { TablePaginationConfig } from 'antd';
import { useTranslation } from 'react-i18next';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { downloadFile, featureExportUrl } from '../../api/download.ts';
import {
  useCan,
  useCreateDoor,
  useDeleteFeature,
  useFeatureTypes,
  useFeatures,
  useTags,
  type FeatureCategory,
  type FeatureKind,
  type FeatureListItem,
} from '../../api/hooks.ts';
import ConfigureLink from '../../components/ConfigureLink.tsx';
import ListSavedFilters from '../../components/filters/ListSavedFilters.tsx';
import SelectResultButton from '../../components/filters/SelectResultButton.tsx';
import {
  featureListDocument,
  featureListToken,
  readFeatureList,
  readFeatureListAddress,
  sameFeatureQuestion,
  writeFeatureListAddress,
  type FeatureListAddress,
  type FeatureListControls,
} from '../../filters/featureList.ts';
import { useDebouncedValue } from '../../hooks/useDebouncedValue.ts';
import { fitGeoJsonGeometry } from '../../map/mapContext.ts';
import { useWorkspaceStore } from '../../stores/workspaceStore.ts';
import { surfaceFeaturesChanged } from '../../workspace/surfaceFeatureRefresh.ts';
import { useTableMapLink } from '../../workspace/tableMapLink.ts';
import { mapAddressForList } from '../../filters/tableMapHandoff.ts';

const exportFormats = ['csv', 'geojson', 'gpx', 'kml', 'shapefile'] as const;
const featureKinds: FeatureKind[] = ['generic', 'cave', 'caveEntrance', 'centerline'];
const featureCategories: FeatureCategory[] = ['surface', 'underground', 'area', 'structure'];

type AddressState = Omit<FeatureListAddress, 'unreadableLink'>;

/**
 * The feature list.
 *
 * Its whole state lives in the address: the controls compose one filter document, carried as a
 * token, beside the table's page. So a pasted link, a reload, the back button and a saved filter
 * all reproduce the same list by the same road, and the export is asked with the very token the
 * list was — the file holds the rows on the screen.
 */
export default function FeatureListPage() {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const { message } = App.useApp();
  const [searchParams, setSearchParams] = useSearchParams();
  const address = useMemo(() => readFeatureListAddress(searchParams), [searchParams]);
  const { controls, extra } = address;
  const token = featureListToken(address);

  // What this page has written to its address and not yet seen arrive, newest last. The router
  // applies a write a moment after it is asked for, so two writes close together — a choice made
  // while the search box's pause runs out — would otherwise each start from the address as it was
  // before either, and the second would quietly undo the first.
  const written = useRef<string[]>([]);
  const arrivedByOwnWrite = useRef(false);
  useEffect(() => {
    const arrived = written.current.indexOf(searchParams.toString());
    written.current = arrived === -1 ? [] : written.current.slice(arrived);
    arrivedByOwnWrite.current = arrived !== -1;
  }, [searchParams]);

  /** A new state written to the address. Typing replaces the entry; anything chosen adds one. */
  const go = (update: (current: AddressState) => Partial<AddressState>, replace = false) => {
    const newest = written.current.at(-1);
    const base = newest === undefined ? address : readFeatureListAddress(new URLSearchParams(newest));
    const current: AddressState = {
      controls: base.controls,
      extra: base.extra,
      savedFilterId: base.savedFilterId,
      page: base.page,
      pageSize: base.pageSize,
    };
    const params = writeFeatureListAddress({ ...current, ...update(current) });
    written.current = [...written.current, params.toString()];
    setSearchParams(params, { replace });
  };

  /** One control changed: back to the first page, the saved filter kept so the change shows. */
  const setControl = (patch: FeatureListControls, replace = false) =>
    go((current) => ({ controls: { ...current.controls, ...patch }, page: 1 }), replace);

  // The search box is typed into, so it holds its own text and reaches the address only once the
  // typing pauses. The address is still the truth: when it changes under the box — back, forward,
  // a saved filter applied — the box follows it.
  const [searchInput, setSearchInput] = useState(controls.search ?? '');
  const search = useDebouncedValue(searchInput);
  const latest = useRef({ controls, setControl });
  useEffect(() => {
    latest.current = { controls, setControl };
  });
  useEffect(() => {
    const arrived = controls.search ?? '';
    // The address gives the text back trimmed; putting that over the box would take away the
    // space somebody has just typed before their next word.
    const ownWrite = arrivedByOwnWrite.current;
    setSearchInput((typed) => (ownWrite && typed.trim() === arrived.trim() ? typed : arrived));
  }, [controls.search]);
  useEffect(() => {
    const { controls: now, setControl: write } = latest.current;
    if ((search.trim() || undefined) !== (now.search?.trim() || undefined)) {
      write({ search: search.trim() || undefined }, true);
    }
    // Only a settled change of what was typed writes; reacting to the address as well would type
    // the old text back over a step somebody just went back from.
  }, [search]);

  const { data, isFetching, error } = useFeatures({
    page: address.page,
    pageSize: address.pageSize,
    filter: token,
  });
  const { data: featureTypes } = useFeatureTypes();
  const { data: tags } = useTags('');
  const setSelection = useWorkspaceStore((s) => s.setSelection);
  const deleteFeature = useDeleteFeature();
  const tableMap = useTableMapLink<FeatureListItem>();

  const canEdit = useCan('features', 'write');
  const canReadFeatureSets = useCan('featureSets', 'read');
  const canCreateFeatures = useCan('features', 'create');
  // Whoever could have deleted a feature is shown where one is put back from: by the right to
  // delete, or by being able to create one — an owner deletes what they own without any
  // capability that spells "delete", and somebody who records features only for their caving
  // group owns those like anybody else, which is why this asks the create door. The page behind
  // the door is decided row by row on the server.
  const canDeleteFeatures = useCan('features', 'delete');
  const { canCreate: mayOwn } = useCreateDoor('features');
  const canRestore = mayOwn || canDeleteFeatures;
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
    go(() => ({ page: pagination.current ?? 1, pageSize: pagination.pageSize ?? address.pageSize }));
  };

  // The export carries the list's own filter token (not the current page — the server streams all
  // rows), so the file holds exactly what the list shows that can be written with a position.
  const onExport = (format: string) => {
    downloadFile(featureExportUrl(format, { filter: token })).catch(() =>
      message.error(t('common.saveFailed')),
    );
  };

  return (
    <div style={{ padding: 24 }}>
      <Flex justify="space-between" align="center" style={{ marginBottom: 16 }}>
        <Typography.Title level={3} style={{ margin: 0 }}>
          {t('features.title')}
        </Typography.Title>
        <Flex gap={8} align="center">
          {/* The vocabularies this page reads by, reached from the page they govern rather than
              only from the configuration group in the rail. */}
          <ConfigureLink
            items={[
              ...(canReadFeatureSets ? [{ key: 'admin/feature-sets', label: t('nav.featureSets') }] : []),
              ...(canCreateFeatures ? [{ key: 'admin/term-rules', label: t('nav.termRules') }] : []),
            ]}
          />
          {/* The map is opened with this list's own document as its filter, so it draws the rows
              listed here — the same question, run by the map's routes. */}
          <Button
            icon={<AimOutlined />}
            data-testid="feature-list-show-on-map"
            onClick={() => navigate(mapAddressForList(token))}
          >
            {t('tableMap.showOnMap')}
          </Button>
          <SelectResultButton
            list="features"
            query={{ filter: token }}
            total={data?.totalItems}
            data-testid="feature-list-select-all"
          />
          {canRestore && (
            <Button
              icon={<DeleteOutlined />}
              data-testid="feature-list-deleted"
              onClick={() => navigate('/features/deleted')}
            >
              {t('features.deleted.link')}
            </Button>
          )}
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
      </Flex>
      {address.unreadableLink && (
        <Alert
          type="warning"
          showIcon
          style={{ marginBottom: 12 }}
          data-testid="feature-list-unreadable-link"
          title={t('featureList.unreadableLink')}
        />
      )}
      <Flex gap={8} wrap style={{ marginBottom: 12 }}>
        <Input.Search
          placeholder={t('features.searchPlaceholder')}
          aria-label={t('featureList.searchLabel')}
          data-testid="feature-list-search"
          allowClear
          style={{ maxWidth: 320 }}
          value={searchInput}
          onChange={(e) => setSearchInput(e.target.value)}
        />
        <Select
          allowClear
          placeholder={t('features.allKinds')}
          aria-label={t('featureList.kindLabel')}
          data-testid="feature-list-kind"
          style={{ width: 170 }}
          value={controls.kind ?? null}
          options={featureKinds.map((k) => ({ value: k, label: t(`features.kinds.${k}`) }))}
          onChange={(value?: FeatureKind | null) => setControl({ kind: value ?? undefined })}
        />
        <Select
          allowClear
          showSearch
          optionFilterProp="label"
          placeholder={t('features.allTypes')}
          aria-label={t('featureList.typeLabel')}
          data-testid="feature-list-type"
          style={{ width: 220 }}
          value={controls.featureTypeId ?? null}
          options={featureTypes?.map((x) => ({ value: Number(x.id), label: x.name }))}
          onChange={(value?: number | null) => setControl({ featureTypeId: value ?? undefined })}
        />
        <Select
          allowClear
          placeholder={t('features.allCategories')}
          aria-label={t('featureList.categoryLabel')}
          data-testid="feature-list-category"
          style={{ width: 170 }}
          value={controls.category ?? null}
          options={featureCategories.map((c) => ({ value: c, label: t(`features.categories.${c}`) }))}
          onChange={(value?: FeatureCategory | null) => setControl({ category: value ?? undefined })}
        />
        <Select
          allowClear
          showSearch
          optionFilterProp="label"
          placeholder={t('tags.filterPlaceholder')}
          aria-label={t('featureList.tagLabel')}
          data-testid="feature-list-tag"
          style={{ width: 200 }}
          value={controls.tag ?? null}
          options={tags?.map((x) => ({ value: x.slug, label: x.name }))}
          onChange={(value?: string | null) => setControl({ tag: value ?? undefined })}
        />
      </Flex>
      <ListSavedFilters
        reading={address}
        document={featureListDocument(controls, extra)}
        read={readFeatureList}
        same={sameFeatureQuestion}
        savedFilterId={address.savedFilterId}
        testIdPrefix="feature-list"
        notAboutThisListKey="featureList.savedNotAboutFeatures"
        onApply={(reading, savedFilterId) =>
          go(() => ({ controls: reading.controls, extra: reading.extra, savedFilterId, page: 1 }))}
        onSaved={(savedFilterId) => go(() => ({ savedFilterId }))}
        onClearExtra={() => go(() => ({ extra: [], page: 1 }))}
        onForget={() => go(() => ({ savedFilterId: undefined }))}
        onRelease={() => go(() => ({ controls: {}, extra: [], savedFilterId: undefined, page: 1 }))}
      />
      {error && token !== undefined && (
        <Alert
          type="error"
          showIcon
          style={{ marginBottom: 12 }}
          data-testid="feature-list-filter-refused"
          title={t('featureList.filterRefused')}
        />
      )}
      <Table<FeatureListItem>
        scroll={{ x: 'max-content' }}
        rowKey="id"
        size="middle"
        loading={isFetching}
        dataSource={data?.items}
        onChange={onTableChange}
        rowClassName={tableMap.rowClassName}
        onRow={(record) => ({
          onClick: () => navigate(`/features/${record.id}`),
          style: { cursor: 'pointer' },
          ...tableMap.rowHover(record),
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
                    description={t('features.deleteRestorable')}
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
