// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, type Dispatch, type SetStateAction } from 'react';
import type { SetURLSearchParams } from 'react-router-dom';
import { transformExtent } from 'ol/proj';
import { useWorkAreas } from '../../api/hooks.ts';
import { readTripListFilter } from '../../pages/trips/tripListFilter.ts';
import type { TripLayerFilter } from '../../map/tripLayer.ts';
import { getWorkspaceMap } from '../../map/mapContext.ts';
import { extentOf } from '../../workareas/tree.ts';

interface Args {
  searchParams: URLSearchParams;
  setSearchParams: SetURLSearchParams;
  requestedTrips: string | null;
  requestedModelId: string | null;
  requestedAreaId: string | null;
  setTripFilter: Dispatch<SetStateAction<TripLayerFilter>>;
  setUnappliedTripFilters: Dispatch<SetStateAction<number>>;
  setTripsVisible: Dispatch<SetStateAction<boolean>>;
  setCaveViewModelId: (id: string | null) => void;
}

/**
 * The one-shot instructions the map's address can carry (?trips=, ?model=, ?area=): each is acted
 * on once on arrival and then removed from the address.
 */
export function useMapUrlRequests(a: Args) {
  const {
    searchParams, setSearchParams, requestedTrips, requestedModelId, requestedAreaId, setTripFilter,
    setUnappliedTripFilters, setTripsVisible, setCaveViewModelId,
  } = a;
  /**
   * The trip listing's "show on map" button (?trips=1, plus the narrowings it was showing).
   *
   * Consumed on arrival like the params above — it is an instruction and not a description of the
   * URL — but the layer is left on afterwards, because the overlay somebody asked for is a place
   * they stay rather than a camera move that finishes.
   *
   * Only the narrowings this overlay can actually answer are adopted. The listing can also cut by
   * who was on the trip, which areas it named, one cave and one camp, and none of those are
   * questions the map layer asks; carrying them silently would draw an answer to a question
   * nobody asked. They are counted instead, and the panel says how many were left behind.
   */
  useEffect(() => {
    if (!requestedTrips) {
      return;
    }
    const carried = readTripListFilter(searchParams);
    setTripFilter({
      from: carried.from,
      to: carried.to,
      types: carried.types,
      states: carried.states,
      visibilities: carried.visibilities,
      hadIncident: carried.hadIncident,
    });
    setUnappliedTripFilters(
      [
        carried.search !== '',
        carried.participantIds.length > 0,
        carried.areaIds.length > 0,
        carried.caveId !== undefined,
        carried.expeditionId !== undefined,
      ].filter(Boolean).length,
    );
    setTripsVisible(true);
    setSearchParams(
      (params) => {
        for (const key of ['trips', 'q', 'from', 'to', 'types', 'states', 'visibilities',
          'hadIncident', 'participantIds', 'areaIds', 'caveId', 'expeditionId', 'page', 'sort']) {
          params.delete(key);
        }
        return params;
      },
      { replace: true },
    );
    // eslint-disable-next-line react-hooks/exhaustive-deps -- a one-shot instruction, read once
  }, [requestedTrips]);

  // A survey model picked elsewhere (?model=<id>, from a cave's model list) opens the survey
  // viewer beside the map. Consumed on arrival like ?view= and ?area= above, and for the same
  // reason — it is an instruction, not a description of the URL — but unlike those two it is
  // remembered here afterwards, because the pane it opens is a place the reader stays rather than
  // a camera move that finishes.
  useEffect(() => {
    if (!requestedModelId) {
      return;
    }
    setCaveViewModelId(requestedModelId);
    setSearchParams(
      (params) => {
        params.delete('model');
        return params;
      },
      { replace: true },
    );
  }, [requestedModelId, setSearchParams, setCaveViewModelId]);


  // An area picked elsewhere (?area=<id>, from the work-area overview or the dashboard board) is
  // framed on arrival, then the param is consumed — a one-shot instruction, exactly like ?view=
  // above and for the same reason: the camera is synced into the hash as the reader pans, so
  // leaving it behind would re-frame the area over a position somebody had panned to and shared.
  //
  // Fitted to the shape rather than centred on it at a guessed zoom: an area is a stretch of
  // country, and the only honest answer to "show me this massif" is one that has all of it on
  // screen. A guessed zoom would frame a valley and a mountain range identically.
  const { data: workAreas } = useWorkAreas(requestedAreaId !== null);
  useEffect(() => {
    if (!requestedAreaId || !workAreas) {
      return;
    }
    const extent = extentOf(workAreas.items.find((a) => a.id === requestedAreaId)?.geometry ?? null);
    if (extent) {
      getWorkspaceMap().getView().fit(transformExtent(extent, 'EPSG:4326', 'EPSG:3857'), {
        padding: [48, 48, 48, 48],
        duration: 250,
        maxZoom: 15,
      });
    }
    // Consumed whether or not it framed anything: an area with no boundary drawn yet, or one this
    // reader may not see, must not leave the map trying again on every refetch.
    setSearchParams(
      (params) => {
        params.delete('area');
        return params;
      },
      { replace: true },
    );
  }, [requestedAreaId, workAreas, setSearchParams]);
}
