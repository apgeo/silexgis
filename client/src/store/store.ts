import {
  configureStore
} from '@reduxjs/toolkit';

import drawer from './drawer';

import featuresDrawer from './featuresDrawer';

export const store = configureStore({
  reducer: {
    featuresDrawer,
    drawer
  }
});

export type RootState = ReturnType<typeof store.getState>;
export type AppDispatch = typeof store.dispatch;
