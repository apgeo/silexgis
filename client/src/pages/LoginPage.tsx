// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { Alert, Button, Card, Flex, Form, Input, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { useNavigate, useSearchParams } from 'react-router-dom';

interface LoginFormValues {
  email: string;
  password: string;
  twoFactorCode?: string;
}

/**
 * Establishes the server cookie session, then resumes the OIDC authorize flow the server
 * redirected away from (returnUrl), or navigates home where RequireAuth restarts it.
 */
export default function LoginPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [needsMfa, setNeedsMfa] = useState(false);

  const onFinish = async (values: LoginFormValues) => {
    setSubmitting(true);
    setError(null);
    try {
      const response = await fetch('/api/v1/auth/login', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(values),
      });

      if (!response.ok) {
        const problem = (await response.json().catch(() => null)) as { code?: string } | null;
        if (problem?.code === 'auth.mfa_required') {
          // Password accepted; reveal the second-factor field and resubmit.
          setNeedsMfa(true);
          setError(t('auth.mfaRequired'));
          return;
        }
        if (problem?.code === 'auth.mfa_invalid') {
          setError(t('auth.mfaInvalid'));
          return;
        }
        setError(problem?.code === 'auth.locked_out' ? t('auth.lockedOut') : t('auth.invalidCredentials'));
        return;
      }

      const returnUrl = params.get('returnUrl');
      // Only resume OIDC authorize URLs — never follow arbitrary redirect targets.
      if (returnUrl?.startsWith('/connect/')) {
        window.location.assign(returnUrl);
      } else {
        navigate('/', { replace: true });
      }
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <Flex align="center" justify="center" style={{ minHeight: '100%' }}>
      <Card style={{ width: 360 }}>
        <Typography.Title level={3} style={{ textAlign: 'center' }}>
          {t('app.name')}
        </Typography.Title>
        {error && <Alert type="error" message={error} style={{ marginBottom: 16 }} />}
        <Form<LoginFormValues> layout="vertical" onFinish={onFinish} requiredMark={false}>
          <Form.Item name="email" label={t('auth.email')} rules={[{ required: true }, { type: 'email' }]}>
            <Input autoComplete="username" autoFocus />
          </Form.Item>
          <Form.Item name="password" label={t('auth.password')} rules={[{ required: true }]}>
            <Input.Password autoComplete="current-password" />
          </Form.Item>
          {needsMfa && (
            <Form.Item name="twoFactorCode" label={t('auth.mfaCode')} rules={[{ required: true }]}>
              <Input autoComplete="one-time-code" autoFocus placeholder="123456" />
            </Form.Item>
          )}
          <Button type="primary" htmlType="submit" block loading={submitting}>
            {t('auth.signIn')}
          </Button>
        </Form>
      </Card>
    </Flex>
  );
}
