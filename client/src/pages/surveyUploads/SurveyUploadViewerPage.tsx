// SPDX-License-Identifier: AGPL-3.0-or-later
import { ArrowLeftOutlined, ExportOutlined } from '@ant-design/icons';
import { Alert, Breadcrumb, Flex, Skeleton } from 'antd';
import { Link, useParams } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { ApiError } from '../../api/client.ts';
import {
  surveyResultShare,
  surveyUploadGeodataUrl,
  surveyUploadUnsettled,
  useSurveyUpload,
  useSurveyUploadFigures,
  useSurveyUploadLineWork,
  viewerModelOf,
} from '../../api/hooks.ts';
import Button from '../../components/Button.tsx';
import SurveyResultViewer, { type SurveyGeoState } from '../../components/surveyUploads/viewer/SurveyResultViewer.tsx';

/**
 * One upload's results for a signed-in reader: the model in both viewers, where the survey lies,
 * what the compiler measured, and this upload's own survey measured beneath.
 *
 * The page asks for the upload by its id alone, and the server decides whether this account may
 * see it — the same "not found" as the status page for an address that is made up or foreign. The
 * line work follows the same terms as the upload's sent files: whoever may open those gets it.
 *
 * "Copy link" copies the public link's page when the upload has one, which anybody holding it may
 * open; otherwise this page's own address, which opens only for an account the upload answers.
 */
export default function SurveyUploadViewerPage() {
  const { t } = useTranslation();
  const { uploadId } = useParams<{ caveId: string; surveyId: string; uploadId: string }>();
  const { data: upload, isPending, error, refetch } = useSurveyUpload(uploadId);
  const settled = !!upload && !surveyUploadUnsettled(upload.status);
  const figures = useSurveyUploadFigures(uploadId, settled);
  // Asked for only when the figures say the run placed a line, so a survey that
  // was never placed is not one more refused request.
  const placed = figures.data?.placed === true;
  const lineWork = useSurveyUploadLineWork(uploadId, settled && placed);

  if (isPending && !error) {
    return (
      <div style={{ padding: 24, maxWidth: 1200 }}>
        <Skeleton active paragraph={{ rows: 6 }} />
      </div>
    );
  }
  if (error || !upload) {
    const missing = error instanceof ApiError && error.status === 404;
    return (
      <div style={{ padding: 24, maxWidth: 1200 }}>
        <Alert
          type={missing ? 'warning' : 'error'}
          showIcon
          data-testid="survey-viewer-unavailable"
          title={missing ? t('surveyUploads.page.notFound') : t('surveyUploads.page.loadFailed')}
          action={missing ? undefined : <Button onClick={() => void refetch()}>{t('common.retry')}</Button>}
        />
      </div>
    );
  }

  const geo: SurveyGeoState = !settled || figures.isPending
    ? { kind: 'loading' }
    : figures.isError || lineWork.isError
      ? { kind: 'failed' }
      : !placed
        ? { kind: 'unplaced' }
        : lineWork.data
          ? {
              kind: 'shown',
              lineWork: lineWork.data,
              downloadUrl: (format) => surveyUploadGeodataUrl(upload.id, format),
              catalogue: true,
            }
          : lineWork.isPending
            ? { kind: 'loading' }
            : { kind: 'unplaced' };

  const share = surveyResultShare(upload, window.location.origin);

  return (
    <SurveyResultViewer
      title={upload.survey.title}
      caveName={upload.cave.name}
      version={upload.version}
      shareUrl={share.url}
      shareKind={share.kind}
      model={viewerModelOf(upload.artefacts)}
      figures={{
        data: figures.data?.compiler,
        isPending: settled ? figures.isPending : false,
        isError: figures.isError,
      }}
      analyses={figures.data?.analyses}
      geo={geo}
      openInApp={
        <Link to={upload.cave.url} data-testid="survey-viewer-open-in-app">
          <Button type="primary" icon={<ExportOutlined />}>
            {t('surveyViewer.header.openCave')}
          </Button>
        </Link>
      }
      navigation={
        <Flex vertical gap={4}>
          <Breadcrumb
            items={[
              { title: <Link to="/caves">{t('caves.title')}</Link> },
              { title: <Link to={upload.cave.url}>{upload.cave.name}</Link> },
              { title: upload.caveSurvey.title },
            ]}
          />
          <Link to={upload.statusPageUrl} data-testid="survey-viewer-status-link">
            <ArrowLeftOutlined aria-hidden /> {t('surveyViewer.header.backToStatus')}
          </Link>
        </Flex>
      }
    />

  );
}
