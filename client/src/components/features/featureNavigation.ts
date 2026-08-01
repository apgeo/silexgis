// SPDX-License-Identifier: AGPL-3.0-or-later
import { fetchFeature, type FeatureKind } from '../../api/hooks.ts';

/**
 * Detail path for a feature id, by kind. Caves and generic features have pages of their
 * own; entrances and centerlines live on their parent cave's page, which costs one
 * envelope fetch to resolve. Used by search results and the dashboard activity feed —
 * places that only know {kind, id} and must not center the map (results carry no
 * coordinates any more; location stays behind the entity's own protection rules).
 */
export async function featureDetailPath(kind: FeatureKind, id: string): Promise<string> {
  switch (kind) {
    case 'cave':
      return `/caves/${id}`;
    case 'caveEntrance':
    case 'centerline': {
      const envelope = await fetchFeature(id);
      const caveId = envelope.entrance?.caveFeatureId ?? envelope.centerline?.caveFeatureId;
      return caveId ? `/caves/${caveId}` : `/features/${id}`;
    }
    default:
      return `/features/${id}`;
  }
}
