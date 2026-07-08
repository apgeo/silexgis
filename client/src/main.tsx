// SPDX-License-Identifier: AGPL-3.0-or-later
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { App as AntApp, ConfigProvider } from 'antd';
import enUS from 'antd/locale/en_US';
import roRO from 'antd/locale/ro_RO';
import { useTranslation } from 'react-i18next';
import 'ol/ol.css';
import './i18n';
import './index.css';
import App from './App.tsx';
import { themeConfig } from './theme.ts';

const queryClient = new QueryClient();

/** antd locale follows the i18next language (component-internal strings, pickers, …). */
function Root() {
  const { i18n } = useTranslation();
  return (
    <ConfigProvider theme={themeConfig} locale={i18n.resolvedLanguage === 'ro' ? roRO : enUS}>
      {/* antd App provides the context consumed by App.useApp() (message/modal/notification). */}
      <AntApp style={{ height: '100%' }}>
        <App />
      </AntApp>
    </ConfigProvider>
  );
}

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <Root />
    </QueryClientProvider>
  </StrictMode>,
);
