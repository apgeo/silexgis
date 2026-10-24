// SPDX-License-Identifier: AGPL-3.0-or-later
import { useUiPrefsStore } from '../../../stores/uiPrefsStore.ts';
import { useWorkspacePanesStore } from '../../../stores/workspacePanesStore.ts';
import { DEFAULT_ARRANGEMENT, sanitizeSavedWorkspace, type SavedWorkspace } from '../../../stores/workspacePrefs.ts';
import type { WorkspaceSnapshot } from './workspacePresets.ts';

/** Opens the 3D scene in a window of its own — the same window the map's pop-out button opens. */
export function openScene3dWindow(): void {
  window.open('/panel/scene3d', 'silexgis-scene3d', 'popup,width=1100,height=800');
}

/** The workspace as it is now, in the shape a preset is compared with. */
export function currentSnapshot(): WorkspaceSnapshot {
  const prefs = useUiPrefsStore.getState();
  const panes = useWorkspacePanesStore.getState();
  return {
    arrangement: prefs.workspace ?? DEFAULT_ARRANGEMENT,
    docks: prefs.docks,
    scene3d: panes.scene3dOpen ? 'open' : 'closed',
    caveViewPane: panes.caveViewPaneOpen || panes.caveViewModelId !== null,
  };
}

/**
 * Puts a whole arrangement in place: where the panes go, the docks, and which panes are up.
 *
 * `mayOpenWindow` is whether this runs in answer to somebody pressing something. A browser opens a
 * window only then, so an arrangement applied on its own — an installation's starting preset, on
 * a first visit — leaves a scene meant for a window shut rather than asking for a window the
 * browser will refuse.
 *
 * The strip's height is kept: it is how tall somebody likes it, not part of any one arrangement.
 */
export function applyWorkspaceSnapshot(snapshot: WorkspaceSnapshot, mayOpenWindow: boolean): void {
  const prefs = useUiPrefsStore.getState();
  const panes = useWorkspacePanesStore.getState();
  const bottomSize = prefs.workspace?.bottomSize;
  prefs.setWorkspace({ ...snapshot.arrangement, ...(bottomSize === undefined ? {} : { bottomSize }) });
  prefs.setDocks(snapshot.docks);
  panes.setMaximised(null);
  if (!snapshot.caveViewPane) {
    panes.setCaveViewModelId(null);
  }
  panes.setCaveViewPaneOpen(snapshot.caveViewPane);
  if (snapshot.scene3d === 'window') {
    panes.setScene3dOpen(false);
    if (mayOpenWindow) {
      openScene3dWindow();
    }
  } else {
    panes.setScene3dOpen(snapshot.scene3d === 'open');
  }
}

/** The workspace as a named layout keeps it. */
export function savedWorkspaceNow(): SavedWorkspace {
  const prefs = useUiPrefsStore.getState();
  return {
    arrangement: { ...(prefs.workspace ?? DEFAULT_ARRANGEMENT) },
    docks: { ...prefs.docks },
    scene3dOpen: useWorkspacePanesStore.getState().scene3dOpen,
  };
}

/** Saves the panels and the workspace around the map under a name. */
export function saveCurrentLayout(name: string): void {
  const prefs = useUiPrefsStore.getState();
  prefs.saveLayout(name, prefs.mapChromeHidden, savedWorkspaceNow());
}

/**
 * Applies a named layout: its panels, and — for a layout that holds one — its workspace, including
 * the 3D scene it was saved with. A layout from before layouts held the workspace changes the
 * panels alone, exactly as it did when it was saved.
 */
export function applySavedLayout(id: string): void {
  const prefs = useUiPrefsStore.getState();
  const layout = prefs.layouts.find((candidate) => candidate.id === id);
  if (!layout) {
    return;
  }
  prefs.applyLayout(id);
  const workspace = sanitizeSavedWorkspace(layout.workspace);
  if (workspace) {
    const panes = useWorkspacePanesStore.getState();
    panes.setMaximised(null);
    panes.setScene3dOpen(workspace.scene3dOpen);
  }
}
