// SPDX-License-Identifier: AGPL-3.0-or-later
import BottomTable from './BottomTable.tsx';
import type { PaneRoot } from './paneRoot.ts';

export interface TablePaneProps {
  paneRoot: PaneRoot;
  maximised: boolean;
  onToggleMaximised: () => void;
  onClose: () => void;
}

/** The table under the map. */
export default function TablePane({ paneRoot, maximised, onToggleMaximised, onClose }: TablePaneProps) {
  return (
    <div {...paneRoot('table', 'workspace-pane')}>
      <BottomTable maximised={maximised} onToggleMaximised={onToggleMaximised} onClose={onClose} />
    </div>
  );
}
