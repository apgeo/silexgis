// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { Alert, Button, Card, Flex, Form, Input, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';

interface ResetFormValues {
  newPassword: string;
  confirmPassword: string;
}

/** Where a password-reset link lands: the token is in the URL, the new password is typed here. */
export default function ResetPasswordPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const [error, setError] = useState<string | null>(null);
  const [done, setDone] = useState(false);
  const [submitting, setSubmitting] = useState(false);

  const email = params.get('email');
  const token = params.get('token');

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
        body: JSON.stringify({ email, token, newPassword: values.newPassword }),
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
          {t('auth.resetPasswordTitle')}
        </Typography.Title>

        {error && <Alert type="error" title={error} style={{ marginBottom: 16 }} />}

        {done ? (
          <>
            <Alert type="success" showIcon title={t('auth.resetPasswordDone')} style={{ marginBottom: 16 }} />
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
            <Button type="primary" htmlType="submit" block loading={submitting}>
              {t('auth.resetPasswordSubmit')}
            </Button>
          </Form>
        )}
      </Card>
    </Flex>
  );
}
