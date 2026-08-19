// SPDX-License-Identifier: AGPL-3.0-or-later
import { Checkbox, Divider, Radio, Slider, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import type { MapLayerInfo } from '../../api/hooks.ts';
import {
  CAVE_DATA_3D_LAYERS,
  CENTERLINE_SOURCE_ID,
  ENTRANCE_SOURCE_ID,
  type CaveData3DLayer,
} from '../../scene3d/caveData3d.ts';
import { CENTERLINE_DEPTH_BANDS } from '../../scene3d/centerlines3d.ts';
import type { Scene3DSurfaceMode, Scene3DSurfaceState } from '../../scene3d/scene3dEngine.ts';
import type { SurveyMesh3DState } from '../../scene3d/surveyMesh3d.ts';
import { cutawayPauseMessage } from './surfaceMessages.ts';
import './Scene3DLayerPanel.css';

// What a viewer can turn on, fade and cut into, in the same order and with the same words the
// flat map uses for the same things: basemap first, then the layers drawn on it. The third
// section has no counterpart there, because a flat map has no ground to be underneath.
//
// Controlled throughout — every value and every change handler is a prop, and the component holds
// no state of its own. The view above it owns the settings and is the only thing that talks to
// the scene, so there is exactly one path from a control to something on screen.

export interface Scene3DLayerPanelProps {
  layers: MapLayerInfo[];
  activeBaseId: number | undefined;
  onBaseChange: (id: number) => void;
  /** Per-base-layer opacity (0..1) keyed by catalog id; missing = fully opaque. */
  baseOpacity: Record<number, number>;
  onBaseOpacityChange: (id: number, opacity: number) => void;
  /** Keyed by layer; a missing key means shown. */
  overlayVisible: Record<string, boolean>;
  onOverlayVisibleChange: (layer: CaveData3DLayer, visible: boolean) => void;
  /** Keyed by layer; a missing key means fully opaque. */
  overlayOpacity: Record<string, number>;
  onOverlayOpacityChange: (layer: CaveData3DLayer, opacity: number) => void;
  /**
   * Whether the walls of the selected cave are drawn. Its own prop rather than another entry in
   * the overlay records above, because it is not one of the layers those are keyed by: the walls
   * belong to one cave rather than to the view, and turning them off frees graphics memory rather
   * than hiding a source that stays loaded.
   */
  meshVisible: boolean;
  onMeshVisibleChange: (visible: boolean) => void;
  /** What the scene is doing about the selected cave's walls, said in words under the switch. */
  meshState: SurveyMesh3DState;
  surfaceMode: Scene3DSurfaceMode;
  onSurfaceModeChange: (mode: Scene3DSurfaceMode) => void;
  /** What the scene is actually doing about the ground; undefined until it has started. */
  surfaceState: Scene3DSurfaceState | undefined;
}

export default function Scene3DLayerPanel({
  layers,
  activeBaseId,
  onBaseChange,
  baseOpacity,
  onBaseOpacityChange,
  overlayVisible,
  onOverlayVisibleChange,
  overlayOpacity,
  onOverlayOpacityChange,
  meshVisible,
  onMeshVisibleChange,
  meshState,
  surfaceMode,
  onSurfaceModeChange,
  surfaceState,
}: Scene3DLayerPanelProps) {
  const { t, i18n } = useTranslation();

  const overlayName = (layer: CaveData3DLayer) => {
    if (layer === CENTERLINE_SOURCE_ID) {
      return t('map.centerlines');
    }
    return layer === ENTRANCE_SOURCE_ID ? t('map.entrances') : t('map.surfaceFeatures');
  };

  const percent = (value: number | undefined) => Math.round((value ?? 1) * 100);
  const opacityTooltip = { formatter: (value?: number) => `${value ?? 0}%` };

  const cutawayAvailable = surfaceState?.cutawayAvailable ?? false;
  const surfaceHint = () => {
    if (surfaceMode === 'overlay') {
      return t('scene3d.surfaceOverlayHint');
    }
    if (!cutawayAvailable) {
      return t('scene3d.cutawayUnsupported');
    }
    if (!surfaceState?.hasFootprint) {
      return t('scene3d.cutawayNoCave');
    }
    return surfaceState.pausedBy
      ? t(cutawayPauseMessage(surfaceState.pausedBy))
      : t('scene3d.surfaceCutawayHint');
  };

  /**
   * What the walls of the selected cave are doing, in a sentence.
   *
   * A wall mesh is the one thing this scene draws that a viewer waits for, and the wait is worth
   * a number: the ordinary export is a few hundred kilobytes and arrives before the sentence is
   * read, while the case this was built for is fifty megabytes. The triangle count is the size the
   * server publishes, and it is what tells those two apart before either arrives.
   */
  const meshHint = () => {
    const triangles =
      meshState.triangleCount === undefined
        ? undefined
        : meshState.triangleCount.toLocaleString(i18n.language);
    switch (meshState.status) {
      case 'off':
        return meshVisible ? t('scene3d.meshNoCave') : t('scene3d.meshOff');
      case 'looking':
        return t('scene3d.meshLooking');
      case 'loading':
        return triangles
          ? t('scene3d.meshLoadingSized', { triangles })
          : t('scene3d.meshLoading');
      case 'drawn':
        return triangles ? t('scene3d.meshDrawnSized', { triangles }) : t('scene3d.meshDrawn');
      case 'converting':
        return t('scene3d.meshConverting');
      case 'unavailable':
        return t('scene3d.meshNone');
      case 'failed':
        // The server's own words when it gave any: a conversion states what it could not read,
        // and that sentence says more than any phrase written here in advance could.
        return meshState.message
          ? t('scene3d.meshFailedBecause', { reason: meshState.message })
          : t('scene3d.meshFailed');
    }
  };

  const baseLayers = layers.filter((layer) => layer.isBase);

  return (
    <div className="scene3d-layer-panel" data-testid="scene3d-layer-panel">
      {/* Left out entirely when there is no catalog to choose from, rather than shown as a
          heading over nothing. The rest of the panel drives the scene and is offered either way:
          a catalog this installation refuses to serve must not take the layer switches and the
          surface mode down with it. */}
      {baseLayers.length > 0 && (
        <>
          <Typography.Text strong>{t('map.baseLayers')}</Typography.Text>
          {/* Fading the basemap makes it easier to read a survey drawn over it. It does not reveal
              anything buried, which is what the third section below is for. */}
          <Radio.Group
            className="scene3d-layer-rows"
            value={activeBaseId}
            onChange={(e) => onBaseChange(e.target.value as number)}
          >
            {baseLayers.map((layer) => {
              const id = Number(layer.id);
              return (
                <div key={id} className="scene3d-layer-row">
                  <Radio value={id}>{layer.name}</Radio>
                  <Slider
                    className="scene3d-layer-opacity"
                    min={0}
                    max={100}
                    value={percent(baseOpacity[id])}
                    onChange={(value) => onBaseOpacityChange(id, value / 100)}
                    tooltip={opacityTooltip}
                    // Named on the handle rather than on the control: the handle is the element
                    // that carries the slider role, and a label on the wrapper reaches nothing.
                    ariaLabelForHandle={t('map.baseOpacity', { name: layer.name })}
                  />
                </div>
              );
            })}
          </Radio.Group>

          <Divider style={{ margin: '12px 0' }} />
        </>
      )}

      <Typography.Text strong>{t('map.activeOverlays')}</Typography.Text>
      <div className="scene3d-layer-rows">
        {CAVE_DATA_3D_LAYERS.map((layer) => {
          const name = overlayName(layer);
          const shown = overlayVisible[layer] ?? true;
          return (
            <div key={layer} className="scene3d-layer-row">
              <Checkbox
                checked={shown}
                onChange={(e) => onOverlayVisibleChange(layer, e.target.checked)}
              >
                {name}
              </Checkbox>
              <Slider
                className="scene3d-layer-opacity"
                min={0}
                max={100}
                disabled={!shown}
                value={percent(overlayOpacity[layer])}
                onChange={(value) => onOverlayOpacityChange(layer, value / 100)}
                tooltip={opacityTooltip}
                ariaLabelForHandle={t('map.baseOpacity', { name })}
              />
              {/* The survey is drawn over the ground, so nothing about where a line sits on
                  screen says how far down it is — the colour is the only thing that does, and a
                  ramp nobody has been shown the key to is not a cue. */}
              {layer === CENTERLINE_SOURCE_ID && shown && (
                <div className="scene3d-depth-legend">
                  <Typography.Text type="secondary" className="scene3d-depth-caption">
                    {t('scene3d.depthLegend')}
                  </Typography.Text>
                  <div className="scene3d-depth-bands" data-testid="scene3d-depth-legend">
                    {CENTERLINE_DEPTH_BANDS.map((band) => (
                      <span key={band.fromMeters} className="scene3d-depth-band">
                        <span
                          className="scene3d-depth-swatch"
                          // The colour is the datum being shown, not styling: it is the value the
                          // scene draws this band of depths in, read from the same table.
                          style={{ background: band.color }}
                        />
                        {t('scene3d.depthLegendBand', { meters: band.fromMeters })}
                      </span>
                    ))}
                  </div>
                </div>
              )}
            </div>
          );
        })}

        {/* The walls of one cave, and no opacity beside them: a fading control over a mesh that
            cannot be faded would be a switch wired to nothing. What the row does carry is the
            state of the load, because this is the only thing in the scene a viewer waits for. */}
        <div className="scene3d-layer-row">
          <Checkbox
            checked={meshVisible}
            onChange={(e) => onMeshVisibleChange(e.target.checked)}
            data-testid="scene3d-mesh-toggle"
          >
            {t('scene3d.meshLayer')}
          </Checkbox>
          <Typography.Text
            type="secondary"
            className="scene3d-mesh-hint"
            data-testid="scene3d-mesh-status"
          >
            {meshHint()}
          </Typography.Text>
          {/* Said whenever the mesh is on screen, not only while it loads: the survey itself is
              degraded, and the person who can fix it is the one who exported the file. */}
          {meshState.precisionLost && (
            <Typography.Text
              type="warning"
              className="scene3d-mesh-hint"
              data-testid="scene3d-mesh-precision"
            >
              {t('scene3d.meshPrecisionLost')}
            </Typography.Text>
          )}
        </div>
      </div>

      <Divider style={{ margin: '12px 0' }} />

      <Typography.Text strong>{t('scene3d.surfaceTitle')}</Typography.Text>
      <Radio.Group
        className="scene3d-layer-rows"
        value={surfaceMode}
        onChange={(e) => onSurfaceModeChange(e.target.value as Scene3DSurfaceMode)}
      >
        <Radio value="overlay">{t('scene3d.surfaceOverlay')}</Radio>
        {/* Offered but refused rather than hidden: a viewer who cannot have the cutaway is better
            told why than left wondering which control they are missing. */}
        <Radio value="cutaway" disabled={!cutawayAvailable}>
          {t('scene3d.surfaceCutaway')}
        </Radio>
      </Radio.Group>
      <Typography.Text type="secondary" className="scene3d-surface-hint">
        {surfaceHint()}
      </Typography.Text>
    </div>
  );
}
