// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useState } from 'react';
import { GithubOutlined, GoogleOutlined, InfoCircleOutlined, LoginOutlined } from '@ant-design/icons';
import { Alert, Button, Card, Divider, Flex, Form, Input, Popover, Segmented, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { useNavigate, useSearchParams } from 'react-router-dom';

type TwoFactorMethod = 'authenticator' | 'email' | 'sms';

interface LoginFormValues {
  email: string;
  password: string;
  twoFactorCode?: string;
}

interface ExternalProvider {
  name: string;
  displayName: string;
}

/** A demo account a test installation announces on this page, credentials included. */
interface TestLogin {
  email: string;
  password: string;
  role: string;
}

interface AuthConfig {
  openRegistration: boolean;
  externalOnly: boolean;
  providers: ExternalProvider[];
  testLogins?: TestLogin[] | null;
}

/** The second-factor step the server asked for, as described by the 401 that started it. */
interface MfaChallenge {
  methods: TwoFactorMethod[];
  preferredMethod: TwoFactorMethod | null;
  recoveryAccepted: boolean;
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
 *
 * A second factor turns this into two round trips: the first is refused with the list of methods
 * the account can use, and the second carries the code. Codes that have to be delivered are asked
 * for in between — the server holds the half-finished sign-in, so nothing is re-sent here.
 */
export default function LoginPage() {
  const { t } = useTranslation();
  const [form] = Form.useForm<LoginFormValues>();
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [sending, setSending] = useState(false);
  const [challenge, setChallenge] = useState<MfaChallenge | null>(null);
  const [method, setMethod] = useState<TwoFactorMethod | null>(null);
  const [useRecovery, setUseRecovery] = useState(false);
  const [unconfirmedEmail, setUnconfirmedEmail] = useState<string | null>(null);
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

  const sendCode = async (target: TwoFactorMethod) => {
    setSending(true);
    setError(null);
    setNotice(null);
    try {
      const response = await fetch('/api/v1/auth/2fa/send', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ method: target }),
      });
      const body = (await response.json().catch(() => null)) as
        | { destination?: string; expiresMinutes?: number; code?: string }
        | null;

      if (!response.ok) {
        setError(
          body?.code === 'auth.mfa_resend_too_soon' ? t('auth.mfaResendTooSoon') : t('auth.mfaSendFailed'),
        );
        return;
      }
      setNotice(t('auth.mfaCodeSent', { destination: body?.destination ?? '' }));
    } catch {
      setError(t('auth.mfaSendFailed'));
    } finally {
      setSending(false);
    }
  };

  const resendConfirmation = async (email: string) => {
    setSending(true);
    try {
      await fetch('/api/v1/auth/email/resend', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email }),
      });
      // Always reported as sent: the endpoint deliberately does not say whether the address has
      // an account, and the page must not leak what the API withholds.
      setNotice(t('auth.confirmationResent'));
    } finally {
      setSending(false);
    }
  };

  const onFinish = async (values: LoginFormValues) => {
    setSubmitting(true);
    setError(null);
    setNotice(null);
    try {
      const code = values.twoFactorCode?.trim();
      const response = await fetch('/api/v1/auth/login', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          email: values.email,
          password: values.password,
          twoFactorCode: code ? (useRecovery ? `recovery:${code}` : code) : undefined,
          twoFactorMethod: code && !useRecovery ? (method ?? 'authenticator') : undefined,
        }),
      });

      if (!response.ok) {
        const problem = (await response.json().catch(() => null)) as
          | { code?: string; methods?: TwoFactorMethod[]; preferredMethod?: TwoFactorMethod | null; recoveryAccepted?: boolean }
          | null;

        if (problem?.code === 'auth.mfa_required') {
          const methods = problem.methods ?? [];
          const preferred = problem.preferredMethod ?? methods[0] ?? null;
          setChallenge({
            methods,
            preferredMethod: preferred,
            recoveryAccepted: problem.recoveryAccepted ?? true,
          });
          setMethod(preferred);
          // Nothing can be delivered, so a recovery code is the only way through — say so up
          // front instead of showing an empty chooser.
          setUseRecovery(methods.length === 0);
          setError(methods.length === 0 ? t('auth.mfaRecoveryOnly') : t('auth.mfaRequired'));
          // A code the user cannot produce unaided is requested for them, so the common case is
          // one click: choose nothing, read the mail, type the code.
          if (preferred === 'email' || preferred === 'sms') {
            void sendCode(preferred);
          }
          return;
        }
        if (problem?.code === 'auth.mfa_invalid') {
          setError(t('auth.mfaInvalid'));
          return;
        }
        if (problem?.code === 'auth.email_not_confirmed') {
          setUnconfirmedEmail(values.email);
          setError(t('auth.emailNotConfirmed'));
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
  const deliverable = method === 'email' || method === 'sms';
  const testLogins = showPasswordForm ? (config?.testLogins ?? []) : [];

  // Literal keys, not `t(\`…_${role}\`)`: the translation guard verifies keys it can read
  // out of the source, and a composed key would be invisible to it.
  const testRoleLabel = (role: string) => {
    if (role === 'administrator') return t('auth.testLogins.roleAdministrator');
    if (role === 'editor') return t('auth.testLogins.roleEditor');
    if (role === 'viewer') return t('auth.testLogins.roleViewer');
    return role;
  };
  const testRoleDescription = (role: string) => {
    if (role === 'administrator') return t('auth.testLogins.permsAdministrator');
    if (role === 'editor') return t('auth.testLogins.permsEditor');
    if (role === 'viewer') return t('auth.testLogins.permsViewer');
    return null;
  };

  return (
    <Flex align="center" justify="center" style={{ minHeight: '100%' }}>
      <Card style={{ width: 360 }}>
        <Typography.Title level={3} style={{ textAlign: 'center' }}>
          {t('app.name')}
        </Typography.Title>
        {error && <Alert type="error" title={error} style={{ marginBottom: 16 }} />}
        {notice && <Alert type="success" title={notice} style={{ marginBottom: 16 }} />}
        {unconfirmedEmail && (
          <Button
            block
            loading={sending}
            style={{ marginBottom: 16 }}
            onClick={() => void resendConfirmation(unconfirmedEmail)}
          >
            {t('auth.resendConfirmation')}
          </Button>
        )}

        {testLogins.length > 0 && (
          <Alert
            type="info"
            title={t('auth.testLogins.title')}
            style={{ marginBottom: 16 }}
            description={
              <Flex vertical gap={8}>
                <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                  {t('auth.testLogins.hint')}
                </Typography.Text>
                {testLogins.map((login) => (
                  <Flex key={login.email} vertical gap={2}>
                    <Flex align="center" gap={8}>
                      <Button
                        size="small"
                        style={{ flex: 1 }}
                        disabled={challenge !== null}
                        onClick={() => form.setFieldsValue({ email: login.email, password: login.password })}
                      >
                        {testRoleLabel(login.role)}
                      </Button>
                      {testRoleDescription(login.role) && (
                        <Popover
                          trigger="click"
                          title={testRoleLabel(login.role)}
                          content={
                            <Typography.Paragraph style={{ maxWidth: 280, marginBottom: 0 }}>
                              {testRoleDescription(login.role)}
                            </Typography.Paragraph>
                          }
                        >
                          <Button
                            size="small"
                            type="text"
                            icon={<InfoCircleOutlined />}
                            aria-label={t('auth.testLogins.about')}
                          />
                        </Popover>
                      )}
                    </Flex>
                    <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                      {login.email} · {login.password}
                    </Typography.Text>
                  </Flex>
                ))}
              </Flex>
            }
          />
        )}

        {showPasswordForm && (
          <Form<LoginFormValues> form={form} layout="vertical" onFinish={onFinish} requiredMark={false}>
            <Form.Item name="email" label={t('auth.email')} rules={[{ required: true }, { type: 'email' }]}>
              <Input autoComplete="username" autoFocus disabled={challenge !== null} />
            </Form.Item>
            <Form.Item name="password" label={t('auth.password')} rules={[{ required: true }]}>
              <Input.Password autoComplete="current-password" disabled={challenge !== null} />
            </Form.Item>

            {challenge && challenge.methods.length > 1 && !useRecovery && (
              <Form.Item label={t('auth.mfaMethod')}>
                <Segmented<TwoFactorMethod>
                  block
                  value={method ?? challenge.methods[0]}
                  onChange={(next) => {
                    setMethod(next);
                    setNotice(null);
                    if (next === 'email' || next === 'sms') {
                      void sendCode(next);
                    }
                  }}
                  options={challenge.methods.map((m) => ({ label: t(`auth.mfaMethod_${m}`), value: m }))}
                />
              </Form.Item>
            )}

            {challenge && (
              <Form.Item
                name="twoFactorCode"
                label={useRecovery ? t('auth.recoveryCode') : t('auth.mfaCode')}
                rules={[{ required: true }]}
              >
                <Input
                  autoComplete="one-time-code"
                  autoFocus
                  placeholder={useRecovery ? undefined : '123456'}
                />
              </Form.Item>
            )}

            {challenge && deliverable && !useRecovery && (
              <Button type="link" block loading={sending} onClick={() => void sendCode(method!)}>
                {t('auth.mfaResend')}
              </Button>
            )}

            <Button type="primary" htmlType="submit" block loading={submitting}>
              {t('auth.signIn')}
            </Button>

            {challenge?.recoveryAccepted && challenge.methods.length > 0 && (
              <Button
                type="link"
                block
                onClick={() => {
                  setUseRecovery((previous) => !previous);
                  setNotice(null);
                }}
              >
                {useRecovery ? t('auth.mfaUseMethod') : t('auth.mfaUseRecovery')}
              </Button>
            )}
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
