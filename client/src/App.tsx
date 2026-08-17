// SPDX-License-Identifier: AGPL-3.0-or-later
import { lazy, Suspense, type ReactNode } from 'react';
import { Spin } from 'antd';
import { createBrowserRouter, Navigate, RouterProvider, type RouteObject } from 'react-router-dom';
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
const Scene3DPage = lazy(() => import('./pages/Scene3DPage.tsx'));
const DashboardPage = lazy(() => import('./pages/dashboard/DashboardPage.tsx'));
const CaveListPage = lazy(() => import('./pages/caves/CaveListPage.tsx'));
const CaveFormPage = lazy(() => import('./pages/caves/CaveFormPage.tsx'));
const CaveDetailPage = lazy(() => import('./pages/caves/CaveDetailPage.tsx'));
const FeatureListPage = lazy(() => import('./pages/features/FeatureListPage.tsx'));
const FeatureDetailPage = lazy(() => import('./pages/features/FeatureDetailPage.tsx'));
const SharedFeaturePage = lazy(() => import('./pages/SharedFeaturePage.tsx'));
const GeodataPage = lazy(() => import('./pages/geodata/GeodataPage.tsx'));
const ImportWorkspacePage = lazy(() => import('./pages/geodata/ImportWorkspacePage.tsx'));
const PhotoImportWorkspacePage = lazy(() => import('./pages/geodata/PhotoImportWorkspacePage.tsx'));
const TermRulesPage = lazy(() => import('./pages/admin/TermRulesPage.tsx'));
const TripLogListPage = lazy(() => import('./pages/trips/TripLogListPage.tsx'));
const TripLogDetailPage = lazy(() => import('./pages/trips/TripLogDetailPage.tsx'));
const TripReportPage = lazy(() => import('./pages/trips/TripReportPage.tsx'));
const ExpeditionListPage = lazy(() => import('./pages/expeditions/ExpeditionListPage.tsx'));
const ExpeditionDetailPage = lazy(() => import('./pages/expeditions/ExpeditionDetailPage.tsx'));
const AuditPage = lazy(() => import('./pages/admin/AuditPage.tsx'));
const MessagingSettingsPage = lazy(() => import('./pages/admin/MessagingSettingsPage.tsx'));
const MessageTemplatesPage = lazy(() => import('./pages/admin/MessageTemplatesPage.tsx'));
const PermissionGroupsPage = lazy(() => import('./pages/admin/permissionGroups/PermissionGroupsPage.tsx'));
const FeatureSetsPage = lazy(() => import('./pages/admin/FeatureSetsPage.tsx'));
const DocumentTypesPage = lazy(() => import('./pages/admin/DocumentTypesPage.tsx'));
const TripTypesPage = lazy(() => import('./pages/admin/TripTypesPage.tsx'));
const TripParticipantRolesPage = lazy(() => import('./pages/admin/TripParticipantRolesPage.tsx'));
const TripReportTemplatesPage = lazy(() => import('./pages/admin/TripReportTemplatesPage.tsx'));
const RelationTypesPage = lazy(() => import('./pages/admin/RelationTypesPage.tsx'));
const CabinetsPage = lazy(() => import('./pages/documents/CabinetsPage.tsx'));
const DocumentDetailPage = lazy(() => import('./pages/documents/DocumentDetailPage.tsx'));
const UploadsPage = lazy(() => import('./pages/documents/UploadsPage.tsx'));
const GalleryPage = lazy(() => import('./pages/gallery/GalleryPage.tsx'));
const AlbumsPage = lazy(() => import('./pages/gallery/AlbumsPage.tsx'));
const AlbumDetailPage = lazy(() => import('./pages/gallery/AlbumDetailPage.tsx'));
const PublicGalleryPage = lazy(() => import('./pages/gallery/PublicGalleryPage.tsx'));
const SharedAlbumPage = lazy(() =>
  import('./pages/gallery/PublicGalleryPage.tsx').then((m) => ({ default: m.SharedAlbumPage })));
const LinkPage = lazy(() => import('./pages/links/LinkPage.tsx'));
const CavingGroupsPage = lazy(() => import('./pages/cavingGroups/CavingGroupsPage.tsx'));
const CaversPage = lazy(() => import('./pages/cavers/CaversPage.tsx'));
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

/**
 * Every address this application answers.
 *
 * Exported so a test can ask the reverse question: whether an address the application *hands out*
 * — a chip's route, the link in a notification about a grant — is one this table matches. A route
 * named somewhere and missing here does not fail to navigate; it lands the reader on the router's
 * own error screen, which is worse than a chip that does not move.
 */
export const routes: RouteObject[] = [
  { path: '/login', element: <LoginPage /> },
  { path: '/auth/callback', element: <CallbackPage /> },
  { path: '/confirm-email', element: <ConfirmEmailPage /> },
  { path: '/reset-password', element: <ResetPasswordPage /> },
  // The opt-out link in a notification; anonymous, because it is opened from a mail client.
  { path: '/unsubscribe', element: <UnsubscribePage /> },
  { path: '/shared/view/:token', element: <Loadable><SharedViewPage /></Loadable> },
  { path: '/shared/features/:token', element: <Loadable><SharedFeaturePage /></Loadable> },
  // The two surfaces a visitor reaches without an account: one album by its link, and the
  // installation's curated gallery. Both show renderings and nothing else.
  { path: '/shared/albums/:token', element: <Loadable><SharedAlbumPage /></Loadable> },
  { path: '/gallery/public', element: <Loadable><PublicGalleryPage /></Loadable> },
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
          { path: '/map3d', element: <Loadable><Scene3DPage /></Loadable> },
          { path: '/dashboard', element: <Loadable><DashboardPage /></Loadable> },
          { path: '/caves', element: <Loadable><CaveListPage /></Loadable> },
          { path: '/caves/new', element: <Loadable><CaveFormPage /></Loadable> },
          { path: '/caves/:id', element: <Loadable><CaveDetailPage /></Loadable> },
          { path: '/caves/:id/edit', element: <Loadable><CaveFormPage /></Loadable> },
          { path: '/features', element: <Loadable><FeatureListPage /></Loadable> },
          { path: '/features/:id', element: <Loadable><FeatureDetailPage /></Loadable> },
          { path: '/geodata', element: <Loadable><GeodataPage /></Loadable> },
          { path: '/geodata/:geofileId/import', element: <Loadable><ImportWorkspacePage /></Loadable> },
          { path: '/geodata/photo-import', element: <Loadable><PhotoImportWorkspacePage /></Loadable> },
          { path: '/admin/term-rules', element: <Loadable><TermRulesPage /></Loadable> },
          { path: '/trip-logs', element: <Loadable><TripLogListPage /></Loadable> },
          { path: '/trip-logs/:id', element: <Loadable><TripLogDetailPage /></Loadable> },
          { path: '/trip-logs/:id/report', element: <Loadable><TripReportPage /></Loadable> },
          { path: '/expeditions', element: <Loadable><ExpeditionListPage /></Loadable> },
          { path: '/expeditions/:id', element: <Loadable><ExpeditionDetailPage /></Loadable> },
          { path: '/admin/audit', element: <Loadable><AuditPage /></Loadable> },
          { path: '/admin/messaging', element: <Loadable><MessagingSettingsPage /></Loadable> },
          { path: '/admin/message-templates', element: <Loadable><MessageTemplatesPage /></Loadable> },
          { path: '/admin/permission-groups', element: <Loadable><PermissionGroupsPage /></Loadable> },
          { path: '/admin/feature-sets', element: <Loadable><FeatureSetsPage /></Loadable> },
          { path: '/admin/document-types', element: <Loadable><DocumentTypesPage /></Loadable> },
          { path: '/admin/trip-types', element: <Loadable><TripTypesPage /></Loadable> },
          { path: '/admin/participant-roles', element: <Loadable><TripParticipantRolesPage /></Loadable> },
          { path: '/admin/report-templates', element: <Loadable><TripReportTemplatesPage /></Loadable> },
          { path: '/admin/relation-types', element: <Loadable><RelationTypesPage /></Loadable> },
          { path: '/cabinets', element: <Loadable><CabinetsPage /></Loadable> },
          { path: '/uploads', element: <Loadable><UploadsPage /></Loadable> },
          { path: '/gallery', element: <Loadable><GalleryPage /></Loadable> },
          { path: '/albums', element: <Loadable><AlbumsPage /></Loadable> },
          { path: '/albums/:id', element: <Loadable><AlbumDetailPage /></Loadable> },
          { path: '/documents/:id', element: <Loadable><DocumentDetailPage /></Loadable> },
          // A link's own page, reached by the short code someone pasted into a chat.
          { path: '/links/:code', element: <Loadable><LinkPage /></Loadable> },
          { path: '/caving-groups', element: <Loadable><CavingGroupsPage /></Loadable> },
          { path: '/cavers', element: <Loadable><CaversPage /></Loadable> },
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
];

const router = createBrowserRouter(routes);

export default function App() {
  return (
    <AuthProvider>
      <RouterProvider router={router} />
    </AuthProvider>
  );
}
