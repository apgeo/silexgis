// SPDX-License-Identifier: AGPL-3.0-or-later
import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { InMemoryWebStorage, UserManager, WebStorageStateStore, type User } from 'oidc-client-ts';

// Authorization Code + PKCE against the embedded OpenIddict server (same origin).
// Tokens live in memory only (never persisted to storage); refresh tokens drive renewal.
export const userManager = new UserManager({
  authority: window.location.origin,
  client_id: 'silexgis-spa',
  redirect_uri: `${window.location.origin}/auth/callback`,
  post_logout_redirect_uri: window.location.origin,
  response_type: 'code',
  scope: 'openid profile email roles offline_access',
  userStore: new WebStorageStateStore({ store: new InMemoryWebStorage() }),
  automaticSilentRenew: true,
});

interface AuthContextValue {
  user: User | null;
  loading: boolean;
  signIn: (returnTo?: string) => Promise<void>;
  signOut: () => Promise<void>;
  /**
   * Renews the tokens now instead of waiting for the automatic renewal.
   *
   * Needed after a credential change: the account name shown in the header comes from the token
   * rather than from the profile, and changing an address or a user name invalidates the session
   * the token endpoint relies on — so without an immediate renewal the header stays stale and
   * the session lapses some minutes later.
   */
  refreshSession: () => Promise<void>;
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    void userManager.getUser().then((current) => {
      setUser(current && !current.expired ? current : null);
      setLoading(false);
    });

    const onLoaded = (loaded: User) => setUser(loaded);
    const onUnloaded = () => setUser(null);
    userManager.events.addUserLoaded(onLoaded);
    userManager.events.addUserUnloaded(onUnloaded);
    return () => {
      userManager.events.removeUserLoaded(onLoaded);
      userManager.events.removeUserUnloaded(onUnloaded);
    };
  }, []);

  const value = useMemo<AuthContextValue>(
    () => ({
      user,
      loading,
      signIn: (returnTo) => userManager.signinRedirect({ state: returnTo ?? '/' }),
      signOut: () => userManager.signoutRedirect(),
      refreshSession: async () => {
        // Best effort: a failure here only means the header shows the previous name until the
        // next automatic renewal, which is not worth interrupting the user for.
        try {
          await userManager.signinSilent();
        } catch {
          // ignored
        }
      },
    }),
    [user, loading],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error('useAuth must be used inside <AuthProvider>');
  }
  return context;
}
