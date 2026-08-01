// SPDX-License-Identifier: AGPL-3.0-or-later
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import MapContext from '@terrestris/react-util/dist/Context/MapContext/MapContext';
import { cleanup, render, screen, fireEvent, waitFor } from '@testing-library/react';
import Feature from 'ol/Feature';
import Point from 'ol/geom/Point';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import '../../i18n';
import { getEntranceSource } from '../../map/entranceLayer.ts';
import { getSurfaceFeatureSource } from '../../map/featureLayer.ts';
import { getWorkspaceMap } from '../../map/mapContext.ts';
import { useWorkspaceStore } from '../../stores/workspaceStore.ts';
import FeatureListPanel from './FeatureListPanel.tsx';

afterEach(cleanup);

beforeEach(() => {
  getEntranceSource().clear();
  getSurfaceFeatureSource().clear();
  useWorkspaceStore.setState({ selection: null });
});

function entrance(id: string, caveId: string, name: string): Feature {
  const feature = new Feature(new Point([0, 0]));
  feature.setProperties({ id, caveId, name });
  return feature;
}

function cluster(count: number): Feature {
  const feature = new Feature(new Point([0, 0]));
  feature.setProperties({ cluster: true, count });
  return feature;
}

function mapFeature(id: string, name: string, typeCode: string): Feature {
  const feature = new Feature(new Point([0, 0]));
  feature.setProperties({ id, name, typeCode });
  return feature;
}

function renderPanel() {
  return render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <MapContext.Provider value={getWorkspaceMap()}>
        <FeatureListPanel />
      </MapContext.Provider>
    </QueryClientProvider>,
  );
}

describe('FeatureListPanel', () => {
  it('lists the loaded entrances and features with counts', async () => {
    getEntranceSource().addFeatures([entrance('e1', 'c1', 'Peștera Mare'), entrance('e2', 'c1', 'Aven')]);
    getSurfaceFeatureSource().addFeatures([mapFeature('f1', 'Doline field', 'doline')]);
    renderPanel();

    // The source listener is debounced; the initial render already sees the data.
    await waitFor(() => expect(screen.getByText('Peștera Mare')).toBeInTheDocument());
    expect(screen.getByText('Aven')).toBeInTheDocument();
    expect(screen.getByText('Doline field')).toBeInTheDocument();
    expect(screen.getByText(/Cave entrances \(2\)/)).toBeInTheDocument();
    // The features heading label is an i18n key; the count is what the panel derives.
    expect(screen.getByText(/\(1\)/)).toBeInTheDocument();
  });

  it('hints at clustering instead of listing cluster placeholders', async () => {
    getEntranceSource().addFeatures([cluster(42)]);
    renderPanel();

    await waitFor(() =>
      expect(screen.getByText('Zoom in to list individual entrances.')).toBeInTheDocument(),
    );
    expect(screen.getByText(/Cave entrances \(0\)/)).toBeInTheDocument();
  });

  it('publishes the workspace selection when a row is clicked', async () => {
    getEntranceSource().addFeatures([entrance('e1', 'c9', 'Ice Cave')]);
    renderPanel();

    await waitFor(() => expect(screen.getByText('Ice Cave')).toBeInTheDocument());
    fireEvent.click(screen.getByText('Ice Cave'));

    expect(useWorkspaceStore.getState().selection).toEqual({
      kind: 'entrance',
      entranceId: 'e1',
      caveId: 'c9',
    });
  });
});
