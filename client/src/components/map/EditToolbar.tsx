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
import { useTranslation } from 'react-i18next';
import {
  createSurfaceFeature,
  fetchSurfaceFeature,
  updateSurfaceFeature,
  useFeatureTypes,
} from '../../api/hooks.ts';
import { reloadSurfaceFeatures } from '../../map/featureLayer.ts';
import { MapEditController, type DrawShape, type EditMode, type EditState } from '../../map/mapEdit.ts';

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

  useEffect(() => controller.subscribe(setState), [controller]);

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
        await createSurfaceFeature({
          name: null,
          featureTypeId: Number(feature.get('featureTypeId') ?? typeId ?? 0),
          geometry: MapEditController.toGeoJsonGeometry(feature.getGeometry()!) as never,
          description: null,
          properties: null,
          caveId: null,
          teamId: null,
          visibility: 'private',
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
      <Divider type="vertical" />
      <Tooltip title={t('mapEdit.undo')}>
        <Button size="small" icon={<UndoOutlined />} disabled={!state.canUndo} onClick={() => controller.undo()} />
      </Tooltip>
      <Tooltip title={t('mapEdit.redo')}>
        <Button size="small" icon={<RedoOutlined />} disabled={!state.canRedo} onClick={() => controller.redo()} />
      </Tooltip>
      <Divider type="vertical" />
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
      <Divider type="vertical" />
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
    </Space>
  );
}
