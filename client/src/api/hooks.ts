// SPDX-License-Identifier: AGPL-3.0-or-later
// The application's API hooks and the type names it reads the API through, one module per
// area under ./hooks/. Import from here; the per-area modules are where a hook is written. A new hook
// goes in the module of its area (or a new module, added to this list), never in this file.
// ./hooks/shared.ts holds what the areas share; of it, only the history refresh is re-exported.
export * from './hooks/types.ts';
export * from './hooks/queryKeys.ts';
export * from './hooks/me.ts';
export * from './hooks/notifications.ts';
export * from './hooks/access.ts';
export * from './hooks/admin.ts';
export * from './hooks/taxonomies.ts';
export * from './hooks/map.ts';
export * from './hooks/filters.ts';
export * from './hooks/savedFilters.ts';
export * from './hooks/mapViews.ts';
export * from './hooks/sharedViews.ts';
export * from './hooks/caves.ts';
export * from './hooks/surveys.ts';
export * from './hooks/features.ts';
export * from './hooks/geodata.ts';
export * from './hooks/documents.ts';
export * from './hooks/photos.ts';
export * from './hooks/photoLibraries.ts';
export * from './hooks/people.ts';
export * from './hooks/trips.ts';
export * from './hooks/calendar.ts';
export * from './hooks/events.ts';
export * from './hooks/expeditions.ts';
export * from './hooks/tracking.ts';
export * from './hooks/imports.ts';
export * from './hooks/karst.ts';
export * from './hooks/statistics.ts';
export * from './hooks/terrain.ts';
export * from './hooks/resLinks.ts';
export * from './hooks/sync.ts';
export * from './hooks/accessKeys.ts';
export * from './hooks/surveyUploads.ts';
export * from './hooks/surveyUploadPublicLinks.ts';
export { useInvalidateHistory } from './hooks/shared.ts';
