// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';

/**
 * A box of its own for a pane whose content must survive being moved about the workspace.
 *
 * The 3D scene and a survey model each hold a graphics context, and both are expensive to build —
 * the scene is a megabyte of engine and a terrain stream, the model a file read through. React
 * keeps a component alive only while it stays in the same place in the tree, so a pane moved from
 * beside the map to the strip under it would be torn down and built again, with a new context each
 * time. The browser keeps only a handful of those and drops the oldest without asking.
 *
 * So the pane's content is drawn once, through a portal, into an element made here, and that
 * element is what moves: whichever slot is showing the pane takes it in. A move unmounts
 * one slot and mounts another — the slots are cheap, the content is never touched — and in between
 * the element is merely outside the document for the moment of the commit, which a drawing surface
 * copes with the way it copes with a hidden tab.
 */
export function usePaneElement(name: string): HTMLDivElement {
  const [element] = useState(() => {
    const div = document.createElement('div');
    div.className = 'workspace-pane-host';
    div.dataset.paneHost = name;
    return div;
  });
  return element;
}
