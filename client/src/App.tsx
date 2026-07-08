// SPDX-License-Identifier: AGPL-3.0-or-later
import { lazy, Suspense, type ReactNode } from 'react';
import { Spin } from 'antd';
import { createBrowserRouter, RouterProvider } from 'react-router-dom';
import { AuthProvider } from './auth/auth.tsx';
import RequireAuth from './auth/RequireAuth.tsx';
import AppLayout from './components/AppLayout.tsx';
import CallbackPage from './pages/CallbackPage.tsx';
import LoginPage from './pages/LoginPage.tsx';

// Route-level code-splitting: the map workspace (OpenLayers) and the cave pages load on
// demand, keeping the initial bundle to the shell + auth. Login/callback stay eager —
// they are tiny and always needed first.
const MapPage = lazy(() => import('./pages/MapPage.tsx'));
const CaveListPage = lazy(() => import('./pages/caves/CaveListPage.tsx'));
const CaveFormPage = lazy(() => import('./pages/caves/CaveFormPage.tsx'));
const CaveDetailPage = lazy(() => import('./pages/caves/CaveDetailPage.tsx'));
const FeatureListPage = lazy(() => import('./pages/features/FeatureListPage.tsx'));
const GeodataPage = lazy(() => import('./pages/geodata/GeodataPage.tsx'));

function Loadable({ children }: { children: ReactNode }) {
  return (
    <Suspense fallback={<Spin style={{ display: 'block', marginTop: '20vh' }} />}>
      {children}
    </Suspense>
  );
}

const router = createBrowserRouter([
  { path: '/login', element: <LoginPage /> },
  { path: '/auth/callback', element: <CallbackPage /> },
  {
    element: <RequireAuth />,
    children: [
      {
        element: <AppLayout />,
        children: [
          { index: true, element: <Loadable><MapPage /></Loadable> },
          { path: '/caves', element: <Loadable><CaveListPage /></Loadable> },
          { path: '/caves/new', element: <Loadable><CaveFormPage /></Loadable> },
          { path: '/caves/:id', element: <Loadable><CaveDetailPage /></Loadable> },
          { path: '/caves/:id/edit', element: <Loadable><CaveFormPage /></Loadable> },
          { path: '/features', element: <Loadable><FeatureListPage /></Loadable> },
          { path: '/geodata', element: <Loadable><GeodataPage /></Loadable> },
        ],
      },
    ],
  },
]);

export default function App() {
  return (
    <AuthProvider>
      <RouterProvider router={router} />
    </AuthProvider>
  );
}
