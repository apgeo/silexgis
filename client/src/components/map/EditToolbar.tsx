// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useState } from 'react';
import MeasureButton from '@terrestris/react-geo/dist/Button/MeasureButton/MeasureButton';
import {
  AimOutlined,
  BorderOutlined,
  CheckOutlined,
  CloseOutlined,
  ColumnWidthOutlined,
  DragOutlined,
  EditOutlined,
  EnvironmentOutlined,
  LoginOutlined,
  RedoOutlined,
  UndoOutlined,
} from '@ant-design/icons';
import { App, Badge, Button, Divider, Space, Tooltip } from 'antd';
import type Feature from 'ol/Feature';
import { useTranslation } from 'react-i18next';
import {
  createSurfaceFeature,
  fetchSurfaceFeature,
  updateSurfaceFeature,
  useFeatureTypes,
} from '../../api/hooks.ts';
import { reloadSurfaceFeatures } from '../../map/featureLayer.ts';
import {
  MapEditController,
  type DrawShape,
  type EditMode,
  type EditState,
  type PlacementMode,
} from '../../map/mapEdit.ts';
import { useUiPrefsStore } from '../../stores/uiPrefsStore.ts';
import FeatureEditModal, { type FeatureAttributeValues } from '../features/FeatureEditModal.tsx';
import CaveAddModal from './CaveAddModal.tsx';
import FeaturePalette, { FeatureSymbol } from './FeaturePalette.tsx';

interface EditToolbarProps {
  controller: MapEditController;
}

const shapeForKind: Record<string, DrawShape | null> = {
  point: 'Point',
  line: 'LineString',
  polygon: 'Polygon',
  any: null, // user picks the shape explicitly
};

/** Pinned shortcuts beyond this many stay reachable through the palette only. */
const MAX_PINNED_BUTTONS = 8;

/** Dispatches edit intents to the MapEditController; owns no OL objects itself. */
export default function EditToolbar({ controller }: EditToolbarProps) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const { data: featureTypes } = useFeatureTypes();
  const [state, setState] = useState<EditState>({
    mode: 'none', snap: true, canUndo: false, canRedo: false, dirty: 0,
  });
  const [typeId, setTypeId] = useState<number>();
  const [saving, setSaving] = useState(false);
  // Measuring lives outside the edit controller (react-geo owns those
  // interactions); arming either side disarms the other.
  const [measure, setMeasure] = useState<'line' | 'polygon' | null>(null);
  // Freshly drawn feature awaiting attributes; the modal stashes them on the
  // OL feature (pendingAttrs) — nothing hits the server until Save.
  const [pendingFeature, setPendingFeature] = useState<Feature | null>(null);
  // A landed cave/entrance placement click awaiting its create dialog.
  const [placement, setPlacement] = useState<{ mode: PlacementMode; lonLat: [number, number] } | null>(null);

  useEffect(() => controller.subscribe(setState), [controller]);

  useEffect(() => {
    controller.onDrawEnd = (feature) => setPendingFeature(feature);
    controller.onPointPlaced = (mode, lonLat) => setPlacement({ mode, lonLat });
    return () => {
      controller.onDrawEnd = undefined;
      controller.onPointPlaced = undefined;
    };
  }, [controller]);

  const pinnedTypeIds = useUiPrefsStore((s) => s.pinnedTypeIds);
  const pinnedTypes = pinnedTypeIds
    .map((id) => featureTypes?.find((ft) => Number(ft.id) === id))
    .filter((ft) => ft !== undefined)
    .slice(0, MAX_PINNED_BUTTONS);

  const selectedType = featureTypes?.find((ft) => Number(ft.id) === typeId);
  const kind = (selectedType?.geometryKind ?? 'point').toString().toLowerCase();
  const fixedShape = shapeForKind[kind] ?? 'Point';

  // Picking a symbol (palette or pinned shortcut) arms drawing immediately
  // (reference-software behavior), with the shape implied by the geometry kind.
  const armType = (id: number) => {
    setTypeId(id);
    setMeasure(null);
    const picked = featureTypes?.find((ft) => Number(ft.id) === id);
    const pickedKind = (picked?.geometryKind ?? 'point').toString().toLowerCase();
    controller.setMode('draw', shapeForKind[pickedKind] ?? 'Point', id);
  };

  const setMode = (mode: EditMode, shape?: DrawShape) => {
    setMeasure(null);
    controller.setMode(state.mode === mode && mode !== 'draw' ? 'none' : mode, shape, typeId);
  };

  const toggleMeasure = (type: 'line' | 'polygon') => {
    setMeasure((current) => {
      const next = current === type ? null : type;
      if (next) {
        controller.setMode('none');
      }
      return next;
    });
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
      <FeaturePalette featureTypes={featureTypes ?? []} value={typeId} onChange={armType} />
      {pinnedTypes.map((ft) => {
        const id = Number(ft.id);
        return (
          <Tooltip key={id} title={ft.name}>
            <Button
              size="small"
              type={state.mode === 'draw' && typeId === id ? 'primary' : 'default'}
              icon={<FeatureSymbol type={ft} size={16} />}
              aria-label={ft.name}
              data-testid={`pinned-type-${id}`}
              onClick={() => armType(id)}
            />
          </Tooltip>
        );
      })}
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
      <Tooltip title={t('mapEdit.newCaveHere')}>
        <Button
          size="small"
          type={state.mode === 'add-cave' ? 'primary' : 'default'}
          icon={<EnvironmentOutlined />}
          onClick={() => setMode('add-cave')}
          data-testid="tool-add-cave"
        />
      </Tooltip>
      <Tooltip title={t('mapEdit.newEntranceHere')}>
        <Button
          size="small"
          type={state.mode === 'add-entrance' ? 'primary' : 'default'}
          icon={<LoginOutlined />}
          onClick={() => setMode('add-entrance')}
          data-testid="tool-add-entrance"
        />
      </Tooltip>
      <Divider orientation="vertical" />
      <Tooltip title={t('mapEdit.undo')}>
        <Button size="small" icon={<UndoOutlined />} disabled={!state.canUndo} onClick={() => controller.undo()} />
      </Tooltip>
      <Tooltip title={t('mapEdit.redo')}>
        <Button size="small" icon={<RedoOutlined />} disabled={!state.canRedo} onClick={() => controller.redo()} />
      </Tooltip>
      <Divider orientation="vertical" />
      <MeasureButton
        size="small"
        measureType="line"
        pressed={measure === 'line'}
        onChange={() => toggleMeasure('line')}
        tooltip={t('mapEdit.measureDistance')}
        icon={<ColumnWidthOutlined />}
        pressedIcon={<ColumnWidthOutlined />}
        showSegmentLengths
        clickToDrawText={t('mapEdit.clickToMeasure')}
        continueLineMsg={t('mapEdit.continueLine')}
      />
      <MeasureButton
        size="small"
        measureType="polygon"
        pressed={measure === 'polygon'}
        onChange={() => toggleMeasure('polygon')}
        tooltip={t('mapEdit.measureArea')}
        icon={<BorderOutlined />}
        pressedIcon={<BorderOutlined />}
        clickToDrawText={t('mapEdit.clickToMeasure')}
        continuePolygonMsg={t('mapEdit.continueArea')}
      />
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
          disabled={state.dirty === 0}
          onClick={discard}
        />
      </Tooltip>
      <CaveAddModal
        mode={placement?.mode ?? null}
        lonLat={placement?.lonLat ?? null}
        onClose={() => setPlacement(null)}
      />
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
