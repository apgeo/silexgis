import React from 'react';

import {
  Alert
} from 'antd';

import ConfigProvider from 'antd/lib/config-provider';
import deDE from 'antd/lib/locale/de_DE';
import enGB from 'antd/lib/locale/en_GB';

import {
  render
} from 'react-dom';
import {
  Provider
} from 'react-redux';

import App from './App';
import i18n from './i18n';
import {
  store
} from './store/store';

import './index.less';

import {EventEmitter} from "events";

const getConfigLang = (lang: string) => {
  switch (lang) {
    case 'en':
      return enGB;
    case 'de':
      return deDE;
    default:
      return enGB;
  }
};

const compComBus = new EventEmitter();

const renderApp = async () => {
  try {
    // const map = await setupMap();

    render(
      <React.StrictMode>
        <React.Suspense fallback={<span></span>}>
          <ConfigProvider locale={getConfigLang(i18n.language)}>
            <Provider store={store}>
              <App />
            </Provider>
          </ConfigProvider>
        </React.Suspense>
      </React.StrictMode>,
      document.getElementById('app')
    );
  } catch (error) {
    console.error(error);

    render(
      <React.StrictMode>
        <Alert
          className="error-boundary"
          message="Error while loading the application"
          description="An unexpected error occured while loading the application. Please try to reload the page."
          type="error"
          showIcon
        />
      </React.StrictMode>,
      document.getElementById('app')
    );
  }
};

renderApp();
