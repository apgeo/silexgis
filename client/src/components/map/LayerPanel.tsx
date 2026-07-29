// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useState, type ReactNode } from 'react';
import LayerTree from '@terrestris/react-geo/dist/LayerTree/LayerTree';
import LayerTransparencySlider from '@terrestris/react-geo/dist/Slider/LayerTransparencySlider/LayerTransparencySlider';
import { Alert, Checkbox, Divider, InputNumber, Radio, Select, Slider, Typography } from 'antd';
import type OlLayerBase from 'ol/layer/Base';
import { useTranslation } from 'react-i18next';
import { useTags, type GeofileInfo, type MapConfig, type MapLayerInfo, type RasterMapInfo } from '../../api/hooks.ts';
import {
  CENTERLINE_LAYER_ID,
  getCenterlineLoadState,
  subscribeCenterlineLoadState,
  type CenterlineLoadState,
} from '../../map/centerlineLayer.ts';
import { ENTRANCE_LAYER_ID } from '../../map/entranceLayer.ts';
import { SURFACE_FEATURE_LAYER_ID } from '../../map/featureLayer.ts';
import { ENTRANCE_HEATMAP_LAYER_ID } from '../../map/heatmapLayer.ts';
import { PHOTO_LAYER_ID } from '../../map/photoLayer.ts';
import { getOverlayGroup } from '../../map/mapContext.ts';

interface LayerPanelProps {
  layers: MapLayerInfo[];
  activeBaseId: number | undefined;
  onBaseChange: (id: number) => void;
  /** Per-base-layer opacity (0..1) keyed by catalog id; missing = fully opaque. */
  baseOpacity: Record<number, number>;
  onBaseOpacityChange: (id: number, opacity: number) => void;
  geofiles: GeofileInfo[];
  visibleGeofileIds: string[];
  onGeofileVisibleChange: (id: string, visible: boolean) => void;
  rasters: RasterMapInfo[];
  visibleRasterIds: string[];
  onRasterVisibleChange: (id: string, visible: boolean) => void;
  /** Fired when a layer's checkbox in the composer tree is toggled. */
  onOverlayVisibilityChanged: (layer: OlLayerBase, visible: boolean) => void;
  /**
   * Bumped when a saved view is applied. The transparency sliders are uncontrolled
   * (they read the layer's opacity once on mount), so remounting them here is what
   * moves the knobs to the restored values.
   */
  treeNonce: number;
  tagFilter: string | null;
  onTagFilterChange: (slug: string | null) => void;
  /** True while the centerline overlay is on — its detail controls are hidden otherwise. */
  centerlinesVisible: boolean;
  /** Installation limits; undefined until /map/config has loaded. */
  mapConfig?: MapConfig;
  /** This viewer's overrides; undefined fields follow the installation. */
  centerlineDetailZoom?: number;
  centerlineMaxPaths?: number;
  onCenterlineLimitsChange: (limits: { detailZoom?: number; maxPaths?: number }) => void;
  footer?: ReactNode;
}

export default function LayerPanel({
  layers,
  activeBaseId,
  onBaseChange,
  baseOpacity,
  onBaseOpacityChange,
  geofiles,
  visibleGeofileIds,
  onGeofileVisibleChange,
  rasters,
  visibleRasterIds,
  onRasterVisibleChange,
  onOverlayVisibilityChanged,
  treeNonce,
  tagFilter,
  onTagFilterChange,
  centerlinesVisible,
  mapConfig,
  centerlineDetailZoom,
  centerlineMaxPaths,
  onCenterlineLimitsChange,
  footer,
}: LayerPanelProps) {
  const { t } = useTranslation();
  const { data: tags } = useTags('');

  // The overlay reports what its limits held back; the panel is where that gets explained.
  const [centerlineLoad, setCenterlineLoad] = useState<CenterlineLoadState>(getCenterlineLoadState);
  useEffect(() => subscribeCenterlineLoadState(setCenterlineLoad), []);

  const overlayName = (layer: OlLayerBase): string => {
    const id = layer.get('id') as string | undefined;
    switch (id) {
      case ENTRANCE_LAYER_ID:
        return t('map.entrances');
      case SURFACE_FEATURE_LAYER_ID:
        return t('map.surfaceFeatures');
      case CENTERLINE_LAYER_ID:
        return t('map.centerlines');
      case ENTRANCE_HEATMAP_LAYER_ID:
        return t('map.heatmap');
      case PHOTO_LAYER_ID:
        return t('map.photos');
      default:
        // Geofile/raster layers carry their catalog name on the OL layer itself.
        return (layer.get('name') as string | undefined) ?? id ?? '';
    }
  };

  const nodeTitle = (layer: OlLayerBase) => (
    <div className="layer-node">
      <span className="layer-node-name">{overlayName(layer)}</span>
      {/* The tree row is HTML5-draggable for reordering; grabbing the slider
          must not start a row drag. */}
      <div
        className="layer-node-opacity"
        draggable={false}
        onDragStart={(event) => {
          event.preventDefault();
          event.stopPropagation();
        }}
      >
        <LayerTransparencySlider
          key={`${layer.get('id')}:${treeNonce}`}
          layer={layer}
          tooltip={{ formatter: (value) => `${100 - (value ?? 0)}%` }}
        />
      </div>
    </div>
  );

  return (
    <div style={{ padding: 12, overflow: 'auto', height: '100%' }}>
      <Typography.Text strong>{t('map.baseLayers')}</Typography.Text>
      {/* Each base keeps its own opacity slider (like overlays); only the active base
          is visible, so switching to a dimmed base restores its opacity. */}
      <Radio.Group
        style={{ display: 'flex', flexDirection: 'column', gap: 6, marginTop: 8, width: '100%' }}
        value={activeBaseId}
        onChange={(e) => onBaseChange(e.target.value as number)}
      >
        {layers
          .filter((l) => l.isBase)
          .map((l) => {
            const id = Number(l.id);
            return (
              <div key={id} className="base-layer-row">
                <Radio value={id}>{l.name}</Radio>
                <Slider
                  className="base-layer-opacity"
                  min={0}
                  max={100}
                  value={Math.round((baseOpacity[id] ?? 1) * 100)}
                  onChange={(value) => onBaseOpacityChange(id, (value as number) / 100)}
                  tooltip={{ formatter: (value) => `${value ?? 0}%` }}
                  aria-label={t('map.baseOpacity', { name: l.name })}
                />
              </div>
            );
          })}
      </Radio.Group>
      <Divider style={{ margin: '12px 0' }} />
      <Typography.Text strong>{t('map.activeOverlays')}</Typography.Text>
      <Select
        allowClear
        showSearch
        size="small"
        optionFilterProp="label"
        placeholder={t('tags.filterPlaceholder')}
        style={{ width: '100%', marginTop: 8 }}
        value={tagFilter ?? undefined}
        options={tags?.map((x) => ({ value: x.slug, label: x.name }))}
        onChange={(value?: string) => onTagFilterChange(value ?? null)}
      />
      {/* The composer: visibility checkboxes, drag-to-reorder stacking (top row
          renders topmost) and a per-layer transparency slider. */}
      <LayerTree
        className="layer-composer"
        layerGroup={getOverlayGroup()}
        nodeTitleRenderer={nodeTitle}
        onLayerVisibilityChanged={onOverlayVisibilityChanged}
      />
      {centerlinesVisible && (
        <div style={{ marginTop: 8 }}>
          {centerlineLoad.withheldCount > 0 && (
            <Alert
              type="info"
              showIcon
              style={{ marginBottom: 8 }}
              message={t('map.centerlinesWithheld', { count: centerlineLoad.withheldCount })}
            />
          )}
          <Typography.Text type="secondary" style={{ fontSize: 12 }}>
            {centerlineLoad.detail ? t('map.centerlineDetailOn') : t('map.centerlineSkeletonOn')}
          </Typography.Text>
          {/* Personal limits: the installation's values are the placeholders, so leaving a field
              empty means "follow the server". */}
          <div className="centerline-limits">
            <label>
              <span>{t('map.centerlineDetailZoom')}</span>
              <InputNumber
                size="small"
                min={0}
                max={24}
                value={centerlineDetailZoom}
                placeholder={String(mapConfig?.centerlineDetailZoom ?? '')}
                onChange={(value) =>
                  onCenterlineLimitsChange({
                    detailZoom: value ?? undefined,
                    maxPaths: centerlineMaxPaths,
                  })}
              />
            </label>
            <label>
              <span>{t('map.centerlineMaxPaths')}</span>
              <InputNumber
                size="small"
                min={0}
                max={mapConfig?.centerlineMaxPathsLimit}
                step={5000}
                value={centerlineMaxPaths}
                placeholder={String(mapConfig?.centerlineMaxPaths ?? '')}
                onChange={(value) =>
                  onCenterlineLimitsChange({
                    detailZoom: centerlineDetailZoom,
                    maxPaths: value ?? undefined,
                  })}
              />
            </label>
          </div>
          <Typography.Text type="secondary" style={{ fontSize: 12 }}>
            {t('map.centerlineLimitsHint')}
          </Typography.Text>
        </div>
      )}
      {geofiles.length > 0 && (
        <>
          <Divider style={{ margin: '12px 0' }} />
          <Typography.Text strong>{t('map.geofiles')}</Typography.Text>
          <div style={{ marginTop: 8, display: 'flex', flexDirection: 'column', gap: 6 }}>
            {geofiles.map((geofile) => (
              <Checkbox
                key={geofile.id}
                checked={visibleGeofileIds.includes(geofile.id)}
                onChange={(e) => onGeofileVisibleChange(geofile.id, e.target.checked)}
              >
                {geofile.name}
              </Checkbox>
            ))}
          </div>
        </>
      )}
      {rasters.length > 0 && (
        <>
          <Divider style={{ margin: '12px 0' }} />
          <Typography.Text strong>{t('map.rasterMaps')}</Typography.Text>
          <div style={{ marginTop: 8, display: 'flex', flexDirection: 'column', gap: 4 }}>
            {rasters.map((raster) => (
              <Checkbox
                key={raster.id}
                checked={visibleRasterIds.includes(raster.id)}
                onChange={(e) => onRasterVisibleChange(raster.id, e.target.checked)}
              >
                {raster.name}
              </Checkbox>
            ))}
          </div>
        </>
      )}
      {footer}
    </div>
  );
}
