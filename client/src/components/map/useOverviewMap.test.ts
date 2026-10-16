// SPDX-License-Identifier: AGPL-3.0-or-later
import Map from 'ol/Map';
import View from 'ol/View';
import OverviewMap from 'ol/control/OverviewMap';
import TileLayer from 'ol/layer/Tile';
import XYZ from 'ol/source/XYZ';
import { act, cleanup, renderHook } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { useUiPrefsStore } from '../../stores/uiPrefsStore.ts';
import { useOverviewMap } from './useOverviewMap.ts';

afterEach(() => {
  cleanup();
  useUiPrefsStore.setState({ overviewMap: false });
});

describe('useOverviewMap', () => {
  it('opens the inset when the account’s choice arrives after the map was drawn', () => {
    const map = new Map({ view: new View({ center: [0, 0], zoom: 8 }) });
    const layers = [new TileLayer({ source: new XYZ({ url: 'https://tiles.invalid/{z}/{x}/{y}.png' }), visible: true })];
    const inset = () => map.getControls().getArray().some((control) => control instanceof OverviewMap);
    renderHook(() => useOverviewMap(map, layers, true, 'Overview map'));
    expect(inset()).toBe(false);

    // What the sign-in read takes from the account, a moment later.
    act(() => useUiPrefsStore.setState({ overviewMap: true }));
    expect(inset()).toBe(true);

    act(() => useUiPrefsStore.setState({ overviewMap: false }));
    expect(inset()).toBe(false);
  });
});
