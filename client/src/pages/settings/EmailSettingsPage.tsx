// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useRef } from 'react';
import { App, Alert, Button, Card, Flex, Form, Input, Spin, Tag, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { useSearchParams } from 'react-router-dom';
import {
  useCancelEmailChange,
  useConfirmEmailChange,
  useMe,
  useRequestEmailChange,
  useResendEmailChange,
  useVerifyEmail,
} from '../../api/hooks.ts';
import { useAuth } from '../../auth/auth.tsx';

/**
 * The account's email address and the verified flow for changing it.
 *
 * The confirmation link lands here carrying its token in the query, which this page consumes
 * once and then strips — the same shape the external sign-in round trip already uses.
 */
export default function EmailSettingsPage() {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const [form] = Form.useForm<{ newEmail: string }>();
  const [params, setParams] = useSearchParams();
  const { data: me, isPending } = useMe();
  const { refreshSession } = useAuth();

  const request = useRequestEmailChange();
  const confirm = useConfirmEmailChange();
  const resend = useResendEmailChange();
  const cancel = useCancelEmailChange();
  const verify = useVerifyEmail();

  // React runs effects twice in development; without this the token would be spent on the first
  // run and the second would report a failure for a change that actually succeeded.
  const consumed = useRef(false);

  useEffect(() => {
    const token = params.get('confirm');
    if (!token || consumed.current) {
      return;
    }
    consumed.current = true;

    confirm
      .mutateAsync(token)
      .then(async () => {
        message.success(t('settings.emails.confirmSuccess'));
        // Changing the address rotates the sign-in stamp, and the name in the header comes from
        // the token rather than from the profile — so the session has to be renewed for the
        // change to show and for the session not to lapse.
        await refreshSession();
      })
      .catch(() => message.error(t('settings.emails.confirmFailed')))
      .finally(() => {
        const next = new URLSearchParams(params);
        next.delete('confirm');
        setParams(next, { replace: true });
      });
  }, [params, setParams, confirm, message, t, refreshSession]);

  if (isPending || !me) {
    return <Spin />;
  }

  const requestChange = async (values: { newEmail: string }) => {
    try {
      await request.mutateAsync(values.newEmail);
      form.resetFields();
      // Deliberately non-committal: the server does not say whether the address is already in
      // use, because answering would let anyone test which addresses have an account here.
      message.success(t('settings.emails.requested'));
    } catch {
      message.error(t('settings.emails.requestFailed'));
    }
  };

  return (
    <Flex vertical gap={16}>
      <Card
        size="small"
        title={t('settings.emails.current')}
        extra={
          me.emailConfirmed ? (
            <Tag color="green">{t('settings.emails.confirmed')}</Tag>
          ) : (
            <Tag color="orange">{t('settings.emails.unconfirmed')}</Tag>
          )
        }
      >
        <Flex vertical gap={12} align="flex-start">
          <Typography.Text strong>{me.email}</Typography.Text>
          {!me.emailConfirmed && (
            <Button
              loading={verify.isPending}
              onClick={() => {
                verify.mutate(undefined, {
                  onSuccess: () => message.success(t('settings.emails.verifySent')),
                  onError: () => message.error(t('settings.emails.tooSoon')),
                });
              }}
            >
              {t('settings.emails.verify')}
            </Button>
          )}
        </Flex>
      </Card>

      {me.pendingEmail && (
        <Alert
          type="info"
          showIcon
          message={t('settings.emails.pending', { email: me.pendingEmail })}
          description={
            <Flex vertical gap={8} align="flex-start">
              <Typography.Text>{t('settings.emails.pendingHint')}</Typography.Text>
              <Flex gap={8}>
                <Button
                  size="small"
                  loading={resend.isPending}
                  onClick={() => {
                    resend.mutate(undefined, {
                      onSuccess: () => message.success(t('settings.emails.resent')),
                      onError: () => message.error(t('settings.emails.tooSoon')),
                    });
                  }}
                >
                  {t('settings.emails.resend')}
                </Button>
                <Button
                  size="small"
                  danger
                  loading={cancel.isPending}
                  onClick={() => {
                    cancel.mutate(undefined, {
                      onSuccess: () => message.success(t('settings.emails.cancelled')),
                      onError: () => message.error(t('common.saveFailed')),
                    });
                  }}
                >
                  {t('settings.emails.cancelChange')}
                </Button>
              </Flex>
            </Flex>
          }
        />
      )}

      <Card size="small" title={t('settings.emails.change')}>
        <Form form={form} layout="vertical" onFinish={(v) => void requestChange(v)}>
          <Typography.Paragraph type="secondary">{t('settings.emails.changeHint')}</Typography.Paragraph>
          <Form.Item
            name="newEmail"
            label={t('settings.emails.newEmail')}
            rules={[{ required: true }, { type: 'email' }, { max: 256 }]}
          >
            <Input autoComplete="email" />
          </Form.Item>
          <Button type="primary" htmlType="submit" loading={request.isPending}>
            {t('settings.emails.sendLink')}
          </Button>
        </Form>
      </Card>
    </Flex>
  );
}
