// SPDX-License-Identifier: AGPL-3.0-or-later
import type { DockPrefs } from '../../../stores/uiPrefsStore.ts';
import { DEFAULT_ARRANGEMENT, type WorkspaceArrangement } from '../../../stores/workspacePrefs.ts';

/**
 * What the 3D scene does when an arrangement is applied: open in a pane, stay shut, or open in a
 * window of its own (which also shuts the pane — one window holds one scene).
 */
export type Scene3dIntent = 'open' | 'closed' | 'window';

/** Everything one press of a preset puts in place. */
export interface WorkspaceSnapshot {
  arrangement: WorkspaceArrangement;
  docks: DockPrefs;
  scene3d: Scene3dIntent;
  /** The survey-model pane up even with no model open in it, saying how to open one. */
  caveViewPane: boolean;
}

export type WorkspacePresetId = 'map' | 'map3d' | 'survey' | 'trip' | 'twoScreens';

export interface WorkspacePreset {
  id: WorkspacePresetId;
  snapshot: WorkspaceSnapshot;
}

const BOTH_DOCKS: DockPrefs = { leftHidden: false, rightHidden: false };

/**
 * The arrangements offered with one press, in the order the menu lists them.
 *
 * Each is a whole arrangement, not a change to the current one: pressing "Map" after a day of
 * moving panes about has to give the map back as it ships, docks unswapped and the strip gone.
 *
 * - **Map** — the map between its two docks; nothing else.
 * - **Map + 3D** — the 3D scene beside the map, on the right.
 * - **Survey review** — a survey model beside the map. The layer panel folds away to give the model
 *   room; the details panel stays, because that is where a cave's survey models are listed.
 * - **Trip planning** — the map over a strip holding the table of what is in view and the 3D scene:
 *   which caves are in the area being planned, and the ground the approach crosses.
 * - **Two screens** — the map and its docks here, the 3D scene in a window of its own for a second
 *   monitor.
 */
export const WORKSPACE_PRESETS: readonly WorkspacePreset[] = [
  {
    id: 'map',
    snapshot: { arrangement: { ...DEFAULT_ARRANGEMENT }, docks: BOTH_DOCKS, scene3d: 'closed', caveViewPane: false },
  },
  {
    id: 'map3d',
    snapshot: {
      arrangement: { ...DEFAULT_ARRANGEMENT, scene3dPlace: 'right' },
      docks: BOTH_DOCKS,
      scene3d: 'open',
      caveViewPane: false,
    },
  },
  {
    id: 'survey',
    snapshot: {
      arrangement: { ...DEFAULT_ARRANGEMENT, caveViewPlace: 'right' },
      docks: { leftHidden: true, rightHidden: false },
      scene3d: 'closed',
      caveViewPane: true,
    },
  },
  {
    id: 'trip',
    snapshot: {
      arrangement: { ...DEFAULT_ARRANGEMENT, scene3dPlace: 'bottom', table: true },
      docks: BOTH_DOCKS,
      scene3d: 'open',
      caveViewPane: false,
    },
  },
  {
    id: 'twoScreens',
    snapshot: { arrangement: { ...DEFAULT_ARRANGEMENT }, docks: BOTH_DOCKS, scene3d: 'window', caveViewPane: false },
  },
];

export function presetById(id: unknown): WorkspacePreset | undefined {
  return WORKSPACE_PRESETS.find((preset) => preset.id === id);
}

/**
 * Whether the workspace as it is now is what this preset puts in place — so the menu can mark the
 * one in force without remembering which was pressed, and stops marking it the moment somebody
 * moves a pane.
 *
 * The strip's height is not compared: dragging it is adjusting the preset, not leaving it.
 */
export function snapshotMatches(current: WorkspaceSnapshot, preset: WorkspaceSnapshot): boolean {
  // Sending the scene to another window is something done, not a state of this one: whether that
  // window is still open is not known here, and the map it leaves behind is the "Map" preset's.
  if (preset.scene3d === 'window') {
    return false;
  }
  const a = current.arrangement;
  const b = preset.arrangement;
  const sceneShown = (intent: Scene3dIntent) => intent === 'open';
  return (
    a.table === b.table &&
    a.docksSwapped === b.docksSwapped &&
    current.docks.leftHidden === preset.docks.leftHidden &&
    current.docks.rightHidden === preset.docks.rightHidden &&
    sceneShown(current.scene3d) === sceneShown(preset.scene3d) &&
    // Where a pane goes matters only while that pane is up.
    (!sceneShown(preset.scene3d) || a.scene3dPlace === b.scene3dPlace) &&
    current.caveViewPane === preset.caveViewPane &&
    (!preset.caveViewPane || a.caveViewPlace === b.caveViewPlace)
  );
}

/**
 * The preset an installation starts a new account on, read from its published starting
 * arrangement — the same document that carries the panels' default sections, under a key of its
 * own. Anything that does not name a preset this client offers is no preset.
 */
export function installationPresetOf(defaults: unknown): WorkspacePreset | undefined {
  if (typeof defaults !== 'object' || defaults === null) {
    return undefined;
  }
  return presetById((defaults as Record<string, unknown>).workspacePreset);
}

/** The key the starting preset is stored under in the installation's starting arrangement. */
export const INSTALLATION_PRESET_KEY = 'workspacePreset';
