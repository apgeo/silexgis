// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * Where a pane that is not one of the two docks sits in the map workspace: beside the map on one
 * side or the other, or in the strip under it.
 */
export type PanePlace = 'left' | 'right' | 'bottom';

export const PANE_PLACES: readonly PanePlace[] = ['left', 'right', 'bottom'];

/**
 * How the map workspace is laid out around the map — which is the account's, like the docks.
 *
 * What is kept is where things go, never what they show: the survey model a pane was opened on is
 * not here (it names a cave somebody looked at), and neither is whether the 3D scene is open. The
 * scene is a second renderer with a graphics context of its own, opened when somebody asks for it
 * — by its button or by a preset — and a page that opened one by itself on every visit, on every
 * machine the account signs in on, would be spending a laptop's battery on a choice made once at a
 * workstation.
 */
export interface WorkspaceArrangement {
  /** Where the 3D scene goes when it is opened. */
  scene3dPlace: PanePlace;
  /** Where a survey model goes when one is opened beside the map. */
  caveViewPlace: PanePlace;
  /** Whether the table of what the map shows sits under the map. */
  table: boolean;
  /** The layer panel on the right and the details panel on the left, instead of the other way. */
  docksSwapped: boolean;
  /** The strip under the map, as a percentage of the map column's height; absent is the default. */
  bottomSize?: number;
}

export const DEFAULT_ARRANGEMENT: WorkspaceArrangement = {
  scene3dPlace: 'right',
  caveViewPlace: 'right',
  table: false,
  docksSwapped: false,
};

/** The bounds a stored strip height is held to: the map column keeps at least a third. */
export const BOTTOM_SIZE_MIN = 15;
export const BOTTOM_SIZE_MAX = 65;

function isPlace(value: unknown): value is PanePlace {
  return typeof value === 'string' && (PANE_PLACES as readonly string[]).includes(value);
}

/**
 * An arrangement as this client can draw it, from whatever was stored — by this version or another.
 * Member by member: a value it does not know falls back to its default and keeps the rest.
 * Undefined for something that is not an arrangement at all.
 */
export function sanitizeArrangement(value: unknown): WorkspaceArrangement | undefined {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    return undefined;
  }
  const stored = value as Record<string, unknown>;
  const arrangement: WorkspaceArrangement = {
    scene3dPlace: isPlace(stored.scene3dPlace) ? stored.scene3dPlace : DEFAULT_ARRANGEMENT.scene3dPlace,
    caveViewPlace: isPlace(stored.caveViewPlace) ? stored.caveViewPlace : DEFAULT_ARRANGEMENT.caveViewPlace,
    table: stored.table === true,
    docksSwapped: stored.docksSwapped === true,
  };
  if (
    typeof stored.bottomSize === 'number' &&
    Number.isFinite(stored.bottomSize) &&
    stored.bottomSize >= BOTTOM_SIZE_MIN &&
    stored.bottomSize <= BOTTOM_SIZE_MAX
  ) {
    arrangement.bottomSize = Math.round(stored.bottomSize);
  }
  return arrangement;
}

/**
 * The workspace as a named layout remembers it, next to the panels' sections: the arrangement, the
 * two docks, and whether the 3D scene was open — applying a layout is somebody asking for it, so a
 * layout saved with the scene open opens it again.
 */
export interface SavedWorkspace {
  arrangement: WorkspaceArrangement;
  docks: { leftHidden: boolean; rightHidden: boolean };
  scene3dOpen: boolean;
}

/** A saved layout's workspace as this client can apply it, or undefined when it holds none. */
export function sanitizeSavedWorkspace(value: unknown): SavedWorkspace | undefined {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    return undefined;
  }
  const stored = value as Record<string, unknown>;
  const arrangement = sanitizeArrangement(stored.arrangement);
  if (!arrangement) {
    return undefined;
  }
  const docks = (typeof stored.docks === 'object' && stored.docks !== null ? stored.docks : {}) as Record<
    string,
    unknown
  >;
  return {
    arrangement,
    docks: { leftHidden: docks.leftHidden === true, rightHidden: docks.rightHidden === true },
    scene3dOpen: stored.scene3dOpen === true,
  };
}
