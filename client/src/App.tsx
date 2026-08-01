// SPDX-License-Identifier: AGPL-3.0-or-later
import { lazy, Suspense, type ReactNode } from 'react';
import { Spin } from 'antd';
import { createBrowserRouter, Navigate, RouterProvider } from 'react-router-dom';
import { AuthProvider } from './auth/auth.tsx';
import RequireAuth from './auth/RequireAuth.tsx';
import AppLayout from './components/AppLayout.tsx';
import CallbackPage from './pages/CallbackPage.tsx';
import LandingRoute from './pages/LandingRoute.tsx';
import ConfirmEmailPage from './pages/ConfirmEmailPage.tsx';
import LoginPage from './pages/LoginPage.tsx';
import ResetPasswordPage from './pages/ResetPasswordPage.tsx';
import UnsubscribePage from './pages/UnsubscribePage.tsx';
import LegacySecurityRedirect from './pages/settings/LegacySecurityRedirect.tsx';

// Route-level code-splitting: the map workspace (OpenLayers) and the cave pages load on
// demand, keeping the initial bundle to the shell + auth. Login/callback stay eager —
// they are tiny and always needed first.
const MapPage = lazy(() => import('./pages/MapPage.tsx'));
const DashboardPage = lazy(() => import('./pages/dashboard/DashboardPage.tsx'));
const CaveListPage = lazy(() => import('./pages/caves/CaveListPage.tsx'));
const CaveFormPage = lazy(() => import('./pages/caves/CaveFormPage.tsx'));
const CaveDetailPage = lazy(() => import('./pages/caves/CaveDetailPage.tsx'));
const FeatureListPage = lazy(() => import('./pages/features/FeatureListPage.tsx'));
const FeatureDetailPage = lazy(() => import('./pages/features/FeatureDetailPage.tsx'));
const SharedFeaturePage = lazy(() => import('./pages/SharedFeaturePage.tsx'));
const GeodataPage = lazy(() => import('./pages/geodata/GeodataPage.tsx'));
const TripLogListPage = lazy(() => import('./pages/trips/TripLogListPage.tsx'));
const TripLogDetailPage = lazy(() => import('./pages/trips/TripLogDetailPage.tsx'));
const AuditPage = lazy(() => import('./pages/admin/AuditPage.tsx'));
const MessagingSettingsPage = lazy(() => import('./pages/admin/MessagingSettingsPage.tsx'));
const MessageTemplatesPage = lazy(() => import('./pages/admin/MessageTemplatesPage.tsx'));
const TeamsPage = lazy(() => import('./pages/teams/TeamsPage.tsx'));
const SharedViewPage = lazy(() => import('./pages/SharedViewPage.tsx'));
const PanelPage = lazy(() => import('./pages/panel/PanelPage.tsx'));
const SettingsLayout = lazy(() => import('./pages/settings/SettingsLayout.tsx'));
const ProfileSettingsPage = lazy(() => import('./pages/settings/ProfileSettingsPage.tsx'));
const AccountSettingsPage = lazy(() => import('./pages/settings/AccountSettingsPage.tsx'));
const EmailSettingsPage = lazy(() => import('./pages/settings/EmailSettingsPage.tsx'));
const NotificationSettingsPage = lazy(() => import('./pages/settings/NotificationSettingsPage.tsx'));
const SecuritySettingsPage = lazy(() => import('./pages/settings/SecuritySettingsPage.tsx'));
const AccessibilitySettingsPage = lazy(() => import('./pages/settings/AccessibilitySettingsPage.tsx'));

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
  { path: '/confirm-email', element: <ConfirmEmailPage /> },
  { path: '/reset-password', element: <ResetPasswordPage /> },
  // The opt-out link in a notification; anonymous, because it is opened from a mail client.
  { path: '/unsubscribe', element: <UnsubscribePage /> },
  { path: '/shared/view/:token', element: <Loadable><SharedViewPage /></Loadable> },
  { path: '/shared/features/:token', element: <Loadable><SharedFeaturePage /></Loadable> },
  {
    element: <RequireAuth />,
    children: [
      // Chrome-less pop-out panels join the workspace bus from their own windows.
      { path: '/panel/:panelId', element: <Loadable><PanelPage /></Loadable> },
      {
        element: <AppLayout />,
        children: [
          // "/" keeps rendering the map unless the user chose the dashboard as their landing
          // page; "/map" is the map's dedicated path, which never dispatches.
          { index: true, element: <LandingRoute map={<Loadable><MapPage /></Loadable>} /> },
          { path: '/map', element: <Loadable><MapPage /></Loadable> },
          { path: '/dashboard', element: <Loadable><DashboardPage /></Loadable> },
          { path: '/caves', element: <Loadable><CaveListPage /></Loadable> },
          { path: '/caves/new', element: <Loadable><CaveFormPage /></Loadable> },
          { path: '/caves/:id', element: <Loadable><CaveDetailPage /></Loadable> },
          { path: '/caves/:id/edit', element: <Loadable><CaveFormPage /></Loadable> },
          { path: '/features', element: <Loadable><FeatureListPage /></Loadable> },
          { path: '/features/:id', element: <Loadable><FeatureDetailPage /></Loadable> },
          { path: '/geodata', element: <Loadable><GeodataPage /></Loadable> },
          { path: '/trip-logs', element: <Loadable><TripLogListPage /></Loadable> },
          { path: '/trip-logs/:id', element: <Loadable><TripLogDetailPage /></Loadable> },
          { path: '/admin/audit', element: <Loadable><AuditPage /></Loadable> },
          { path: '/admin/messaging', element: <Loadable><MessagingSettingsPage /></Loadable> },
          { path: '/admin/message-templates', element: <Loadable><MessageTemplatesPage /></Loadable> },
          { path: '/teams', element: <Loadable><TeamsPage /></Loadable> },
          {
            path: '/settings',
            element: <Loadable><SettingsLayout /></Loadable>,
            children: [
              { index: true, element: <Navigate to="/settings/profile" replace /> },
              { path: 'profile', element: <Loadable><ProfileSettingsPage /></Loadable> },
              { path: 'account', element: <Loadable><AccountSettingsPage /></Loadable> },
              { path: 'emails', element: <Loadable><EmailSettingsPage /></Loadable> },
              { path: 'notifications', element: <Loadable><NotificationSettingsPage /></Loadable> },
              { path: 'security', element: <Loadable><SecuritySettingsPage /></Loadable> },
              { path: 'accessibility', element: <Loadable><AccessibilitySettingsPage /></Loadable> },
            ],
          },
          // The security page used to live here; links out in the wild still point at it.
          { path: '/account/security', element: <LegacySecurityRedirect /> },
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
