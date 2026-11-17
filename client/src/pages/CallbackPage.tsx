// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useRef, useState } from 'react';
import { Result, Spin } from 'antd';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import Button from '../components/Button.tsx';
import { userManager } from '../auth/auth.tsx';
import { signInAgain } from '../auth/renewSignIn.ts';

/**
 * Completes the OIDC redirect: exchanges the code (PKCE) and stores tokens in memory, then goes
 * back to the page the sign-in began from.
 *
 * A return that fails stays on this page and says so. It must not go on to a page that needs an
 * account: that page begins signing in, the sign-in server answers at once from its own session,
 * the browser comes straight back here and fails the same way — a loop with a spinner and no
 * message, which spends one more sign-in request each time against a limit shared by everyone
 * behind the same address. A browser clock far from the server's, a lost state record or a token
 * endpoint that refuses all fail like this every time, so only the person can break the loop.
 */
export default function CallbackPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  // The code is single-use; guard against StrictMode double-effects.
  const handled = useRef(false);
  const [failed, setFailed] = useState(false);
  const [unreachable, setUnreachable] = useState(false);

  useEffect(() => {
    if (handled.current) {
      return;
    }
    handled.current = true;

    void userManager
      .signinRedirectCallback()
      .then((user) => navigate(typeof user.state === 'string' ? user.state : '/', { replace: true }))
      .catch(() => setFailed(true));
  }, [navigate]);

  if (!failed) {
    return <Spin size="large" fullscreen />;
  }

  // The page the sign-in began from went with the failed return, so a fresh sign-in starts from
  // the home page. The spent code is never exchanged again; each press begins one new sign-in.
  return (
    <Result
      status="warning"
      data-testid="sign-in-return-failed"
      title={t('auth.callbackFailed')}
      subTitle={unreachable ? t('auth.serverUnreachableHint') : t('auth.callbackFailedHint')}
      extra={
        <Button
          type="primary"
          onClick={() => {
            setUnreachable(false);
            signInAgain('/').catch(() => setUnreachable(true));
          }}
        >
          {t('common.retry')}
        </Button>
      }
    />
  );
}
