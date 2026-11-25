// SPDX-License-Identifier: AGPL-3.0-or-later
import { useCallback, useEffect, useState } from 'react';

export interface ExternalProvider {
  name: string;
  displayName: string;
}

/** A demo account a test installation announces on the sign-in page, credentials included. */
export interface TestLogin {
  email: string;
  password: string;
  role: string;
}

/** What an installation tells a visitor before anybody has signed in. */
export interface AuthConfig {
  openRegistration: boolean;
  externalOnly: boolean;
  providers: ExternalProvider[];
  testLogins?: TestLogin[] | null;
}

export interface AuthConfigRead {
  /** `null` until read, and after a read that failed. */
  config: AuthConfig | null;
  /** The read got no usable answer: the page says so rather than acting as if nothing is offered. */
  failed: boolean;
  reload: () => void;
}

/**
 * The installation's sign-in options, read from the anonymous configuration endpoint.
 *
 * Read with plain `fetch` like the rest of the sign-in pages: the endpoint is anonymous, and the
 * generated client's sign-in renewal has nothing to do before anybody is signed in. A failed read
 * is reported, not swallowed: without it a page would quietly drop the provider buttons, or offer a
 * password form an installation that signs people in only through providers would refuse.
 */
export function useAuthConfig(): AuthConfigRead {
  const [config, setConfig] = useState<AuthConfig | null>(null);
  const [failed, setFailed] = useState(false);
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    let current = true;
    fetch('/api/v1/auth/config')
      .then((response) => (response.ok ? (response.json() as Promise<AuthConfig>) : null))
      .catch(() => null)
      .then((read) => {
        if (!current) return;
        setConfig(read);
        setFailed(read === null);
      });
    return () => {
      current = false;
    };
  }, [attempt]);

  const reload = useCallback(() => {
    setFailed(false);
    setAttempt((n) => n + 1);
  }, []);

  return { config, failed, reload };
}
