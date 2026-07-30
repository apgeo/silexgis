// SPDX-License-Identifier: AGPL-3.0-or-later
import { DownloadOutlined } from '@ant-design/icons';
import { App, Alert, Button, Card, Flex, Form, Input, Popconfirm, Spin, Tag, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { ApiError } from '../../api/client.ts';
import { downloadFile } from '../../api/download.ts';
import {
  useChangeUsername,
  useDataExport,
  useMe,
  useRequestDataExport,
} from '../../api/hooks.ts';
import { useAuth } from '../../auth/auth.tsx';

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
  const { data: me, isPending } = useMe();
  const { refreshSession } = useAuth();
  const changeUsername = useChangeUsername();
  const requestExport = useRequestDataExport();
  const { data: dataExport } = useDataExport();

  if (isPending || !me) {
    return <Spin />;
  }

  const submitUsername = async (values: { username: string }) => {
    try {
      await changeUsername.mutateAsync(values.username);
      // Renaming invalidates the session behind the tokens; renew before it lapses.
      await refreshSession();
      message.success(t('settings.account.usernameChanged'));
    } catch (error) {
      const code = error instanceof ApiError ? error.code : undefined;
      message.error(
        code === 'me.username_taken'
          ? t('settings.account.usernameTaken')
          : t('settings.account.usernameInvalid'),
      );
    }
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
                    downloadFile(`/api/v1/me/data-export/${dataExport.id}/download`).catch(() =>
                      message.error(t('common.saveFailed')),
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
                onError: () => message.error(t('common.saveFailed')),
              });
            }}
          >
            {t('settings.account.export.request')}
          </Button>
        </Flex>
      </Card>

      <Alert type="info" showIcon message={t('settings.account.noDeletion')} />
    </Flex>
  );
}
