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
import { useUiPrefsStore } from './stores/uiPrefsStore.ts';
import { buildThemeConfig, resolveDark } from './theme.ts';

/** antd locale follows the i18next language (component-internal strings, pickers, …). */
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
        <QueryProvider>
          <App />
        </QueryProvider>
      </AntApp>
    </ConfigProvider>
  );
}

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <Root />
  </StrictMode>,
);
