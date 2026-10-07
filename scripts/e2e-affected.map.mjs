// SPDX-License-Identifier: AGPL-3.0-or-later

// Which browser specs answer for a change, by the area the change lands in.
//
// The browser suite takes twenty minutes; a change to one page is answered by a handful of specs.
// `node scripts/e2e.mjs --affected <base>` runs those — and the smoke spec, always — so a look at
// work in progress is minutes long. It is the browser suite's counterpart of gate-affected.map.mjs
// and borrows from it: a server path is read through that map's `areas`, so an API feature has ONE
// name whichever suite is asked about it, and this file only says which specs go with each name.
//
// Like that map it fails CLOSED. A path under the application's sources that nothing here claims,
// and an area whose list is empty, select the whole suite: a selection that quietly ran nothing
// would be read as a pass. The whole suite remains the verdict; this decides what is looked at first.
//
// Maintenance is enforced: e2e-affected.test.mjs fails when a spec on disk is named by no area,
// when an area names a spec that does not exist, and when the server map names an area this file
// has no line for. A new client directory needs no line to be safe — unclaimed, it selects
// everything — only to be quick.

/** The specs that run whatever the change: the application starts, signs in and draws its map. */
export const always = ['smoke.spec.ts'];

/** Area name → the specs that answer for it. An empty list means "nothing narrower than everything". */
export const specs = {
  about: [],
  access: ['permission-groups.spec.ts', 'cave-grant-link.spec.ts', 'expedition-sharing.spec.ts'],
  admin: ['settings.spec.ts', 'notification-health.spec.ts', 'published-trips-admin.spec.ts'],
  audit: ['panel.spec.ts'],
  cabinets: ['documents.spec.ts'],
  calendar: ['calendar.spec.ts', 'calendar-feed.spec.ts'],
  catalogue: ['speologie-catalogue.spec.ts'],
  cavers: ['expeditions.spec.ts', 'settings.spec.ts'],
  caves: [
    'cave-attachments.spec.ts', 'cave-grant-link.spec.ts', 'cave-levels.spec.ts', 'cave-morphometry.spec.ts',
    'cave-statistics.spec.ts', 'cave-topology.spec.ts', 'cave-clustering.spec.ts',
    'overburden-profile.spec.ts', 'survey-sources.spec.ts', 'qr-landing.spec.ts',
    'feature-restore.spec.ts',
  ],
  cavingGroups: ['announcements.spec.ts', 'events.spec.ts', 'plan-trip.spec.ts'],
  checklists: ['checklists.spec.ts'],
  crs: [],
  dashboard: ['my-trips.spec.ts'],
  diagnostics: ['consoleGuard.spec.ts', 'errorReporting.spec.ts'],
  documents: ['documents.spec.ts', 'annotated-text.spec.ts', 'reslinks.spec.ts'],
  events: ['events.spec.ts', 'calendar.spec.ts'],
  expeditions: ['expeditions.spec.ts', 'expedition-sharing.spec.ts', 'calendar.spec.ts'],
  exports: ['karstlink-export.spec.ts', 'registry-statistics.spec.ts', 'trip-list.spec.ts'],
  featureSets: ['doline-morphometry.spec.ts', 'vector-import.spec.ts'],
  featureShares: [],
  files: ['uploads.spec.ts', 'documents.spec.ts', 'gallery.spec.ts', 'cave-attachments.spec.ts'],
  filters: ['trip-list.spec.ts', 'trip-map.spec.ts', 'registry-statistics.spec.ts'],
  geoFeatures: [
    'doline-morphometry.spec.ts', 'vector-import.spec.ts', 'photo-import.spec.ts', 'feature-restore.spec.ts',
  ],
  geofiles: ['vector-import.spec.ts'],
  georeferencedMaps: [
    'rastermap.spec.ts', 'rastermap-authoring.spec.ts', 'rastermap-public.spec.ts',
    'rastermap-tracking.spec.ts', 'rastermap-mobile.spec.ts',
  ],
  history: ['panel.spec.ts'],
  imports: ['vector-import.spec.ts', 'photo-import.spec.ts', 'trip-import.spec.ts'],
  jobs: ['survey-extraction.spec.ts', 'documents.spec.ts'],
  map: [
    'cave-clustering.spec.ts', 'karst-density.spec.ts', 'karst-autocorrelation.spec.ts',
    'trip-map.spec.ts', 'panel.spec.ts', 'mobile.spec.ts', 'mobile-ios.spec.ts',
  ],
  mapViews: [],
  me: ['settings.spec.ts', 'my-trips.spec.ts'],
  messaging: ['announcements.spec.ts', 'notifications.spec.ts', 'notification-health.spec.ts'],
  metadata: ['documents.spec.ts', 'gallery.spec.ts'],
  notifications: ['notifications.spec.ts', 'notification-health.spec.ts', 'announcements.spec.ts'],
  photoLibraries: [
    'photo-library-smoke.spec.ts', 'photo-library-browse.spec.ts',
    'photo-library-health.spec.ts', 'photo-library-pictures.spec.ts',
  ],
  photos: ['gallery.spec.ts', 'photo-import.spec.ts', 'cave-attachments.spec.ts'],
  qr: ['qr-landing.spec.ts'],
  resLinks: ['reslinks.spec.ts', 'annotated-text.spec.ts', 'cave-grant-link.spec.ts'],
  scene: ['scene3d.spec.ts', 'scene3d-walls.spec.ts', 'scene3d-mobile.spec.ts'],
  search: ['documents.spec.ts'],
  settings: ['settings.spec.ts'],
  sms: ['notifications.spec.ts', 'notification-health.spec.ts'],
  statistics: [
    'registry-statistics.spec.ts', 'cave-statistics.spec.ts', 'cave-morphometry.spec.ts',
    'cave-topology.spec.ts', 'cave-levels.spec.ts', 'doline-morphometry.spec.ts',
    'karst-density.spec.ts', 'karst-autocorrelation.spec.ts', 'trip-stats.spec.ts',
  ],
  surveys: [
    'survey-compare.spec.ts', 'survey-extraction.spec.ts', 'survey-quality.spec.ts',
    'survey-sources.spec.ts', 'cave-statistics.spec.ts', 'cave-topology.spec.ts',
    'cave-morphometry.spec.ts', 'cave-levels.spec.ts', 'scene3d-walls.spec.ts',
  ],
  sync: ['settings.spec.ts'],
  tags: ['trips.spec.ts'],
  taxonomies: [],
  terrain: [
    'terrain.spec.ts', 'terrain-derivatives.spec.ts', 'overburden-profile.spec.ts',
    'scene3d.spec.ts',
  ],
  trips: [
    'trips.spec.ts', 'trip-list.spec.ts', 'trip-stats.spec.ts', 'trip-map.spec.ts',
    'trip-import.spec.ts', 'trip-restore.spec.ts', 'trip-report-map.spec.ts',
    'trip-tracking-writes.spec.ts', 'trip-tracking-writes-mobile.spec.ts', 'my-trips.spec.ts',
    'plan-trip.spec.ts', 'published-trips-admin.spec.ts', 'public-trip-offline.spec.ts',
    'public-trip-past.spec.ts', 'public-trip-mobile.spec.ts', 'public-trip-past-mobile.spec.ts',
    'rastermap-tracking.spec.ts', 'tracking-movie.spec.ts', 'tracking-withheld.spec.ts',
    'checklists.spec.ts',
  ],
  uploads: ['uploads.spec.ts'],
  users: ['settings.spec.ts', 'permission-groups.spec.ts'],
  workAreas: ['karst-density.spec.ts'],
};

/** Client path prefix → the area, or areas, it belongs to. Longest prefix wins. */
export const clientAreas = {
  'client/src/caveview/': ['surveys', 'trips'],
  'client/src/diagnostics/': 'diagnostics',
  'client/src/filters/': 'filters',
  'client/src/imagelink/': 'resLinks',
  'client/src/map/': 'map',
  'client/src/notifications/': 'notifications',
  'client/src/pdf/': 'documents',
  'client/src/photolibrary/': 'photoLibraries',
  'client/src/publictrips/': 'trips',
  'client/src/rastermap/': 'georeferencedMaps',
  'client/src/scene3d/': ['scene', 'terrain'],
  'client/src/textlink/': 'resLinks',
  'client/src/viewlinks/': 'resLinks',
  'client/src/workareas/': 'workAreas',
  'client/src/components/attachments/': 'files',
  'client/src/components/catalogue/': 'catalogue',
  'client/src/components/cavers/': 'cavers',
  'client/src/components/caves/': 'caves',
  'client/src/components/caveview/': ['surveys', 'trips'],
  'client/src/components/documents/': 'documents',
  'client/src/components/events/': 'events',
  'client/src/components/expeditions/': 'expeditions',
  'client/src/components/features/': 'geoFeatures',
  'client/src/components/gallery/': 'photos',
  'client/src/components/history/': 'history',
  'client/src/components/import/': 'imports',
  'client/src/components/invitations/': ['trips', 'events'],
  'client/src/components/map/': 'map',
  'client/src/components/permissions/': 'access',
  'client/src/components/photolibrary/': 'photoLibraries',
  'client/src/components/qr/': 'qr',
  'client/src/components/reslinks/': 'resLinks',
  'client/src/components/scene3d/': ['scene', 'terrain'],
  'client/src/components/settings/': 'settings',
  'client/src/components/shares/': 'access',
  'client/src/components/statistics/': 'statistics',
  'client/src/components/tags/': 'tags',
  'client/src/components/trips/': 'trips',
  'client/src/components/uploads/': 'uploads',
  'client/src/pages/admin/': 'admin',
  'client/src/pages/calendar/': 'calendar',
  'client/src/pages/catalogue/': 'catalogue',
  'client/src/pages/cavers/': 'cavers',
  'client/src/pages/caves/': 'caves',
  'client/src/pages/cavingGroups/': 'cavingGroups',
  'client/src/pages/checklists/': 'checklists',
  'client/src/pages/dashboard/': 'dashboard',
  'client/src/pages/documents/': 'documents',
  'client/src/pages/events/': 'events',
  'client/src/pages/expeditions/': 'expeditions',
  'client/src/pages/features/': 'geoFeatures',
  'client/src/pages/gallery/': 'photos',
  'client/src/pages/geodata/': ['geofiles', 'georeferencedMaps', 'terrain'],
  'client/src/pages/links/': 'resLinks',
  'client/src/pages/notifications/': 'notifications',
  'client/src/pages/panel/': 'history',
  'client/src/pages/photolibrary/': 'photoLibraries',
  'client/src/pages/public/': 'trips',
  'client/src/pages/settings/': 'settings',
  'client/src/pages/statistics/': 'statistics',
  'client/src/pages/trips/': 'trips',
  'client/src/pages/workareas/': 'workAreas',
  'client/src/pages/Scene3DPage': ['scene', 'terrain'],
  'client/src/pages/QrLandingPage': 'qr',
  'client/src/pages/MapPage': 'map',
};

/**
 * Client paths that every page leans on: the generated API client, sign-in, the shell, shared
 * state, the translations, the theme, the build itself. A change here is the whole suite's.
 * Anything under `client/` that neither this list nor `clientAreas` claims is treated the same
 * way; the list exists so that the common cases say why.
 */
export const everything = [
  'client/src/api/', 'client/src/auth/', 'client/src/hooks/', 'client/src/stores/',
  'client/src/i18n/', 'client/src/geo/', 'client/src/workspace/', 'client/src/components/',
  'client/src/pages/', 'client/src/App', 'client/src/main.tsx', 'client/src/theme',
  'client/src/index.css', 'client/package.json', 'client/package-lock.json', 'client/vite.config',
  'client/playwright.config', 'client/index.html', 'client/public/', 'client/vendor/',
];

/** Paths whose change no browser spec answers for. */
export const outside = [
  '.github/', 'deploy/', 'docs/', 'scripts/', 'server/tests/', 'README.md', 'NOTICE', 'LICENSE',
];
