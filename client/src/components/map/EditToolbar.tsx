// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useState } from 'react';
import {
  AimOutlined,
  BorderOutlined,
  CheckOutlined,
  CloseOutlined,
  ColumnWidthOutlined,
  DragOutlined,
  EditOutlined,
  RedoOutlined,
  UndoOutlined,
} from '@ant-design/icons';
import { App, Badge, Button, Divider, Select, Space, Tooltip, Typography } from 'antd';
import type Feature from 'ol/Feature';
import { useTranslation } from 'react-i18next';
import {
  createSurfaceFeature,
  fetchSurfaceFeature,
  updateSurfaceFeature,
  useFeatureTypes,
} from '../../api/hooks.ts';
import { reloadSurfaceFeatures } from '../../map/featureLayer.ts';
import { MapEditController, type DrawShape, type EditMode, type EditState } from '../../map/mapEdit.ts';
import FeatureEditModal, { type FeatureAttributeValues } from '../features/FeatureEditModal.tsx';

interface EditToolbarProps {
  controller: MapEditController;
}

const shapeForKind: Record<string, DrawShape | null> = {
  point: 'Point',
  line: 'LineString',
  polygon: 'Polygon',
  any: null, // user picks the shape explicitly
};

/** Dispatches edit intents to the MapEditController; owns no OL objects itself. */
export default function EditToolbar({ controller }: EditToolbarProps) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const { data: featureTypes } = useFeatureTypes();
  const [state, setState] = useState<EditState>({
    mode: 'none', snap: true, canUndo: false, canRedo: false, dirty: 0, measureResult: null,
  });
  const [typeId, setTypeId] = useState<number>();
  const [saving, setSaving] = useState(false);
  // Freshly drawn feature awaiting attributes; the modal stashes them on the
  // OL feature (pendingAttrs) — nothing hits the server until Save.
  const [pendingFeature, setPendingFeature] = useState<Feature | null>(null);

  useEffect(() => controller.subscribe(setState), [controller]);

  useEffect(() => {
    controller.onDrawEnd = (feature) => setPendingFeature(feature);
    return () => {
      controller.onDrawEnd = undefined;
    };
  }, [controller]);

  const selectedType = featureTypes?.find((ft) => Number(ft.id) === typeId);
  const kind = (selectedType?.geometryKind ?? 'point').toString().toLowerCase();
  const fixedShape = shapeForKind[kind] ?? 'Point';

  const setMode = (mode: EditMode, shape?: DrawShape) => {
    controller.setMode(state.mode === mode && mode !== 'draw' ? 'none' : mode, shape, typeId);
  };

  const save = async () => {
    setSaving(true);
    try {
      const { created, modified } = controller.getPendingEdits();
      for (const { feature } of created) {
        const attrs = feature.get('pendingAttrs') as FeatureAttributeValues | undefined;
        await createSurfaceFeature({
          name: attrs?.name ?? null,
          featureTypeId: attrs?.featureTypeId ?? Number(feature.get('featureTypeId') ?? typeId ?? 0),
          geometry: MapEditController.toGeoJsonGeometry(feature.getGeometry()!) as never,
          description: attrs?.description ?? null,
          properties: (attrs?.properties ?? null) as never,
          caveId: attrs?.caveId ?? null,
          teamId: null,
          visibility: attrs?.visibility ?? 'private',
        });
      }
      for (const [id, geometry] of modified) {
        const current = await fetchSurfaceFeature(id);
        await updateSurfaceFeature(id, {
          name: current.name,
          featureTypeId: current.featureTypeId,
          geometry: geometry as never,
          description: current.description,
          properties: current.properties,
          caveId: current.caveId,
          teamId: current.teamId,
          visibility: current.visibility,
        });
      }
      controller.reset();
      reloadSurfaceFeatures();
      message.success(t('common.saved'));
    } catch {
      message.error(t('common.saveFailed'));
    } finally {
      setSaving(false);
    }
  };

  const discard = () => {
    controller.reset();
    reloadSurfaceFeatures();
  };

  const pendingGeometryType = (pendingFeature?.getGeometry()?.getType() ?? 'Point') as DrawShape;

  return (
    <Space size={4} wrap>
      <Select
        size="small"
        style={{ width: 170 }}
        placeholder={t('mapEdit.featureType')}
        value={typeId}
        onChange={setTypeId}
        options={featureTypes?.map((ft) => ({ value: Number(ft.id), label: ft.name }))}
      />
      <Tooltip title={t('mapEdit.draw')}>
        <Button
          size="small"
          type={state.mode === 'draw' ? 'primary' : 'default'}
          icon={<EditOutlined />}
          disabled={typeId === undefined}
          onClick={() => setMode('draw', fixedShape)}
        />
      </Tooltip>
      <Tooltip title={t('mapEdit.modify')}>
        <Button
          size="small"
          type={state.mode === 'modify' ? 'primary' : 'default'}
          icon={<AimOutlined />}
          onClick={() => setMode('modify')}
        />
      </Tooltip>
      <Tooltip title={t('mapEdit.translate')}>
        <Button
          size="small"
          type={state.mode === 'translate' ? 'primary' : 'default'}
          icon={<DragOutlined />}
          onClick={() => setMode('translate')}
        />
      </Tooltip>
      <Tooltip title={t('mapEdit.snap')}>
        <Button
          size="small"
          type={state.snap ? 'primary' : 'default'}
          ghost={state.snap}
          onClick={() => controller.toggleSnap()}
        >
          {t('mapEdit.snapShort')}
        </Button>
      </Tooltip>
      <Divider orientation="vertical" />
      <Tooltip title={t('mapEdit.undo')}>
        <Button size="small" icon={<UndoOutlined />} disabled={!state.canUndo} onClick={() => controller.undo()} />
      </Tooltip>
      <Tooltip title={t('mapEdit.redo')}>
        <Button size="small" icon={<RedoOutlined />} disabled={!state.canRedo} onClick={() => controller.redo()} />
      </Tooltip>
      <Divider orientation="vertical" />
      <Tooltip title={t('mapEdit.measureDistance')}>
        <Button
          size="small"
          type={state.mode === 'measure-distance' ? 'primary' : 'default'}
          icon={<ColumnWidthOutlined />}
          onClick={() => setMode('measure-distance')}
        />
      </Tooltip>
      <Tooltip title={t('mapEdit.measureArea')}>
        <Button
          size="small"
          type={state.mode === 'measure-area' ? 'primary' : 'default'}
          icon={<BorderOutlined />}
          onClick={() => setMode('measure-area')}
        />
      </Tooltip>
      {state.measureResult && (
        <Typography.Text
          copyable={{ text: state.measureResult }}
          style={{ fontSize: 12 }}
        >
          {state.measureResult}
        </Typography.Text>
      )}
      <Divider orientation="vertical" />
      <Badge count={state.dirty} size="small">
        <Button
          size="small"
          type="primary"
          icon={<CheckOutlined />}
          disabled={state.dirty === 0}
          loading={saving}
          onClick={() => void save()}
        >
          {t('common.save')}
        </Button>
      </Badge>
      <Tooltip title={t('mapEdit.discard')}>
        <Button
          size="small"
          icon={<CloseOutlined />}
          disabled={state.dirty === 0 && !state.measureResult}
          onClick={discard}
        />
      </Tooltip>
      <FeatureEditModal
        open={pendingFeature !== null}
        title={t('features.newFeature')}
        geometryType={pendingGeometryType}
        initial={{
          featureTypeId: Number(pendingFeature?.get('featureTypeId') ?? typeId),
          visibility: 'private',
        }}
        onCancel={() => setPendingFeature(null)}
        onSubmit={(values) => {
          // Stash on the OL feature; the type also drives the map symbol.
          pendingFeature?.set('pendingAttrs', values);
          pendingFeature?.set('featureTypeId', values.featureTypeId);
          setPendingFeature(null);
        }}
      />
    </Space>
  );
}
