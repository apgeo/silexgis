// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useMemo, useState, type ReactNode } from 'react';
import LayerTree from '@terrestris/react-geo/dist/LayerTree/LayerTree';
import LayerTransparencySlider from '@terrestris/react-geo/dist/Slider/LayerTransparencySlider/LayerTransparencySlider';
import { Alert, Button, Checkbox, Collapse, Divider, Input, InputNumber, Radio, Select, Slider, Typography } from 'antd';
import type OlLayerBase from 'ol/layer/Base';
import { useTranslation } from 'react-i18next';
import {
  useRecheckPhotoLibrary,
  useTags,
  useTripTypes,
  type GeofileInfo,
  type LibraryPhotoProvider,
  type MapConfig,
  type MapLayerInfo,
  type RasterMapInfo,
} from '../../api/hooks.ts';
import { tripTypeLabelOf } from '../trips/tripTypes.ts';
import i18n from '../../i18n';
import { ApiError } from '../../api/client.ts';
import {
  CENTERLINE_LAYER_ID,
  getCenterlineLoadState,
  subscribeCenterlineLoadState,
  type CenterlineLoadState,
} from '../../map/centerlineLayer.ts';
import { CLOSEST_APPROACH_LAYER_ID } from '../../map/closestApproachLayer.ts';
import {
  getLibraryPhotoLoadStates,
  libraryPhotoSourceOf,
  setLibraryPhotoPictures,
  subscribeLibraryPhotoLoadStates,
  type LibraryPhotoLoadStates,
} from '../../map/libraryPhotoLayer.ts';
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

/**
 * The refusal a library gives when it will not accept the credential this installation is
 * configured with, named exactly as the server names it.
 *
 * Kept apart from every other failure because it sends an operator somewhere else entirely: to the
 * library's own settings screen to issue a credential, rather than to a container that is not
 * running. One of these products expires its tokens after a year by default, so this is what a
 * working installation turns into twelve months after somebody set it up and stopped thinking
 * about it.
 */
const CREDENTIAL_REFUSED = 'photo_library.unauthorized';

/**
 * The refusal a library gives when a question did not come back at all: nothing listening, or
 * nothing said in time.
 *
 * It arrives in two situations that are not the same errand, and the reach beside it is what
 * separates them. Against a library that answered nothing it is why the reach is unreachable, and
 * it is read there. Against one that answered the first question and not the second it means a
 * container that is busy or still starting up — which is a wait, not a fault. Without this it
 * lands in the catch-all sentence and sends an operator looking for a malformed answer that never
 * existed, when what they have is a library that would answer again in a minute.
 */
const ANSWER_DID_NOT_ARRIVE = 'photo_library.unavailable';

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
  /** The photo libraries this installation is pointed at, as this account may see them; empty when none. */
  photoLibraries: LibraryPhotoProvider[];
  /**
   * The products this build can read that no address or credential has been supplied for. The
   * server sends these to a full administrator and to nobody else, so the panel draws whatever it
   * is given: an ordinary account has no errand that begins with a library nobody connected.
   */
  unconfiguredPhotoLibraries: LibraryPhotoProvider[];
  /** Which of them are switched on — a status block for an overlay nobody is looking at is noise. */
  visibleLibraryPhotoSources: string[];
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
  photoLibraries,
  unconfiguredPhotoLibraries,
  visibleLibraryPhotoSources,
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
  // Each photo-library overlay reports what its library answered; this panel is where that gets
  // explained, because on a map an empty answer, a failed request and an overlay still waiting all
  // look like the same blank patch.
  const [libraryLoad, setLibraryLoad] = useState<LibraryPhotoLoadStates>(getLibraryPhotoLoadStates);
  useEffect(() => subscribeLibraryPhotoLoadStates(setLibraryLoad), []);

  // Re-opens one library's pictures and makes the server ask it again what state it is in. The
  // same button does both, because from where an operator stands they are one act: they have just
  // fixed something on the other side and want to know whether it took.
  const recheck = useRecheckPhotoLibrary();

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
    // Photo-library overlays are named for the library they read rather than for their layer id,
    // and only the frame around that name is translated.
    const librarySource = libraryPhotoSourceOf(id);
    if (librarySource) {
      return t('libraryPhotos.layerName', {
        library:
          photoLibraries.find((library) => library.source === librarySource)?.name ?? librarySource,
      });
    }
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
      {photoLibraries
        .filter((library) => visibleLibraryPhotoSources.includes(library.source))
        .map((library) => {
          const state = libraryLoad[library.source];
          const reach = state?.reach ?? 'idle';
          const health = library.health;
          const missing = health?.missingPermissions ?? [];
          /*
           * What is wrong with the library itself, as opposed to what came back for the rectangle
           * on screen. Every state below draws the same empty patch of map, which is why they are
           * words rather than an absence: a wrong address, a credential that expired, a credential
           * that never carried the rights this integration needs, and a valley nobody has
           * photographed are one picture on a map and four different afternoons for whoever has to
           * fix them.
           *
           * Ordered by what an operator acts on first. The refused credential is named before
           * anything else because it is the one that arrives at either question — a library can
           * refuse the credential on the route that proves it is alive as readily as on the one
           * that describes it — and because it is a five-minute fix that otherwise reads as a
           * library that is down.
           */
          const problem =
            health?.failureCode === CREDENTIAL_REFUSED
              ? t('libraryPhotos.health.credentialRefused')
              : health?.reach === 'unreachable'
                ? t('libraryPhotos.health.notAnswering')
                : missing.length > 0
                  ? // Named one by one rather than counted. "Your key is missing asset.view" is a
                    // fix; "the key is not sufficient" is a search through a permission list of
                    // over a hundred entries.
                    t('libraryPhotos.health.missingPermissions', { permissions: missing.join(', ') })
                  : health?.failureCode === ANSWER_DID_NOT_ARRIVE
                    ? // Reachable, and one question still unanswered: a container that is busy or
                      // has not finished starting. Named apart from the sentence below because
                      // that one sends an operator to look at an answer, and there is no answer to
                      // look at — the thing to do is wait and press the button again.
                      t('libraryPhotos.health.partlyAnswering')
                    : health?.failureCode
                      ? t('libraryPhotos.health.unreadable')
                      : null;
          /*
           * What came back for the rectangle on screen, which is a different question from whether
           * the library is working, and null when there is nothing honest to say about it.
           *
           * A count survives a warning about the library on purpose. A library that answered one
           * of the questions asked about itself and not another can still be answering viewports
           * perfectly, and a panel that withholds the number while the map draws the pins beside
           * it is arguing with what the reader can already see. "Nothing here" does not survive
           * one: that sentence is a claim about what the library holds, and a library that is not
           * fully answering has not earned it.
           */
          const viewport =
            reach === 'loading' && !state?.shownCount
              ? t('libraryPhotos.loading')
              : state?.shownCount
                ? t('libraryPhotos.count', { count: state.shownCount })
                : problem
                  ? null
                  : t('libraryPhotos.empty');
          const checking = recheck.isPending && recheck.variables === library.source;
          // Why the recheck was refused, where the server named it. Taken from the refusal's own
          // code rather than matched out of its sentence, which is prose written for a person and
          // may be reworded or translated without anything here noticing.
          const recheckCode = recheck.error instanceof ApiError ? recheck.error.code : undefined;
          return (
            <div
              key={library.source}
              style={{ marginTop: 8 }}
              data-testid={`library-photos-status-${library.source}`}
            >
              <Typography.Text strong style={{ fontSize: 12 }}>
                {library.name}
              </Typography.Text>
              {/* The library's own state comes first, because when it is wrong most of what can be
                  said about the viewport means less: an overlay reporting "no photographs here"
                  for a container that is not running is a true sentence about a question nobody
                  asked. */}
              {problem && (
                <Alert type="warning" showIcon style={{ marginTop: 4 }} title={problem} />
              )}
              {/* Three answers, never one. A blank map because the library holds nothing here, a
                  blank map because the library did not answer this viewport, and a map still
                  waiting are different facts, and an overlay that renders all three as emptiness is
                  why somebody spends an afternoon debugging a library that was working. */}
              {reach === 'unreachable' ? (
                // Said once. The line above already names why nothing answered and names it more
                // precisely, so repeating it here in weaker words adds a sentence and no fact.
                problem ? null : (
                  <Alert
                    type="warning"
                    showIcon
                    style={{ marginTop: 4 }}
                    title={t('libraryPhotos.unavailable')}
                  />
                )
              ) : (
                viewport && (
                  <Typography.Paragraph type="secondary" style={{ fontSize: 12, margin: '2px 0 0' }}>
                    {viewport}
                  </Typography.Paragraph>
                )
              )}
              {/* A viewport that stopped at this installation's own point limit, which on a screen
                  looks exactly like a viewport that ended. Only this says which happened, and
                  without it an operator reading a partly drawn valley concludes the library holds
                  that many photographs. Keyed off the flag and never off how many were held back:
                  a library asked for a clamped count truncates with nothing left to count, so a
                  line keyed off the number would go quiet for the very case it exists for. */}
              {state?.truncated && (
                <Alert
                  type="info"
                  showIcon
                  style={{ marginTop: 4 }}
                  data-testid={`library-photos-truncated-${library.source}`}
                  title={t('libraryPhotos.truncated', { count: state.shownCount })}
                />
              )}
              {/* Not a failure and not a probe result: this application's own gate, closed by a
                  library answering a picture request without a picture. Said out loud because the
                  map simply stops showing photographs, and the way back is the button below. */}
              {health && !health.picturesAvailable && (
                <Typography.Paragraph type="secondary" style={{ fontSize: 12, margin: '2px 0 0' }}>
                  {t('libraryPhotos.health.picturesStopped')}
                </Typography.Paragraph>
              )}
              {/* When this line was read, and what the library says it is. An operator who has just
                  restarted a container is asking exactly this, and a health line that describes a
                  minute ago while looking like now is what the moment prevents. */}
              {health?.probedAt && (
                <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                  {health.version
                    ? t('libraryPhotos.health.versionChecked', {
                        version: health.version,
                        when: new Date(health.probedAt).toLocaleTimeString(i18n.resolvedLanguage),
                      })
                    : t('libraryPhotos.health.checked', {
                        when: new Date(health.probedAt).toLocaleTimeString(i18n.resolvedLanguage),
                      })}
                </Typography.Text>
              )}
              {/* Unconditional wherever anything has been read. One library answers a rectangle
                  live and another answers from a reading of its whole library taken earlier, and
                  "how old are these positions" has to be answered the same way for both — a line
                  that appeared only for the cached one would quietly decline the question for the
                  other. While a library is not answering, the pins are the last positions it gave,
                  which is exactly when this line earns its space. */}
              {state?.readAt && (
                <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                  {t('libraryPhotos.readAt', {
                    when: new Date(state.readAt).toLocaleString(i18n.resolvedLanguage),
                  })}
                </Typography.Text>
              )}
              {/* Pins or the photographs themselves. Per library, because one may be worth looking
                  at as pictures while the other is not, and because the two answer from libraries
                  with different amounts in them. */}
              <div style={{ marginTop: 4 }}>
                <Checkbox
                  checked={state?.pictures ?? false}
                  onChange={(e) => setLibraryPhotoPictures(library.source, e.target.checked)}
                >
                  <span style={{ fontSize: 12 }}>{t('libraryPhotos.asPictures')}</span>
                </Checkbox>
                {/* Said out loud rather than left looking broken. A switch that silently does
                    nothing is worse than no switch, and this one stops working exactly when the map
                    is busiest — which is when somebody is most likely to assume it failed. */}
                {state?.picturesSuppressed && (
                  <Typography.Paragraph type="secondary" style={{ fontSize: 12, margin: '2px 0 0' }}>
                    {t('libraryPhotos.picturesSuppressed')}
                  </Typography.Paragraph>
                )}
              </div>
              {/* The one act an operator can take from here: ask the library again. It re-opens
                  the picture path this application closed and makes the server forget what it last
                  heard, so a fix made on the other side shows up in the lines above rather than
                  waiting out a window nobody can see. */}
              <Button
                size="small"
                style={{ marginTop: 4 }}
                loading={checking}
                onClick={() => recheck.mutate(library.source)}
                data-testid={`library-photos-recheck-${library.source}`}
              >
                {t('libraryPhotos.recheck')}
              </Button>
              {/* The lines above are re-read either way — the server forgets what it last heard
                  whether or not the check succeeded — but what the check itself ran into appears
                  nowhere in them, and an operator watching a health line stay exactly as it was is
                  watching a button that did nothing. Said here instead. */}
              {recheck.isError && recheck.variables === library.source && (
                <Typography.Paragraph type="secondary" style={{ fontSize: 12, margin: '2px 0 0' }}>
                  {/* A library that answered promptly and refused the credential is not a library
                      that did not answer, and telling an operator it is down sends them to the
                      container while the thing to fix is on the library's own settings screen. */}
                  {recheckCode === CREDENTIAL_REFUSED
                    ? t('libraryPhotos.health.recheckRefused')
                    : t('libraryPhotos.health.recheckFailed')}
                </Typography.Paragraph>
              )}
            </div>
          );
        })}
      {/* What an empty layer panel cannot say on its own: that there is nothing to look at because
          nothing was connected. Drawn from what the server sent, which is this list for a full
          administrator and an empty one for everybody else — the decision about who is told which
          products an installation could run is the server's, not a panel's. */}
      {unconfiguredPhotoLibraries.map((library) => (
        <Typography.Paragraph
          key={library.source}
          type="secondary"
          style={{ fontSize: 12, margin: '8px 0 0' }}
          data-testid={`library-photos-absent-${library.source}`}
        >
          {t('libraryPhotos.notConnected', { library: library.name })}
        </Typography.Paragraph>
      ))}
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
