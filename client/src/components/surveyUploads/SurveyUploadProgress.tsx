// SPDX-License-Identifier: AGPL-3.0-or-later
import { Alert, Flex, Progress, Steps, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import type { StageProgress, SurveyUploadArtefactView, SurveyUploadStatus } from '../../api/hooks.ts';
import SurveyUploadArtefacts from './SurveyUploadArtefacts.tsx';
import { surveyUploadRunErrorText } from './surveyUploadProblems.ts';

/** What the view needs from either answer — the signed-in status or the public envelope. */
export interface SurveyUploadProgressView {
  status: SurveyUploadStatus;
  progress: number;
  message: string | null;
  error: { code: string; message: string | null } | null;
  stages: StageProgress[];
  artefacts: readonly SurveyUploadArtefactView[];
}

const STEP_STATUS = {
  done: 'finish',
  active: 'process',
  pending: 'wait',
  failed: 'error',
  skipped: 'wait',
} as const;

/**
 * How far a run has got: the bar, the stages each with their own state, what the server last said,
 * how it ended, and what it produced.
 *
 * Shared by the signed-in status page and the public result page so the two cannot drift apart —
 * everything here is allowed to be shown to someone with only the link. The log, the input files
 * and the team are not part of it and are drawn by the signed-in page alone.
 *
 * Stages are drawn as the steps themselves so a run that stopped early leaves the later ones
 * plainly unvisited. The bar never animates by itself: an animation promises a rate the server
 * does not give.
 */
export default function SurveyUploadProgressPanel({ view }: { view: SurveyUploadProgressView }) {
  const { t } = useTranslation();
  const failed = view.status === 'failed';
  const succeeded = view.status === 'succeeded';
  const errorText = view.error ? surveyUploadRunErrorText(view.error.code, t) : null;

  return (
    <Flex vertical gap={16} data-testid="survey-upload-progress">
      <Steps
        size="small"
        data-testid="survey-upload-stages"
        items={view.stages.map(({ stage, state }) => ({
          title: <span data-testid={`survey-upload-stage-${stage}`}>{t(`surveyUploads.stages.${stage}`)}</span>,
          status: STEP_STATUS[state],
          content: state === 'skipped' ? t('surveyUploads.stageStates.skipped') : undefined,
        }))}
      />

      <Progress
        percent={view.progress}
        data-testid="survey-upload-percent"
        status={failed ? 'exception' : succeeded ? 'success' : 'normal'}
      />

      <Typography.Text type="secondary" data-testid="survey-upload-status">
        {t(`surveyUploads.status.${view.status}`)}
      </Typography.Text>

      {failed && view.error ? (
        <Alert
          type="error"
          showIcon
          data-testid="survey-upload-error"
          title={errorText ?? view.error.message ?? t('surveyUploads.problems.unexpected')}
          description={
            <Flex vertical gap={4}>
              {errorText && view.error.message && <span>{view.error.message}</span>}
              <Typography.Text type="secondary" code data-testid="survey-upload-error-code">
                {view.error.code}
              </Typography.Text>
            </Flex>
          }
        />
      ) : view.message ? (
        // A server with no compiler ends a run succeeded with a sentence saying so; it is a note
        // on a success, not a warning.
        <Alert
          type="info"
          showIcon
          data-testid="survey-upload-message"
          title={view.message}
        />
      ) : null}

      <div>
        <Typography.Text strong>{t('surveyUploads.artefacts.title')}</Typography.Text>
        <div style={{ marginTop: 8 }}>
          <SurveyUploadArtefacts artefacts={view.artefacts} />
        </div>
      </div>
    </Flex>
  );
}
