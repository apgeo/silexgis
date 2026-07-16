// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ReactNode } from 'react';
import { Navigate } from 'react-router-dom';
import { hasMapHash } from '../map/urlHash.ts';
import { useUiPrefsStore } from '../stores/uiPrefsStore.ts';

interface LandingRouteProps {
  map: ReactNode;
}

/**
 * What "/" shows. The map is the default and stays at "/" untouched for anyone who has not
 * opted in, so existing links keep working. Two guards matter:
 * - a map hash ("/#map=z/lon/lat") means the URL *is* a map position — a shared or bookmarked
 *   map link must show the map regardless of the landing preference;
 * - the map keeps a dedicated "/map" route, so nav and "show on map" jumps can reach it
 *   without bouncing back here.
 */
export default function LandingRoute({ map }: LandingRouteProps) {
  const landingPage = useUiPrefsStore((s) => s.landingPage);

  if (landingPage === 'dashboard' && !hasMapHash()) {
    return <Navigate to="/dashboard" replace />;
  }
  return <>{map}</>;
}
