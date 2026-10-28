// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { Alert, Card, Flex, Form, Input, Switch, Typography } from 'antd';
import Button from '../components/Button.tsx';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';

interface ResetFormValues {
  newPassword: string;
  confirmPassword: string;
  revokeAccessKeys?: boolean;
}

/**
 * Where a password-reset link lands: the token is in the URL, the new password is typed here.
 *
 * An invitation to an account an administrator opened lands here too, marked `invite=1`: it is the
 * same link and the same request, and only the wording changes — somebody choosing their first
 * password has nothing to reset.
 *
 * A reset asks, as a change at the settings does, whether the account's personal access keys go
 * too; they stay unless the reader says so. An invitation does not ask — a new account has no keys.
 */
export default function ResetPasswordPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const [error, setError] = useState<string | null>(null);
  const [done, setDone] = useState(false);
  const [submitting, setSubmitting] = useState(false);

  const email = params.get('email');
  const token = params.get('token');
  const invite = params.get('invite') === '1';

  const onFinish = async (values: ResetFormValues) => {
    if (!email || !token) {
      setError(t('auth.resetLinkInvalid'));
      return;
    }

    setSubmitting(true);
    setError(null);
    try {
      const response = await fetch('/api/v1/auth/password/reset', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          email,
          token,
          newPassword: values.newPassword,
          revokeAccessKeys: !invite && (values.revokeAccessKeys ?? false),
        }),
      });

      if (!response.ok) {
        const problem = (await response.json().catch(() => null)) as
          | { code?: string; errors?: Record<string, string> }
          | null;
        setError(
          problem?.code === 'validation.failed'
            ? t('auth.resetPasswordRejected')
            : t('auth.resetLinkInvalid'),
        );
        return;
      }

      setDone(true);
      setTimeout(() => navigate('/login', { replace: true }), 2000);
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <Flex align="center" justify="center" style={{ minHeight: '100%' }}>
      <Card style={{ width: 400 }}>
        <Typography.Title level={3} style={{ textAlign: 'center' }}>
          {invite ? t('auth.invitePasswordTitle') : t('auth.resetPasswordTitle')}
        </Typography.Title>
        {invite && !done && (
          <Typography.Paragraph type="secondary">{t('auth.invitePasswordIntro')}</Typography.Paragraph>
        )}

        {error && <Alert type="error" title={error} style={{ marginBottom: 16 }} />}

        {done ? (
          <>
            <Alert type="success" showIcon title={invite ? t('auth.invitePasswordDone') : t('auth.resetPasswordDone')} style={{ marginBottom: 16 }} />
            <Link to="/login">
              <Button type="primary" block>
                {t('auth.signIn')}
              </Button>
            </Link>
          </>
        ) : (
          <Form<ResetFormValues> layout="vertical" onFinish={onFinish} requiredMark={false}>
            <Form.Item
              name="newPassword"
              label={t('auth.newPassword')}
              rules={[{ required: true }, { min: 10, message: t('auth.passwordTooShort') }]}
            >
              <Input.Password autoComplete="new-password" autoFocus />
            </Form.Item>
            <Form.Item
              name="confirmPassword"
              label={t('auth.confirmPassword')}
              dependencies={['newPassword']}
              rules={[
                { required: true },
                ({ getFieldValue }) => ({
                  validator: (_, value) =>
                    !value || getFieldValue('newPassword') === value
                      ? Promise.resolve()
                      : Promise.reject(new Error(t('auth.passwordsDiffer'))),
                }),
              ]}
            >
              <Input.Password autoComplete="new-password" />
            </Form.Item>
            {!invite && (
              <Form.Item
                name="revokeAccessKeys"
                label={t('security.password.revokeKeys')}
                valuePropName="checked"
                initialValue={false}
                extra={t('security.password.revokeKeysHint')}
              >
                <Switch data-testid="reset-revoke-access-keys" />
              </Form.Item>
            )}
            <Button type="primary" htmlType="submit" block loading={submitting}>
              {t('auth.resetPasswordSubmit')}
            </Button>
          </Form>
        )}
      </Card>
    </Flex>
  );
}
