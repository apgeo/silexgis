// SPDX-License-Identifier: AGPL-3.0-or-later
import '@ant-design/v5-patch-for-react-19';
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { App as AntApp, ConfigProvider } from 'antd';
import 'ol/ol.css';
import './i18n';
import './index.css';
import App from './App.tsx';
import { themeConfig } from './theme.ts';

const queryClient = new QueryClient();

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <ConfigProvider theme={themeConfig}>
        {/* antd App provides the context consumed by App.useApp() (message/modal/notification). */}
        <AntApp style={{ height: '100%' }}>
          <App />
        </AntApp>
      </ConfigProvider>
    </QueryClientProvider>
  </StrictMode>,
);
