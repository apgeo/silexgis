// SPDX-License-Identifier: AGPL-3.0-or-later
import { DownloadOutlined } from '@ant-design/icons';
import { App, Alert, Card, Flex, Form, Input, Popconfirm, Spin, Tag, Typography } from 'antd';
import Button from '../../components/Button.tsx';
import ReadFailedAlert from '../../components/ReadFailedAlert.tsx';
import type { TFunction } from 'i18next';
import { useTranslation } from 'react-i18next';
import { ApiError } from '../../api/client.ts';
import { DownloadError, downloadFile } from '../../api/download.ts';
import {
  useChangeUsername,
  useDataExport,
  useMe,
  useRequestDataExport,
} from '../../api/hooks.ts';
import { useAuth } from '../../auth/auth.tsx';
import CalendarFeedCard from './CalendarFeedCard.tsx';

/**
 * Account-level actions: the sign-in name, and a copy of the account's own data.
 *
 * There is deliberately no self-service account deletion. A user owns caves, trips and files
 * that other members depend on, so removing the account is an administrative action with a
 * decision behind it — what happens to that content — rather than a button.
 */
export default function AccountSettingsPage() {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const [form] = Form.useForm<{ username: string }>();
  const { data: me, isPending, isError, error: readError, refetch } = useMe();
  const { refreshSession } = useAuth();
  const changeUsername = useChangeUsername();
  const requestExport = useRequestDataExport();
  const { data: dataExport } = useDataExport();

  if (isError && !me) {
    return (
      <ReadFailedAlert
        error={readError}
        isError
        held={false}
        onRetry={refetch}
        title={t('settings.meLoadFailed')}
        testId="account-load-failed"
      />
    );
  }
  if (isPending || !me) {
    return <Spin />;
  }

  const submitUsername = async (values: { username: string }) => {
    try {
      await changeUsername.mutateAsync(values.username);
    } catch (error) {
      const code = error instanceof ApiError ? error.code : undefined;
      message.error(
        code === 'me.username_taken'
          ? t('settings.account.usernameTaken')
          : code === 'me.username_invalid'
            ? t('settings.account.usernameInvalid')
            : t('common.saveFailed'),
      );
      return;
    }
    // Renaming invalidates the session behind the tokens; renew before it lapses. The renewal is
    // best effort and never throws, and it stays outside the rename's catch so that nothing it does
    // could be reported as a refused name.
    await refreshSession();
    message.success(t('settings.account.usernameChanged'));
  };

  const ready = dataExport?.status === 'ready';
  const building = dataExport?.status === 'queued' || dataExport?.status === 'running';

  return (
    <Flex vertical gap={16}>
      <Card size="small" title={t('settings.account.usernameSection')}>
        <Form
          form={form}
          layout="vertical"
          initialValues={{ username: me.userName }}
          onFinish={(v) => void submitUsername(v)}
        >
          <Typography.Paragraph type="secondary">{t('settings.account.usernameHint')}</Typography.Paragraph>
          <Form.Item
            name="username"
            label={t('settings.profile.username')}
            rules={[
              { required: true },
              { max: 64 },
              { pattern: /^[A-Za-z0-9._@+-]+$/, message: t('settings.account.usernameCharacters') },
            ]}
          >
            <Input autoComplete="username" />
          </Form.Item>
          <Popconfirm
            title={t('settings.account.usernameConfirm')}
            onConfirm={() => form.submit()}
          >
            <Button loading={changeUsername.isPending}>{t('settings.account.changeUsername')}</Button>
          </Popconfirm>
        </Form>
      </Card>

      <Card size="small" title={t('settings.account.export.heading')}>
        <Flex vertical gap={12} align="flex-start">
          <Typography.Paragraph type="secondary" style={{ margin: 0 }}>
            {t('settings.account.export.intro')}
          </Typography.Paragraph>

          {dataExport && (
            <Flex gap={8} align="center" wrap>
              <Tag color={ready ? 'green' : dataExport.status === 'failed' ? 'red' : 'blue'}>
                {t(`settings.account.export.status.${dataExport.status}`)}
              </Tag>
              {ready && (
                <Button
                  icon={<DownloadOutlined />}
                  onClick={() => {
                    downloadFile(`/api/v1/me/data-export/${dataExport.id}/download`).catch((error: unknown) =>
                      message.error(exportRefusal(error, t, 'settings.account.export.downloadFailed')),
                    );
                  }}
                >
                  {t('settings.account.export.download')}
                </Button>
              )}
              {dataExport.status === 'failed' && dataExport.error && (
                <Typography.Text type="danger">{dataExport.error}</Typography.Text>
              )}
            </Flex>
          )}

          <Button
            type="primary"
            loading={requestExport.isPending || building}
            onClick={() => {
              requestExport.mutate(undefined, {
                onSuccess: () => message.success(t('settings.account.export.requested')),
                onError: (error) => message.error(exportRefusal(error, t, 'settings.account.export.requestFailed')),
              });
            }}
          >
            {t('settings.account.export.request')}
          </Button>
        </Flex>
      </Card>

      <CalendarFeedCard />

      <Alert type="info" showIcon title={t('settings.account.noDeletion')} />
    </Flex>
  );
}

/**
 * Why asking for, or fetching, the account's export did not go through. The server refuses a
 * second request while one is still being prepared, and a download of an export that has expired
 * or been swept; each of those tells the person what to do next. Anything else is said with the
 * action's own sentence rather than as a failed save.
 */
function exportRefusal(error: unknown, t: TFunction, fallbackKey: string): string {
  const code = error instanceof ApiError || error instanceof DownloadError ? error.code : undefined;
  switch (code) {
    case 'me.export_in_progress':
      return t('settings.account.export.inProgress');
    case 'me.export_expired':
      return t('settings.account.export.expired');
    case 'me.export_not_found':
      return t('settings.account.export.gone');
    default:
      return t(fallbackKey);
  }
}
