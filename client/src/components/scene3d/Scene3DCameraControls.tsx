// SPDX-License-Identifier: AGPL-3.0-or-later
import { BorderOuterOutlined, ExpandOutlined } from '@ant-design/icons';
import { Button, Tooltip } from 'antd';
import { useTranslation } from 'react-i18next';
import { CAMERA_3D_PRESETS, type Camera3DPreset } from '../../scene3d/presets3d.ts';
import type { Scene3DProjection } from '../../scene3d/scene3dEngine.ts';
import './Scene3DCameraControls.css';

export interface Scene3DCameraControlsProps {
  /** Which preset the camera is currently sitting at, or undefined when the viewer has moved it. */
  activePreset: Camera3DPreset | undefined;
  onPreset(preset: Camera3DPreset): void;
  projection: Scene3DProjection;
  onProjectionChange(projection: Scene3DProjection): void;
  /** Frames the cave the view is centred on. Disabled when no survey is drawn to frame. */
  onFitCave(): void;
  fitDisabled: boolean;
}

/**
 * The one-press camera positions, over the scene.
 *
 * Controlled throughout: which preset is highlighted is read from the camera by whoever mounts
 * this, not remembered here. That is the whole behaviour of a preset — it places the camera and
 * lets go, so the highlight goes out the moment the viewer drags, and pressing the same button
 * again is a fresh instruction rather than a toggle.
 */
export default function Scene3DCameraControls({
  activePreset,
  onPreset,
  projection,
  onProjectionChange,
  onFitCave,
  fitDisabled,
}: Scene3DCameraControlsProps) {
  const { t } = useTranslation();
  const orthographic = projection === 'orthographic';

  return (
    <div className="scene3d-camera-controls" data-testid="scene3d-camera-controls">
      {CAMERA_3D_PRESETS.map((preset) => (
        <Tooltip key={preset} title={t(`scene3d.preset.${preset}`)} placement="left">
          <Button
            size="small"
            type={activePreset === preset ? 'primary' : 'default'}
            aria-label={t(`scene3d.preset.${preset}`)}
            aria-pressed={activePreset === preset}
            onClick={() => onPreset(preset)}
            data-testid={`scene3d-preset-${preset}`}
          >
            {/* The letter on the button is translated like everything else a reader reads. A
                compass point is not the same letter in every language — west is V in Romanian —
                and the plan view is not a compass point at all, so it gets a symbol. */}
            {t(`scene3d.presetGlyph.${preset}`)}
          </Button>
        </Tooltip>
      ))}
      <Tooltip title={t('scene3d.fitCave')} placement="left">
        <Button
          size="small"
          icon={<ExpandOutlined />}
          disabled={fitDisabled}
          aria-label={t('scene3d.fitCave')}
          onClick={onFitCave}
          data-testid="scene3d-fit-cave"
        />
      </Tooltip>
      <Tooltip
        title={orthographic ? t('scene3d.projectionPerspectiveHint') : t('scene3d.projectionOrthographicHint')}
        placement="left"
      >
        <Button
          size="small"
          type={orthographic ? 'primary' : 'default'}
          icon={<BorderOuterOutlined />}
          aria-label={t('scene3d.projectionOrthographic')}
          aria-pressed={orthographic}
          onClick={() => onProjectionChange(orthographic ? 'perspective' : 'orthographic')}
          data-testid="scene3d-projection-toggle"
        />
      </Tooltip>
    </div>
  );
}
