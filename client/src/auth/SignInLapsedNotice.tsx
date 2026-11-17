// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { App, Modal, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { useLocation } from 'react-router-dom';
import Button from '../components/Button.tsx';
import { signInAgain } from './renewSignIn.ts';
import { useSignInLapsed } from './signInLapsed.ts';

/**
 * Says that the sign-in has ended and offers signing in again, once a request has found it so.
 *
 * Nothing leaves the page until the person presses the button: whatever form is open stays on
 * screen, so its text can be copied first. Staying is offered too; the notice comes back with the
 * next request the server refuses.
 */
export default function SignInLapsedNotice() {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const location = useLocation();
  const lapsed = useSignInLapsed((state) => state.lapsed);
  const dismiss = useSignInLapsed((state) => state.dismiss);
  const [leaving, setLeaving] = useState(false);

  const signIn = () => {
    setLeaving(true);
    // Back to this very page. A sign-in server that cannot be reached rejects before the page is
    // left, and the page is still here to say so.
    signInAgain(location.pathname + location.search + location.hash).catch(() => {
      setLeaving(false);
      void message.error(t('auth.serverUnreachable'));
    });
  };

  return (
    <Modal
      open={lapsed}
      title={t('auth.signInLapsedTitle')}
      closable={false}
      mask={{ closable: false }}
      keyboard={false}
      footer={[
        <Button key="stay" onClick={dismiss} data-testid="sign-in-lapsed-stay">
          {t('auth.signInLapsedStay')}
        </Button>,
        <Button
          key="sign-in"
          type="primary"
          loading={leaving}
          onClick={signIn}
          data-testid="sign-in-lapsed-sign-in"
        >
          {t('auth.signInLapsedSignIn')}
        </Button>,
      ]}
    >
      <Typography.Paragraph style={{ marginBottom: 0 }}>{t('auth.signInLapsedBody')}</Typography.Paragraph>
    </Modal>
  );
}
