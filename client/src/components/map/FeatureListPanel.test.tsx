// SPDX-License-Identifier: AGPL-3.0-or-later
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import MapContext from '@terrestris/react-util/dist/Context/MapContext/MapContext';
import { act, cleanup, render, screen, fireEvent, waitFor } from '@testing-library/react';
import Feature from 'ol/Feature';
import Point from 'ol/geom/Point';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { getEntranceSource } from '../../map/entranceLayer.ts';
import { getSurfaceFeatureSource } from '../../map/featureLayer.ts';
import { fitExtent, getWorkspaceMap } from '../../map/mapContext.ts';
import { useWorkspaceStore } from '../../stores/workspaceStore.ts';
import FeatureListPanel from './FeatureListPanel.tsx';

// The map view's own fit needs a sized map; what the list asks it to fit is what is under test.
vi.mock('../../map/mapContext.ts', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../map/mapContext.ts')>()),
  fitExtent: vi.fn(),
}));

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

function mapFeature(id: string, name: string, typeCode: string, at: [number, number] = [0, 0]): Feature {
  const feature = new Feature(new Point(at));
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

  it('names each row with a button that does what the row’s click does, for the keyboard', async () => {
    getSurfaceFeatureSource().addFeatures([mapFeature('f7', 'Doline field', 'doline')]);
    renderPanel();

    const open = await screen.findByRole('button', { name: 'Doline field' });
    open.focus();
    expect(open).toHaveFocus();
    fireEvent.click(open);

    expect(useWorkspaceStore.getState().selection).toEqual({ kind: 'feature', featureId: 'f7' });
  });

  it('settles once the sources are still, instead of restyling and re-deriving forever', async () => {
    const aven = entrance('e1', 'c1', 'Aven');
    const field = mapFeature('f1', 'Doline field', 'doline');
    getEntranceSource().addFeatures([aven]);
    getSurfaceFeatureSource().addFeatures([field]);
    // The refresh after a change waits on a timer; a fake clock moves it exactly, so "nothing
    // more happened" is said of a known stretch rather than of however long a loaded machine took.
    vi.useFakeTimers({ shouldAdvanceTime: true });
    try {
      renderPanel();
      await screen.findByText('Aven');
      // One change on a source — a reload, a selection highlight — starts a refresh; what it does
      // has to end there, not set off the next one.
      getEntranceSource().addFeatures([entrance('e2', 'c2', 'Second aven')]);
      await act(async () => {
        await vi.advanceTimersByTimeAsync(300);
      });
      // The witness that the refresh ran: the row the change brought is drawn.
      expect(screen.getByText('Second aven')).toBeInTheDocument();
      const restyled = [vi.spyOn(aven, 'setStyle'), vi.spyOn(field, 'setStyle')];

      await act(async () => {
        await vi.advanceTimersByTimeAsync(900);
      });

      for (const spy of restyled) {
        expect(spy).not.toHaveBeenCalled();
      }
    } finally {
      vi.useRealTimers();
    }
  });

  it('takes the checked-row marker off the shared map features when it closes', async () => {
    const field = mapFeature('f1', 'Doline field', 'doline');
    getSurfaceFeatureSource().addFeatures([field]);
    const { unmount } = renderPanel();
    await screen.findByText('Doline field');
    fireEvent.click(screen.getAllByRole('checkbox').at(-1)!);
    expect(field.getStyle()).toBeTruthy();

    unmount();

    expect(field.getStyle()).toBeFalsy();
  });

  it('says nothing is in view when the sources are empty, rather than showing two empty tables', async () => {
    renderPanel();

    expect(await screen.findByText('Nothing loaded in the current view.')).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
  });

  it('zooms to the extent that holds every checked row, and only those', async () => {
    getSurfaceFeatureSource().addFeatures([
      mapFeature('f1', 'West sink', 'doline', [100, 200]),
      mapFeature('f2', 'East sink', 'doline', [900, 600]),
      mapFeature('f3', 'Far sink', 'doline', [50000, 50000]),
    ]);
    renderPanel();
    await screen.findByText('Far sink');
    const rowCheckbox = (name: string) =>
      screen.getByText(name).closest('tr')!.querySelector('input[type="checkbox"]')!;
    fireEvent.click(rowCheckbox('West sink'));
    fireEvent.click(rowCheckbox('East sink'));
    expect(screen.getByText('2 selected')).toBeInTheDocument();
    vi.mocked(fitExtent).mockClear();

    fireEvent.click(screen.getByRole('button', { name: /Zoom to selected/ }));

    expect(fitExtent).toHaveBeenCalledTimes(1);
    expect(vi.mocked(fitExtent).mock.calls[0][0]).toEqual([100, 200, 900, 600]);
  });

  it('takes the hover marker off a map feature when the list closes under the pointer', async () => {
    const field = mapFeature('f1', 'Doline field', 'doline');
    getSurfaceFeatureSource().addFeatures([field]);
    const { unmount } = renderPanel();
    fireEvent.mouseOver((await screen.findByText('Doline field')).closest('tr')!);
    expect(field.getStyle()).toBeTruthy();

    unmount();

    expect(field.getStyle()).toBeFalsy();
  });
});
