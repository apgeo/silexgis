// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useRef, useState } from 'react';
import type { useSearchParams } from 'react-router-dom';
import { reloadEntrances } from '../../map/entranceLayer.ts';
import {
  getMapFilter,
  mapFilterAddressKey,
  onMapFilterRefusal,
  readMapFilterAddress,
  sameMapFilter,
  setMapFilter,
  writeMapFilterAddress,
  type MapFilter,
} from '../../map/mapFilters.ts';
import { surfaceFeaturesChanged } from '../../workspace/surfaceFeatureRefresh.ts';

/**
 * The filter the main entrance and feature layers draw through, kept in step with the page's
 * address, and the notices that go with it: an unreadable link and a refusal from the server.
 */
export function useMapFilterState(
  searchParams: URLSearchParams,
  setSearchParams: ReturnType<typeof useSearchParams>[1],
) {
  // The filter the map's layers draw through. Unlike the one-shot parameters around it, it is a
  // description of the page and stays in the address — `filter` for a document, `saved` for a
  // saved filter's id — so a link to a filtered map opens filtered, as the cave registry's does.
  // The loaders read the module's copy; this state is what renders, and the two change together.
  const [mapFilter, setMapFilterState] = useState<MapFilter | null>(() => {
    const initial = readMapFilterAddress(searchParams).filter;
    setMapFilter(initial);
    return initial;
  });
  const [unreadableFilterLink, setUnreadableFilterLink] = useState(
    () => readMapFilterAddress(searchParams).unreadableLink,
  );
  const [filterRefused, setFilterRefused] = useState(false);
  const filterAddressKey = mapFilterAddressKey(searchParams);
  // Whether the filter in force came in by the address — a link opened or followed — rather than
  // from a chip, the tag chooser or a view picked on this page. A link's filter the recipient
  // cannot run (a clubmate's private saved filter, one from another world) is a link that cannot
  // be read: the map opens with nothing filtered and says so, rather than empty under a refusal.
  const filterFromAddress = useRef(readMapFilterAddress(searchParams).filter !== null);

  const adoptMapFilter = (next: MapFilter | null, fromAddress = false) => {
    filterFromAddress.current = fromAddress && next !== null;
    setMapFilter(next);
    setMapFilterState(next);
    setFilterRefused(false);
    reloadEntrances();
    surfaceFeaturesChanged();
  };

  useEffect(
    () =>
      onMapFilterRefusal(() => {
        // Several layers ask under one filter, so the refusal can come more than once; once the
        // filter has been dropped the later ones are about a filter no longer in force.
        if (getMapFilter() === null) {
          return;
        }
        if (filterFromAddress.current) {
          adoptMapFilter(null);
          setUnreadableFilterLink(true);
        } else {
          setFilterRefused(true);
        }
      }),
    [],
  );

  // The address changed under the page — a link followed inside the app, back or forward. Keyed
  // on the two values alone, so the one-shot parameters being consumed never re-reads it.
  useEffect(() => {
    const reading = readMapFilterAddress(searchParams);
    if (reading.unreadableLink) {
      setUnreadableFilterLink(true);
    } else if (!sameMapFilter(reading.filter, getMapFilter())) {
      adoptMapFilter(reading.filter, true);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- keyed on the filter's own two values; adoptMapFilter only sets state
  }, [filterAddressKey]);

  // The filter changed on the page — the tag chooser, a saved view — and is written into the
  // address after the render that changed it. Written then rather than at the change because a
  // view applied on arrival consumes `?view=` in the same breath, and two writes from one render
  // both start from the same parameters: the second would put the first one's filter back out.
  // An unreadable token is taken out too; the notice it raised stays until dismissed.
  useEffect(() => {
    const wanted = writeMapFilterAddress(searchParams, mapFilter);
    if (mapFilterAddressKey(wanted) !== mapFilterAddressKey(searchParams)) {
      setSearchParams((params) => writeMapFilterAddress(params, mapFilter), { replace: true });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- runs when the filter changes, not on every address change
  }, [mapFilter]);

  return { mapFilter, adoptMapFilter, unreadableFilterLink, setUnreadableFilterLink, filterRefused };
}
