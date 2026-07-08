// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo, useState } from 'react';
import { AimOutlined, DeleteOutlined, EditOutlined, ExportOutlined } from '@ant-design/icons';
import { Alert, App, Button, Descriptions, Empty, Flex, Popconfirm, Spin, Tag, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate } from 'react-router-dom';
import {
  useCave,
  useCaveTypes,
  useDeleteSurfaceFeature,
  useEntrances,
  useFeatureTypes,
  useMe,
  useSurfaceFeature,
  useUpdateSurfaceFeature,
} from '../../api/hooks.ts';
import { formatLonLat } from '../../geo/coords.ts';
import { reloadSurfaceFeatures } from '../../map/featureLayer.ts';
import { fitGeoJsonGeometry, flyTo } from '../../map/mapContext.ts';
import { useWorkspaceStore, type CaveSelection, type EntranceSelection, type FeatureSelection } from '../../stores/workspaceStore.ts';
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

  return <CaveCard selection={selection} />;
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
  const { data: feature, isPending } = useSurfaceFeature(selection.featureId);
  const { data: featureTypes } = useFeatureTypes();
  const { data: linkedCave } = useCave(feature?.caveId ?? undefined);
  const { data: me } = useMe();
  const updateFeature = useUpdateSurfaceFeature();
  const deleteFeature = useDeleteSurfaceFeature();
  const [editing, setEditing] = useState(false);

  const featureType = featureTypes?.find((x) => Number(x.id) === feature?.featureTypeId);

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

  if (isPending || !feature) {
    return (
      <Flex align="center" justify="center" style={{ height: '100%' }}>
        <Spin />
      </Flex>
    );
  }

  const canEdit = me?.roles.some((r) => ['Admin', 'Manager', 'Editor'].includes(r)) ?? false;
  const geometryType = feature.geometry.type as 'Point' | 'LineString' | 'Polygon';

  const onEditSubmit = async (values: FeatureAttributeValues) => {
    try {
      await updateFeature.mutateAsync({
        id: feature.id,
        body: {
          name: values.name,
          featureTypeId: values.featureTypeId,
          geometry: feature.geometry,
          description: values.description,
          properties: values.properties as never,
          caveId: values.caveId,
          teamId: feature.teamId,
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
      await deleteFeature.mutateAsync(feature.id);
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
      <Descriptions column={1} size="small">
        <Descriptions.Item label={t('features.type')}>
          {featureType?.name ?? feature.featureTypeId}
        </Descriptions.Item>
        <Descriptions.Item label={t('features.geometry')}>
          {t(`features.geometryTypes.${geometryType}`)}
        </Descriptions.Item>
        {feature.description && (
          <Descriptions.Item label={t('features.description')}>{feature.description}</Descriptions.Item>
        )}
        {linkedCave && (
          <Descriptions.Item label={t('features.linkedCave')}>
            <Link to={`/caves/${linkedCave.id}`}>{linkedCave.name}</Link>
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
        <Button
          icon={<AimOutlined />}
          size="small"
          onClick={() => fitGeoJsonGeometry(feature.geometry)}
        >
          {t('map.zoomTo')}
        </Button>
        {canEdit && (
          <>
            <Button icon={<EditOutlined />} size="small" onClick={() => setEditing(true)}>
              {t('features.edit')}
            </Button>
            <Popconfirm
              title={t('features.deleteConfirm')}
              onConfirm={() => void onDelete()}
              okButtonProps={{ danger: true, loading: deleteFeature.isPending }}
            >
              <Button icon={<DeleteOutlined />} size="small" danger>
                {t('features.delete')}
              </Button>
            </Popconfirm>
          </>
        )}
      </Flex>
      <FeatureEditModal
        open={editing}
        title={t('features.editFeature')}
        geometryType={geometryType}
        initial={{
          name: feature.name,
          featureTypeId: feature.featureTypeId,
          description: feature.description,
          visibility: feature.visibility,
          caveId: feature.caveId,
          properties: (feature.properties ?? {}) as Record<string, unknown>,
        }}
        busy={updateFeature.isPending}
        onCancel={() => setEditing(false)}
        onSubmit={(values) => void onEditSubmit(values)}
      />
    </div>
  );
}
