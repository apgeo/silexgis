// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ComponentProps } from 'react';
import type { TabsProps } from 'antd';
import type { ViewerModel } from '../../api/hooks.ts';
import { viewerFileName } from '../../caveview/viewerFileName.ts';
import CaveViewPanel from './CaveViewPanel.tsx';
import DankezCaveViewPane from './DankezCaveViewPane.tsx';

/** The key of the embedded viewer's tab. */
export const TAB_3D = '3d';
/** The key of the tab trying the same file in dankez-caveview (LochViewer), a second viewer on trial. */
export const TAB_DANKEZ = 'dankez-caveview';

/** What a mount may say to the embedded viewer beyond the file itself. */
export type SurveyViewerPanelProps = Omit<ComponentProps<typeof CaveViewPanel>, 'fileUrl' | 'fileName' | 'height'>;

type TabItem = NonNullable<TabsProps['items']>[number];

/**
 * The two viewer tabs one survey is shown in: the embedded viewer, and the same file in
 * dankez-caveview.
 *
 * One home for both mounts — the model's own window and the page of an upload's results — because
 * what it carries is two rules that have to agree wherever a survey is viewed: the file name the
 * parser is chosen by, and that the second viewer is mounted only while its tab is shown, since it
 * is a whole application with a drawing context of its own, out of the same small budget the
 * embedded viewer and any map on the page draw from. The embedded viewer stays mounted while
 * another tab is shown, so coming back finds it where it was.
 */
export function surveyModelTabItems({
  model,
  activeTab,
  height,
  panel,
  label3d,
  labelDankez,
}: {
  model: ViewerModel;
  activeTab: string;
  height: string;
  panel?: SurveyViewerPanelProps;
  label3d: string;
  labelDankez: string;
}): TabItem[] {
  return [
    {
      key: TAB_3D,
      label: label3d,
      children: (
        <CaveViewPanel fileUrl={model.modelUrl} fileName={viewerFileName(model)} height={height} {...panel} />
      ),
    },
    {
      key: TAB_DANKEZ,
      label: labelDankez,
      children: activeTab === TAB_DANKEZ ? <DankezCaveViewPane model={model} height={height} /> : null,
    },
  ];
}
