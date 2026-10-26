// SPDX-License-Identifier: AGPL-3.0-or-later
import { Alert, Card, Flex, Skeleton, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { useLocation, useParams } from 'react-router-dom';
import Button from '../../components/Button.tsx';
import SurveyUploadProgressPanel from '../../components/surveyUploads/SurveyUploadProgress.tsx';
import { isSignInRequiredPublicLink, isUnknownPublicLink, usePublicSurveyUpload } from '../../api/hooks.ts';
import { useAuth } from '../../auth/auth.tsx';

/**
 * The page behind a survey upload's public link: how far the run has got and what it produced,
 * for somebody who was handed the link and holds no account.
 *
 * It draws the same progress panel as the signed-in status page and nothing more. What the
 * server's answer carries is already limited to the survey's title, the cave's name, the upload's
 * moment, the stages and the files that can be opened; the log, the files that were sent, the team
 * and any position are not in it, so there is nothing here to hide. A link that is wrong, withdrawn
 * or no longer allowed (the cave has since been protected) is answered exactly like one that never
 * existed, and the page says the same thing for all three.
 *
 * A link made for the organization opens for any signed-in account of the installation. Read
 * without signing in, the server says so (and only for a link that would otherwise open), and the
 * page offers to sign in and come back here. A signed-in reader's requests already carry the
 * account, so after signing in the same request simply succeeds.
 */
export default function PublicSurveyResultPage() {
  const { t, i18n } = useTranslation();
  const { token } = useParams<{ token: string }>();
  const { data: view, isPending, error, refetch } = usePublicSurveyUpload(token);
  const { signIn } = useAuth();
  const location = useLocation();

  if (isPending && !error) {
    return (
      <div style={{ padding: 24, maxWidth: 900, margin: '0 auto' }}>
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
            <Button
              type="primary"
              data-testid="public-survey-sign-in-button"
              onClick={() => void signIn(location.pathname + location.search + location.hash)}
            >
              {t('publicSurvey.signIn')}
            </Button>
          }
        />
      </div>
    );
  }

  if (error || !view) {
    const unknown = isUnknownPublicLink(error);
    return (
      <div style={{ padding: 24, maxWidth: 900, margin: '0 auto' }}>
        <Alert
          type={unknown ? 'warning' : 'error'}
          showIcon
          data-testid="public-survey-unavailable"
          title={unknown ? t('publicSurvey.notFound') : t('publicSurvey.loadFailed')}
          action={unknown ? undefined : <Button onClick={() => void refetch()}>{t('common.retry')}</Button>}
        />
      </div>
    );
  }

  return (
    <main style={{ padding: 24, maxWidth: 900, margin: '0 auto' }} data-testid="public-survey-result">
      <Flex vertical gap={16}>
        <Flex vertical>
          <Typography.Title level={3} style={{ margin: 0 }}>
            {view.title}
          </Typography.Title>
          <Typography.Text type="secondary" data-testid="public-survey-cave">
            {t('publicSurvey.caveAndVersion', {
              cave: view.caveName,
              when: new Date(view.version).toLocaleString(i18n.language),
            })}
          </Typography.Text>
        </Flex>
        <Card size="small" title={t('surveyUploads.page.progressTitle')}>
          <SurveyUploadProgressPanel view={view} />
        </Card>
      </Flex>
    </main>
  );
}
