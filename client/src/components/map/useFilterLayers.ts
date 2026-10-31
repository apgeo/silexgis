// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useMemo, useState } from 'react';
import { useSavedFilters } from '../../api/hooks/savedFilters.ts';
import { useSelectionSetMax } from '../../api/hooks/workingSet.ts';
import {
  nextFreeFilterLayerColor,
  nextFreeFilterLayerSymbol,
  filterLayerStatus,
} from '../../map/filterLayerList.ts';
import { detachFilterLayers, onFilterLayerRefusal, syncFilterLayers } from '../../map/filterLayers.ts';
import { applyPendingOverlayOrder, getWorkspaceMap } from '../../map/mapContext.ts';
import { useWorkspaceStore } from '../../stores/workspaceStore.ts';
import type { FilterLayerRowInfo } from './FilterLayerRow.tsx';

/** The map layers drawn from saved filters, their rows for the layer panel and their upkeep. */
export function useFilterLayers(overlayOpacity: Record<string, number>) {
  // Layers drawn from saved filters, each beside the main layers rather than instead of them. The
  // list is this page's state and a saved view's; the OpenLayers layers follow it. Whether a
  // layer's filter can still be run is decided from the saved-filter list and from the server's
  // own refusals, and a layer whose filter cannot is kept as a row that says so and draws nothing.
  const filterLayers = useWorkspaceStore((s) => s.filterLayers);
  const setFilterLayers = useWorkspaceStore((s) => s.setFilterLayers);
  const [refusedFilterLayers, setRefusedFilterLayers] = useState<ReadonlySet<string>>(() => new Set());
  const { data: savedFilters } = useSavedFilters();
  // The installation's ceiling on the working set, so a modifier-click stops where the server's
  // summary request does.
  useSelectionSetMax();
  useEffect(
    () =>
      onFilterLayerRefusal((savedFilterId) =>
        setRefusedFilterLayers((current) => new Set([...current, savedFilterId.toLowerCase()])),
      ),
    [],
  );
  const filterLayerRows = useMemo<FilterLayerRowInfo[]>(
    () =>
      filterLayers.map((spec) => ({
        spec,
        name:
          savedFilters?.find((filter) => filter.id.toLowerCase() === spec.savedFilterId.toLowerCase())?.name ?? null,
        status: filterLayerStatus(spec, savedFilters, refusedFilterLayers),
        nextColor: nextFreeFilterLayerColor(filterLayers, spec),
        nextSymbol: nextFreeFilterLayerSymbol(filterLayers, spec),
      })),
    [filterLayers, savedFilters, refusedFilterLayers],
  );
  const unreadableFilterLayerKey = filterLayerRows
    .filter((row) => row.status === 'unreadable')
    .map((row) => row.spec.savedFilterId.toLowerCase())
    .join(',');
  useEffect(() => () => detachFilterLayers(), []);
  // The 3D scene beside the map draws the same layers, and draws nothing for the same unreadable ones.
  useEffect(() => {
    useWorkspaceStore
      .getState()
      .setUnreadableFilterLayers(unreadableFilterLayerKey === '' ? [] : unreadableFilterLayerKey.split(','));
  }, [unreadableFilterLayerKey]);

  // The saved-filter layers follow their list, their filters' readability and their transparency.
  useEffect(() => {
    syncFilterLayers(
      getWorkspaceMap(),
      filterLayers,
      new Set(unreadableFilterLayerKey === '' ? [] : unreadableFilterLayerKey.split(',')),
      new globalThis.Map(Object.entries(overlayOpacity)),
    );
    // A just-applied saved view may prescribe a stacking that includes these layers.
    applyPendingOverlayOrder();
  }, [filterLayers, unreadableFilterLayerKey, overlayOpacity]);

  return { filterLayers, setFilterLayers, setRefusedFilterLayers, savedFilters, filterLayerRows };
}
