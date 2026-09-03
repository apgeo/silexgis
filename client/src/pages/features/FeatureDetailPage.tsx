// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo, useState } from 'react';
import {
  AimOutlined,
  DeleteOutlined,
  EditOutlined,
  LinkOutlined,
  LockOutlined,
  ShareAltOutlined,
} from '@ant-design/icons';
import { Alert, App, Breadcrumb, Button, Card, Descriptions, Flex, Popconfirm, Spin, Tag, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate, useParams } from 'react-router-dom';
import {
  parseAccessActions,
  useCan,
  useCaveTypes,
  useDeleteFeature,
  useEffectiveAccess,
  useEntranceTypes,
  useFeature,
  useFeatureTypes,
  useUpdateFeature,
} from '../../api/hooks.ts';
import AttachmentSection from '../../components/attachments/AttachmentSection.tsx';
import FeatureEditModal, { type FeatureAttributeValues } from '../../components/features/FeatureEditModal.tsx';
import AreaHypsometryCard from '../../components/features/AreaHypsometryCard.tsx';
import AreaStructureCard from '../../components/features/AreaStructureCard.tsx';
import FeatureMorphometryCard from '../../components/features/FeatureMorphometryCard.tsx';
import { parsePropertiesSchema } from '../../components/typedProperties/propertiesSchema.ts';
import HistoryPanel from '../../components/history/HistoryPanel.tsx';
import PermissionsModal from '../../components/permissions/PermissionsModal.tsx';
import LinksSection from '../../components/reslinks/LinksSection.tsx';
import ShareLinksModal from '../../components/shares/ShareLinksModal.tsx';
import QrCodeSquare from '../../components/qr/QrCodeSquare.tsx';
import { printedCode } from '../../components/qr/printedCode.ts';
import TagChips from '../../components/tags/TagChips.tsx';
import { fitGeoJsonGeometry } from '../../map/mapContext.ts';
import { useWorkspaceStore } from '../../stores/workspaceStore.ts';
import { surfaceFeaturesChanged } from '../../workspace/surfaceFeatureRefresh.ts';
import HierarchyCard from './HierarchyCard.tsx';
import LinksCard from './LinksCard.tsx';

type DrawShape = 'Point' | 'LineString' | 'Polygon';

/** The base shape a GeoJSON geometry type edits as; multi-variants edit as their base. */
const drawShapeOf: Record<string, DrawShape> = {
  Point: 'Point',
  MultiPoint: 'Point',
  LineString: 'LineString',
  MultiLineString: 'LineString',
  Polygon: 'Polygon',
  MultiPolygon: 'Polygon',
};

export default function FeatureDetailPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { message } = App.useApp();
  const { id } = useParams<{ id: string }>();

  const { data: envelope, isPending, isError } = useFeature(id);
  const { data: featureTypes } = useFeatureTypes();
  const { data: caveTypes } = useCaveTypes();
  const { data: entranceTypes } = useEntranceTypes();
  const updateFeature = useUpdateFeature();
  const deleteFeature = useDeleteFeature();
  const setSelection = useWorkspaceStore((s) => s.setSelection);

  const [editOpen, setEditOpen] = useState(false);
  const [permissionsOpen, setPermissionsOpen] = useState(false);
  const [shareOpen, setShareOpen] = useState(false);

  // Per-object capabilities once the answer arrives; the coarse domain-level check only
  // bridges the first render (the server enforces regardless).
  const { data: effective } = useEffectiveAccess('feature', id);
  const domainFallback = useCan('features', 'write');
  const held = effective ? parseAccessActions(effective.actions) : null;
  const canEdit = held ? held.has('write') : domainFallback;
  const canDelete = held ? held.has('delete') : domainFallback;
  const canShare = held ? held.has('share') : domainFallback;
  const canManagePermissions = held ? held.has('managePermissions') : domainFallback;

  const feature = envelope?.feature;
  const featureType = feature
    ? featureTypes?.find((x) => x.code === feature.featureTypeCode)
    : undefined;

  // Property labels come from the type's schema; keys the schema does not know
  // about still render, under their raw key.
  const propertyLabels = useMemo(() => {
    const labels = new Map<string, string>();
    for (const field of parsePropertiesSchema(featureType?.propertiesSchema)) {
      labels.set(field.key, field.label);
    }
    return labels;
  }, [featureType]);

  if (isError) {
    return (
      <div style={{ padding: 24 }}>
        <Alert type="error" showIcon title={t('common.loadFailed')} />
      </div>
    );
  }

  if (isPending || !envelope || !feature || !id) {
    return (
      <Flex align="center" justify="center" style={{ height: '100%' }}>
        <Spin size="large" />
      </Flex>
    );
  }

  const displayName = feature.name ?? t('features.unnamed');
  const properties =
    feature.properties && typeof feature.properties === 'object' && !Array.isArray(feature.properties)
      ? Object.entries(feature.properties as Record<string, unknown>)
      : [];
  // The codes the caving app prints live on the places inside a cave, not on the cave, so this
  // is the surface where a stored one actually exists. Whether it resolves for a visitor is a
  // decision taken about the cave above it; the square only shows what is on the label.
  const labelCode = printedCode(feature.properties);

  // Typed subtypes have their own full page; this page shows a compact summary
  // plus the shared hierarchy/links panels and points at the typed page.
  const typedPagePath =
    envelope.kind === 'cave'
      ? `/caves/${feature.id}`
      : envelope.kind === 'caveEntrance' && envelope.entrance
        ? `/caves/${envelope.entrance.caveFeatureId}`
        : envelope.kind === 'centerline' && envelope.centerline
          ? `/caves/${envelope.centerline.caveFeatureId}`
          : null;

  const showOnMap = () => {
    if (!feature.geometry) {
      return;
    }
    setSelection({ kind: 'feature', featureId: feature.id });
    fitGeoJsonGeometry(feature.geometry);
    navigate('/map');
  };

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
          locationProtected: values.locationProtected,
          cavingGroupId: feature.cavingGroupId,
          visibility: values.visibility,
        },
      });
      surfaceFeaturesChanged();
      setEditOpen(false);
      message.success(t('common.saved'));
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  const onDelete = async () => {
    try {
      await deleteFeature.mutateAsync(feature.id);
      surfaceFeaturesChanged();
      message.success(t('common.deleted'));
      navigate('/features');
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  const detailItem = (label: string, value: unknown) =>
    value == null || value === '' ? null : (
      <Descriptions.Item key={label} label={label}>
        {String(value)}
      </Descriptions.Item>
    );

  return (
    <div style={{ padding: 24, maxWidth: 1100 }}>
      {/* Primary-parent chain, outermost ancestor first, truncated server-side at
          the first ancestor the caller may not read. */}
      <Breadcrumb
        style={{ marginBottom: 8 }}
        items={[
          { title: <Link to="/features">{t('features.title')}</Link> },
          ...feature.parents.map((p) => ({
            title: <Link to={`/features/${p.id}`}>{p.name ?? t('features.unnamed')}</Link>,
          })),
          { title: displayName },
        ]}
      />

      <Flex justify="space-between" align="center" wrap gap={8} style={{ marginBottom: 12 }}>
        <Flex align="center" gap={12}>
          <Typography.Title level={3} style={{ margin: 0 }}>
            {displayName}
          </Typography.Title>
          <Tag>{t(`features.kinds.${envelope.kind}`)}</Tag>
        </Flex>
        <Flex gap={8} wrap>
          <Button icon={<AimOutlined />} disabled={!feature.geometry} onClick={showOnMap}>
            {t('features.showOnMap')}
          </Button>
          {typedPagePath && (
            <Button type="primary" icon={<LinkOutlined />} onClick={() => navigate(typedPagePath)}>
              {t('features.openTypedPage')}
            </Button>
          )}
          {envelope.kind === 'generic' && (
            <>
              {canShare && (
                <Button icon={<ShareAltOutlined />} onClick={() => setShareOpen(true)}>
                  {t('shares.button')}
                </Button>
              )}
              {canManagePermissions && (
                <Button icon={<LockOutlined />} onClick={() => setPermissionsOpen(true)}>
                  {t('permissions.button')}
                </Button>
              )}
              {canEdit && (
                <Button icon={<EditOutlined />} onClick={() => setEditOpen(true)}>
                  {t('features.edit')}
                </Button>
              )}
              {canDelete && (
                <Popconfirm
                  title={t('features.deleteConfirm')}
                  onConfirm={() => void onDelete()}
                  okButtonProps={{ danger: true }}
                >
                  <Button danger icon={<DeleteOutlined />}>
                    {t('features.delete')}
                  </Button>
                </Popconfirm>
              )}
            </>
          )}
        </Flex>
      </Flex>

      {feature.approximateLocation && (
        <Alert type="warning" showIcon title={t('map.approximate')} style={{ marginBottom: 12 }} />
      )}
      {feature.omittedLocation && (
        <Alert type="info" showIcon title={t('features.locationWithheld')} style={{ marginBottom: 12 }} />
      )}

      <Card style={{ marginBottom: 16 }}>
        <Descriptions column={{ xs: 1, sm: 2, md: 3 }} size="small">
          {detailItem(t('features.type'), featureType?.name ?? feature.featureTypeCode)}
          <Descriptions.Item label={t('features.category')}>
            {t(`features.categories.${feature.category}`)}
          </Descriptions.Item>
          {feature.geometry &&
            detailItem(t('features.geometry'), t(`features.geometryTypes.${feature.geometry.type}`))}
          <Descriptions.Item label={t('features.visibility')}>
            <Tag>{t(`caves.visibilityValues.${feature.visibility}`)}</Tag>
          </Descriptions.Item>
          {feature.locationProtected && (
            <Descriptions.Item label={t('features.locationProtected')}>
              <Tag color="orange">✓</Tag>
            </Descriptions.Item>
          )}
        </Descriptions>
        {feature.description && (
          <Typography.Paragraph style={{ marginTop: 12, marginBottom: 0 }}>
            {feature.description}
          </Typography.Paragraph>
        )}
      </Card>

      {envelope.kind === 'cave' && envelope.cave && (
        <Card title={t('features.summary')} style={{ marginBottom: 16 }}>
          <Descriptions column={{ xs: 1, sm: 2, md: 3 }} size="small">
            {detailItem(t('caves.type'), caveTypes?.find((x) => x.id === envelope.cave?.caveTypeId)?.name)}
            {detailItem(t('caves.region'), envelope.cave.region)}
            {detailItem(t('caves.surveyedLength'), envelope.cave.surveyedLength)}
            {detailItem(t('caves.depth'), envelope.cave.depth)}
            {detailItem(t('caves.fields.altitude'), envelope.cave.altitude)}
            {detailItem(t('features.entranceCount'), envelope.cave.entranceCount)}
          </Descriptions>
        </Card>
      )}

      {envelope.kind === 'caveEntrance' && envelope.entrance && (
        <Card title={t('features.summary')} style={{ marginBottom: 16 }}>
          <Descriptions column={{ xs: 1, sm: 2, md: 3 }} size="small">
            {detailItem(
              t('caves.type'),
              entranceTypes?.find((x) => x.id === envelope.entrance?.entranceTypeId)?.name,
            )}
            {detailItem(t('caves.fields.altitude'), envelope.entrance.altitude)}
            <Descriptions.Item label={t('entrances.main')}>
              {envelope.entrance.isMain ? <Tag color="green">✓</Tag> : '—'}
            </Descriptions.Item>
            {detailItem(
              t('entrances.positionQuality'),
              t(`entrances.qualityValues.${envelope.entrance.positionQuality}`),
            )}
          </Descriptions>
        </Card>
      )}

      {envelope.kind === 'centerline' && envelope.centerline && (
        <Card title={t('features.summary')} style={{ marginBottom: 16 }}>
          <Descriptions column={{ xs: 1, sm: 2, md: 3 }} size="small">
            {detailItem(t('centerlines.length'), envelope.centerline.lengthM)}
            {detailItem(
              t('centerlines.source'),
              t(`centerlines.sourceValues.${envelope.centerline.source}`),
            )}
            <Descriptions.Item label={t('features.isDefault')}>
              {envelope.centerline.isDefault ? <Tag color="green">✓</Tag> : '—'}
            </Descriptions.Item>
          </Descriptions>
        </Card>
      )}

      {envelope.kind === 'generic' && properties.length > 0 && (
        <Card title={t('features.typedProperties')} style={{ marginBottom: 16 }}>
          <Descriptions column={{ xs: 1, sm: 2, md: 3 }} size="small">
            {properties.map(([key, value]) =>
              detailItem(propertyLabels.get(key) ?? key, value),
            )}
          </Descriptions>
        </Card>
      )}

      <FeatureMorphometryCard featureId={id} geometryType={feature.geometry?.type ?? null} />

      {/* An area's two vertical and structural readings sit beside its measured shape: all three
          are asked of the same outline and answered over the same subtree. */}
      <AreaHypsometryCard featureId={id} geometryType={feature.geometry?.type ?? null} />
      <AreaStructureCard featureId={id} geometryType={feature.geometry?.type ?? null} />
      {labelCode && (
        <Card title={t('qr.cardTitle')} style={{ marginBottom: 16 }}>
          <QrCodeSquare code={labelCode} />
        </Card>
      )}

      <HierarchyCard featureId={id} canEdit={canEdit} />
      <LinksCard featureId={id} canEdit={canEdit} />
      <LinksSection entityType="feature" entityId={id} canAdd entityTitle={displayName} />

      {envelope.kind === 'generic' && (
        <>
          <div style={{ marginTop: 12 }}>
            <TagChips entityType="feature" entityId={id} canEdit={canEdit} />
          </div>
          <AttachmentSection entityType="feature" entityId={id} canEdit={canEdit} />
          <HistoryPanel entityType="feature" entityId={id} />
          <PermissionsModal
            entityType="feature"
            entityId={id}
            open={permissionsOpen}
            onClose={() => setPermissionsOpen(false)}
          />
          <ShareLinksModal featureId={id} open={shareOpen} onClose={() => setShareOpen(false)} />
          <FeatureEditModal
            open={editOpen}
            title={t('features.editFeature')}
            geometryType={feature.geometry ? (drawShapeOf[feature.geometry.type] ?? null) : null}
            initial={{
              name: feature.name,
              featureTypeId: featureType ? Number(featureType.id) : undefined,
              description: feature.description,
              visibility: feature.visibility,
              locationProtected: feature.locationProtected,
              properties:
                feature.properties && typeof feature.properties === 'object'
                  ? (feature.properties as Record<string, unknown>)
                  : {},
            }}
            busy={updateFeature.isPending}
            onCancel={() => setEditOpen(false)}
            onSubmit={(values) => void onEditSubmit(values)}
          />
        </>
      )}
    </div>
  );
}
