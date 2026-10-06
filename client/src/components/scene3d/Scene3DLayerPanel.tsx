// SPDX-License-Identifier: AGPL-3.0-or-later
import { Checkbox, Collapse, Divider, Radio, Slider, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import type { MapLayerInfo, RasterMapInfo } from '../../api/hooks.ts';
import {
  CAVE_DATA_3D_LAYERS,
  CENTERLINE_SOURCE_ID,
  ENTRANCE_SOURCE_ID,
  type CaveData3DLayer,
} from '../../scene3d/caveData3d.ts';
import { CENTERLINE_DEPTH_BANDS } from '../../scene3d/centerlines3d.ts';
import type { GeofileTrack3DFile } from '../../scene3d/geofileTracks3d.ts';
import type { Scene3DSurfaceMode, Scene3DSurfaceState } from '../../scene3d/scene3dEngine.ts';
import type { SurveyMesh3DState } from '../../scene3d/surveyMesh3d.ts';
import type {
  SurveyMeshesInView3DState,
  SurveyWalls3DMode,
} from '../../scene3d/surveyMeshesInView3d.ts';
import { extentSizeKm, type TerrainBuildChoice } from '../../scene3d/terrainBuilds3d.ts';
import { meshesInViewMessage, meshProgressMessage } from './meshMessages.ts';
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
  /**
   * Catalogue tile overlays drawn over the basemap, several at a time — the same set and the same
   * choice as the flat map, so a viewer moving between the two views is not shown two different
   * maps of the same place.
   */
  visibleTileOverlayIds: number[];
  onTileOverlayVisibleChange: (id: number, visible: boolean) => void;
  tileOverlayOpacity: Record<number, number>;
  onTileOverlayOpacityChange: (id: number, opacity: number) => void;
  /** This installation's georeferenced maps, and which of them are draped on the globe. */
  rasters: RasterMapInfo[];
  visibleRasterIds: string[];
  onRasterVisibleChange: (id: string, visible: boolean) => void;
  /** Keyed by layer; a missing key means shown. */
  overlayVisible: Record<string, boolean>;
  onOverlayVisibleChange: (layer: CaveData3DLayer, visible: boolean) => void;
  /**
   * Keyed by layer; a missing key means fully opaque. An imported file's fade is under the file's
   * own id, which is the key the flat map keeps it under, so a file faded in one view is faded
   * in both.
   */
  overlayOpacity: Record<string, number>;
  onOverlayOpacityChange: (layer: CaveData3DLayer, opacity: number) => void;
  /**
   * This installation's imported files, and which of them have their tracks drawn — the same
   * choice the flat map shows, so a viewer moving between the views finds the same files on.
   */
  geofiles: GeofileTrack3DFile[];
  visibleGeofileIds: string[];
  onGeofileVisibleChange: (id: string, visible: boolean) => void;
  onGeofileOpacityChange: (id: string, opacity: number) => void;
  /**
   * Whether cave walls are drawn. Its own prop rather than another entry in the overlay records
   * above, because it is not one of the layers those are keyed by: turning the walls off frees
   * graphics memory rather than hiding a source that stays loaded.
   */
  meshVisible: boolean;
  onMeshVisibleChange: (visible: boolean) => void;
  /**
   * Whose walls: the selected cave's, or those of every cave in view. A choice beside the switch
   * rather than a second switch, because the two are one layer asked for in two ways and holding
   * both would read the selected cave's mesh twice.
   */
  wallsMode: SurveyWalls3DMode;
  onWallsModeChange: (mode: SurveyWalls3DMode) => void;
  /** What the scene is doing about the selected cave's walls, said in words under the switch. */
  meshState: SurveyMesh3DState;
  /** What the scene holds of the walls of the caves in view, said in the same place in that mode. */
  meshesInViewState: SurveyMeshesInView3DState;
  /**
   * The elevation models this installation has baked, newest first, for the viewer to draw the
   * ground from. Empty leaves the section out: there is nothing to switch between.
   */
  terrainBuilds: TerrainBuildChoice[];
  /**
   * True when the ground is drawn by default from an address the operator configured rather than
   * from one of the builds, which puts an entry for that address first so a viewer who switched
   * to a build can get back to it.
   */
  terrainConfigured: boolean;
  /** The build the viewer chose to draw, or undefined for whatever the installation draws by default. */
  terrainChoice: string | undefined;
  onTerrainChoiceChange: (buildId: string | undefined) => void;
  surfaceMode: Scene3DSurfaceMode;
  onSurfaceModeChange: (mode: Scene3DSurfaceMode) => void;
  /** What the scene is actually doing about the ground; undefined until it has started. */
  surfaceState: Scene3DSurfaceState | undefined;
}

/**
 * The radio entry standing for the address the operator configured, which is not a build and has
 * no id of its own. A value no build id can collide with: build ids are UUIDs.
 */
const CONFIGURED_TERRAIN_ENTRY = 'configured';

export default function Scene3DLayerPanel({
  layers,
  activeBaseId,
  onBaseChange,
  baseOpacity,
  onBaseOpacityChange,
  visibleTileOverlayIds,
  onTileOverlayVisibleChange,
  tileOverlayOpacity,
  onTileOverlayOpacityChange,
  rasters,
  visibleRasterIds,
  onRasterVisibleChange,
  overlayVisible,
  onOverlayVisibleChange,
  overlayOpacity,
  onOverlayOpacityChange,
  geofiles,
  visibleGeofileIds,
  onGeofileVisibleChange,
  onGeofileOpacityChange,
  meshVisible,
  onMeshVisibleChange,
  wallsMode,
  onWallsModeChange,
  meshState,
  meshesInViewState,
  terrainBuilds,
  terrainConfigured,
  terrainChoice,
  onTerrainChoiceChange,
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
   * a number — which number, and how it is worded, is decided once beside the scene's own notice
   * so that the two never describe one load in two different measures.
   */
  const meshHint = () => {
    if (wallsMode === 'inView') {
      // The other mode's own sentence: how many caves' walls are drawn out of how many, and what
      // kept the rest out. With the walls off it is the same "not loaded" either mode says.
      return meshesInViewMessage(meshesInViewState, t);
    }
    switch (meshState.status) {
      case 'off':
        return meshVisible ? t('scene3d.meshNoCave') : t('scene3d.meshOff');
      case 'looking':
        return t('scene3d.meshLooking');
      case 'loading':
      case 'drawn':
        return meshProgressMessage(meshState, i18n.language, t);
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

  /**
   * How a build is named in the list: its level, when it was finished, and how much ground it
   * covers. None of the three is a name, and together they are what a viewer actually chooses by —
   * "the finer one", "the recent one", "the one over the whole region".
   */
  const kilometres = (value: number) =>
    value.toLocaleString(i18n.language, { maximumFractionDigits: value >= 10 ? 0 : 1 });
  const buildLabel = (build: TerrainBuildChoice) => {
    const parts = [t('scene3d.terrainBuildLevel', { level: build.requestedMaxDepth })];
    if (build.finishedAt) {
      parts.push(new Date(build.finishedAt).toLocaleDateString(i18n.language));
    }
    const size = extentSizeKm(build.extent);
    if (size) {
      parts.push(
        t('scene3d.terrainBuildSize', {
          width: kilometres(size.widthKm),
          height: kilometres(size.heightKm),
        }),
      );
    }
    return parts.join(' \u00b7 ');
  };
  // What the ground is drawn from when the viewer has not said: the configured address when
  // there is one, otherwise the build the server marks as its own default. Choosing that entry
  // again is reported as no choice, so that "the default" and "the build that happens to be the
  // default" are one state rather than two that draw the same ground.
  const defaultTerrainEntry = terrainConfigured
    ? CONFIGURED_TERRAIN_ENTRY
    : terrainBuilds.find((build) => build.isDrawn)?.id;
  const terrainEntry = terrainChoice ?? defaultTerrainEntry;

  const baseLayers = layers.filter((layer) => layer.isBase);
  const tileOverlays = layers.filter((layer) => !layer.isBase && layer.layerKind === 'xyz');
  // Only rasters that finished converting have anything to drape. The others are shown in the
  // georeferenced-maps screen with their status; offering them here would be offering a switch
  // that does nothing and says nothing about why.
  const readyRasters = rasters.filter((raster) => raster.status === 'ready' && raster.cogUrl);
  // Only files whose import has finished have anything to draw; the others are listed with
  // their status on the geodata screen, and a switch here would draw nothing and say nothing.
  const importedGeofiles = geofiles.filter((geofile) => geofile.importStatus === 'imported');

  const tileOverlayRow = (layer: MapLayerInfo) => {
    const id = Number(layer.id);
    return (
      <div key={id} className="scene3d-layer-row">
        <Checkbox
          checked={visibleTileOverlayIds.includes(id)}
          onChange={(e) => onTileOverlayVisibleChange(id, e.target.checked)}
        >
          {layer.name}
        </Checkbox>
        <Slider
          className="scene3d-layer-opacity"
          min={0}
          max={100}
          value={percent(tileOverlayOpacity[id])}
          onChange={(value) => onTileOverlayOpacityChange(id, value / 100)}
          tooltip={opacityTooltip}
          ariaLabelForHandle={t('map.baseOpacity', { name: layer.name })}
        />
      </div>
    );
  };

  /**
   * The catalogue's groups, in the order the catalogue first mentions each.
   *
   * Same rule as the flat map's panel and for the same reason: this panel is narrower than that
   * one, so a catalogue of forty sources has to fold up or it is the only thing on screen.
   */
  const groupsOf = (entries: MapLayerInfo[]) => {
    const grouped = new globalThis.Map<string, MapLayerInfo[]>();
    const loose: MapLayerInfo[] = [];
    for (const entry of entries) {
      const name = entry.groupName?.trim();
      if (!name) {
        loose.push(entry);
        continue;
      }
      const bucket = grouped.get(name);
      if (bucket) {
        bucket.push(entry);
      } else {
        grouped.set(name, [entry]);
      }
    }
    return { grouped, loose };
  };


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
            {(() => {
              const { grouped, loose } = groupsOf(baseLayers);
              const baseRow = (layer: MapLayerInfo) => {
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
              };
              const open = [...grouped].find(([, entries]) =>
                entries.some((l) => Number(l.id) === activeBaseId),
              );
              return (
                <>
                  {loose.map(baseRow)}
                  {grouped.size > 0 && (
                    <Collapse
                      ghost
                      size="small"
                      className="layer-group-collapse"
                      defaultActiveKey={open ? [open[0]] : []}
                      items={[...grouped].map(([name, entries]) => ({
                        key: name,
                        label: `${name} (${entries.length})`,
                        children: <div className="layer-group-body">{entries.map(baseRow)}</div>,
                      }))}
                    />
                  )}
                </>
              );
            })()}
          </Radio.Group>

          <Divider style={{ margin: '12px 0' }} />
        </>
      )}

      {tileOverlays.length > 0 && (
        <>
          <Typography.Text strong>{t('map.tileOverlays')}</Typography.Text>
          <div className="scene3d-layer-rows">
            {(() => {
              const { grouped, loose } = groupsOf(tileOverlays);
              return (
                <>
                  {loose.map(tileOverlayRow)}
                  {grouped.size > 0 && (
                    <Collapse
                      ghost
                      size="small"
                      className="layer-group-collapse"
                      items={[...grouped].map(([name, entries]) => ({
                        key: name,
                        label: `${name} (${entries.length})`,
                        children: <div className="layer-group-body">{entries.map(tileOverlayRow)}</div>,
                      }))}
                    />
                  )}
                </>
              );
            })()}
          </div>
          <Divider style={{ margin: '12px 0' }} />
        </>
      )}

      {readyRasters.length > 0 && (
        <>
          <Typography.Text strong>{t('map.georeferencedMaps')}</Typography.Text>
          {/* Said once, here, because it is the one thing about these that differs between the two
              views and a viewer comparing them WILL notice: on the globe each sheet is flattened
              to a single picture, so it softens under close zoom where the flat map keeps reading
              the file's own pyramid. */}
          <Typography.Paragraph type="secondary" style={{ margin: '4px 0 0', fontSize: 12 }}>
            {t('scene3d.rasterOverlayHint')}
          </Typography.Paragraph>
          <div className="scene3d-layer-rows">
            {readyRasters.map((raster) => (
              <div key={raster.id} className="scene3d-layer-row">
                <Checkbox
                  checked={visibleRasterIds.includes(raster.id)}
                  onChange={(e) => onRasterVisibleChange(raster.id, e.target.checked)}
                >
                  {raster.name}
                </Checkbox>
              </div>
            ))}
          </div>
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

        {/* Cave walls, and no opacity beside them: a fading control over a mesh that cannot be
            faded would be a switch wired to nothing. What the row does carry is the state of the
            load, because this is the only thing in the scene a viewer waits for. */}
        <div className="scene3d-layer-row">
          <Checkbox
            checked={meshVisible}
            onChange={(e) => onMeshVisibleChange(e.target.checked)}
            data-testid="scene3d-mesh-toggle"
          >
            {t('scene3d.meshLayer')}
          </Checkbox>
          {/* Whose walls. Left usable while the walls are off, so the choice can be made before
              anything is read: switching on straight into "every cave in view" must not first
              fetch the selected cave's mesh only to drop it a moment later. */}
          <Radio.Group
            className="scene3d-walls-mode"
            aria-label={t('scene3d.meshMode')}
            data-testid="scene3d-walls-mode"
            value={wallsMode}
            onChange={(e) => onWallsModeChange(e.target.value as SurveyWalls3DMode)}
          >
            <Radio value="selected">{t('scene3d.meshModeSelected')}</Radio>
            <Radio value="inView">{t('scene3d.meshModeInView')}</Radio>
          </Radio.Group>
          <Typography.Text
            type="secondary"
            className="scene3d-mesh-hint"
            data-testid="scene3d-mesh-status"
          >
            {meshHint()}
          </Typography.Text>
          {/* Said whenever the mesh is on screen, not only while it loads: the survey itself is
              degraded, and the person who can fix it is the one who exported the file. */}
          {wallsMode === 'selected' && meshState.precisionLost && (
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

      {importedGeofiles.length > 0 && (
        <>
          <Divider style={{ margin: '12px 0' }} />
          {/* The flat map's own heading for the same files, so the list reads as the same list.
              What differs is said once underneath: on the globe a file is its lines, and a track
              that recorded no altitude lies on the ground rather than at sea level. */}
          <Typography.Text strong>{t('map.geofiles')}</Typography.Text>
          <Typography.Paragraph type="secondary" style={{ margin: '4px 0 0', fontSize: 12 }}>
            {t('scene3d.geofileTracksHint')}
          </Typography.Paragraph>
          <div className="scene3d-layer-rows" data-testid="scene3d-geofile-rows">
            {importedGeofiles.map((geofile) => {
              const shown = visibleGeofileIds.includes(geofile.id);
              return (
                <div key={geofile.id} className="scene3d-layer-row">
                  <Checkbox
                    checked={shown}
                    onChange={(e) => onGeofileVisibleChange(geofile.id, e.target.checked)}
                  >
                    {geofile.name}
                  </Checkbox>
                  <Slider
                    className="scene3d-layer-opacity"
                    min={0}
                    max={100}
                    disabled={!shown}
                    value={percent(overlayOpacity[geofile.id])}
                    onChange={(value) => onGeofileOpacityChange(geofile.id, value / 100)}
                    tooltip={opacityTooltip}
                    ariaLabelForHandle={t('map.baseOpacity', { name: geofile.name })}
                  />
                </div>
              );
            })}
          </div>
        </>
      )}

      {terrainBuilds.length > 0 && (
        <>
          <Divider style={{ margin: '12px 0' }} />
          {/* Which elevation model shapes the globe. Offered only where there is more than the
              default to draw — an installation with no builds has a smooth sphere or a configured
              address, and neither is a choice. A switch is a real reload of the ground, so the
              hint says so: the viewer should expect the surveys to move with it. */}
          <Typography.Text strong>{t('scene3d.terrainTitle')}</Typography.Text>
          <Typography.Paragraph type="secondary" style={{ margin: '4px 0 0', fontSize: 12 }}>
            {t('scene3d.terrainBuildsHint')}
          </Typography.Paragraph>
          <Radio.Group
            className="scene3d-layer-rows"
            data-testid="scene3d-terrain-rows"
            value={terrainEntry}
            onChange={(e) => {
              const entry = e.target.value as string;
              onTerrainChoiceChange(entry === defaultTerrainEntry ? undefined : entry);
            }}
          >
            {terrainConfigured && (
              <Radio value={CONFIGURED_TERRAIN_ENTRY}>{t('scene3d.terrainConfiguredEntry')}</Radio>
            )}
            {terrainBuilds.map((build) => (
              <Radio key={build.id} value={build.id}>
                {buildLabel(build)}
              </Radio>
            ))}
          </Radio.Group>
        </>
      )}

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
