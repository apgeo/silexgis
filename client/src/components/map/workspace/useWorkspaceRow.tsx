// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, type ComponentProps, type MutableRefObject, type ReactElement } from 'react';
import type { TFunction } from 'i18next';
import { Group, Panel, Separator, usePanelRef } from 'react-resizable-panels';
import type { SurveyModelInfo } from '../../../api/hooks.ts';
import type { useCompareOffer } from '../../../caveview/useCompareOffer.ts';
import { useUiPrefsStore } from '../../../stores/uiPrefsStore.ts';
import type { WorkspacePaneId } from '../../../stores/workspacePanesStore.ts';
import {
  BOTTOM_SIZE_MAX,
  BOTTOM_SIZE_MIN,
  DEFAULT_ARRANGEMENT,
  type PanePlace,
  type WorkspaceArrangement,
} from '../../../stores/workspacePrefs.ts';
import CaveViewPane from './CaveViewPane.tsx';
import MapCanvasPane from './MapCanvasPane.tsx';
import PaneSlot from './PaneSlot.tsx';
import ScenePane from './ScenePane.tsx';
import TablePane from './TablePane.tsx';
import StablePanel from './StablePanel.tsx';
import type { useWorkspaceDocks } from './useWorkspaceDocks.tsx';

type PanelRef = ReturnType<typeof usePanelRef>;

export interface RowArgs {
  t: TFunction;
  dockApi: ReturnType<typeof useWorkspaceDocks>;
  isMobile: boolean;
  arrangement: WorkspaceArrangement;
  maximised: WorkspacePaneId | null;
  docks: { leftHidden: boolean; rightHidden: boolean };
  rightPinned: boolean;
  rightWidth: number;
  setPanelPrefs: ReturnType<typeof useUiPrefsStore.getState>['setPanelPrefs'];
  setWorkspace: (arrangement: WorkspaceArrangement) => void;
  setScene3dOpen: (open: boolean) => void;
  setLayersCollapsed: (hidden: boolean) => void;
  setDetailsCollapsed: (hidden: boolean) => void;
  layersPanelRef: PanelRef;
  detailsPanelRef: PanelRef;
  relayingOutRef: MutableRefObject<boolean>;
  panelSetRef: MutableRefObject<string>;
  layerDock: ReactElement;
  rightDock: ReactElement;
  scenePaneElement: HTMLElement;
  caveViewPaneElement: HTMLElement;
  tablePaneElement: HTMLElement;
  caveViewModel: SurveyModelInfo | undefined;
  caveViewCompare: ReturnType<typeof useCompareOffer>;
  setCaveViewOverlay: (model: SurveyModelInfo | null) => void;
  setCaveViewModelId: (id: string | null) => void;
  mapCanvasProps: ComponentProps<typeof MapCanvasPane>;
}

/**
 * The workspace's row of panels: the docks, the panes sent beside the map, the map's column and the
 * strip under it, plus the three drawing panes that are portalled into their slots.
 */

export function useWorkspaceRow(a: RowArgs) {
  const {
    t, dockApi, isMobile, arrangement, maximised, docks, rightPinned, rightWidth, setPanelPrefs,
    setWorkspace, setScene3dOpen, setLayersCollapsed, setDetailsCollapsed, layersPanelRef,
    detailsPanelRef, relayingOutRef, panelSetRef, layerDock, rightDock, scenePaneElement,
    caveViewPaneElement, tablePaneElement, caveViewModel, caveViewCompare, setCaveViewOverlay,
    setCaveViewModelId, mapCanvasProps,
  } = a;
  const {
    dockOnSide, sideOfDock, caveViewShown, tableShown, sceneShown, toggleMaximised, paneRoot,
    restoreFloat, moveScene3d, moveCaveView, closeCaveView,
  } = dockApi;
  // The details panel's separator: a double-click snaps between the width somebody dragged to and
  // the one the panel ships at, which is the gesture every resizable pane has and the fastest way
  // to get the map back whole without losing the width you chose.
  const snapDetailsWidth = () => {
    const shipped = 22;
    // The pane reports both units; the stored width is a percentage, so compare in one.
    const size = detailsPanelRef.current?.getSize();
    const current = Math.round(typeof size === 'number' ? size : (size?.asPercentage ?? shipped));
    const next = current === shipped ? (rightWidth === shipped ? 34 : rightWidth) : shipped;
    detailsPanelRef.current?.resize(`${next}%`);
    setPanelPrefs('main', { width: next });
  };

  const layersDockPanel = (
    <StablePanel
      key={`dock-layers-${sideOfDock('layers')}`}
      id="ws-dock-layers"
      panelRef={layersPanelRef}
      collapsible
      collapsedSize="0%"
      defaultSize={docks.leftHidden ? '0%' : '16%'}
      minSize="10%"
      className="map-workspace-panel"
      onResize={() => {
        if (!relayingOutRef.current) {
          setLayersCollapsed(layersPanelRef.current?.isCollapsed() ?? false);
        }
      }}
    >
      <div {...paneRoot('layers', 'workspace-dock')}>
        {layerDock}
        {restoreFloat('layers', t('map.layersTitle'))}
      </div>
    </StablePanel>
  );

  // Pinned pushes the map aside; unpinned floats over it. A preference rather than a breakpoint,
  // because which one is right depends on the screen *and* on what somebody is doing — a wide
  // monitor still wants the map whole while tracing a passage.
  const detailsDockPanel = rightPinned ? (
    <StablePanel
      key={`dock-details-${sideOfDock('details')}`}
      id="ws-dock-details"
      panelRef={detailsPanelRef}
      collapsible
      collapsedSize="0%"
      defaultSize={docks.rightHidden ? '0%' : `${rightWidth}%`}
      minSize="12%"
      className="map-workspace-panel"
      onResize={(size) => {
        if (relayingOutRef.current) {
          return;
        }
        setDetailsCollapsed(detailsPanelRef.current?.isCollapsed() ?? false);
        // Remembered as the person drags, so the width survives a reload. Rounded: a stored
        // 21.7318% is noise that makes every save look like a change.
        if (typeof size === 'number' && size > 0) {
          setPanelPrefs('main', { width: Math.round(size) });
        }
      }}
    >
      <div {...paneRoot('details', 'workspace-dock')}>
        {rightDock}
        {restoreFloat('details', t('map.detailsTitle'))}
      </div>
    </StablePanel>
  ) : null;

  const dockPanelOn = (side: 'left' | 'right') =>
    dockOnSide(side) === 'layers' ? layersDockPanel : detailsDockPanel;

  // A pane beside the map or in the strip under it is a slot its content's element is shown in.
  // Keyed by place, so a move mounts a new slot — a new panel the layout library sizes afresh —
  // while what is drawn in it is never touched.
  const scene3dSlot = (place: PanePlace) => (
    <StablePanel
      key={`scene3d-${place}`}
      id={`ws-scene3d-${place}`}
      defaultSize={place === 'bottom' ? undefined : '34%'}
      minSize="20%"
      className="map-workspace-panel"
    >
      <PaneSlot element={scenePaneElement} />
    </StablePanel>
  );
  const caveViewSlot = (place: PanePlace) => (
    <StablePanel
      key={`caveview-${place}`}
      id={`ws-caveview-${place}`}
      defaultSize={place === 'bottom' ? undefined : '30%'}
      minSize="20%"
      className="map-workspace-panel"
    >
      <PaneSlot element={caveViewPaneElement} />
    </StablePanel>
  );

  // Panels with a separator between each pair, keyed by the pair, so a separator is made afresh
  // whenever what it stands between changes and the layout library never resizes a stale pair.
  const withSeparators = (panels: ReactElement[], separatorClass: string, label: string): ReactElement[] =>
    panels.flatMap((panel, index) => {
      if (index === 0) {
        return [panel];
      }
      const before = panels[index - 1];
      const nextToDetails = before.key?.startsWith('dock-details') || panel.key?.startsWith('dock-details');
      return [
        <Separator
          key={`sep:${before.key}|${panel.key}`}
          className={separatorClass}
          aria-label={label}
          inert={maximised !== null ? true : undefined}
          onDoubleClick={nextToDetails ? snapDetailsWidth : undefined}
        />,
        panel,
      ];
    });

  // Under the map: the table, then whichever of the two drawing panes was sent there.
  const bottomPanels: ReactElement[] = [
    ...(tableShown
      ? [
          <StablePanel key="table" id="ws-table" minSize="20%" className="map-workspace-panel">
            <PaneSlot element={tablePaneElement} />
          </StablePanel>,
        ]
      : []),
    ...(sceneShown && arrangement.scene3dPlace === 'bottom' ? [scene3dSlot('bottom')] : []),
    ...(caveViewShown && arrangement.caveViewPlace === 'bottom' ? [caveViewSlot('bottom')] : []),
  ];
  const bottomShown = bottomPanels.length > 0;

  const mapColumn = (
    <Panel key="center" id="ws-center" minSize="30%">
      <Group orientation="vertical" className="map-workspace-column">
        <Panel id="ws-map" minSize="30%">
          <MapCanvasPane {...mapCanvasProps} />
        </Panel>
        {bottomShown && (
          <Separator
            className="map-workspace-handle map-workspace-handle-row"
            aria-label={t('workspace.resizeBottom')}
            inert={maximised !== null ? true : undefined}
          />
        )}
        {bottomShown && (
          <StablePanel
            id="ws-bottom"
            defaultSize={`${arrangement.bottomSize ?? 35}%`}
            minSize={`${BOTTOM_SIZE_MIN}%`}
            maxSize={`${BOTTOM_SIZE_MAX}%`}
            className="map-workspace-panel"
            onResize={(size) => {
              // Remembered as it is dragged, rounded for the same reason the details width is.
              const percent = Math.round(size.asPercentage);
              const now = useUiPrefsStore.getState().workspace ?? DEFAULT_ARRANGEMENT;
              if (percent >= BOTTOM_SIZE_MIN && percent <= BOTTOM_SIZE_MAX && percent !== now.bottomSize) {
                setWorkspace({ ...now, bottomSize: percent });
              }
            }}
          >
            <Group orientation="horizontal" className="map-workspace-bottom">
              {withSeparators(bottomPanels, 'map-workspace-handle', t('workspace.resizePanes'))}
            </Group>
          </StablePanel>
        )}
      </Group>
    </Panel>
  );

  // The workspace's row, left to right: a dock, the panes sent left (the 3D scene nearest the
  // map), the map's column, the panes sent right (the 3D scene nearest the map again), a dock.
  const row: ReactElement[] = isMobile
    ? [mapColumn]
    : [
        ...[dockPanelOn('left')].filter((panel): panel is ReactElement => panel !== null),
        ...(caveViewShown && arrangement.caveViewPlace === 'left' ? [caveViewSlot('left')] : []),
        ...(sceneShown && arrangement.scene3dPlace === 'left' ? [scene3dSlot('left')] : []),
        mapColumn,
        ...(sceneShown && arrangement.scene3dPlace === 'right' ? [scene3dSlot('right')] : []),
        ...(caveViewShown && arrangement.caveViewPlace === 'right' ? [caveViewSlot('right')] : []),
        ...[dockPanelOn('right')].filter((panel): panel is ReactElement => panel !== null),
      ];

  // Which panels the row and the strip hold. When that changes the layout library lays the row out
  // afresh — from the sizes it last had for this same set of panels, or from their starting sizes —
  // and reports each dock's new size as though somebody had dragged it, so a folded dock would come
  // back unfolded and its being folded would be forgotten. For the frames that takes, a dock's report
  // is not taken as the person's choice; afterwards each dock is put back as the stored state says.
  const panelSetKey = [...row, ...bottomPanels].map((panel) => panel.key).join('|');
  if (panelSetRef.current !== panelSetKey) {
    panelSetRef.current = panelSetKey;
    relayingOutRef.current = true;
  }
  useEffect(() => {
    let second = 0;
    const first = requestAnimationFrame(() => {
      second = requestAnimationFrame(() => {
        relayingOutRef.current = false;
        const stored = useUiPrefsStore.getState().docks;
        for (const [panel, hidden] of [
          [layersPanelRef.current, stored.leftHidden],
          [detailsPanelRef.current, stored.rightHidden],
        ] as const) {
          if (panel && panel.isCollapsed() !== hidden) {
            if (hidden) {
              panel.collapse();
            } else {
              panel.expand();
            }
          }
        }
      });
    });
    return () => {
      cancelAnimationFrame(first);
      cancelAnimationFrame(second);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps -- a ref object passed in is stable
  }, [panelSetKey, layersPanelRef, detailsPanelRef]);

  const scenePane = (
    <ScenePane
      paneRoot={paneRoot}
      place={arrangement.scene3dPlace}
      onMove={moveScene3d}
      maximised={maximised === 'scene3d'}
      onToggleMaximised={() => toggleMaximised('scene3d')}
      onClose={() => setScene3dOpen(false)}
    />
  );

  const caveViewPane = (
    <CaveViewPane
      paneRoot={paneRoot}
      model={caveViewModel}
      compare={caveViewCompare}
      place={arrangement.caveViewPlace}
      onMove={moveCaveView}
      maximised={maximised === 'caveview'}
      onToggleMaximised={() => toggleMaximised('caveview')}
      onClose={closeCaveView}
      onOpenOverlay={setCaveViewOverlay}
      onOpenModel={setCaveViewModelId}
    />
  );

  const tablePane = (
    <TablePane
      paneRoot={paneRoot}
      maximised={maximised === 'table'}
      onToggleMaximised={() => toggleMaximised('table')}
      onClose={() => setWorkspace({ ...arrangement, table: false })}
    />
  );

  return { row, withSeparators, scenePane, caveViewPane, tablePane };
}
