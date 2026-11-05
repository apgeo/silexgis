// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { Tabs } from 'antd';
import { useTranslation } from 'react-i18next';
import type { ViewerModel } from '../../api/hooks.ts';
import { surveyModelTabItems, TAB_3D, type SurveyViewerPanelProps } from './surveyModelTabItems.tsx';

/** The two viewer tabs as a block of a page, keeping which one is shown itself. */
export default function SurveyModelTabs({
  model,
  height,
  panel,
}: {
  model: ViewerModel;
  height: string;
  panel?: SurveyViewerPanelProps;
}) {
  const { t } = useTranslation();
  const [activeTab, setActiveTab] = useState(TAB_3D);
  return (
    <Tabs
      activeKey={activeTab}
      onChange={setActiveTab}
      data-testid="survey-model-tabs"
      items={surveyModelTabItems({
        model,
        activeTab,
        height,
        panel,
        label3d: t('rastermap.tab3d'),
        labelDankez: t('caveview.dankezCaveView.tab'),
      })}
    />
  );
}
