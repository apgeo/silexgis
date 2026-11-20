// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { Alert, Card, Flex, Form, Input, Typography } from 'antd';
import Button from '../components/Button.tsx';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import { authTransportProblem, fetchOrNull } from '../auth/authTransportProblem.ts';

interface ForgotFormValues {
  email: string;
}

/**
 * Asks for a password-reset mail. The link in that mail opens the reset page.
 *
 * The server answers alike whether or not the address has an account, so this page cannot tell,
 * and does not pretend to: once the server has the request, it always says "if an account exists,
 * a mail is on its way". What it does not do is say that after a request the server never got or
 * could not complete, because then no mail is coming whatever the address.
 */
export default function ForgotPasswordPage() {
  const { t } = useTranslation();
  const [error, setError] = useState<string | null>(null);
  const [sent, setSent] = useState(false);
  const [submitting, setSubmitting] = useState(false);

  const onFinish = async (values: ForgotFormValues) => {
    setSubmitting(true);
    setError(null);
    try {
      const response = await fetchOrNull('/api/v1/auth/password/forgot', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email: values.email }),
      });
      const transportProblem = authTransportProblem(response, t);
      if (response === null || transportProblem) {
        setError(transportProblem ?? t('auth.unreachable'));
        return;
      }
      // Any answer the server gave about the request itself — accepted, or an address too long to
      // belong to an account — is reported the same way, so the page reveals no more than the API.
      setSent(true);
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <Flex component="main" align="center" justify="center" style={{ minHeight: '100%' }}>
      <Card style={{ width: 360, maxWidth: '100%' }}>
        <Typography.Title level={3} style={{ textAlign: 'center' }}>
          {t('auth.forgotTitle')}
        </Typography.Title>
        {error && <Alert type="error" title={error} style={{ marginBottom: 16 }} />}
        {sent ? (
          <Alert type="success" showIcon title={t('auth.forgotSent')} />
        ) : (
          <>
            <Typography.Paragraph type="secondary">{t('auth.forgotIntro')}</Typography.Paragraph>
            <Form<ForgotFormValues> layout="vertical" onFinish={onFinish} requiredMark={false}>
              <Form.Item name="email" label={t('auth.email')} rules={[{ required: true }, { type: 'email' }]}>
                <Input autoComplete="username" autoFocus />
              </Form.Item>
              <Button type="primary" htmlType="submit" block loading={submitting}>
                {t('auth.forgotSubmit')}
              </Button>
            </Form>
          </>
        )}
        <Flex justify="center" style={{ marginTop: 12 }}>
          <Link to="/login">{t('auth.backToSignIn')}</Link>
        </Flex>
      </Card>
    </Flex>
  );
}
