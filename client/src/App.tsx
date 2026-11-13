// SPDX-License-Identifier: AGPL-3.0-or-later
import { Suspense, type ReactNode } from 'react';
import { Spin } from 'antd';
import { createBrowserRouter, Navigate, RouterProvider, type RouteObject } from 'react-router-dom';
import { AuthProvider } from './auth/auth.tsx';
import RequireAuth from './auth/RequireAuth.tsx';
import AppLayout from './components/AppLayout.tsx';
import PanelShell from './pages/panel/PanelShell.tsx';
import RouteErrorPage from './components/RouteErrorPage.tsx';
import { lazyRetrying } from './codeLoading.ts';
import CallbackPage from './pages/CallbackPage.tsx';
import LandingRoute from './pages/LandingRoute.tsx';
import ConfirmEmailPage from './pages/ConfirmEmailPage.tsx';
import LoginPage from './pages/LoginPage.tsx';
import NotFoundPage from './pages/NotFoundPage.tsx';
import ResetPasswordPage from './pages/ResetPasswordPage.tsx';
import UnsubscribePage from './pages/UnsubscribePage.tsx';
import LegacySecurityRedirect from './pages/settings/LegacySecurityRedirect.tsx';

// Route-level code-splitting: the map workspace (OpenLayers) and the cave pages load on
// demand, keeping the initial bundle to the shell + auth. Login/callback stay eager —
// they are tiny and always needed first.
const MapPage = lazyRetrying(() => import('./pages/MapPage.tsx'));
const Scene3DPage = lazyRetrying(() => import('./pages/Scene3DPage.tsx'));
const DashboardPage = lazyRetrying(() => import('./pages/dashboard/DashboardPage.tsx'));
const WorkAreasPage = lazyRetrying(() => import('./pages/workareas/WorkAreasPage.tsx'));
const CaveListPage = lazyRetrying(() => import('./pages/caves/CaveListPage.tsx'));
const CaveFormPage = lazyRetrying(() => import('./pages/caves/CaveFormPage.tsx'));
const CaveDetailPage = lazyRetrying(() => import('./pages/caves/CaveDetailPage.tsx'));
const FeatureListPage = lazyRetrying(() => import('./pages/features/FeatureListPage.tsx'));
const DeletedFeaturesPage = lazyRetrying(() => import('./pages/features/DeletedFeaturesPage.tsx'));
const FeatureDetailPage = lazyRetrying(() => import('./pages/features/FeatureDetailPage.tsx'));
const SharedFeaturePage = lazyRetrying(() => import('./pages/SharedFeaturePage.tsx'));
const GeodataPage = lazyRetrying(() => import('./pages/geodata/GeodataPage.tsx'));
const ImportWorkspacePage = lazyRetrying(() => import('./pages/geodata/ImportWorkspacePage.tsx'));
const PhotoImportWorkspacePage = lazyRetrying(() => import('./pages/geodata/PhotoImportWorkspacePage.tsx'));
const SpeologieSearchPage = lazyRetrying(() => import('./pages/catalogue/SpeologieSearchPage.tsx'));
const SpeologieImportPage = lazyRetrying(() => import('./pages/catalogue/SpeologieImportPage.tsx'));
const TripImportPage = lazyRetrying(() => import('./pages/trips/TripImportPage.tsx'));
const SpeleolocImportPage = lazyRetrying(() => import('./pages/trips/SpeleolocImportPage.tsx'));
const SpeleolocRecordingImportPage = lazyRetrying(
  () => import('./pages/trips/SpeleolocRecordingImportPage.tsx'),
);
const TermRulesPage = lazyRetrying(() => import('./pages/admin/TermRulesPage.tsx'));
const ReviewQueuePage = lazyRetrying(() => import('./pages/review/ReviewQueuePage.tsx'));
const ReviewBatchPage = lazyRetrying(() => import('./pages/review/ReviewBatchPage.tsx'));
const ChecklistsPage = lazyRetrying(() => import('./pages/checklists/ChecklistsPage.tsx'));
const SavedFiltersPage = lazyRetrying(() => import('./pages/filters/SavedFiltersPage.tsx'));
const CalendarPage = lazyRetrying(() => import('./pages/calendar/CalendarPage.tsx'));
const TripLogListPage = lazyRetrying(() => import('./pages/trips/TripLogListPage.tsx'));
const MyTripsPage = lazyRetrying(() => import('./pages/trips/MyTripsPage.tsx'));
const DeletedTripsPage = lazyRetrying(() => import('./pages/trips/DeletedTripsPage.tsx'));
const TripStatsPage = lazyRetrying(() => import('./pages/trips/TripStatsPage.tsx'));
const RegistryDistributionPage = lazyRetrying(
  () => import('./pages/statistics/RegistryDistributionPage.tsx'),
);
const RegistryCorrelationPage = lazyRetrying(
  () => import('./pages/statistics/RegistryCorrelationPage.tsx'),
);
const RegistryRegionsPage = lazyRetrying(() => import('./pages/statistics/RegistryRegionsPage.tsx'));
const RegistryCategoriesPage = lazyRetrying(() => import('./pages/statistics/RegistryCategoriesPage.tsx'));
const TripLogDetailPage = lazyRetrying(() => import('./pages/trips/TripLogDetailPage.tsx'));
const TripReportPage = lazyRetrying(() => import('./pages/trips/TripReportPage.tsx'));
const EventListPage = lazyRetrying(() => import('./pages/events/EventListPage.tsx'));
const EventDetailPage = lazyRetrying(() => import('./pages/events/EventDetailPage.tsx'));
const ExpeditionListPage = lazyRetrying(() => import('./pages/expeditions/ExpeditionListPage.tsx'));
const ExpeditionDetailPage = lazyRetrying(() => import('./pages/expeditions/ExpeditionDetailPage.tsx'));
const ExpeditionReportPage = lazyRetrying(() => import('./pages/expeditions/ExpeditionReportPage.tsx'));
const AuditPage = lazyRetrying(() => import('./pages/admin/AuditPage.tsx'));
const MessagingSettingsPage = lazyRetrying(() => import('./pages/admin/MessagingSettingsPage.tsx'));
const MessageTemplatesPage = lazyRetrying(() => import('./pages/admin/MessageTemplatesPage.tsx'));
const NotificationHealthPage = lazyRetrying(() => import('./pages/admin/NotificationHealthPage.tsx'));
const StoragePage = lazyRetrying(() => import('./pages/admin/StoragePage.tsx'));
const TestDataPage = lazyRetrying(() => import('./pages/admin/TestDataPage.tsx'));
const PublishedTripsPage = lazyRetrying(() => import('./pages/admin/PublishedTripsPage.tsx'));
const PermissionGroupsPage = lazyRetrying(() => import('./pages/admin/permissionGroups/PermissionGroupsPage.tsx'));
const UsersPage = lazyRetrying(() => import('./pages/admin/UsersPage.tsx'));
const FeatureSetsPage = lazyRetrying(() => import('./pages/admin/FeatureSetsPage.tsx'));
const DocumentTypesPage = lazyRetrying(() => import('./pages/admin/DocumentTypesPage.tsx'));
const FeatureTypesPage = lazyRetrying(() => import('./pages/admin/FeatureTypesPage.tsx'));
const TripTypesPage = lazyRetrying(() => import('./pages/admin/TripTypesPage.tsx'));
const TripParticipantRolesPage = lazyRetrying(() => import('./pages/admin/TripParticipantRolesPage.tsx'));
const ExpeditionRosterRolesPage = lazyRetrying(() => import('./pages/admin/ExpeditionRosterRolesPage.tsx'));
const ReportTemplatesPage = lazyRetrying(() => import('./pages/admin/ReportTemplatesPage.tsx'));
const RelationTypesPage = lazyRetrying(() => import('./pages/admin/RelationTypesPage.tsx'));
const TerrainPage = lazyRetrying(() => import('./pages/admin/TerrainPage.tsx'));
const CabinetsPage = lazyRetrying(() => import('./pages/documents/CabinetsPage.tsx'));
const DeletedDocumentsPage = lazyRetrying(() => import('./pages/documents/DeletedDocumentsPage.tsx'));
const DocumentDetailPage = lazyRetrying(() => import('./pages/documents/DocumentDetailPage.tsx'));
const UploadsPage = lazyRetrying(() => import('./pages/documents/UploadsPage.tsx'));
const GalleryPage = lazyRetrying(() => import('./pages/gallery/GalleryPage.tsx'));
const PhotoLibraryPage = lazyRetrying(() => import('./pages/photolibrary/PhotoLibraryPage.tsx'));
const AlbumsPage = lazyRetrying(() => import('./pages/gallery/AlbumsPage.tsx'));
const AlbumDetailPage = lazyRetrying(() => import('./pages/gallery/AlbumDetailPage.tsx'));
const PublicGalleryPage = lazyRetrying(() => import('./pages/gallery/PublicGalleryPage.tsx'));
const QrLandingPage = lazyRetrying(() => import('./pages/QrLandingPage.tsx'));
const SharedAlbumPage = lazyRetrying(() =>
  import('./pages/gallery/PublicGalleryPage.tsx').then((m) => ({ default: m.SharedAlbumPage })));
const LinkPage = lazyRetrying(() => import('./pages/links/LinkPage.tsx'));
const CavingGroupsPage = lazyRetrying(() => import('./pages/cavingGroups/CavingGroupsPage.tsx'));
const CaversPage = lazyRetrying(() => import('./pages/cavers/CaversPage.tsx'));
const PhoneUsersPage = lazyRetrying(() => import('./pages/cavers/PhoneUsersPage.tsx'));
const NotificationsPage = lazyRetrying(() => import('./pages/notifications/NotificationsPage.tsx'));
const SharedViewPage = lazyRetrying(() => import('./pages/SharedViewPage.tsx'));
const PublicTripPage = lazyRetrying(() => import('./pages/public/PublicTripPage.tsx'));
const PublicTripEmbedPage = lazyRetrying(() => import('./pages/public/PublicTripEmbedPage.tsx'));
const PanelPage = lazyRetrying(() => import('./pages/panel/PanelPage.tsx'));
const SettingsLayout = lazyRetrying(() => import('./pages/settings/SettingsLayout.tsx'));
const ProfileSettingsPage = lazyRetrying(() => import('./pages/settings/ProfileSettingsPage.tsx'));
const AccountSettingsPage = lazyRetrying(() => import('./pages/settings/AccountSettingsPage.tsx'));
const EmailSettingsPage = lazyRetrying(() => import('./pages/settings/EmailSettingsPage.tsx'));
const NotificationSettingsPage = lazyRetrying(() => import('./pages/settings/NotificationSettingsPage.tsx'));
const SecuritySettingsPage = lazyRetrying(() => import('./pages/settings/SecuritySettingsPage.tsx'));
const AccessibilitySettingsPage = lazyRetrying(() => import('./pages/settings/AccessibilitySettingsPage.tsx'));
const SavedFilterSettingsPage = lazyRetrying(() => import('./pages/settings/SavedFilterSettingsPage.tsx'));
const CoordinateSettingsPage = lazyRetrying(() => import('./pages/settings/CoordinateSettingsPage.tsx'));
const CoordinateConverterPage = lazyRetrying(() => import('./pages/tools/CoordinateConverterPage.tsx'));
const SyncSettingsPage = lazyRetrying(() => import('./pages/settings/SyncSettingsPage.tsx'));
const PublicSurveyResultPage = lazyRetrying(() => import('./pages/public/PublicSurveyResultPage.tsx'));
const SurveyUploadStatusPage = lazyRetrying(() => import('./pages/surveyUploads/SurveyUploadStatusPage.tsx'));
const SurveyUploadViewerPage = lazyRetrying(() => import('./pages/surveyUploads/SurveyUploadViewerPage.tsx'));
const PublicSurveyViewerPage = lazyRetrying(() => import('./pages/public/PublicSurveyViewerPage.tsx'));
const AccessKeysSettingsPage = lazyRetrying(() => import('./pages/settings/AccessKeysSettingsPage.tsx'));
const SurveyUploadSettingsPage = lazyRetrying(() => import('./pages/settings/SurveyUploadSettingsPage.tsx'));
const ScanProfileSettingsPage = lazyRetrying(() => import('./pages/settings/ScanProfileSettingsPage.tsx'));
const AdvancedSettingsPage = lazyRetrying(() => import('./pages/settings/AdvancedSettingsPage.tsx'));

function Loadable({ children }: { children: ReactNode }) {
  return (
    <Suspense fallback={<Spin style={{ display: 'block', marginTop: '20vh' }} />}>
      {children}
    </Suspense>
  );
}

/**
 * Gives every top-level route the application's own error screen, so a failure anywhere is never
 * met by the router's bare one, which offers no way back.
 */
function guarded(table: RouteObject[]): RouteObject[] {
  return table.map((route) => ({ errorElement: <RouteErrorPage />, ...route }));
}

/**
 * Every address this application answers, named so it can be checked from both directions.
 *
 * An address nobody answers lands on the not-found page inside the signed-in frame (the last
 * route of the frame's list), and a page that fails is caught around the frame's content, so
 * neither costs the navigation. That makes a missing route quieter, not harmless: whoever was
 * sent there is still stranded on a page that says nothing is here.
 *
 * So the sidebar is checked against this table, and this table against the addresses the
 * application *hands out* — a chip's route, the link in a notification about a grant — and
 * those checks must not count the catch-all as an answer.
 */
export const routes: RouteObject[] = guarded([
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
  // A party underground, followed by somebody who was handed the link and holds no account — a
  // family, a callout contact, a club's own website. The second address is the same trip as a
  // viewer with no chrome around it, for an iframe; it is the only address in this application
  // meant to be framed, and the web server's `frame-ancestors` rule is what says by whom.
  { path: '/shared/trips/:token', element: <Loadable><PublicTripPage /></Loadable> },
  { path: '/shared/trips/:token/embed', element: <Loadable><PublicTripEmbedPage /></Loadable> },
  // The address in a QR square bolted to a cave wall. Anonymous, because the person reading it
  // is standing in a field with a camera app and no account. A path route rather than a hash
  // one on purpose: the scanner that produces these addresses searches the path and query for
  // the last separator and never looks past a '#', so a code carried in a fragment would be
  // discarded before anything could resolve it.
  { path: '/q/:code', element: <Loadable><QrLandingPage /></Loadable> },
  // The result of one survey upload, opened from a link its owner handed out. Anonymous: the
  // answer behind it is a closed envelope (title, cave name, progress, files), and a wrong,
  // withdrawn or no-longer-permitted token is answered exactly like an invented one.
  { path: '/p/survey/:token', element: <Loadable><PublicSurveyResultPage /></Loadable> },
  // The same link's results in one place: the model in both viewers and the compiler's figures.
  // Decided by the same answers as the page above; it carries no map, as that page carries no
  // position of the cave's own.
  { path: '/p/survey/:token/view', element: <Loadable><PublicSurveyViewerPage /></Loadable> },
  {
    element: <RequireAuth />,
    children: [
      // Chrome-less pop-out panels join the workspace bus from their own windows, and keep the
      // account's preferences in step from there.
      {
        element: <PanelShell />,
        children: [{ path: '/panel/:panelId', element: <Loadable><PanelPage /></Loadable> }],
      },
      {
        element: <AppLayout />,
        children: [
          // "/" keeps rendering the map unless the user chose the dashboard as their landing
          // page; "/map" is the map's dedicated path, which never dispatches.
          { index: true, element: <LandingRoute map={<Loadable><MapPage /></Loadable>} /> },
          { path: '/map', element: <Loadable><MapPage /></Loadable> },
          { path: '/map3d', element: <Loadable><Scene3DPage /></Loadable> },
          { path: '/dashboard', element: <Loadable><DashboardPage /></Loadable> },
          { path: '/work-areas', element: <Loadable><WorkAreasPage /></Loadable> },
          { path: '/caves', element: <Loadable><CaveListPage /></Loadable> },
          { path: '/caves/new', element: <Loadable><CaveFormPage /></Loadable> },
          { path: '/caves/:id', element: <Loadable><CaveDetailPage /></Loadable> },
          { path: '/caves/:id/edit', element: <Loadable><CaveFormPage /></Loadable> },
          { path: '/caves/:caveId/surveys/:surveyId/uploads/:uploadId', element: <Loadable><SurveyUploadStatusPage /></Loadable> },
          { path: '/caves/:caveId/surveys/:surveyId/uploads/:uploadId/view', element: <Loadable><SurveyUploadViewerPage /></Loadable> },
          { path: '/features', element: <Loadable><FeatureListPage /></Loadable> },
          { path: '/features/deleted', element: <Loadable><DeletedFeaturesPage /></Loadable> },
          { path: '/features/:id', element: <Loadable><FeatureDetailPage /></Loadable> },
          { path: '/geodata', element: <Loadable><GeodataPage /></Loadable> },
          { path: '/geodata/:geofileId/import', element: <Loadable><ImportWorkspacePage /></Loadable> },
          { path: '/geodata/photo-import', element: <Loadable><PhotoImportWorkspacePage /></Loadable> },
          { path: '/catalogue/speologie', element: <Loadable><SpeologieSearchPage /></Loadable> },
          { path: '/catalogue/speologie/import', element: <Loadable><SpeologieImportPage /></Loadable> },
          { path: '/admin/term-rules', element: <Loadable><TermRulesPage /></Loadable> },
          { path: '/checklists', element: <Loadable><ChecklistsPage /></Loadable> },
          { path: '/saved-filters', element: <Loadable><SavedFiltersPage /></Loadable> },
          { path: '/calendar', element: <Loadable><CalendarPage /></Loadable> },
          { path: '/tools/coordinates', element: <Loadable><CoordinateConverterPage /></Loadable> },
          { path: '/trip-logs', element: <Loadable><TripLogListPage /></Loadable> },
          { path: '/trip-logs/mine', element: <Loadable><MyTripsPage /></Loadable> },
          { path: '/trip-logs/deleted', element: <Loadable><DeletedTripsPage /></Loadable> },
          { path: '/trip-logs/stats', element: <Loadable><TripStatsPage /></Loadable> },
          { path: '/statistics/distribution', element: <Loadable><RegistryDistributionPage /></Loadable> },
          { path: '/statistics/correlation', element: <Loadable><RegistryCorrelationPage /></Loadable> },
          { path: '/statistics/regions', element: <Loadable><RegistryRegionsPage /></Loadable> },
          { path: '/statistics/categories', element: <Loadable><RegistryCategoriesPage /></Loadable> },
          { path: '/trip-logs/import', element: <Loadable><TripImportPage /></Loadable> },
          { path: '/trip-logs/import/:fileId', element: <Loadable><TripImportPage /></Loadable> },
          { path: '/trip-logs/speleoloc-import', element: <Loadable><SpeleolocImportPage /></Loadable> },
          { path: '/trip-logs/speleoloc-import/:fileId', element: <Loadable><SpeleolocImportPage /></Loadable> },
          { path: '/trip-logs/speleoloc-import/recording/:recordingId', element: <Loadable><SpeleolocRecordingImportPage /></Loadable> },
          { path: '/trip-logs/:id', element: <Loadable><TripLogDetailPage /></Loadable> },
          { path: '/trip-logs/:id/report', element: <Loadable><TripReportPage /></Loadable> },
          { path: '/events', element: <Loadable><EventListPage /></Loadable> },
          { path: '/events/:id', element: <Loadable><EventDetailPage /></Loadable> },
          { path: '/expeditions', element: <Loadable><ExpeditionListPage /></Loadable> },
          { path: '/expeditions/:id', element: <Loadable><ExpeditionDetailPage /></Loadable> },
          { path: '/expeditions/:id/report', element: <Loadable><ExpeditionReportPage /></Loadable> },
          { path: '/review', element: <Loadable><ReviewQueuePage /></Loadable> },
          { path: '/review/batches/:id', element: <Loadable><ReviewBatchPage /></Loadable> },
          { path: '/admin/audit', element: <Loadable><AuditPage /></Loadable> },
          { path: '/admin/messaging', element: <Loadable><MessagingSettingsPage /></Loadable> },
          { path: '/admin/notification-health', element: <Loadable><NotificationHealthPage /></Loadable> },
          { path: '/admin/storage', element: <Loadable><StoragePage /></Loadable> },
          { path: '/admin/test-data', element: <Loadable><TestDataPage /></Loadable> },
          { path: '/admin/published-trips', element: <Loadable><PublishedTripsPage /></Loadable> },
          { path: '/admin/message-templates', element: <Loadable><MessageTemplatesPage /></Loadable> },
          { path: '/admin/permission-groups', element: <Loadable><PermissionGroupsPage /></Loadable> },
          { path: '/admin/users', element: <Loadable><UsersPage /></Loadable> },
          { path: '/admin/feature-sets', element: <Loadable><FeatureSetsPage /></Loadable> },
          { path: '/admin/document-types', element: <Loadable><DocumentTypesPage /></Loadable> },
          { path: '/admin/feature-types', element: <Loadable><FeatureTypesPage /></Loadable> },
          { path: '/admin/trip-types', element: <Loadable><TripTypesPage /></Loadable> },
          { path: '/admin/participant-roles', element: <Loadable><TripParticipantRolesPage /></Loadable> },
          { path: '/admin/camp-roster-roles', element: <Loadable><ExpeditionRosterRolesPage /></Loadable> },
          { path: '/admin/report-templates', element: <Loadable><ReportTemplatesPage /></Loadable> },
          { path: '/admin/relation-types', element: <Loadable><RelationTypesPage /></Loadable> },
          { path: '/admin/terrain', element: <Loadable><TerrainPage /></Loadable> },
          { path: '/cabinets', element: <Loadable><CabinetsPage /></Loadable> },
          { path: '/uploads', element: <Loadable><UploadsPage /></Loadable> },
          { path: '/gallery', element: <Loadable><GalleryPage /></Loadable> },
          { path: '/photo-library', element: <Loadable><PhotoLibraryPage /></Loadable> },
          { path: '/albums', element: <Loadable><AlbumsPage /></Loadable> },
          { path: '/albums/:id', element: <Loadable><AlbumDetailPage /></Loadable> },
          { path: '/documents/deleted', element: <Loadable><DeletedDocumentsPage /></Loadable> },
          { path: '/documents/:id', element: <Loadable><DocumentDetailPage /></Loadable> },
          // A link's own page, reached by the short code someone pasted into a chat.
          { path: '/links/:code', element: <Loadable><LinkPage /></Loadable> },
          { path: '/caving-groups', element: <Loadable><CavingGroupsPage /></Loadable> },
          { path: '/cavers', element: <Loadable><CaversPage /></Loadable> },
          { path: '/cavers/phone-users', element: <Loadable><PhoneUsersPage /></Loadable> },
          { path: '/notifications', element: <Loadable><NotificationsPage /></Loadable> },
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
              { path: 'filters', element: <Loadable><SavedFilterSettingsPage /></Loadable> },
              { path: 'coordinates', element: <Loadable><CoordinateSettingsPage /></Loadable> },
              { path: 'sync', element: <Loadable><SyncSettingsPage /></Loadable> },
              { path: 'access-keys', element: <Loadable><AccessKeysSettingsPage /></Loadable> },
              { path: 'survey-uploads', element: <Loadable><SurveyUploadSettingsPage /></Loadable> },
              { path: 'scan-profiles', element: <Loadable><ScanProfileSettingsPage /></Loadable> },
              { path: 'advanced', element: <Loadable><AdvancedSettingsPage /></Loadable> },
            ],
          },
          // The security page used to live here; links out in the wild still point at it.
          { path: '/account/security', element: <LegacySecurityRedirect /> },
          // Last, and the only route that matches anything: an address nobody answers.
          { path: '*', element: <NotFoundPage /> },
        ],
      },
    ],
  },
]);

const router = createBrowserRouter(routes);

export default function App() {
  return (
    <AuthProvider>
      <RouterProvider router={router} />
    </AuthProvider>
  );
}
