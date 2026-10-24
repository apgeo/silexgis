// SPDX-License-Identifier: AGPL-3.0-or-later
import { afterEach, describe, expect, it, vi } from 'vitest';
import { useUiPrefsStore } from '../../../stores/uiPrefsStore.ts';
import { useWorkspacePanesStore } from '../../../stores/workspacePanesStore.ts';
import { DEFAULT_ARRANGEMENT } from '../../../stores/workspacePrefs.ts';
import {
  applySavedLayout,
  applyWorkspaceSnapshot,
  currentSnapshot,
  saveCurrentLayout,
} from './workspaceActions.ts';
import { installationPresetOf, presetById, snapshotMatches, WORKSPACE_PRESETS } from './workspacePresets.ts';

afterEach(() => {
  useUiPrefsStore.getState().releaseAccount();
  useWorkspacePanesStore.setState({
    scene3dOpen: false,
    caveViewPaneOpen: false,
    caveViewModelId: null,
    maximised: null,
  });
  vi.restoreAllMocks();
});

describe('the workspace presets', () => {
  it('offers the five arrangements, each different from every other', () => {
    expect(WORKSPACE_PRESETS.map((preset) => preset.id)).toEqual(['map', 'map3d', 'survey', 'trip', 'twoScreens']);
    for (const a of WORKSPACE_PRESETS) {
      for (const b of WORKSPACE_PRESETS) {
        if (a !== b && a.snapshot.scene3d !== 'window' && b.snapshot.scene3d !== 'window') {
          expect(snapshotMatches(a.snapshot, b.snapshot), `${a.id} against ${b.id}`).toBe(false);
        }
      }
    }
  });

  it('marks a preset in force after it is applied, and no longer once a pane is moved', () => {
    for (const preset of WORKSPACE_PRESETS.filter((candidate) => candidate.snapshot.scene3d !== 'window')) {
      applyWorkspaceSnapshot(preset.snapshot, true);
      expect(snapshotMatches(currentSnapshot(), preset.snapshot), preset.id).toBe(true);
    }
    const trip = presetById('trip')!;
    applyWorkspaceSnapshot(trip.snapshot, true);
    useUiPrefsStore.getState().setWorkspace({ ...useUiPrefsStore.getState().workspace!, scene3dPlace: 'left' });
    expect(snapshotMatches(currentSnapshot(), trip.snapshot)).toBe(false);
  });

  it('keeps the strip height across presets, and dragging it does not leave the preset', () => {
    useUiPrefsStore.getState().setWorkspace({ ...DEFAULT_ARRANGEMENT, table: true, bottomSize: 42 });
    applyWorkspaceSnapshot(presetById('trip')!.snapshot, true);
    expect(useUiPrefsStore.getState().workspace?.bottomSize).toBe(42);
    expect(snapshotMatches(currentSnapshot(), presetById('trip')!.snapshot)).toBe(true);
  });

  it('never marks the two-screens preset, which sends the scene elsewhere rather than arranging this window', () => {
    vi.spyOn(window, 'open').mockReturnValue(null);
    const twoScreens = presetById('twoScreens')!;
    applyWorkspaceSnapshot(twoScreens.snapshot, true);
    expect(snapshotMatches(currentSnapshot(), twoScreens.snapshot)).toBe(false);
    expect(snapshotMatches(currentSnapshot(), presetById('map')!.snapshot)).toBe(true);
  });

  it('opens the 3D window only when somebody pressed for it, and shuts the pane either way', () => {
    const open = vi.spyOn(window, 'open').mockReturnValue(null);
    useWorkspacePanesStore.getState().setScene3dOpen(true);
    applyWorkspaceSnapshot(presetById('twoScreens')!.snapshot, false);
    expect(open).not.toHaveBeenCalled();
    expect(useWorkspacePanesStore.getState().scene3dOpen).toBe(false);
    applyWorkspaceSnapshot(presetById('twoScreens')!.snapshot, true);
    expect(open).toHaveBeenCalledWith('/panel/scene3d', 'silexgis-scene3d', expect.any(String));
  });

  it('puts the survey pane up empty for survey review, and takes an open model down with any other preset', () => {
    applyWorkspaceSnapshot(presetById('survey')!.snapshot, true);
    expect(useWorkspacePanesStore.getState().caveViewPaneOpen).toBe(true);
    expect(useUiPrefsStore.getState().docks).toEqual({ leftHidden: true, rightHidden: false });
    useWorkspacePanesStore.getState().setCaveViewModelId('model-1');
    applyWorkspaceSnapshot(presetById('map')!.snapshot, true);
    expect(useWorkspacePanesStore.getState().caveViewModelId).toBeNull();
    expect(useWorkspacePanesStore.getState().caveViewPaneOpen).toBe(false);
  });

  it('gives back every pane its own size when a preset is applied', () => {
    useWorkspacePanesStore.getState().setMaximised('map');
    applyWorkspaceSnapshot(presetById('map3d')!.snapshot, true);
    expect(useWorkspacePanesStore.getState().maximised).toBeNull();
  });
});

describe("the installation's starting preset", () => {
  it('reads only a preset this client offers', () => {
    expect(installationPresetOf({ workspacePreset: 'trip' })?.id).toBe('trip');
    expect(installationPresetOf({ workspacePreset: 'mosaic' })).toBeUndefined();
    expect(installationPresetOf({ main: { order: [] } })).toBeUndefined();
    expect(installationPresetOf(null)).toBeUndefined();
    expect(installationPresetOf('map3d')).toBeUndefined();
  });
});

describe('a saved arrangement', () => {
  it('keeps the workspace with the panels, and brings back the 3D scene it was saved with', () => {
    useUiPrefsStore.getState().setWorkspace({ ...DEFAULT_ARRANGEMENT, table: true, docksSwapped: true });
    useUiPrefsStore.getState().setDocks({ leftHidden: true });
    useWorkspacePanesStore.getState().setScene3dOpen(true);
    saveCurrentLayout('Planning');

    applyWorkspaceSnapshot(presetById('map')!.snapshot, true);
    expect(useWorkspacePanesStore.getState().scene3dOpen).toBe(false);

    applySavedLayout(useUiPrefsStore.getState().layouts[0].id);
    expect(useUiPrefsStore.getState().workspace).toMatchObject({ table: true, docksSwapped: true });
    expect(useUiPrefsStore.getState().docks.leftHidden).toBe(true);
    expect(useWorkspacePanesStore.getState().scene3dOpen).toBe(true);
  });

  it('saved before layouts held the workspace, it changes the panels and leaves the panes alone', () => {
    useUiPrefsStore.getState().setPanelPrefs('main', { width: 30 });
    useUiPrefsStore.getState().saveLayout('Old', false);
    useUiPrefsStore.getState().setPanelPrefs('main', { width: 25 });
    useUiPrefsStore.getState().setWorkspace({ ...DEFAULT_ARRANGEMENT, table: true });
    useUiPrefsStore.getState().setDocks({ rightHidden: true });
    useWorkspacePanesStore.getState().setScene3dOpen(true);

    applySavedLayout(useUiPrefsStore.getState().layouts[0].id);
    expect(useUiPrefsStore.getState().panels.main?.width).toBe(30);
    expect(useUiPrefsStore.getState().workspace?.table).toBe(true);
    expect(useUiPrefsStore.getState().docks.rightHidden).toBe(true);
    expect(useWorkspacePanesStore.getState().scene3dOpen).toBe(true);
  });
});
