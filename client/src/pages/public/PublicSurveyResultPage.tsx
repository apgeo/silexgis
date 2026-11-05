// SPDX-License-Identifier: AGPL-3.0-or-later
import { BarChartOutlined, DownloadOutlined } from '@ant-design/icons';
import { Alert, App, Card, Flex, Skeleton, Typography } from 'antd';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useLocation, useParams } from 'react-router-dom';
import Button from '../../components/Button.tsx';
import { formatSize } from '../../components/attachments/fileFormat.ts';
import SurveyUploadProgressPanel from '../../components/surveyUploads/SurveyUploadProgress.tsx';
import { downloadFile } from '../../api/download.ts';
import {
  isSignInRequiredPublicLink,
  isUnknownPublicLink,
  publicSurveyViewerPath,
  surveyUploadUnsettled,
  usePublicSurveyUpload,
  type PublicResultView,
} from '../../api/hooks.ts';
import { useAuth } from '../../auth/auth.tsx';
import { DOWNLOAD_BUTTON } from '../../theme.ts';

/**
 * The page behind a survey upload's public link: how far the run has got and what it produced,
 * for somebody who was handed the link and holds no account.
 *
 * It draws the same progress panel as the signed-in status page, and under it the compiler's log:
 * its last lines, and the whole of it to download. What the server's answer carries is already
 * limited to the survey's title, the cave's name, the upload's moment, the stages, the files that
 * can be opened and the log; the files that were sent, the team and any position of the cave's own
 * are not in it, so there is nothing here to hide. A link that is wrong, withdrawn
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
        {!surveyUploadUnsettled(view.status) && token && (
          <Flex>
            <Link to={publicSurveyViewerPath(`/p/survey/${encodeURIComponent(token)}`)} data-testid="public-survey-open-viewer">
              <Button type="primary" icon={<BarChartOutlined />}>
                {t('surveyViewer.open')}
              </Button>
            </Link>
          </Flex>
        )}
        <Card size="small" title={t('surveyUploads.page.progressTitle')}>
          <SurveyUploadProgressPanel view={view} logShown={hasLog(view)} />
        </Card>
        {hasLog(view) && <LogCard view={view} />}
      </Flex>
    </main>
  );
}

function hasLog(view: PublicResultView): boolean {
  return !!view.logTail || view.log !== null;
}

/**
 * The compiler's log: the last lines as the server keeps them, and the whole log to save.
 *
 * The whole log is fetched rather than linked: a link made for the installation's accounts opens
 * its log only for a signed-in reader, and a plain link cannot carry the account — the fetch does.
 */
function LogCard({ view }: { view: PublicResultView }) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const [saving, setSaving] = useState(false);
  const log = view.log;

  const save = async () => {
    if (!log) return;
    setSaving(true);
    try {
      await downloadFile(log.url);
    } catch {
      message.error(t('publicSurvey.logDownloadFailed'));
    } finally {
      setSaving(false);
    }
  };

  return (
    <Card size="small" title={t('surveyUploads.page.logTitle')} data-testid="public-survey-log">
      <Flex vertical gap={8}>
        {view.logTail && (
          <pre
            data-testid="public-survey-log-tail"
            style={{ margin: 0, fontSize: 12, whiteSpace: 'pre-wrap', maxHeight: 240, overflow: 'auto' }}
          >
            {view.logTail}
          </pre>
        )}
        {log && (
          <Flex gap={8} align="center" wrap>
            <Button
              size="small"
              {...DOWNLOAD_BUTTON}
              icon={<DownloadOutlined />}
              loading={saving}
              onClick={() => void save()}
              data-testid="public-survey-log-download"
            >
              {t('publicSurvey.downloadLog')}
            </Button>
            <Typography.Text type="secondary">
              {log.name} · {formatSize(log.sizeBytes)}
            </Typography.Text>
          </Flex>
        )}
      </Flex>
    </Card>
  );
}
