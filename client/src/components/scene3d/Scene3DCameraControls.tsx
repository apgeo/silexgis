// SPDX-License-Identifier: AGPL-3.0-or-later
import type { RefObject } from 'react';
import {
  BorderOuterOutlined,
  DisconnectOutlined,
  ExpandOutlined,
  LinkOutlined,
  VideoCameraOutlined,
} from '@ant-design/icons';
import { Button, Popover, Tooltip } from 'antd';
import { useTranslation } from 'react-i18next';
import { useCoarsePointer } from '../../hooks/useCoarsePointer.ts';
import { useIsMobile } from '../../hooks/useIsMobile.ts';
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
  /**
   * Whether this scene's camera moves with the flat map, and the map with it. Controlled like
   * everything else here — the switch belongs to the workspace, not to this strip, because the
   * same scene is mounted in three places and they share one answer.
   */
  coupled: boolean;
  onCoupledChange(coupled: boolean): void;
  /** Handed out so whoever mounts this can measure how much of the view it stands on. */
  containerRef?: RefObject<HTMLDivElement | null>;
}

/**
 * The one-press camera positions, over the scene.
 *
 * Controlled throughout: which preset is highlighted is read from the camera by whoever mounts
 * this, not remembered here. That is the whole behaviour of a preset — it places the camera and
 * lets go, so the highlight goes out the moment the viewer drags, and pressing the same button
 * again is a fresh instruction rather than a toggle.
 *
 * The same seven controls are folded behind one button for a narrow screen or for a finger, and
 * the two are separate reasons rather than one restated. Seven small buttons down the right edge
 * of a four-hundred-pixel screen take a quarter of its height and stand over the scene they are
 * for — that is the width. And each of them is a single letter or glyph explained by a tooltip
 * that only opens on hover, so on any device without a hovering pointer the strip is seven
 * unlabelled buttons at a size a finger cannot reliably hit — that is the pointer, and it is true
 * of a phone turned sideways and of a tablet, both of which are wide. Folding on width alone would
 * hand exactly those viewers the layout the strip was folded to spare them. In the panel the same
 * controls are captioned in words and are finger-sized.
 */
export default function Scene3DCameraControls({
  activePreset,
  onPreset,
  projection,
  onProjectionChange,
  onFitCave,
  fitDisabled,
  coupled,
  onCoupledChange,
  containerRef,
}: Scene3DCameraControlsProps) {
  const { t } = useTranslation();
  // Both asked, always, and combined afterwards: a short-circuit here would skip a hook on the
  // renders where the first answer is already true, and the order hooks are called in has to be
  // the same every time.
  const narrow = useIsMobile();
  const coarse = useCoarsePointer();
  const folded = narrow || coarse;
  const orthographic = projection === 'orthographic';
  const projectionLabel = orthographic
    ? t('scene3d.projectionPerspectiveHint')
    : t('scene3d.projectionOrthographicHint');
  // The hint says what pressing it would DO, like the projection toggle beside it, rather than
  // naming the state it is already in — which the highlight and aria-pressed already say.
  const couplingLabel = coupled ? t('scene3d.uncoupleFromMapHint') : t('scene3d.coupleToMapHint');

  if (folded) {
    return (
      <div ref={containerRef} className="scene3d-camera-controls scene3d-camera-controls-compact">
        <Popover
          trigger="click"
          placement="bottomRight"
          title={t('scene3d.cameraTitle')}
          content={
            <div
              className="scene3d-camera-menu"
              data-testid="scene3d-camera-controls"
              role="group"
              aria-label={t('scene3d.cameraTitle')}
            >
              {CAMERA_3D_PRESETS.map((preset) => (
                <Button
                  key={preset}
                  block
                  type={activePreset === preset ? 'primary' : 'default'}
                  aria-pressed={activePreset === preset}
                  onClick={() => onPreset(preset)}
                  data-testid={`scene3d-preset-${preset}`}
                >
                  {t(`scene3d.preset.${preset}`)}
                </Button>
              ))}
              <Button
                block
                icon={<ExpandOutlined />}
                disabled={fitDisabled}
                onClick={onFitCave}
                data-testid="scene3d-fit-cave"
              >
                {t('scene3d.fitCave')}
              </Button>
              <Button
                block
                type={orthographic ? 'primary' : 'default'}
                icon={<BorderOuterOutlined />}
                aria-pressed={orthographic}
                onClick={() => onProjectionChange(orthographic ? 'perspective' : 'orthographic')}
                data-testid="scene3d-projection-toggle"
              >
                {projectionLabel}
              </Button>
              <Button
                block
                type={coupled ? 'primary' : 'default'}
                icon={coupled ? <LinkOutlined /> : <DisconnectOutlined />}
                aria-pressed={coupled}
                onClick={() => onCoupledChange(!coupled)}
                data-testid="scene3d-coupling-toggle"
              >
                {couplingLabel}
              </Button>
            </div>
          }
        >
          <Button
            icon={<VideoCameraOutlined />}
            aria-label={t('scene3d.cameraTitle')}
            data-testid="scene3d-camera-trigger"
          />
        </Popover>
      </div>
    );
  }

  return (
    <div ref={containerRef} className="scene3d-camera-controls" data-testid="scene3d-camera-controls">
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
      <Tooltip title={projectionLabel} placement="left">
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
      <Tooltip title={couplingLabel} placement="left">
        <Button
          size="small"
          type={coupled ? 'primary' : 'default'}
          icon={coupled ? <LinkOutlined /> : <DisconnectOutlined />}
          aria-label={t('scene3d.coupleToMap')}
          aria-pressed={coupled}
          onClick={() => onCoupledChange(!coupled)}
          data-testid="scene3d-coupling-toggle"
        />
      </Tooltip>
    </div>
  );
}
