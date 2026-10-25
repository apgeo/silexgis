// SPDX-License-Identifier: AGPL-3.0-or-later
import { DownloadOutlined, ExportOutlined, ReloadOutlined } from '@ant-design/icons';
import { Alert, App, Breadcrumb, Card, Descriptions, Empty, Flex, Popconfirm, Skeleton, Table, Tag, Typography } from 'antd';
import { Link, useParams } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import Button from '../../components/Button.tsx';
import { formatSize } from '../../components/attachments/fileFormat.ts';
import { ApiError } from '../../api/client.ts';
import {
  surveyUploadLinkHref,
  surveyUploadPublicLinkRefusal,
  useCaveSurveys,
  surveyUploadStageStates,
  surveyUploadUnsettled,
  useCaveSummary,
  useMintSurveyUploadPublicLink,
  useRefreshCaveWhenUploadSettles,
  useRerunSurveyUpload,
  useRevokeSurveyUploadPublicLink,
  useSurveyUpload,
  type SurveyUpload,
  type SurveyUploadFile,
} from '../../api/hooks.ts';
import SurveyUploadProgressPanel from '../../components/surveyUploads/SurveyUploadProgress.tsx';
import { surveyUploadRequestErrorText } from '../../components/surveyUploads/surveyUploadProblems.ts';

/**
 * The place a run is watched: where it is, what it said, what it produced, and what can be done
 * about it.
 *
 * The address carries a cave and a survey as well as the upload, for the reader's sake (the
 * breadcrumb) — they decide nothing. The page asks for the upload by its id alone and the server
 * decides whether this account may see it, so a made-up or foreign address gets the same "not
 * found" as one that never existed.
 *
 * Stages are drawn with their state only: the upload's answer carries no time per stage, so none
 * can be shown.
 *
 * The log tail is drawn only when the server sent one, and it sends it to people who may write the
 * cave. There is no *delete this upload* here: the server has no way to take back a finished
 * upload yet, and a button that could only fail is worse than no button.
 */
export default function SurveyUploadStatusPage() {
  const { t } = useTranslation();
  const { uploadId } = useParams<{ caveId: string; surveyId: string; uploadId: string }>();
  const { data: upload, isPending, error, refetch } = useSurveyUpload(uploadId);
  useRefreshCaveWhenUploadSettles(upload);
  const { data: summary } = useCaveSummary(upload?.cave.id);
  const canWrite = summary?.permissions.canWrite ?? false;

  if (isPending && !error) {
    return (
      <div style={{ padding: 24, maxWidth: 1000 }}>
        <Skeleton active paragraph={{ rows: 6 }} />
      </div>
    );
  }
  if (error || !upload) {
    const missing = error instanceof ApiError && error.status === 404;
    return (
      <div style={{ padding: 24, maxWidth: 1000 }}>
        <Alert
          type={missing ? 'warning' : 'error'}
          showIcon
          data-testid="survey-upload-unavailable"
          title={missing ? t('surveyUploads.page.notFound') : t('surveyUploads.page.loadFailed')}
          action={
            missing ? undefined : (
              <Button onClick={() => void refetch()}>{t('common.retry')}</Button>
            )
          }
        />
      </div>
    );
  }

  return <StatusBody upload={upload} canWrite={canWrite} />;
}

function StatusBody({ upload, canWrite }: { upload: SurveyUpload; canWrite: boolean }) {
  const { t, i18n } = useTranslation();
  const { message } = App.useApp();
  const rerun = useRerunSurveyUpload();
  const when = (value: string | null) => (value ? new Date(value).toLocaleString(i18n.language) : '—');
  const unsettled = surveyUploadUnsettled(upload.status);
  const canRerun = canWrite && (upload.status === 'succeeded' || upload.status === 'failed');

  const view = {
    status: upload.status,
    progress: upload.progress,
    message: upload.message,
    error: upload.error,
    stages: surveyUploadStageStates({ status: upload.status, stage: upload.stage, compile: upload.compile }),
    artefacts: upload.artefacts,
  };

  const onRerun = async () => {
    try {
      await rerun.mutateAsync({ id: upload.id });
      message.success(t('surveyUploads.page.rerunStarted'));
    } catch (error) {
      message.error(surveyUploadRequestErrorText(error, t));
    }
  };

  return (
    <div style={{ padding: 24, maxWidth: 1000 }}>
      <Breadcrumb
        style={{ marginBottom: 8 }}
        items={[
          { title: <Link to="/caves">{t('caves.title')}</Link> },
          { title: <Link to={`/caves/${upload.cave.id}`}>{upload.cave.name}</Link> },
          { title: upload.caveSurvey.title },
        ]}
      />

      <Flex justify="space-between" align="center" wrap gap={8} style={{ marginBottom: 12 }}>
        <Flex vertical>
          <Typography.Title level={3} style={{ margin: 0 }}>
            {upload.survey.title}
          </Typography.Title>
          <Typography.Text type="secondary">
            {t('surveyUploads.page.version', { when: when(upload.version) })}
          </Typography.Text>
        </Flex>
        {canRerun && (
          <Button
            icon={<ReloadOutlined />}
            loading={rerun.isPending}
            onClick={() => void onRerun()}
            data-testid="survey-upload-rerun"
          >
            {t('surveyUploads.page.rerun')}
          </Button>
        )}
      </Flex>

      <Flex vertical gap={16}>
        <Card size="small" title={t('surveyUploads.page.progressTitle')}>
          <SurveyUploadProgressPanel view={view} />
        </Card>

        <PublicLinkCard upload={upload} canWrite={canWrite} />

        <VersionsCard upload={upload} />

        {canWrite && upload.logTail !== null && (
          <Card size="small" title={t('surveyUploads.page.logTitle')}>
            {upload.logTail.length === 0 ? (
              <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('surveyUploads.page.noLog')} />
            ) : (
              <pre
                data-testid="survey-upload-log-tail"
                style={{ margin: 0, fontSize: 12, whiteSpace: 'pre-wrap', maxHeight: 240, overflow: 'auto' }}
              >
                {upload.logTail}
              </pre>
            )}
          </Card>
        )}

        <Card size="small" title={t('surveyUploads.page.filesTitle')}>
          <Table<SurveyUploadFile>
            scroll={{ x: 'max-content' }}
            rowKey="name"
            size="small"
            pagination={false}
            dataSource={upload.files}
            locale={{ emptyText: t('surveyUploads.page.noFiles') }}
            columns={[
              { title: t('caves.name'), dataIndex: 'name' },
              {
                title: t('surveyUploads.page.fileRole'),
                dataIndex: 'role',
                render: (role: SurveyUploadFile['role']) => <Tag>{t(`surveyUploads.fileRoles.${role}`)}</Tag>,
              },
              {
                title: t('surveyUploads.page.fileStatus'),
                dataIndex: 'status',
                render: (status: SurveyUploadFile['status']) => t(`surveyUploads.fileStates.${status}`),
              },
              {
                title: t('surveySources.size'),
                align: 'right',
                render: (_, file) => formatSize(file.receivedSize ?? file.declaredSize),
              },
              {
                key: 'download',
                render: (_, file) =>
                  file.contentUrl ? (
                    <Button size="small" icon={<DownloadOutlined />} href={file.contentUrl} target="_blank" rel="noreferrer">
                      {t('surveyUploads.artefacts.download')}
                    </Button>
                  ) : null,
              },
            ]}
          />
        </Card>

        <Card size="small" title={t('surveyUploads.page.detailsTitle')}>
          <Descriptions size="small" column={1} data-testid="survey-upload-details">
            <Descriptions.Item label={t('surveyUploads.page.cave')}>
              <Link to={`/caves/${upload.cave.id}`}>{upload.cave.name}</Link>
              {upload.caveCreated && <Tag style={{ marginInlineStart: 8 }}>{t('surveyUploads.page.caveCreated')}</Tag>}
            </Descriptions.Item>
            {upload.survey.name && (
              <Descriptions.Item label={t('surveyUploads.page.surveyName')}>{upload.survey.name}</Descriptions.Item>
            )}
            {upload.survey.date && (
              <Descriptions.Item label={t('surveyUploads.page.surveyDate')}>
                {new Date(upload.survey.date).toLocaleDateString(i18n.language)}
              </Descriptions.Item>
            )}
            {upload.survey.team && (
              <Descriptions.Item label={t('surveyUploads.page.surveyTeam')}>{upload.survey.team}</Descriptions.Item>
            )}
            <Descriptions.Item label={t('surveyUploads.page.uploaded')}>{when(upload.createdAt)}</Descriptions.Item>
            <Descriptions.Item label={t('surveyUploads.page.started')}>{when(upload.startedAt)}</Descriptions.Item>
            <Descriptions.Item label={t('surveyUploads.page.finished')}>
              {unsettled ? t('surveyUploads.page.notFinished') : when(upload.finishedAt)}
            </Descriptions.Item>
            <Descriptions.Item label={t('surveyUploads.page.outputsAsked')}>
              {upload.compile && upload.outputs.length > 0
                ? upload.outputs.map((o) => t(`surveyUploads.outputs.${o}`, { defaultValue: o })).join(', ')
                : t('surveyUploads.page.noCompile')}
            </Descriptions.Item>
          </Descriptions>
        </Card>
      </Flex>
    </div>
  );
}

/**
 * The upload's public link: copy it, open it, withdraw it, replace it.
 *
 * Whether a link exists is the server's word (`publicLink`). Without one the page says why when the
 * server gave a reason (`publicLinkRefusal`), and offers to make one to somebody who may write —
 * except when the cave's protection is the reason, which no press of the button can change. With
 * no reason given it falls back to a sentence naming the possible ones.
 */
function PublicLinkCard({ upload, canWrite }: { upload: SurveyUpload; canWrite: boolean }) {
  const { t, i18n } = useTranslation();
  const { message } = App.useApp();
  const mint = useMintSurveyUploadPublicLink();
  const revoke = useRevokeSurveyUploadPublicLink();
  const link = upload.publicLink;
  const refusal = surveyUploadPublicLinkRefusal(upload);

  const make = async () => {
    try {
      await mint.mutateAsync(upload.id);
      message.success(t('surveyUploads.link.made'));
    } catch (error) {
      message.error(surveyUploadRequestErrorText(error, t));
    }
  };
  const withdraw = async () => {
    try {
      await revoke.mutateAsync(upload.id);
      message.success(t('surveyUploads.link.revoked'));
    } catch (error) {
      message.error(surveyUploadRequestErrorText(error, t));
    }
  };

  return (
    <Card size="small" title={t('surveyUploads.link.title')} data-testid="survey-upload-link-card">
      {link ? (
        <Flex vertical gap={8}>
          <Typography.Text
            code
            copyable={{ text: surveyUploadLinkHref(link.url, window.location.origin) }}
            data-testid="survey-upload-link"
          >
            {surveyUploadLinkHref(link.url, window.location.origin)}
          </Typography.Text>
          <Typography.Text type="secondary">
            {t('surveyUploads.link.madeAt', { when: new Date(link.createdAt).toLocaleString(i18n.language) })}
          </Typography.Text>
          <Flex gap={8} wrap>
            <Button
              size="small"
              icon={<ExportOutlined />}
              href={surveyUploadLinkHref(link.url, window.location.origin)}
              target="_blank"
              rel="noreferrer"
              data-testid="survey-upload-link-open"
            >
              {t('surveyUploads.link.open')}
            </Button>
            {canWrite && (
              <>
              <Popconfirm title={t('surveyUploads.link.remintConfirm')} onConfirm={() => void make()}>
                <Button size="small" loading={mint.isPending} data-testid="survey-upload-link-remint">
                  {t('surveyUploads.link.remint')}
                </Button>
              </Popconfirm>
              <Popconfirm title={t('surveyUploads.link.revokeConfirm')} onConfirm={() => void withdraw()}>
                <Button size="small" danger loading={revoke.isPending} data-testid="survey-upload-link-revoke">
                  {t('surveyUploads.link.revoke')}
                </Button>
              </Popconfirm>
              </>
            )}
          </Flex>
        </Flex>
      ) : (
        <Flex vertical gap={8}>
          <Typography.Text type="secondary" data-testid="survey-upload-no-link">
            {refusal ? t(`surveyUploads.link.refusal.${refusal}`) : t('surveyUploads.link.none')}
          </Typography.Text>
          {canWrite && refusal !== 'protected-cave' && (
            <Flex>
              <Button size="small" loading={mint.isPending} onClick={() => void make()} data-testid="survey-upload-link-make">
                {t('surveyUploads.link.make')}
              </Button>
            </Flex>
          )}
        </Flex>
      )}
    </Card>
  );
}

/**
 * The other versions of this survey, newest first with this one marked, so a person who followed
 * a link to an older upload can see that a newer one exists and reach it. Drawn only when the
 * survey has more than one version.
 */
function VersionsCard({ upload }: { upload: SurveyUpload }) {
  const { t, i18n } = useTranslation();
  const { data: groups } = useCaveSurveys(upload.cave.id);
  const versions = groups?.find((g) => g.id === upload.caveSurvey.id)?.versions ?? [];
  if (versions.length < 2) {
    return null;
  }
  const newest = versions[0]!;
  return (
    <Card size="small" title={t('surveyUploads.page.versionsTitle')} data-testid="survey-upload-versions">
      {newest.id !== upload.id && (
        <Alert
          type="info"
          showIcon
          style={{ marginBottom: 8 }}
          data-testid="survey-upload-newer"
          title={t('surveyUploads.page.newerExists')}
        />
      )}
      <Flex vertical gap={4}>
        {versions.map((v) => (
          <Flex key={v.id} gap={8} align="center">
            {v.id === upload.id ? (
              <Typography.Text strong>{new Date(v.version).toLocaleString(i18n.language)}</Typography.Text>
            ) : (
              <Link to={v.statusPageUrl}>{new Date(v.version).toLocaleString(i18n.language)}</Link>
            )}
            <Tag>{t(`surveyUploads.status.${v.status}`)}</Tag>
            {v.id === upload.id && <Typography.Text type="secondary">{t('surveyUploads.page.thisVersion')}</Typography.Text>}
          </Flex>
        ))}
      </Flex>
    </Card>
  );
}
