// SPDX-License-Identifier: AGPL-3.0-or-later
import { StrictMode, useEffect, useState } from 'react';
import { createRoot } from 'react-dom/client';
import { App as AntApp, ConfigProvider } from 'antd';
import enUS from 'antd/locale/en_US';
import roRO from 'antd/locale/ro_RO';
import { useTranslation } from 'react-i18next';
import 'ol/ol.css';
import './i18n';
import './index.css';
import App from './App.tsx';
import { QueryProvider } from './api/QueryProvider.tsx';
import ErrorBoundary from './components/ErrorBoundary.tsx';
import { installErrorReporting } from './diagnostics/reporter.ts';
import { useUiPrefsStore } from './stores/uiPrefsStore.ts';
import { buildThemeConfig, resolveDark } from './theme.ts';

// Before anything renders, so that a failure while the application is starting up is reported
// rather than being the one class of failure this never sees. Development only: what it reports to
// is a route the development server answers, and nothing serves that in a built application.
if (import.meta.env.DEV) {
  installErrorReporting();
}

/**
 * antd locale follows the i18next language: component-internal strings, and a date picker's
 * chrome — its placeholders, its "Today" button. A picker's *calendar* comes from elsewhere:
 * the weekday initials, the month names and the day a week starts on are dayjs's, and the
 * locale registration that supplies them lives beside the i18next setup.
 */
function Root() {
  const { i18n } = useTranslation();
  const appearance = useUiPrefsStore((s) => s.appearance);
  const [, setSystemTick] = useState(0);

  // Following the system theme means reacting when the system changes it, not only at load.
  useEffect(() => {
    if (appearance.theme !== 'system' || typeof window.matchMedia !== 'function') {
      return;
    }

    const query = window.matchMedia('(prefers-color-scheme: dark)');
    const onChange = () => setSystemTick((tick) => tick + 1);
    query.addEventListener('change', onChange);
    return () => query.removeEventListener('change', onChange);
  }, [appearance.theme]);

  // Keeps the pre-mount script's decision in step with the live one, so the page background and
  // the reduced-motion rules follow a change made after load.
  useEffect(() => {
    const root = document.documentElement;
    root.dataset.theme = resolveDark(appearance.theme) ? 'dark' : 'light';
    root.dataset.reduceMotion = String(appearance.reduceMotion);
  }, [appearance]);

  return (
    <ConfigProvider
      theme={buildThemeConfig(appearance)}
      locale={i18n.resolvedLanguage === 'ro' ? roRO : enUS}
    >
      {/* antd App provides the context consumed by App.useApp() (message/modal/notification). */}
      <AntApp style={{ height: '100%' }}>
        {/* Inside the providers, so the screen it falls back to is themed and translated like the
            rest of the application. What it cannot catch is a failure in the component above it,
            which is only this file's theme and locale plumbing. */}
        <ErrorBoundary>
          <QueryProvider>
            <App />
          </QueryProvider>
        </ErrorBoundary>
      </AntApp>
    </ConfigProvider>
  );
}

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <Root />
  </StrictMode>,
);
