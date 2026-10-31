// SPDX-License-Identifier: AGPL-3.0-or-later
import type { WorkspacePaneId } from '../../../stores/workspacePanesStore.ts';

/**
 * What a pane's root element carries: its class names, its name for the maximise key to find which
 * pane focus is in, and `inert` while another pane fills the workspace.
 */
export type PaneRoot = (
  pane: WorkspacePaneId,
  className: string,
) => {
  className: string;
  'data-workspace-pane': WorkspacePaneId;
  inert: boolean | undefined;
};
