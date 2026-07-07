// SPDX-License-Identifier: AGPL-3.0-or-later
import { createBrowserRouter, RouterProvider } from 'react-router-dom';
import { AuthProvider } from './auth/auth.tsx';
import RequireAuth from './auth/RequireAuth.tsx';
import AppLayout from './components/AppLayout.tsx';
import CallbackPage from './pages/CallbackPage.tsx';
import CaveDetailPage from './pages/caves/CaveDetailPage.tsx';
import CaveFormPage from './pages/caves/CaveFormPage.tsx';
import CaveListPage from './pages/caves/CaveListPage.tsx';
import LoginPage from './pages/LoginPage.tsx';
import MapPage from './pages/MapPage.tsx';

const router = createBrowserRouter([
  { path: '/login', element: <LoginPage /> },
  { path: '/auth/callback', element: <CallbackPage /> },
  {
    element: <RequireAuth />,
    children: [
      {
        element: <AppLayout />,
        children: [
          { index: true, element: <MapPage /> },
          { path: '/caves', element: <CaveListPage /> },
          { path: '/caves/new', element: <CaveFormPage /> },
          { path: '/caves/:id', element: <CaveDetailPage /> },
          { path: '/caves/:id/edit', element: <CaveFormPage /> },
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
