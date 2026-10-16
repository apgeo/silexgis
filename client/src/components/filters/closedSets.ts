// SPDX-License-Identifier: AGPL-3.0-or-later

/** The closed sets: their values are the application's own words, translated rather than fetched. */
export const CLOSED_SETS: Record<string, readonly string[]> = {
  featureKinds: ['generic', 'cave', 'caveEntrance', 'centerline'],
  featureCategories: ['surface', 'underground', 'area', 'structure'],
  visibilities: ['private', 'cavingGroup', 'authenticated', 'public'],
  activityStates: ['draft', 'proposed', 'planned', 'confirmed', 'done', 'published', 'cancelled', 'delayed'],
};
