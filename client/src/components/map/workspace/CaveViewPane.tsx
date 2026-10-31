// SPDX-License-Identifier: AGPL-3.0-or-later
import { lazy, Suspense } from 'react';
import { Spin, Tooltip } from 'antd';
import { ExpandOutlined, ExportOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import type { SurveyModelInfo } from '../../../api/hooks.ts';
import { openModelWindow } from '../../../caveview/openModelWindow.ts';
import type { useCompareOffer } from '../../../caveview/useCompareOffer.ts';
import { viewerFileName } from '../../../caveview/viewerFileName.ts';
import type { PanePlace } from '../../../stores/workspacePrefs.ts';
import Button from '../../Button.tsx';
import PaneHeader, { type PaneDestination } from './PaneHeader.tsx';
import type { PaneRoot } from './paneRoot.ts';
import SurveyModelChooser from './SurveyModelChooser.tsx';

// Loaded on demand: it is a survey-viewer bundle that most visits to the map never open.
const CaveViewPanel = lazy(() => import('../../caveview/CaveViewPanel.tsx'));

export interface CaveViewPaneProps {
  paneRoot: PaneRoot;
  /** The model open in the pane, if any. */
  model: SurveyModelInfo | undefined;
  compare: ReturnType<typeof useCompareOffer>;
  place: PanePlace;
  onMove: (destination: PaneDestination) => void;
  maximised: boolean;
  onToggleMaximised: () => void;
  onClose: () => void;
  /** Escalates the open model to a view over the whole window. */
  onOpenOverlay: (model: SurveyModelInfo) => void;
  /** Picks a model from the chooser shown while the pane holds none. */
  onOpenModel: (modelId: string) => void;
}

/** The survey model beside the map (or the chooser for one), with its header. */
export default function CaveViewPane({
  paneRoot,
  model: caveViewModel,
  compare: caveViewCompare,
  place,
  onMove,
  maximised,
  onToggleMaximised,
  onClose,
  onOpenOverlay,
  onOpenModel,
}: CaveViewPaneProps) {
  const { t } = useTranslation();
  return (
    <div {...paneRoot('caveview', 'workspace-pane map-caveview-pane')}>
      <PaneHeader
        title={caveViewModel?.name ?? t('workspace.surveyName')}
        name={t('workspace.surveyName')}
        testId="map-caveview"
        place={place}
        onMove={onMove}
        mayPopOut={caveViewModel !== undefined}
        maximised={maximised}
        onToggleMaximised={onToggleMaximised}
        onClose={onClose}
        extra={
          caveViewModel && (
            <>
              <Tooltip title={t('surveyModels.openOverlay')}>
                <Button
                  size="small"
                  type="text"
                  icon={<ExpandOutlined />}
                  aria-label={t('surveyModels.openOverlay')}
                  onClick={() => onOpenOverlay(caveViewModel)}
                  data-testid="map-caveview-overlay"
                />
              </Tooltip>
              <Tooltip title={t('surveyModels.openInWindow')}>
                <Button
                  size="small"
                  type="text"
                  icon={<ExportOutlined />}
                  aria-label={t('surveyModels.openInWindow')}
                  onClick={() => openModelWindow(caveViewModel.id)}
                  data-testid="map-caveview-popout"
                />
              </Tooltip>
            </>
          )
        }
      />
      <div className="map-caveview-body">
        {caveViewModel ? (
          <Suspense fallback={<Spin style={{ margin: 48 }} />}>
            <CaveViewPanel
              fileUrl={caveViewModel.modelUrl}
              fileName={viewerFileName(caveViewModel)}
              height="100%"
              surveyModelId={caveViewModel.id}
              compare={caveViewCompare}
            />
          </Suspense>
        ) : (
          <SurveyModelChooser onOpen={onOpenModel} />
        )}
      </div>
    </div>
  );
}
