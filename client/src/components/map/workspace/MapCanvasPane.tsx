// SPDX-License-Identifier: AGPL-3.0-or-later
import type { RefObject } from 'react';
import GeoLocationButton from '@terrestris/react-geo/dist/Button/GeoLocationButton/GeoLocationButton';
import { App, Tooltip } from 'antd';
import {
  AimOutlined,
  BorderVerticleOutlined,
  CodeSandboxOutlined,
  ExportOutlined,
  EyeInvisibleOutlined,
  EyeOutlined,
  FullscreenExitOutlined,
  FullscreenOutlined,
  GlobalOutlined,
  LeftOutlined,
  ProfileOutlined,
  QuestionCircleOutlined,
  RightOutlined,
  SelectOutlined,
} from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { isGenericFeatureType, type useFeatureTypes } from '../../../api/hooks.ts';
import Button from '../../Button.tsx';
import { MAP_CANVAS_ID } from '../../mapCanvasId.ts';
import QuickFilterChips from '../../filters/QuickFilterChips.tsx';
import { getBaseLayerId, type getBaseLayers } from '../../../map/baseLayers.ts';
import type { MapContextMenuTarget } from '../../../map/contextMenu.ts';
import { getWorkspaceMap } from '../../../map/mapContext.ts';
import { type DrawShape, type MapEditController } from '../../../map/mapEdit.ts';
import type { MapFilter } from '../../../map/mapFilters.ts';
import { useUiPrefsStore } from '../../../stores/uiPrefsStore.ts';
import { useWorkspacePanesStore, type WorkspacePaneId } from '../../../stores/workspacePanesStore.ts';
import BaseLayerChooser from '../BaseLayerChooser.tsx';
import CenterlineWithheldNotice from '../CenterlineWithheldNotice.tsx';
import EditToolbar from '../EditToolbar.tsx';
import { drawShapeForType } from '../featureTypeGroups.ts';
import MapContextMenu from '../MapContextMenu.tsx';
import MapFilterEditor from '../MapFilterEditor.tsx';
import MapFilterNotice from '../MapFilterNotice.tsx';
import MapListInViewButton from '../MapListInViewButton.tsx';
import MapObjectSelector from '../MapObjectSelector.tsx';
import MapSearch from '../MapSearch.tsx';
import ScaleBox from '../ScaleBox.tsx';
import TerrainProfileTool from '../TerrainProfileTool.tsx';
import type { useEditScope } from '../useEditScope.ts';
import type { PaneRoot } from './paneRoot.ts';
import WorkspaceMenu from './WorkspaceMenu.tsx';

export interface MapCanvasPaneProps {
  paneRoot: PaneRoot;
  toggleMaximised: (pane: WorkspacePaneId) => void;
  isMobile: boolean;
  /** The element the shared map is attached to. */
  mapTarget: RefObject<HTMLDivElement | null>;
  mapFilter: MapFilter | null;
  adoptMapFilter: (next: MapFilter | null) => void;
  unreadableFilterLink: boolean;
  onDismissUnreadableLink: () => void;
  filterRefused: boolean;
  editDirty: number;
  leftHidden: boolean;
  rightHidden: boolean;
  layersHidden: boolean;
  toggleLeftDock: () => void;
  toggleRightDock: () => void;
  centerlinesVisible: boolean;
  openLayerPanelAtCenterlines: () => void;
  baseOlLayers: ReturnType<typeof getBaseLayers>;
  swipeActive: boolean;
  swipePos: number;
  onSwipeHandleDown: (event: React.PointerEvent<HTMLDivElement>) => void;
  onToggleSwipe: () => void;
  hasVisibleRasters: boolean;
  onOpenShortcutHelp: () => void;
  mayReadTerrain: boolean;
  toolbarShown: boolean;
  editController: MapEditController | null;
  mayReshapeSaved: ReturnType<typeof useEditScope>['mayReshapeSaved'];
  rightsPending: ReturnType<typeof useEditScope>['rightsPending'];
  contextTarget: MapContextMenuTarget | null;
  onContextTargetChange: (target: MapContextMenuTarget | null) => void;
  featureTypes: ReturnType<typeof useFeatureTypes>['data'];
  canEdit: ReturnType<typeof useEditScope>['canEdit'];
}

/** The map's own pane: the canvas, the search and filter chrome, and the buttons laid over it. */
export default function MapCanvasPane({
  paneRoot,
  toggleMaximised,
  isMobile,
  mapTarget,
  mapFilter,
  adoptMapFilter,
  unreadableFilterLink,
  onDismissUnreadableLink,
  filterRefused,
  editDirty,
  leftHidden,
  rightHidden,
  layersHidden,
  toggleLeftDock,
  toggleRightDock,
  centerlinesVisible,
  openLayerPanelAtCenterlines,
  baseOlLayers,
  swipeActive,
  swipePos,
  onSwipeHandleDown,
  onToggleSwipe,
  hasVisibleRasters,
  onOpenShortcutHelp,
  mayReadTerrain,
  toolbarShown,
  editController,
  mayReshapeSaved,
  rightsPending,
  contextTarget,
  onContextTargetChange,
  featureTypes,
  canEdit,
}: MapCanvasPaneProps) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const mapChromeHidden = useUiPrefsStore((s) => s.mapChromeHidden);
  const setMapChromeHidden = useUiPrefsStore((s) => s.setMapChromeHidden);
  const maximised = useWorkspacePanesStore((s) => s.maximised);
  const scene3dOpen = useWorkspacePanesStore((s) => s.scene3dOpen);
  const setScene3dOpen = useWorkspacePanesStore((s) => s.setScene3dOpen);
  const mapMaximiseLabel =
    maximised === 'map'
      ? t('workspace.restore', { name: t('workspace.mapName') })
      : t('workspace.maximise', { name: t('workspace.mapName') });
  return (
  <div
    {...paneRoot(
      'map',
      [
        'map-canvas-wrap',
        mapChromeHidden ? 'map-chrome-hidden' : '',
        isMobile ? 'map-canvas-wrap-mobile' : '',
      ]
        .filter(Boolean)
        .join(' '),
    )}
  >
    {/* Focusable, because OpenLayers listens for its keyboard pan and zoom on this element
        and nowhere else: without a place in the tab order the map could only be moved with
        a pointer. An application to assistive technology, so that a screen reader hands the
        arrow keys to the map instead of using them to read the page. */}
    <div
      ref={mapTarget}
      id={MAP_CANVAS_ID}
      className="map-canvas"
      data-testid="map-canvas"
      tabIndex={0}
      role="application"
      aria-label={t('a11y.map')}
    />
    {/* Search is the primary action on a phone: it gets the width the pop-out
        buttons no longer need. The selector sits beside it — search asks what mentions
        these words, the selector asks which of these things you mean — and stacks under
        it on a phone, where they cannot both have the strip. */}
    <div className={`map-search-overlay map-chrome${isMobile ? ' map-search-overlay-mobile' : ''}`}>
      <MapSearch fullWidth={isMobile} />
      <MapObjectSelector fullWidth={isMobile} />
      {/* A row of its own under search: on the first row the button ran on past the
          selector, under the pop-out buttons pinned at the top right, which then took
          every press meant for it. */}
      <div className="map-filter-row">
        <MapListInViewButton filter={mapFilter} />
        <MapFilterEditor filter={mapFilter} onChange={(next) => adoptMapFilter(next)} />
        {/* The account's pinned saved filters: one click narrows the main entrance and
            feature layers to one of them, a second click lifts it again. */}
        <QuickFilterChips
          className="map-quick-filters"
          activeId={mapFilter?.kind === 'saved' ? mapFilter.savedFilterId : null}
          onApply={(filter) => adoptMapFilter({ kind: 'saved', savedFilterId: filter.id })}
          onRelease={() => adoptMapFilter(null)}
          data-testid="map-quick-filters"
        />
      </div>
      <MapFilterNotice
        filter={mapFilter}
        unreadableLink={unreadableFilterLink}
        onDismissUnreadableLink={onDismissUnreadableLink}
        refused={filterRefused}
        onClear={() => adoptMapFilter(null)}
      />
    </div>
    <Tooltip title={mapChromeHidden ? t('map.showChrome') : t('map.hideChrome')} placement="left">
      <Button
        className="map-chrome-toggle"
        size="small"
        aria-label={mapChromeHidden ? t('map.showChrome') : t('map.hideChrome')}
        icon={mapChromeHidden ? <EyeOutlined /> : <EyeInvisibleOutlined />}
        onClick={() => setMapChromeHidden(!mapChromeHidden)}
        data-testid="map-chrome-toggle"
      />
    </Tooltip>
    {mapChromeHidden && editDirty > 0 && (
      <Button
        className="map-dirty-pill"
        size="small"
        type="primary"
        onClick={() => setMapChromeHidden(false)}
        data-testid="map-dirty-pill"
      >
        {t('map.unsavedEdits', { count: editDirty })}
      </Button>
    )}
    <Tooltip title={leftHidden ? t('map.showPanel') : t('map.hidePanel')} placement="right">
      <Button
        className="map-dock-toggle map-dock-toggle-left"
        size="small"
        aria-label={leftHidden ? t('map.showPanel') : t('map.hidePanel')}
        icon={leftHidden ? <RightOutlined /> : <LeftOutlined />}
        onClick={toggleLeftDock}
        data-testid="map-dock-toggle-left"
      />
    </Tooltip>
    <Tooltip title={rightHidden ? t('map.showPanel') : t('map.hidePanel')} placement="left">
      <Button
        className="map-dock-toggle map-dock-toggle-right"
        size="small"
        aria-label={rightHidden ? t('map.showPanel') : t('map.hidePanel')}
        icon={rightHidden ? <LeftOutlined /> : <RightOutlined />}
        onClick={toggleRightDock}
        data-testid="map-dock-toggle-right"
      />
    </Tooltip>
    {/* The bottom-left corner holds a column: the survey-line notice over the scale box,
        over OpenLayers' own scale line. One column rather than two boxes each placed by a
        guess at the other's height, so the notice moves up when the scale box grows — it
        does, by a line, when a typed scale is refused — and never lies over it; its width
        stops short of the background chooser and the attribution in the opposite corner.
        The notice comes after the map in the tab order, beside the dock toggles it is a
        shortcut for, so it never lengthens the way from the page start to the map. */}
    <div className={`map-corner-stack${isMobile ? ' map-corner-stack-mobile' : ''}`}>
      <CenterlineWithheldNotice
        centerlinesVisible={centerlinesVisible}
        panelHidden={layersHidden}
        chromeHidden={mapChromeHidden}
        onOpenPanel={openLayerPanelAtCenterlines}
      />
      {/* Jump-to-scale is niche clutter at phone size; OL's own scale line stays. */}
      {!isMobile && (
        <div className="map-scale-overlay map-chrome">
          <ScaleBox map={getWorkspaceMap()} />
        </div>
      )}
    </div>
    {baseOlLayers.length > 0 && (
      <BaseLayerChooser
        layers={baseOlLayers}
        backgroundLayerFilter={(l) => getBaseLayerId(l) !== undefined}
        buttonTooltip={t('map.changeBaseLayer')}
      />
    )}
    {swipeActive && (
      <div
        className="map-swipe-handle"
        style={{ left: `calc(${(swipePos * 100).toFixed(2)}% - 2px)` }}
        onPointerDown={onSwipeHandleDown}
        data-testid="raster-swipe-handle"
      />
    )}
    <div className="map-popout-overlay map-chrome">
      {hasVisibleRasters && (
        <Tooltip title={t('map.swipeCompare')}>
          <Button
            size="small"
            type={swipeActive ? 'primary' : 'default'}
            icon={<BorderVerticleOutlined />}
            aria-label={t('map.swipeCompare')}
            aria-pressed={swipeActive}
            onClick={onToggleSwipe}
            data-testid="raster-swipe-toggle"
          />
        </Tooltip>
      )}
      <GeoLocationButton
        size="small"
        icon={<AimOutlined />}
        pressedIcon={<AimOutlined />}
        tooltip={t('map.locateMe')}
        aria-label={t('map.locateMe')}
        showMarker
        follow
        enableTracking
        onError={() => message.error(t('map.geolocationFailed'))}
      />
      {/* Pop-out windows are meaningless on a phone; the 3D viewer stays reachable
          from the cave detail page. */}
      {!isMobile && (
        <>
          <WorkspaceMenu />
          <Tooltip title={`${mapMaximiseLabel} (M)`}>
            <Button
              size="small"
              type={maximised === 'map' ? 'primary' : 'default'}
              icon={maximised === 'map' ? <FullscreenExitOutlined /> : <FullscreenOutlined />}
              aria-label={mapMaximiseLabel}
              aria-pressed={maximised === 'map'}
              aria-keyshortcuts="M"
              onClick={() => toggleMaximised('map')}
              data-testid="map-maximise"
            />
          </Tooltip>
          <Tooltip title={scene3dOpen ? t('map.hideScene3d') : t('map.showScene3d')}>
            <Button
              size="small"
              type={scene3dOpen ? 'primary' : 'default'}
              icon={<GlobalOutlined />}
              aria-label={scene3dOpen ? t('map.hideScene3d') : t('map.showScene3d')}
              aria-pressed={scene3dOpen}
              onClick={() => setScene3dOpen(!scene3dOpen)}
              data-testid="map-scene3d-toggle"
            />
          </Tooltip>
          <Tooltip title={t('panel.popOut')}>
            <Button
              size="small"
              icon={<ExportOutlined />}
              aria-label={t('panel.popOut')}
              onClick={() => window.open('/panel/registry', 'silexgis-registry', 'popup,width=900,height=700')}
              data-testid="map-popout-registry"
            />
          </Tooltip>
          <Tooltip title={t('panel.popOutScene3d')}>
            <Button
              size="small"
              icon={<SelectOutlined />}
              aria-label={t('panel.popOutScene3d')}
              onClick={() => window.open('/panel/scene3d', 'silexgis-scene3d', 'popup,width=1100,height=800')}
              data-testid="map-popout-scene3d"
            />
          </Tooltip>
          <Tooltip title={t('panel.popOutSelection')}>
            <Button
              size="small"
              icon={<ProfileOutlined />}
              aria-label={t('panel.popOutSelection')}
              onClick={() =>
                window.open('/panel/selection', 'silexgis-selection', 'popup,width=520,height=900')}
              data-testid="map-popout-selection"
            />
          </Tooltip>
          <Tooltip title={t('panel.popOut3d')}>
            <Button
              size="small"
              icon={<CodeSandboxOutlined />}
              aria-label={t('panel.popOut3d')}
              onClick={() => window.open('/panel/viewer3d', 'silexgis-viewer3d', 'popup,width=1000,height=750')}
              data-testid="map-popout-viewer3d"
            />
          </Tooltip>
          <Tooltip title={t('shortcuts.open')}>
            <Button
              size="small"
              icon={<QuestionCircleOutlined />}
              aria-label={t('shortcuts.title')}
              onClick={onOpenShortcutHelp}
              data-testid="map-shortcut-help"
            />
          </Tooltip>
        </>
      )}
    </div>
    {mayReadTerrain && <TerrainProfileTool />}
    {toolbarShown && editController && (
      <div className={`map-edit-overlay map-chrome${isMobile ? ' map-edit-overlay-mobile' : ''}`}>
        <EditToolbar controller={editController} mayModify={mayReshapeSaved} rightsPending={rightsPending} />
      </div>
    )}
    <MapContextMenu
      target={contextTarget}
      featureTypes={(featureTypes ?? []).filter(isGenericFeatureType)}
      canEdit={canEdit}
      onClose={() => {
        // A menu the keyboard opened hands focus back to the map it was opened from; a
        // dialog an item opens takes it from there, and gives it back to the map in turn.
        if (contextTarget?.viaKeyboard) {
          getWorkspaceMap().getTargetElement()?.focus();
        }
        onContextTargetChange(null);
      }}
      onAddFeature={(typeId, lonLat) => {
        const picked = featureTypes?.find((ft) => Number(ft.id) === typeId);
        const shape: DrawShape = drawShapeForType(picked) ?? 'Point';
        if (shape === 'LineString' || shape === 'Polygon') {
          // Multi-click shapes start at the user's next clicks; just arm the tool.
          editController?.setMode('draw', shape, typeId);
        } else {
          // Point types land exactly where the menu was opened.
          editController?.placePointAt(lonLat, typeId);
        }
      }}
      onPlace={(mode, lonLat) => editController?.requestPlacement(mode, lonLat)}
    />
  </div>
  );
}
