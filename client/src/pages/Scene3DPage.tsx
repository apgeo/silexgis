// SPDX-License-Identifier: AGPL-3.0-or-later
import { Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import SelectionPanel from '../components/map/SelectionPanel.tsx';
import Scene3DView from '../components/scene3d/Scene3DView.tsx';
import './Scene3DPage.css';

/**
 * The 3D scene as a full-height page, with the same detail panel the flat map shows beside it.
 *
 * The panel is the one the flat map uses, unchanged and unwrapped: it reads the selection out of
 * the workspace store and fetches whatever it needs itself, so clicking a cave in the scene and
 * clicking the same cave on the map put identical information in front of the viewer with no
 * second implementation to keep in step.
 */
export default function Scene3DPage() {
  const { t } = useTranslation();
  return (
    <div className="scene3d-page">
      <div className="scene3d-page-scene">
        <Scene3DView />
      </div>
      <aside className="scene3d-page-dock">
        <Typography.Text strong className="scene3d-page-dock-title">
          {t('map.detailsTitle')}
        </Typography.Text>
        <SelectionPanel />
      </aside>
    </div>
  );
}
