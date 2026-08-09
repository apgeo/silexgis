// SPDX-License-Identifier: AGPL-3.0-or-later
import type { PanelSectionId } from '../components/map/panelSections.ts';

/**
 * How tightly the interface is packed. Declared here rather than beside the preference store
 * because both this module and that one need it, and having the lower one reach up for it is a
 * cycle the dependency check refuses — correctly, since a cycle between two type modules is the
 * shape that later becomes an undefined import at runtime.
 */
export type DensityPref = 'comfortable' | 'compact';

/**
 * Which panel a preference belongs to.
 *
 * Each mount keeps its own: the dock beside the flat map, the dock beside the 3D scene, and every
 * pop-out window. A pop-out is a screen somebody arranged for one purpose — a second monitor
 * showing only history, say — and having it inherit the main window's arrangement would defeat
 * the reason they popped it out.
 */
export type PanelScope = 'main' | 'scene3d' | 'popout';

export interface PanelPrefs {
  /** Section ids in the order they are drawn. Absent means the default order. */
  order?: PanelSectionId[];
  /** Sections closed right now. Absent means the defaults, which is not the same as none. */
  collapsed?: PanelSectionId[];
  /** Sections switched off entirely. */
  hidden?: PanelSectionId[];
  /** Panel width in pixels for an overlay panel, or percent of the workspace when docked. */
  width?: number;
  /** Overrides the application-wide density for this panel only; absent means follow it. */
  density?: DensityPref;
  /** True pushes the map aside, false floats over it. Absent means pushed aside. */
  pinned?: boolean;
}

/**
 * A named arrangement of the whole workspace, saved and reloaded by name.
 *
 * Deliberately not a saved map view. A view is a *place* — where the camera is, which data is
 * drawn; a layout is *chrome* — how wide the panel is, which sections are in it and in what order.
 * A surveyor and an archivist want different chrome over the same place, and the same person wants
 * their chrome kept while they move around. One object holding both would force a choice between
 * those every time either was loaded.
 */
export interface PanelLayout {
  id: string;
  name: string;
  panels: Partial<Record<PanelScope, PanelPrefs>>;
  /** Whether the on-canvas chrome was hidden, since that is part of how a screen was arranged. */
  mapChromeHidden?: boolean;
}

export const EMPTY_PANEL_PREFS: PanelPrefs = {};

/**
 * The prefs a scope starts from when the installation has published defaults and the person has
 * not overridden them.
 *
 * The merge is per key rather than per object: an installation that publishes a section order must
 * not thereby reset a width somebody chose, and a person who set a width must not thereby freeze
 * the order at whatever it was that day. Absent on both sides falls through to the built-in
 * default, which is what makes "follow the installation" expressible at all.
 */
export function mergePanelPrefs(
  installation: PanelPrefs | undefined,
  personal: PanelPrefs | undefined,
): PanelPrefs {
  return {
    order: personal?.order ?? installation?.order,
    collapsed: personal?.collapsed ?? installation?.collapsed,
    hidden: personal?.hidden ?? installation?.hidden,
    width: personal?.width ?? installation?.width,
    density: personal?.density ?? installation?.density,
    pinned: personal?.pinned ?? installation?.pinned,
  };
}
