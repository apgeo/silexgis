// SPDX-License-Identifier: AGPL-3.0-or-later
import { Card, Empty, Flex, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import type { SurveyUploadAnalyses } from '../../../api/hooks.ts';
import { SurveyDepthProfileView } from '../../statistics/CaveDepthProfilePanel.tsx';
import { SurveyOrientationView } from '../../statistics/CaveOrientationPanel.tsx';
import { SurveyStatisticsView } from '../../statistics/CaveStatisticsPanel.tsx';
import { SurveyTopologyView } from '../../statistics/CaveTopologyPanel.tsx';

/**
 * The upload's own survey measured — its shape, its network, which way it runs and its depth along
 * the passage — drawn by the same panels the cave page uses for the cave's current survey.
 *
 * Every figure here describes this upload and nothing else: the server measures the legs this
 * upload's own model was read into, never the survey the cave happens to be drawn from, which may
 * be another upload. So no panel names a model — there is only one it could be.
 *
 * A survey that was never read into legs — it could not be placed, or its reading has not finished
 * — has nothing to measure, and says so in one sentence rather than as four empty panels.
 */
export default function SurveyUploadAnalysesView({ analyses }: { analyses: SurveyUploadAnalyses | null | undefined }) {
  const { t } = useTranslation();

  return (
    <Flex vertical gap={0} data-testid="survey-viewer-analyses">
      <Typography.Title level={4} style={{ margin: 0 }}>
        {t('surveyViewer.analyses.title')}
      </Typography.Title>
      <Typography.Text type="secondary">{t('surveyViewer.analyses.note')}</Typography.Text>
      {analyses ? (
        <>
          <SurveyStatisticsView data={analyses.statistics} />
          {analyses.topology && <SurveyTopologyView data={analyses.topology} />}
          <SurveyOrientationView data={analyses.orientation} />
          <div style={{ marginTop: 16 }}>
            <SurveyDepthProfileView data={analyses.depthProfile} />
          </div>
        </>
      ) : (
        <Card size="small" style={{ marginTop: 16 }}>
          <Empty
            image={Empty.PRESENTED_IMAGE_SIMPLE}
            description={t('surveyViewer.analyses.none')}
            data-testid="survey-viewer-no-analyses"
          />
        </Card>
      )}
    </Flex>
  );
}
