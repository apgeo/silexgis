// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo, useState } from 'react';
import { AimOutlined, DeleteOutlined, EditOutlined, ExportOutlined } from '@ant-design/icons';
import { Alert, App, Button, Descriptions, Empty, Flex, Popconfirm, Spin, Tag, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import {
  useCave,
  useCaveTypes,
  useClusterEntrances,
  useDeleteFeature,
  useEntrances,
  useFeature,
  useFeatureTypes,
  useCan,
  useUpdateFeature,
  type FeatureUpdate,
} from '../../api/hooks.ts';
import { formatLonLat } from '../../geo/coords.ts';
import { reloadSurfaceFeatures } from '../../map/featureLayer.ts';
import { fitGeoJsonGeometry, flyTo } from '../../map/mapContext.ts';
import { getMapTagFilter } from '../../map/mapFilters.ts';
import {
  useWorkspaceStore,
  type CaveSelection,
  type ClusterSelection,
  type EntranceSelection,
  type FeatureSelection,
} from '../../stores/workspaceStore.ts';
import HistoryPanel, { type HistoryRestore } from '../history/HistoryPanel.tsx';
import { applyFeatureRestore } from '../history/historyModel.ts';
import FeatureEditModal, { type FeatureAttributeValues } from '../features/FeatureEditModal.tsx';
import { parsePropertiesSchema } from '../features/propertiesSchema.ts';

export default function SelectionPanel() {
  const { t } = useTranslation();
  const selection = useWorkspaceStore((s) => s.selection);

  if (!selection) {
    return (
      <Flex align="center" justify="center" style={{ height: '100%', padding: 16 }}>
        <Empty description={t('map.noSelection')} image={Empty.PRESENTED_IMAGE_SIMPLE} />
      </Flex>
    );
  }

  if (selection.kind === 'feature') {
    return <FeatureCard selection={selection} />;
  }

  if (selection.kind === 'cluster') {
    return <ClusterCard selection={selection} />;
  }

  return <CaveCard selection={selection} />;
}

/** A clicked low-zoom cluster: list its member entrances without moving the camera. */
function ClusterCard({ selection }: { selection: ClusterSelection }) {
  const { t } = useTranslation();
  const setSelection = useWorkspaceStore((s) => s.setSelection);
  const { data, isPending } = useClusterEntrances(
    selection.lon,
    selection.lat,
    selection.zoom,
    getMapTagFilter() ?? undefined,
  );

  const entrances = useMemo(() => {
    return (data?.features ?? [])
      .map((feature) => {
        const props = feature.properties as Record<string, unknown>;
        const coords = (feature.geometry as { coordinates?: number[] }).coordinates ?? [];
        return {
          id: String(props.id ?? ''),
          caveId: String(props.caveId ?? ''),
          name: typeof props.name === 'string' && props.name ? props.name : null,
          approximate: props.approximate === true,
          lon: Number(coords[0]),
          lat: Number(coords[1]),
        };
      })
      .filter((e) => e.id && e.caveId)
      .sort((a, b) => (a.name ?? '').localeCompare(b.name ?? ''));
  }, [data]);

  if (isPending) {
    return (
      <Flex align="center" justify="center" style={{ height: '100%' }}>
        <Spin />
      </Flex>
    );
  }

  return (
    <div style={{ padding: 12, overflow: 'auto', height: '100%' }}>
      <Typography.Title level={5} style={{ marginTop: 0 }}>
        {t('map.clusterTitle', { count: entrances.length })}
      </Typography.Title>
      <Button
        icon={<AimOutlined />}
        size="small"
        style={{ marginBottom: 8 }}
        onClick={() => flyTo(selection.lon, selection.lat, selection.zoom + 2)}
      >
        {t('map.zoomHere')}
      </Button>
      <Flex vertical gap={2}>
        {entrances.map((entrance) => (
          <Button
            key={entrance.id}
            type="text"
            size="small"
            style={{ justifyContent: 'flex-start' }}
            onClick={() => {
              setSelection({ kind: 'entrance', entranceId: entrance.id, caveId: entrance.caveId });
              flyTo(entrance.lon, entrance.lat, 15);
            }}
          >
            {entrance.name ?? t('features.unnamed')}
            {entrance.approximate && (
              <Tag color="orange" style={{ marginLeft: 6 }}>
                {t('map.approximateShort')}
              </Tag>
            )}
          </Button>
        ))}
      </Flex>
    </div>
  );
}

function CaveCard({ selection }: { selection: EntranceSelection | CaveSelection }) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { data: cave, isPending } = useCave(selection.caveId);
  const { data: entrances } = useEntrances(selection.caveId);
  const { data: caveTypes } = useCaveTypes();

  if (isPending || !cave) {
    return (
      <Flex align="center" justify="center" style={{ height: '100%' }}>
        <Spin />
      </Flex>
    );
  }

  const entrance = selection.kind === 'entrance' ? entrances?.find((e) => e.id === selection.entranceId) : undefined;
  const typeName = caveTypes?.find((x) => x.id === cave.caveTypeId)?.name;

  return (
    <div style={{ padding: 12, overflow: 'auto', height: '100%' }}>
      <Typography.Title level={5} style={{ marginTop: 0 }}>
        {cave.name}
      </Typography.Title>
      {cave.approximateLocation && (
        <Alert type="warning" showIcon message={t('map.approximate')} style={{ marginBottom: 12 }} />
      )}
      <Descriptions column={1} size="small">
        {typeName && <Descriptions.Item label={t('caves.type')}>{typeName}</Descriptions.Item>}
        {cave.region && <Descriptions.Item label={t('caves.region')}>{cave.region}</Descriptions.Item>}
        {cave.surveyedLength != null && (
          <Descriptions.Item label={t('caves.surveyedLength')}>{cave.surveyedLength}</Descriptions.Item>
        )}
        {cave.depth != null && <Descriptions.Item label={t('caves.depth')}>{cave.depth}</Descriptions.Item>}
        <Descriptions.Item label={t('caves.entrances')}>{cave.entranceCount}</Descriptions.Item>
        {entrance && (
          <Descriptions.Item label={t('entrances.coordinates')}>
            {formatLonLat(entrance.geom.coordinates[0], entrance.geom.coordinates[1])}
          </Descriptions.Item>
        )}
        <Descriptions.Item label={t('caves.visibility')}>
          <Tag>{t(`caves.visibilityValues.${cave.visibility}`)}</Tag>
        </Descriptions.Item>
      </Descriptions>
      <Flex gap={8} style={{ marginTop: 12 }}>
        <Button
          icon={<ExportOutlined />}
          onClick={() => navigate(`/caves/${cave.id}`)}
          type="primary"
          size="small"
        >
          {t('map.openCave')}
        </Button>
        {entrance && (
          <Button
            icon={<AimOutlined />}
            size="small"
            onClick={() => flyTo(entrance.geom.coordinates[0], entrance.geom.coordinates[1], 16)}
          >
            {t('map.zoomTo')}
          </Button>
        )}
      </Flex>
    </div>
  );
}

function FeatureCard({ selection }: { selection: FeatureSelection }) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const setSelection = useWorkspaceStore((s) => s.setSelection);
  const { data: envelope, isPending } = useFeature(selection.featureId);
  const { data: featureTypes } = useFeatureTypes();
  const mayWriteFeatures = useCan('features', 'write');
  const updateFeatureM = useUpdateFeature();
  const deleteFeatureM = useDeleteFeature();
  const [editing, setEditing] = useState(false);

  const feature = envelope?.feature;
  const featureType = featureTypes?.find((x) => x.code === feature?.featureTypeCode);

  // Typed-property labels come from the type's schema; unknown keys show raw.
  const propertyRows = useMemo(() => {
    if (!feature || typeof feature.properties !== 'object' || feature.properties === null) {
      return [];
    }
    const labels = new Map(
      parsePropertiesSchema(featureType?.propertiesSchema).map((f) => [f.key, f.label]),
    );
    return Object.entries(feature.properties as Record<string, unknown>).map(([key, value]) => ({
      key,
      label: labels.get(key) ?? key,
      value: typeof value === 'boolean' ? (value ? '✓' : '✗') : String(value),
    }));
  }, [feature, featureType]);

  if (isPending || !envelope || !feature) {
    return (
      <Flex align="center" justify="center" style={{ height: '100%' }}>
        <Spin />
      </Flex>
    );
  }

  // Any feature id can land here (the list page and the envelope route are
  // cross-kind); caves and entrances have a richer card of their own.
  if (envelope.kind === 'cave') {
    return <CaveCard selection={{ kind: 'cave', caveId: feature.id }} />;
  }
  if (envelope.kind === 'caveEntrance' && envelope.entrance) {
    return (
      <CaveCard
        selection={{ kind: 'entrance', entranceId: feature.id, caveId: envelope.entrance.caveFeatureId }}
      />
    );
  }

  const canEdit = envelope.kind === 'generic' && mayWriteFeatures;
  // A protected feature the viewer may not see exactly can arrive without geometry.
  // Multi* geometries (imported geodata) collapse to their base shape for display
  // and for constraining the edit modal's type options.
  const baseShapes: Record<string, 'Point' | 'LineString' | 'Polygon'> = {
    Point: 'Point',
    MultiPoint: 'Point',
    LineString: 'LineString',
    MultiLineString: 'LineString',
    Polygon: 'Polygon',
    MultiPolygon: 'Polygon',
  };
  const geometryType = feature.geometry ? (baseShapes[feature.geometry.type] ?? null) : null;

  // The detail DTO names the type by code; the write contract wants its id.
  const writeDto = (): FeatureUpdate => ({
    name: feature.name,
    featureTypeId: Number(featureType?.id ?? 0),
    geometry: feature.geometry,
    description: feature.description,
    properties: feature.properties,
    locationProtected: feature.locationProtected,
    cavingGroupId: feature.cavingGroupId,
    visibility: feature.visibility,
  });

  const onEditSubmit = async (values: FeatureAttributeValues) => {
    try {
      await updateFeatureM.mutateAsync({
        id: feature.id,
        body: {
          name: values.name,
          featureTypeId: values.featureTypeId,
          geometry: feature.geometry,
          description: values.description,
          properties: values.properties as never,
          locationProtected: values.locationProtected,
          cavingGroupId: feature.cavingGroupId,
          visibility: values.visibility,
        },
      });
      reloadSurfaceFeatures();
      setEditing(false);
      message.success(t('common.saved'));
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  const onDelete = async () => {
    try {
      await deleteFeatureM.mutateAsync(feature.id);
      reloadSurfaceFeatures();
      setSelection(null);
      message.success(t('common.deleted'));
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  return (
    <div style={{ padding: 12, overflow: 'auto', height: '100%' }}>
      <Typography.Title level={5} style={{ marginTop: 0 }}>
        {feature.name ?? featureType?.name ?? t('features.unnamed')}
      </Typography.Title>
      {feature.omittedLocation && (
        <Alert type="warning" showIcon message={t('map.locationWithheld')} style={{ marginBottom: 12 }} />
      )}
      {!feature.omittedLocation && feature.approximateLocation && (
        <Alert type="warning" showIcon message={t('map.approximate')} style={{ marginBottom: 12 }} />
      )}
      <Descriptions column={1} size="small">
        <Descriptions.Item label={t('features.type')}>
          {featureType?.name ?? feature.featureTypeCode}
        </Descriptions.Item>
        {geometryType && (
          <Descriptions.Item label={t('features.geometry')}>
            {t(`features.geometryTypes.${geometryType}`)}
          </Descriptions.Item>
        )}
        {feature.description && (
          <Descriptions.Item label={t('features.description')}>{feature.description}</Descriptions.Item>
        )}
        {feature.parents.length > 0 && (
          <Descriptions.Item label={t('features.parent')}>
            {feature.parents.map((parent) => (
              <Tag key={parent.id}>{parent.name ?? t('features.unnamed')}</Tag>
            ))}
          </Descriptions.Item>
        )}
        {propertyRows.map((row) => (
          <Descriptions.Item key={row.key} label={row.label}>
            {row.value}
          </Descriptions.Item>
        ))}
        <Descriptions.Item label={t('features.visibility')}>
          <Tag>{t(`caves.visibilityValues.${feature.visibility}`)}</Tag>
        </Descriptions.Item>
      </Descriptions>
      <Flex gap={8} style={{ marginTop: 12 }} wrap>
        {feature.geometry && (
          <Button
            icon={<AimOutlined />}
            size="small"
            onClick={() => fitGeoJsonGeometry(feature.geometry!)}
          >
            {t('map.zoomTo')}
          </Button>
        )}
        {canEdit && (
          <>
            <Button icon={<EditOutlined />} size="small" onClick={() => setEditing(true)}>
              {t('features.edit')}
            </Button>
            <Popconfirm
              title={t('features.deleteConfirm')}
              onConfirm={() => void onDelete()}
              okButtonProps={{ danger: true, loading: deleteFeatureM.isPending }}
            >
              <Button icon={<DeleteOutlined />} size="small" danger>
                {t('features.delete')}
              </Button>
            </Popconfirm>
          </>
        )}
      </Flex>
      <HistoryPanel
        entityType="feature"
        entityId={feature.id}
        restore={
          canEdit
            ? ({
                // Audit rows carry the kind-qualified entity name.
                entityType: 'Feature:Generic',
                onRestore: async (event, props) => {
                  await updateFeatureM.mutateAsync({
                    id: feature.id,
                    body: applyFeatureRestore(writeDto(), event.changes, props),
                  });
                  reloadSurfaceFeatures();
                },
              } satisfies HistoryRestore)
            : undefined
        }
      />
      <FeatureEditModal
        open={editing}
        title={t('features.editFeature')}
        geometryType={geometryType}
        initial={{
          name: feature.name,
          featureTypeId: featureType ? Number(featureType.id) : undefined,
          description: feature.description,
          visibility: feature.visibility,
          locationProtected: feature.locationProtected,
          properties: (feature.properties ?? {}) as Record<string, unknown>,
        }}
        busy={updateFeatureM.isPending}
        onCancel={() => setEditing(false)}
        onSubmit={(values) => void onEditSubmit(values)}
      />
    </div>
  );
}
