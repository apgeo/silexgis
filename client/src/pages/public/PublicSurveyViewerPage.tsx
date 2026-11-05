// SPDX-License-Identifier: AGPL-3.0-or-later
import { ArrowLeftOutlined, ExportOutlined, LoginOutlined } from '@ant-design/icons';
import { Alert, Skeleton } from 'antd';
import { useTranslation } from 'react-i18next';
import { Link, useLocation, useParams } from 'react-router-dom';
import {
  isSignInRequiredPublicLink,
  isUnknownPublicLink,
  publicSurveyViewerPath,
  surveyUploadUnsettled,
  surveyUploadViewerPath,
  usePublicSurveyUpload,
  usePublicSurveyUploadFigures,
  useSurveyUploadByPublicLink,
  viewerModelOf,
} from '../../api/hooks.ts';
import { useAuth } from '../../auth/auth.tsx';
import Button from '../../components/Button.tsx';
import SurveyResultViewer from '../../components/surveyUploads/viewer/SurveyResultViewer.tsx';

/**
 * One upload's results behind its public link: the model in both viewers and what the compiler
 * measured, for somebody who was handed the link.
 *
 * Drawn from the same answers as the link's progress page, so a link that is wrong, withdrawn or no
 * longer allowed answers here exactly as there. There is no map and no geodata: the public page
 * carries no position of the cave's own, and the server offers this page no route to the survey's
 * line in the world — the page says so rather than leaving a gap.
 *
 * "Open in SilexGIS" is offered to a signed-in reader whom the upload answers on its own terms; the
 * server says which, and names the upload's place only then. A reader who is not signed in is
 * offered to sign in and come back here.
 */
export default function PublicSurveyViewerPage() {
  const { t } = useTranslation();
  const { token } = useParams<{ token: string }>();
  const location = useLocation();
  const { user, signIn } = useAuth();
  const { data: view, isPending, error } = usePublicSurveyUpload(token);
  const settled = !!view && !surveyUploadUnsettled(view.status);
  const figures = usePublicSurveyUploadFigures(token, settled);
  const inApp = useSurveyUploadByPublicLink(token, !!user && !!view);
  const here = location.pathname + location.search + location.hash;

  if (isPending && !error) {
    return (
      <div style={{ padding: 24, maxWidth: 1200, margin: '0 auto' }}>
        <Skeleton active paragraph={{ rows: 6 }} />
      </div>
    );
  }

  if (isSignInRequiredPublicLink(error)) {
    return (
      <div style={{ padding: 24, maxWidth: 900, margin: '0 auto' }}>
        <Alert
          type="info"
          showIcon
          data-testid="public-survey-sign-in"
          title={t('publicSurvey.signInRequired')}
          description={t('publicSurvey.signInRequiredHint')}
          action={
            <Button type="primary" data-testid="public-survey-sign-in-button" onClick={() => void signIn(here)}>
              {t('publicSurvey.signIn')}
            </Button>
          }
        />
      </div>
    );
  }

  if (error || !view || !token) {
    return (
      <div style={{ padding: 24, maxWidth: 900, margin: '0 auto' }}>
        <Alert
          type={isUnknownPublicLink(error) ? 'warning' : 'error'}
          showIcon
          data-testid="public-survey-unavailable"
          title={isUnknownPublicLink(error) ? t('publicSurvey.notFound') : t('publicSurvey.loadFailed')}
        />
      </div>
    );
  }

  const pageUrl = new URL(publicSurveyViewerPath(`/p/survey/${encodeURIComponent(token)}`), window.location.origin).toString();

  const openInApp = !user ? (
    <Button icon={<LoginOutlined />} onClick={() => void signIn(here)} data-testid="survey-viewer-sign-in-to-open">
      {t('surveyViewer.header.signInToOpen')}
    </Button>
  ) : inApp.data ? (
    <Link to={surveyUploadViewerPath(inApp.data.statusPageUrl)} data-testid="survey-viewer-open-in-app">
      <Button type="primary" icon={<ExportOutlined />}>
        {t('surveyViewer.header.openInApp')}
      </Button>
    </Link>
  ) : null;

  return (
    <SurveyResultViewer
      title={view.title}
      caveName={view.caveName}
      version={view.version}
      shareUrl={pageUrl}
      shareKind="public"
      model={viewerModelOf(view.artefacts)}
      figures={{
        data: figures.data?.compiler,
        isPending: settled ? figures.isPending : false,
        isError: figures.isError,
      }}
      geo={{ kind: 'withheld' }}
      openInApp={openInApp}
      navigation={
        <Link to={`/p/survey/${encodeURIComponent(token)}`} data-testid="survey-viewer-progress-link">
          <ArrowLeftOutlined aria-hidden /> {t('surveyViewer.header.backToProgress')}
        </Link>
      }
    />
  );
}
