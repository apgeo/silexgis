// SPDX-License-Identifier: AGPL-3.0-or-later
import { lazy, Suspense } from 'react';
import { Spin } from 'antd';
import { useTranslation } from 'react-i18next';
import type { PanePlace } from '../../../stores/workspacePrefs.ts';
import PaneHeader, { type PaneDestination } from './PaneHeader.tsx';
import type { PaneRoot } from './paneRoot.ts';

// Loaded only when a viewer opens the 3D pane, the same way the 3D route loads it: the scene
// module and its runtime assets are about a megabyte, and a session that never opens the pane must
// not pay for it.
const Scene3DView = lazy(() => import('../../scene3d/Scene3DView.tsx'));

export interface ScenePaneProps {
  paneRoot: PaneRoot;
  place: PanePlace;
  onMove: (destination: PaneDestination) => void;
  maximised: boolean;
  onToggleMaximised: () => void;
  onClose: () => void;
}

/** The 3D scene beside the map, with its header. */
export default function ScenePane({
  paneRoot,
  place,
  onMove,
  maximised,
  onToggleMaximised,
  onClose,
}: ScenePaneProps) {
  const { t } = useTranslation();
  return (
    <div {...paneRoot('scene3d', 'workspace-pane')}>
      <PaneHeader
        title={t('workspace.scene3dName')}
        name={t('workspace.scene3dName')}
        testId="workspace-scene3d"
        place={place}
        onMove={onMove}
        maximised={maximised}
        onToggleMaximised={onToggleMaximised}
        onClose={onClose}
      />
      <div className="workspace-pane-body">
        <Suspense fallback={<Spin style={{ margin: 48 }} />}>
          <Scene3DView />
        </Suspense>
      </div>
    </div>
  );
}
