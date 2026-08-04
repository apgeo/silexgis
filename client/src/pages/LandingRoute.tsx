// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ReactNode } from 'react';
import { Navigate } from 'react-router-dom';
import { hasMapHash } from '../map/urlHash.ts';
import { hasScene3dHash } from '../scene3d/urlHash3d.ts';
import { useUiPrefsStore } from '../stores/uiPrefsStore.ts';

interface LandingRouteProps {
  map: ReactNode;
}

/**
 * What "/" shows. The map is the default and stays at "/" untouched for anyone who has not
 * opted in, so existing links keep working. Two guards matter:
 * - a position in the hash ("/#<zoom>/<lat>/<lon>" for the flat map, "/#3d/..." for the scene)
 *   means the URL *is* a place — a shared or bookmarked link must show it regardless of the
 *   landing preference, and bouncing it to the dashboard would throw the position away;
 * - the map keeps a dedicated "/map" route, so nav and "show on map" jumps can reach it
 *   without bouncing back here.
 */
export default function LandingRoute({ map }: LandingRouteProps) {
  const landingPage = useUiPrefsStore((s) => s.landingPage);

  if (landingPage === 'dashboard' && !hasMapHash() && !hasScene3dHash()) {
    return <Navigate to="/dashboard" replace />;
  }
  return <>{map}</>;
}
