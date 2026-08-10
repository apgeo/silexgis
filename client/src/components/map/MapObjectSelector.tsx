// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { useTranslation } from 'react-i18next';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { fetchFeature } from '../../api/hooks.ts';
import type { SelectorScope } from '../../filters/selection.ts';
import type { FilterHit } from '../../filters/types.ts';
import { fitGeoJsonGeometry } from '../../map/mapContext.ts';
import { useWorkspaceStore } from '../../stores/workspaceStore.ts';
import ObjectSelector from '../selector/ObjectSelector.tsx';
import { featureDetailPath } from '../features/featureNavigation.ts';

/**
 * The filter selector, beside the map's search box.
 *
 * It overlaps the search box on purpose. Search answers "what mentions these words" — including
 * words inside documents — and hands back a page to read. This answers "which of these things do I
 * mean", narrowed by buttons rather than by guessing at wording, and for anything the map can draw
 * it takes you there rather than to a page about it. Two questions people genuinely ask
 * differently; collapsing them would make one of them worse.
 */

/**
 * What the selector offers here.
 *
 * A prefix is only spent on a scope people type constantly, because every symbol claimed is a
 * symbol that can no longer begin a cave name — and cave names do begin with punctuation. The rest
 * are reachable by their buttons, which is no slower for something you reach for occasionally.
 */
const MAP_SCOPES: SelectorScope[] = [
  {
    id: 'caves',
    labelKey: 'filters.scopes.caves',
    world: 'feature',
    where: { field: 'kind', value: 'cave' },
    prefix: '#',
  },
  {
    id: 'entrances',
    labelKey: 'filters.scopes.entrances',
    world: 'feature',
    where: { field: 'kind', value: 'caveEntrance' },
  },
  { id: 'features', labelKey: 'filters.scopes.features', world: 'feature', where: null },
  { id: 'documents', labelKey: 'filters.scopes.documents', world: 'document', where: null, prefix: '@' },
  { id: 'trips', labelKey: 'filters.scopes.trips', world: 'tripLog', where: null, prefix: '!' },
  { id: 'views', labelKey: 'filters.scopes.views', world: 'mapView', where: null },
];

interface MapObjectSelectorProps {
  /** Fill the strip rather than sit at its own width — the phone layout. */
  fullWidth?: boolean;
}

export default function MapObjectSelector({ fullWidth = false }: MapObjectSelectorProps) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { message } = App.useApp();
  const [, setSearchParams] = useSearchParams();
  const setSelection = useWorkspaceStore((s) => s.setSelection);

  /**
   * Takes the map to a feature, or opens the page of something the map cannot draw.
   *
   * The hit itself carries no position — nothing a selector returns ever does. So the geometry is
   * asked of the feature's own endpoint, which is the one place that decides how much of a
   * protected position this caller is shown. What lands on the map is therefore exactly what the
   * feature's own page would have drawn: snapped where it must be, withheld where it must be.
   */
  const onPick = async (hit: FilterHit | null) => {
    if (hit === null) {
      return;
    }

    switch (hit.world) {
      case 'feature': {
        try {
          const envelope = await fetchFeature(hit.id);
          if (envelope.feature.geometry) {
            setSelection({ kind: 'feature', featureId: hit.id });
            fitGeoJsonGeometry(envelope.feature.geometry);
            return;
          }

          // Nothing to fly to — either it was never placed, or this caller may not be shown
          // where it is. Its page is the honest destination, and it says which of the two.
          navigate(await featureDetailPath(envelope.kind, hit.id));
        } catch {
          message.error(t('search.openFailed'));
        }

        return;
      }

      case 'document':
        navigate(`/documents/${encodeURIComponent(hit.id)}`);
        return;

      case 'tripLog':
        navigate(`/trip-logs/${encodeURIComponent(hit.id)}`);
        return;

      case 'mapView':
        // A view is a place, so picking one moves the map rather than opening a page. The page
        // already knows how to apply one named in the URL, and consumes the parameter after.
        setSearchParams(
          (params) => {
            params.set('view', hit.id);
            return params;
          },
          { replace: true },
        );
        return;

      default:
        // A world added later with no case here opens nothing rather than doing something
        // surprising. It cannot be silent, though — that reads as a broken click.
        message.info(t('filters.selector.noDestination'));
    }
  };

  return (
    <div style={{ width: fullWidth ? '100%' : 260 }}>
      <ObjectSelector
        scopes={MAP_SCOPES}
        presentation="dropdown"
        rememberAs="map"
        placeholder={t('filters.selector.mapPlaceholder')}
        aria-label={t('filters.selector.mapPlaceholder')}
        data-testid="map-object-selector"
        onPick={(hit) => void onPick(hit)}
      />
    </div>
  );
}
