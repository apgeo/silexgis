// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useMemo, useState, type ReactNode } from 'react';
import LayerTree from '@terrestris/react-geo/dist/LayerTree/LayerTree';
import LayerTransparencySlider from '@terrestris/react-geo/dist/Slider/LayerTransparencySlider/LayerTransparencySlider';
import { Alert, Checkbox, Collapse, Divider, Input, InputNumber, Radio, Select, Slider, Typography } from 'antd';
import type OlLayerBase from 'ol/layer/Base';
import { useTranslation } from 'react-i18next';
import {
  useTags,
  useTripTypes,
  type GeofileInfo,
  type MapConfig,
  type MapLayerInfo,
  type RasterMapInfo,
} from '../../api/hooks.ts';
import { tripTypeLabelOf } from '../trips/tripTypes.ts';
import {
  CENTERLINE_LAYER_ID,
  getCenterlineLoadState,
  subscribeCenterlineLoadState,
  type CenterlineLoadState,
} from '../../map/centerlineLayer.ts';
import { CLOSEST_APPROACH_LAYER_ID } from '../../map/closestApproachLayer.ts';
import { ENTRANCE_LAYER_ID } from '../../map/entranceLayer.ts';
import { SURFACE_FEATURE_LAYER_ID } from '../../map/featureLayer.ts';
import { ENTRANCE_HEATMAP_LAYER_ID } from '../../map/heatmapLayer.ts';
import { PHOTO_LAYER_ID } from '../../map/photoLayer.ts';
import {
  TRIP_LAYER_ID,
  getTripLoadState,
  subscribeTripLoadState,
  type TripLayerFilter,
  type TripLoadState,
} from '../../map/tripLayer.ts';
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
  /** Catalogue tile overlays drawn over the basemap, several at a time. */
  visibleTileOverlayIds: number[];
  onTileOverlayVisibleChange: (id: number, visible: boolean) => void;
  tileOverlayOpacity: Record<number, number>;
  onTileOverlayOpacityChange: (id: number, opacity: number) => void;
  /** Whether crowded labels give way to each other rather than overprinting. */
  declutterLabels: boolean;
  onDeclutterLabelsChange: (value: boolean) => void;
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
  /** True while the trip overlay is on — its filters are hidden otherwise. */
  tripsVisible: boolean;
  /** What the trip overlay is currently asking for. */
  tripFilter: TripLayerFilter;
  onTripFilterChange: (filter: TripLayerFilter) => void;
  /**
   * How many narrowings of a filter carried here from the trip listing this overlay cannot ask
   * about. Said out loud rather than swallowed: a map answering a broader question than the list
   * the reader came from would look like the list had been wrong.
   */
  unappliedTripFilters: number;
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
  visibleTileOverlayIds,
  onTileOverlayVisibleChange,
  tileOverlayOpacity,
  onTileOverlayOpacityChange,
  declutterLabels,
  onDeclutterLabelsChange,
  rasters,
  visibleRasterIds,
  onRasterVisibleChange,
  onOverlayVisibilityChanged,
  treeNonce,
  tagFilter,
  onTagFilterChange,
  centerlinesVisible,
  tripsVisible,
  tripFilter,
  onTripFilterChange,
  unappliedTripFilters,
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

  // The same channel for the trip overlay: how many trips are drawn, whether the answer stopped
  // at the server's cap, and how many trips in the window have no position at all.
  const [tripLoad, setTripLoad] = useState<TripLoadState>(getTripLoadState);
  useEffect(() => subscribeTripLoadState(setTripLoad), []);
  const { data: tripTypes } = useTripTypes();

  /**
   * The catalogue split into the two things this panel draws differently: basemaps, of which one
   * is chosen, and tile overlays, of which any number are.
   *
   * Each is then split again into what the catalogue put in a group and what it did not. Ungrouped
   * entries stay in the open, always visible, because that is where an installation's own handful
   * of everyday sources belongs; a catalogue of forty is what the groups are for, and a panel that
   * needed scrolling past thirty-five sources to reach the overlay controls would have made the
   * catalogue worse than the three hardcoded layers it replaced.
   */
  const { baseGroups, looseBases, overlayGroups, looseOverlays } = useMemo(() => {
    const bases = layers.filter((l) => l.isBase);
    const overlays = layers.filter((l) => !l.isBase && l.layerKind === 'xyz');
    // Insertion-ordered, so groups appear in the order the catalogue first mentions them rather
    // than alphabetically — the file's order is the operator's stated preference.
    const group = (entries: MapLayerInfo[]) => {
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
    const b = group(bases);
    const o = group(overlays);
    return { baseGroups: b.grouped, looseBases: b.loose, overlayGroups: o.grouped, looseOverlays: o.loose };
  }, [layers]);

  /**
   * Which base group starts open: the one holding the basemap currently drawn, and no other.
   *
   * Computed rather than remembered, so that a viewer who restores a saved view using a source
   * from a collapsed group can see which source is active without opening groups one at a time —
   * a checked radio inside a closed panel is a state with nothing on screen to explain it.
   */
  const openBaseGroups = useMemo(() => {
    for (const [name, entries] of baseGroups) {
      if (entries.some((l) => Number(l.id) === activeBaseId)) {
        return [name];
      }
    }
    return [];
  }, [baseGroups, activeBaseId]);

  const baseRow = (l: MapLayerInfo) => {
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
  };

  const overlayRow = (l: MapLayerInfo) => {
    const id = Number(l.id);
    return (
      <div key={id} className="base-layer-row">
        <Checkbox
          checked={visibleTileOverlayIds.includes(id)}
          onChange={(e) => onTileOverlayVisibleChange(id, e.target.checked)}
        >
          {l.name}
        </Checkbox>
        <Slider
          className="base-layer-opacity"
          min={0}
          max={100}
          value={Math.round((tileOverlayOpacity[id] ?? 1) * 100)}
          onChange={(value) => onTileOverlayOpacityChange(id, (value as number) / 100)}
          tooltip={{ formatter: (value) => `${value ?? 0}%` }}
          aria-label={t('map.baseOpacity', { name: l.name })}
        />
      </div>
    );
  };

  const overlayName = (layer: OlLayerBase): string => {
    const id = layer.get('id') as string | undefined;
    switch (id) {
      case ENTRANCE_LAYER_ID:
        return t('map.entrances');
      case SURFACE_FEATURE_LAYER_ID:
        return t('map.features');
      case CENTERLINE_LAYER_ID:
        return t('map.centerlines');
      case ENTRANCE_HEATMAP_LAYER_ID:
        return t('map.heatmap');
      case PHOTO_LAYER_ID:
        return t('map.photos');
      case TRIP_LAYER_ID:
        return t('map.tripsLayer');
      case CLOSEST_APPROACH_LAYER_ID:
        return t('map.closestApproach');
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
        {looseBases.map(baseRow)}
        {baseGroups.size > 0 && (
          // Inside the radio group, so a source in a collapsed panel is still part of the same
          // single choice — collapsing is about room on screen and nothing else.
          <Collapse
            ghost
            size="small"
            className="layer-group-collapse"
            defaultActiveKey={openBaseGroups}
            items={[...baseGroups].map(([name, entries]) => ({
              key: name,
              label: `${name} (${entries.length})`,
              children: <div className="layer-group-body">{entries.map(baseRow)}</div>,
            }))}
          />
        )}
      </Radio.Group>

      {(looseOverlays.length > 0 || overlayGroups.size > 0) && (
        <>
          <Divider style={{ margin: '12px 0' }} />
          <Typography.Text strong>{t('map.tileOverlays')}</Typography.Text>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 6, marginTop: 8 }}>
            {looseOverlays.map(overlayRow)}
            {overlayGroups.size > 0 && (
              <Collapse
                ghost
                size="small"
                className="layer-group-collapse"
                items={[...overlayGroups].map(([name, entries]) => ({
                  key: name,
                  label: `${name} (${entries.length})`,
                  children: <div className="layer-group-body">{entries.map(overlayRow)}</div>,
                }))}
              />
            )}
          </div>
        </>
      )}

      <Divider style={{ margin: '12px 0' }} />
      <Typography.Text strong>{t('map.activeOverlays')}</Typography.Text>
      {/* Sits with the overlays rather than in a settings screen, because what it changes is what
          this map looks like right now and the only way to judge it is to watch the map while
          toggling it. */}
      <div style={{ marginTop: 8 }}>
        <Checkbox
          checked={declutterLabels}
          onChange={(e) => onDeclutterLabelsChange(e.target.checked)}
          data-testid="map-declutter-toggle"
        >
          {t('map.declutterLabels')}
        </Checkbox>
        <Typography.Paragraph type="secondary" style={{ margin: '2px 0 0 24px', fontSize: 12 }}>
          {t('map.declutterLabelsHint')}
        </Typography.Paragraph>
      </div>
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
              title={t('map.centerlinesWithheld', { count: centerlineLoad.withheldCount })}
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
      {tripsVisible && (
        <div style={{ marginTop: 8 }} data-testid="map-trip-filters">
          {/* Honest in three states, because an empty overlay and a failed request look identical
              on a map: "no trips here" is a claim that may only be made once an answer arrived. */}
          {tripLoad.status === 'error' ? (
            <Alert type="warning" showIcon style={{ marginBottom: 8 }} title={t('map.tripsFailed')} />
          ) : (
            <Typography.Text type="secondary" style={{ fontSize: 12 }} data-testid="map-trip-count">
              {tripLoad.status === 'loading'
                ? t('map.tripsLoading')
                : t('map.tripsShown', { count: tripLoad.shownTripCount })}
            </Typography.Text>
          )}
          {tripLoad.truncated && (
            <Alert
              type="info"
              showIcon
              style={{ margin: '8px 0' }}
              title={t('map.tripsTruncated', { count: mapConfig?.maxPoints ?? 0 })}
            />
          )}
          {/* The point of the whole layer for an imported archive: a trip whose only record of
              where it went is a cave nobody may place has no dot, and saying nothing about it
              would make the map read as though the trip did not exist. */}
          {tripLoad.unlocatedCount > 0 && (
            <Alert
              type="info"
              showIcon
              style={{ margin: '8px 0' }}
              title={t('map.tripsUnlocated', { count: tripLoad.unlocatedCount })}
              data-testid="map-trips-unlocated"
            />
          )}
          {unappliedTripFilters > 0 && (
            <Alert
              type="info"
              showIcon
              style={{ margin: '8px 0' }}
              title={t('map.tripsFilterUnapplied', { count: unappliedTripFilters })}
            />
          )}
          <div className="trip-layer-filters">
            <label>
              <span>{t('map.tripsFrom')}</span>
              <Input
                size="small"
                type="date"
                value={tripFilter.from ?? ''}
                onChange={(e) => onTripFilterChange({ ...tripFilter, from: e.target.value || undefined })}
                data-testid="map-trips-from"
              />
            </label>
            <label>
              <span>{t('map.tripsTo')}</span>
              <Input
                size="small"
                type="date"
                value={tripFilter.to ?? ''}
                onChange={(e) => onTripFilterChange({ ...tripFilter, to: e.target.value || undefined })}
                data-testid="map-trips-to"
              />
            </label>
          </div>
          <Select
            mode="multiple"
            allowClear
            size="small"
            optionFilterProp="label"
            placeholder={t('map.tripsTypeFilter')}
            style={{ width: '100%', marginTop: 8 }}
            value={tripFilter.types ?? []}
            options={tripTypes?.map((x) => ({ value: String(x.id), label: tripTypeLabelOf(x.id, tripTypes, t) }))}
            onChange={(values: string[]) => onTripFilterChange({ ...tripFilter, types: values })}
            data-testid="map-trips-types"
          />
          <Typography.Text type="secondary" style={{ fontSize: 12 }}>
            {t('map.tripsFilterHint')}
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
