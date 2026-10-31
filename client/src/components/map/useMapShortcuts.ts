// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo } from 'react';
import { useShortcuts } from '../../hooks/useShortcuts.ts';
import { MAP_WORKSPACE_SHORTCUTS } from '../../hooks/shortcutRegistry.ts';
import { useWorkspaceStore } from '../../stores/workspaceStore.ts';
import { useWorkspacePanesStore, type WorkspacePaneId } from '../../stores/workspacePanesStore.ts';

interface Args {
  isMobile: boolean;
  layersCollapsed: boolean;
  detailsCollapsed: boolean;
  leftDrawerOpen: boolean;
  rightDrawerOpen: boolean;
  swapped: boolean;
  rightPinned: boolean;
  setShortcutHelpOpen: (open: boolean) => void;
  setSelection: (selection: null) => void;
  toggleLeftDock: () => void;
  toggleRightDock: () => void;
}

/** The map workspace's keyboard shortcuts. */

export function useMapShortcuts(a: Args) {
  const {
    isMobile, layersCollapsed, detailsCollapsed, leftDrawerOpen, rightDrawerOpen, swapped, rightPinned,
    setShortcutHelpOpen, setSelection, toggleLeftDock, toggleRightDock,
  } = a;
  useShortcuts(
    useMemo(
      () => [
        { ...MAP_WORKSPACE_SHORTCUTS.help, run: () => setShortcutHelpOpen(true) },
        // Escape clears the selection — but only when nothing modal is open, which the hook
        // decides rather than each caller guessing.
        {
          ...MAP_WORKSPACE_SHORTCUTS.clearSelection,
          run: () => {
            setSelection(null);
            useWorkspaceStore.getState().clearSelectionSet();
          },
        },
        { ...MAP_WORKSPACE_SHORTCUTS.toggleLeftDock, run: () => toggleLeftDock() },
        { ...MAP_WORKSPACE_SHORTCUTS.toggleRightDock, run: () => toggleRightDock() },
        { ...MAP_WORKSPACE_SHORTCUTS.selectionBack, run: () => useWorkspaceStore.getState().goBackSelection() },
        {
          ...MAP_WORKSPACE_SHORTCUTS.selectionForward,
          run: () => useWorkspaceStore.getState().goForwardSelection(),
        },
        // The pane focus is in fills the workspace — the map when focus is in none of them — and
        // the same key gives every pane its own size back. Not on a phone, which has one pane.
        ...(isMobile
          ? []
          : [
              {
                ...MAP_WORKSPACE_SHORTCUTS.maximise,
                run: () => {
                  const panes = useWorkspacePanesStore.getState();
                  if (panes.maximised !== null) {
                    panes.setMaximised(null);
                    return;
                  }
                  const focused = document.activeElement?.closest('[data-workspace-pane]');
                  panes.setMaximised(
                    (focused?.getAttribute('data-workspace-pane') as WorkspacePaneId | null) ?? 'map',
                  );
                },
              },
            ]),
      ],
      // The dock toggles read state that changes with the layout; rebuilding the list when it
      // does is what keeps a shortcut from acting on a stale idea of which host is mounted.
      // eslint-disable-next-line react-hooks/exhaustive-deps
      [isMobile, layersCollapsed, detailsCollapsed, leftDrawerOpen, rightDrawerOpen, swapped, rightPinned],
    ),
  );
}
