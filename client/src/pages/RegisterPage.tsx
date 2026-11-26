// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { Alert, Card, Flex, Form, Input, Spin, Typography } from 'antd';
import Button from '../components/Button.tsx';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import { authTransportProblem, fetchOrNull } from '../auth/authTransportProblem.ts';
import { useAuthConfig } from '../auth/useAuthConfig.ts';

interface RegisterFormValues {
  email: string;
  password: string;
  confirmPassword: string;
  displayName?: string;
}

/** The accepted answer: whether a confirmation mail went out, and whether one is needed at all. */
interface RegistrationAnswer {
  mailSent?: boolean;
  confirmationRequired?: boolean;
}

/** The display-name column's length, which the server also enforces. */
const DISPLAY_NAME_MAX = 100;

/**
 * Opens an account on an installation that allows it.
 *
 * Whether it does is the installation's own setting, read from the sign-in options; where it does
 * not, the page says so and shows no form, so nobody fills one in only to be refused. The server
 * answers a new address and one that already has an account alike (the holder of a taken address
 * is mailed instead), so the page words the answer from what the server says about the mail, never
 * from whether the account is new.
 */
export default function RegisterPage() {
  const { t } = useTranslation();
  const { config, failed: configFailed, reload: reloadConfig } = useAuthConfig();
  const [error, setError] = useState<string | null>(null);
  const [closed, setClosed] = useState(false);
  const [outcome, setOutcome] = useState<{ text: string; offerReset: boolean } | null>(null);
  const [submitting, setSubmitting] = useState(false);

  const onFinish = async (values: RegisterFormValues) => {
    setSubmitting(true);
    setError(null);
    try {
      const response = await fetchOrNull('/api/v1/auth/register', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          email: values.email,
          password: values.password,
          displayName: values.displayName?.trim() || null,
        }),
      });
      const transportProblem = authTransportProblem(response, t);
      if (response === null || transportProblem) {
        setError(transportProblem ?? t('auth.unreachable'));
        return;
      }
      if (!response.ok) {
        const problem = (await response.json().catch(() => null)) as { code?: string } | null;
        if (problem?.code === 'auth.registration_disabled') {
          // Switched off since the page read the options: the form is no use any more.
          setClosed(true);
          return;
        }
        setError(t('auth.registerRejected'));
        return;
      }
      const answer = (await response.json().catch(() => null)) as RegistrationAnswer | null;
      if (answer?.mailSent) {
        setOutcome({ text: t('auth.registerMailSent'), offerReset: false });
      } else if (answer?.confirmationRequired) {
        setOutcome({ text: t('auth.registerMailNotSent'), offerReset: false });
      } else {
        // Also the answer for an address that already had an account, whose password the request
        // left as it was: the way back to that account is a password reset.
        setOutcome({ text: t('auth.registerDone'), offerReset: true });
      }
    } finally {
      setSubmitting(false);
    }
  };

  let body;
  if (outcome) {
    body = (
      <Alert
        type="success"
        showIcon
        title={outcome.text}
        description={outcome.offerReset ? <Link to="/forgot-password">{t('auth.forgotPassword')}</Link> : undefined}
      />
    );
  } else if (configFailed) {
    body = (
      <Alert
        type="error"
        title={t('auth.configFailed')}
        action={
          <Button size="small" onClick={reloadConfig}>
            {t('common.retry')}
          </Button>
        }
      />
    );
  } else if (!config) {
    body = (
      <Flex justify="center">
        <Spin />
      </Flex>
    );
  } else if (closed || !config.openRegistration) {
    body = <Alert type="info" showIcon title={t('auth.registrationDisabled')} />;
  } else {
    body = (
      <>
        {error && <Alert type="error" title={error} style={{ marginBottom: 16 }} />}
        <Form<RegisterFormValues> layout="vertical" onFinish={onFinish} requiredMark={false}>
          <Form.Item name="email" label={t('auth.email')} rules={[{ required: true }, { type: 'email' }]}>
            <Input autoComplete="username" autoFocus />
          </Form.Item>
          <Form.Item
            name="password"
            label={t('auth.password')}
            rules={[{ required: true }, { min: 10, message: t('auth.passwordTooShort') }]}
            extra={t('auth.passwordRule')}
          >
            <Input.Password autoComplete="new-password" />
          </Form.Item>
          <Form.Item
            name="confirmPassword"
            label={t('auth.confirmPassword')}
            dependencies={['password']}
            rules={[
              { required: true },
              ({ getFieldValue }) => ({
                validator: (_, value) =>
                  !value || getFieldValue('password') === value
                    ? Promise.resolve()
                    : Promise.reject(new Error(t('auth.passwordsDiffer'))),
              }),
            ]}
          >
            <Input.Password autoComplete="new-password" />
          </Form.Item>
          <Form.Item name="displayName" label={t('auth.displayName')}>
            <Input autoComplete="name" maxLength={DISPLAY_NAME_MAX} />
          </Form.Item>
          <Button type="primary" htmlType="submit" block loading={submitting}>
            {t('auth.createAccount')}
          </Button>
        </Form>
      </>
    );
  }

  return (
    <Flex component="main" align="center" justify="center" style={{ minHeight: '100%' }}>
      <Card style={{ width: 400, maxWidth: '100%' }}>
        <Typography.Title level={3} style={{ textAlign: 'center' }}>
          {t('auth.registerTitle')}
        </Typography.Title>
        {body}
        <Flex justify="center" style={{ marginTop: 12 }}>
          <Link to="/login">{t('auth.backToSignIn')}</Link>
        </Flex>
      </Card>
    </Flex>
  );
}
