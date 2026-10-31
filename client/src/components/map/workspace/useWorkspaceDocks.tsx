// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, type Dispatch, type RefObject, type SetStateAction } from 'react';
import type { TFunction } from 'i18next';
import { FullscreenExitOutlined } from '@ant-design/icons';
import type { usePanelRef } from 'react-resizable-panels';
import Button from '../../Button.tsx';
import { focusCenterlineStatus } from '../centerlineStatus.ts';
import { openModelWindow } from '../../../caveview/openModelWindow.ts';
import type { SurveyModelInfo } from '../../../api/hooks.ts';
import type { WorkspacePaneId } from '../../../stores/workspacePanesStore.ts';
import type { WorkspaceArrangement } from '../../../stores/workspacePrefs.ts';
import { openScene3dWindow } from './workspaceActions.ts';
import type { PaneDestination } from './PaneHeader.tsx';

/** Which of the two docks a side holds. */
export type DockContent = 'layers' | 'details';

type PanelRef = ReturnType<typeof usePanelRef>;
type Flag = Dispatch<SetStateAction<boolean>>;

export interface DocksArgs {
  t: TFunction;
  isMobile: boolean;
  arrangement: WorkspaceArrangement;
  layersCollapsed: boolean;
  detailsCollapsed: boolean;
  setDetailsCollapsed: (hidden: boolean) => void;
  leftDrawerOpen: boolean;
  rightDrawerOpen: boolean;
  setLeftDrawerOpen: Flag;
  setRightDrawerOpen: Flag;
  layersPanelRef: PanelRef;
  detailsPanelRef: PanelRef;
  workspaceGroupRef: RefObject<HTMLDivElement | null>;
  caveViewModel: SurveyModelInfo | undefined;
  caveViewPaneOpen: boolean;
  scene3dOpen: boolean;
  maximised: WorkspacePaneId | null;
  setMaximised: (pane: WorkspacePaneId | null) => void;
  rightPinned: boolean;
  setWorkspace: (arrangement: WorkspaceArrangement) => void;
  setScene3dOpen: (open: boolean) => void;
  setCaveViewModelId: (id: string | null) => void;
  setCaveViewPaneOpen: (open: boolean) => void;
}

/** The docks' sides, folding and the workspace panes' maximise/move handling. */

export function useWorkspaceDocks(a: DocksArgs) {
  const {
    t, isMobile, arrangement, layersCollapsed, detailsCollapsed, setDetailsCollapsed, leftDrawerOpen,
    rightDrawerOpen, setLeftDrawerOpen, setRightDrawerOpen, layersPanelRef, detailsPanelRef,
    workspaceGroupRef, caveViewModel, caveViewPaneOpen, scene3dOpen, maximised, setMaximised,
    rightPinned, setWorkspace, setScene3dOpen, setCaveViewModelId, setCaveViewPaneOpen,
  } = a;
  // Which dock is on which side. Swapping is a desktop arrangement only: on a phone the docks are
  // drawers opened from the edge buttons, and the layer drawer stays the one on the left.
  const swapped = !isMobile && arrangement.docksSwapped;
  const dockOnSide = (side: 'left' | 'right'): DockContent =>
    (side === 'left') !== swapped ? 'layers' : 'details';
  const sideOfDock = (dock: DockContent): 'left' | 'right' =>
    dockOnSide('left') === dock ? 'left' : 'right';

  // One notion of "is this dock showing" across both layouts, so the toggle buttons keep
  // their icon and label truthful whichever host is mounted.
  const dockHidden = (dock: DockContent) =>
    isMobile
      ? !(dock === 'layers' ? leftDrawerOpen : rightDrawerOpen)
      : dock === 'layers'
        ? layersCollapsed
        : detailsCollapsed;
  const leftHidden = dockHidden(dockOnSide('left'));
  const rightHidden = dockHidden(dockOnSide('right'));
  const layersHidden = dockHidden('layers');

  const toggleDock = (dock: DockContent) => {
    if (isMobile) {
      if (dock === 'layers') {
        setLeftDrawerOpen((open) => !open);
      } else {
        setRightDrawerOpen((open) => !open);
      }
      return;
    }
    const panel = dock === 'layers' ? layersPanelRef.current : detailsPanelRef.current;
    const collapsed = dock === 'layers' ? layersCollapsed : detailsCollapsed;
    if (!panel) {
      // The details panel floating over the map rather than docked: its drawer follows the flag.
      if (dock === 'details') {
        setDetailsCollapsed(!detailsCollapsed);
      }
      return;
    }
    if (collapsed) {
      panel.expand();
    } else {
      panel.collapse();
    }
  };
  // The bracket keys and the edge buttons are about sides, which is what a person sees: after a
  // swap, `[` folds whatever is on the left.
  const toggleLeftDock = () => toggleDock(dockOnSide('left'));
  const toggleRightDock = () => toggleDock(dockOnSide('right'));

  // Opens (never closes) the layer panel and takes focus to its survey-line block, where the
  // withheld-lines alert and the line budget are.
  const openLayerPanelAtCenterlines = () => {
    if (isMobile) {
      setLeftDrawerOpen(true);
    } else {
      layersPanelRef.current?.expand();
    }
    focusCenterlineStatus();
  };

  // The survey-model pane is up while it holds a model, or while a preset has put it up empty.
  const caveViewShown = !isMobile && (caveViewModel !== undefined || caveViewPaneOpen);
  const tableShown = !isMobile && arrangement.table;
  const sceneShown = !isMobile && scene3dOpen;

  // A pane that went away cannot stay filling the workspace, and a phone has no panes to fill it.
  useEffect(() => {
    const gone =
      (maximised === 'scene3d' && !sceneShown) ||
      (maximised === 'caveview' && !caveViewShown) ||
      (maximised === 'table' && !tableShown) ||
      (maximised === 'details' && !rightPinned && detailsCollapsed);
    if (maximised !== null && (isMobile || gone)) {
      setMaximised(null);
    }
  }, [maximised, isMobile, sceneShown, caveViewShown, tableShown, rightPinned, detailsCollapsed, setMaximised]);

  // A pane filling the workspace is drawn over the others at the workspace's own box, which is
  // followed while it is filled: the window can be resized, or the navigation rail opened, under it.
  useEffect(() => {
    const group = workspaceGroupRef.current;
    if (maximised === null || !group) {
      return;
    }
    const place = () => {
      const box = group.getBoundingClientRect();
      group.style.setProperty('--workspace-top', `${box.top}px`);
      group.style.setProperty('--workspace-left', `${box.left}px`);
      group.style.setProperty('--workspace-width', `${box.width}px`);
      group.style.setProperty('--workspace-height', `${box.height}px`);
    };
    place();
    const observer = new ResizeObserver(place);
    observer.observe(group);
    window.addEventListener('resize', place);
    return () => {
      observer.disconnect();
      window.removeEventListener('resize', place);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps -- a ref object passed in is stable
  }, [maximised]);

  const toggleMaximised = (pane: WorkspacePaneId) => setMaximised(maximised === pane ? null : pane);
  // What a pane's root carries: its name, for the maximise key to find which pane focus is in, and
  // — while another pane fills the workspace — `inert`, so the panes drawn over cannot be reached
  // by Tab or a screen reader while they cannot be seen.
  const paneRoot = (pane: WorkspacePaneId, className: string) => ({
    className: [className, maximised === pane ? 'workspace-pane-maximised' : ''].filter(Boolean).join(' '),
    'data-workspace-pane': pane,
    inert: maximised !== null && maximised !== pane ? true : undefined,
  });
  // The docks have no strip of their own to put a restore button in, so one floats in the corner.
  const restoreFloat = (pane: WorkspacePaneId, name: string) =>
    maximised === pane ? (
      <Button
        className="workspace-restore-float"
        size="small"
        icon={<FullscreenExitOutlined />}
        aria-label={t('workspace.restore', { name })}
        aria-keyshortcuts="M"
        onClick={() => setMaximised(null)}
        data-testid={`workspace-${pane}-restore`}
      >
        {t('workspace.restoreShort')}
      </Button>
    ) : null;

  const moveScene3d = (destination: PaneDestination) => {
    setMaximised(null);
    if (destination === 'window') {
      // One window holds one scene: the pane shuts as the window opens.
      setScene3dOpen(false);
      openScene3dWindow();
    } else {
      setWorkspace({ ...arrangement, scene3dPlace: destination });
    }
  };
  const moveCaveView = (destination: PaneDestination) => {
    setMaximised(null);
    if (destination === 'window') {
      if (caveViewModel) {
        openModelWindow(caveViewModel.id);
      }
      setCaveViewModelId(null);
      setCaveViewPaneOpen(false);
    } else {
      setWorkspace({ ...arrangement, caveViewPlace: destination });
    }
  };
  const closeCaveView = () => {
    setCaveViewModelId(null);
    setCaveViewPaneOpen(false);
  };

  return {
    swapped, dockOnSide, sideOfDock, leftHidden, rightHidden, layersHidden, toggleLeftDock,
    toggleRightDock, openLayerPanelAtCenterlines, caveViewShown, tableShown, sceneShown,
    toggleMaximised, paneRoot, restoreFloat, moveScene3d, moveCaveView, closeCaveView,
  };
}
