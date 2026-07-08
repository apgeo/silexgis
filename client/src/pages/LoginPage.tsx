// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useState } from 'react';
import { GithubOutlined, GoogleOutlined, LoginOutlined } from '@ant-design/icons';
import { Alert, Button, Card, Divider, Flex, Form, Input, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { useNavigate, useSearchParams } from 'react-router-dom';

interface LoginFormValues {
  email: string;
  password: string;
  twoFactorCode?: string;
}

interface ExternalProvider {
  name: string;
  displayName: string;
}

interface AuthConfig {
  openRegistration: boolean;
  externalOnly: boolean;
  providers: ExternalProvider[];
}

function providerIcon(name: string) {
  if (name === 'google') return <GoogleOutlined />;
  if (name === 'github') return <GithubOutlined />;
  return <LoginOutlined />;
}

/**
 * Establishes the server cookie session, then resumes the OIDC authorize flow the server
 * redirected away from (returnUrl), or navigates home where RequireAuth restarts it.
 * External providers use the same returnUrl: their callback establishes the session and
 * bounces the browser straight back into that flow.
 */
export default function LoginPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [needsMfa, setNeedsMfa] = useState(false);
  const [config, setConfig] = useState<AuthConfig | null>(null);

  const returnUrl = params.get('returnUrl');

  useEffect(() => {
    // A failed external sign-in bounces back with ?error=<code>.
    if (params.get('error')) {
      setError(t('auth.externalFailed'));
    }
    void fetch('/api/v1/auth/config')
      .then((r) => (r.ok ? (r.json() as Promise<AuthConfig>) : null))
      .then((c) => setConfig(c))
      .catch(() => setConfig(null));
  }, [params, t]);

  const externalHref = (name: string) => {
    const target = new URLSearchParams();
    if (returnUrl) {
      target.set('returnUrl', returnUrl);
    }
    const query = target.toString();
    return `/api/v1/auth/external/${encodeURIComponent(name)}${query ? `?${query}` : ''}`;
  };

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

  const providers = config?.providers ?? [];
  const showPasswordForm = !config?.externalOnly || providers.length === 0;

  return (
    <Flex align="center" justify="center" style={{ minHeight: '100%' }}>
      <Card style={{ width: 360 }}>
        <Typography.Title level={3} style={{ textAlign: 'center' }}>
          {t('app.name')}
        </Typography.Title>
        {error && <Alert type="error" message={error} style={{ marginBottom: 16 }} />}

        {showPasswordForm && (
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
        )}

        {providers.length > 0 && (
          <>
            {showPasswordForm && <Divider plain>{t('auth.or')}</Divider>}
            <Flex vertical gap={8}>
              {providers.map((p) => (
                <Button key={p.name} icon={providerIcon(p.name)} block href={externalHref(p.name)}>
                  {t('auth.signInWith', { provider: p.displayName })}
                </Button>
              ))}
            </Flex>
          </>
        )}
      </Card>
    </Flex>
  );
}
