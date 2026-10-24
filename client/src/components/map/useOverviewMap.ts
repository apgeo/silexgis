// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect } from 'react';
import type Map from 'ol/Map';
import type { BackgroundLayer } from '../../map/baseLayers.ts';
import { attachOverviewMap } from '../../map/overviewMap.ts';
import { useUiPrefsStore } from '../../stores/uiPrefsStore.ts';

/**
 * The map's overview inset, open or shut as the account's preference says — at first, and
 * whenever the preference changes afterwards: the account's own value is read at sign-in and can
 * arrive after the map is drawn, and a later save brings in a change made in another browser.
 * Opening or closing it from its button is what changes the preference.
 */
export function useOverviewMap(map: Map, baseLayers: BackgroundLayer[], enabled: boolean, label: string) {
  const setOverviewMap = useUiPrefsStore((s) => s.setOverviewMap);
  useEffect(() => {
    if (!enabled || baseLayers.length === 0) {
      return undefined;
    }
    const overview = attachOverviewMap(map, baseLayers, {
      open: useUiPrefsStore.getState().overviewMap,
      label,
      onToggle: setOverviewMap,
    });
    const unsubscribe = useUiPrefsStore.subscribe((state, previous) => {
      if (state.overviewMap !== previous.overviewMap) {
        overview.setOpen(state.overviewMap);
      }
    });
    return () => {
      unsubscribe();
      overview.detach();
    };
  }, [map, baseLayers, enabled, label, setOverviewMap]);
}
