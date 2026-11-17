// SPDX-License-Identifier: AGPL-3.0-or-later
import { MemoryRouter } from 'react-router-dom';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import type OlMap from 'ol/Map';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { getWorkspaceMap } from '../../map/mapContext.ts';

// Every map the dialog builds, in order: a second one is the defect.
const built = vi.hoisted(() => [] as OlMap[]);
// The maps that were disposed: one only detached keeps its listeners and its tile requests alive.
const disposed = vi.hoisted(() => new Set<unknown>());
vi.mock('ol/Map', async (original) => {
  const Actual = (await original<typeof import('ol/Map')>()).default;
  class Counted extends Actual {
    constructor(...args: ConstructorParameters<typeof Actual>) {
      super(...args);
      built.push(this);
    }

    override dispose() {
      disposed.add(this);
      super.dispose();
    }
  }
  return { default: Counted };
});

vi.mock('../../api/hooks.ts', async (original) => ({
  ...(await original<typeof import('../../api/hooks.ts')>()),
  useMapLayers: () => ({ isPending: false, data: [] }),
}));

const { default: PointPickerModal } = await import('./PointPickerModal.tsx');

const onPick = vi.fn();

function dialog(open: boolean, onCancel: () => void = () => {}) {
  return (
    <MemoryRouter>
      <PointPickerModal open={open} value={[24, 46]} onCancel={onCancel} onPick={onPick} />
    </MemoryRouter>
  );
}

/** The maps that are on the page right now. */
const attached = () => built.filter((map) => map.getTargetElement()?.isConnected);

beforeEach(() => {
  // The background choice reads the application's one shared map; it exists in every session.
  getWorkspaceMap();
  built.length = 0;
  disposed.clear();
  onPick.mockReset();
});
afterEach(cleanup);

describe('PointPickerModal', () => {
  it('keeps its map on screen while the page around it re-renders', async () => {
    const { rerender } = render(dialog(true));
    await waitFor(() => expect(attached()).toHaveLength(1));
    const map = attached()[0];

    // The page hands a fresh callback on each render, as a parent re-rendering for any reason does.
    for (let i = 0; i < 3; i += 1) {
      rerender(dialog(true, () => {}));
    }
    expect(attached()).toEqual([map]);
    expect(built).toHaveLength(1);
  });

  it('lets its map go when the dialog closes', async () => {
    const { rerender } = render(dialog(true));
    await waitFor(() => expect(attached()).toHaveLength(1));
    const map = attached()[0];

    rerender(dialog(false));
    await waitFor(() => expect(map.getTargetElement()).toBeNull());
    expect(map.getLayers().getLength()).toBe(0);
  });

  it('builds one map when it opens and disposes of it when it closes, every time', async () => {
    const { rerender } = render(dialog(true));
    await waitFor(() => expect(built).toHaveLength(1));

    rerender(dialog(false));
    await waitFor(() => expect(disposed.has(built[0])).toBe(true));

    rerender(dialog(true));
    await waitFor(() => expect(built).toHaveLength(2));
    expect(disposed.has(built[1])).toBe(false);

    rerender(dialog(false));
    await waitFor(() => expect(built.every((map) => disposed.has(map))).toBe(true));
  });

  it('disposes of its map when the page leaves with the dialog still open', async () => {
    const { unmount } = render(dialog(true));
    await waitFor(() => expect(built).toHaveLength(1));

    unmount();
    expect(disposed.has(built[0])).toBe(true);
  });

  it('keeps the last position when a coordinate field is cleared, rather than moving to zero', async () => {
    render(dialog(true));
    const [longitude] = await screen.findAllByRole('spinbutton');
    fireEvent.change(longitude, { target: { value: '' } });
    fireEvent.click(screen.getByRole('button', { name: /Use this point/ }));
    expect(onPick).toHaveBeenCalledWith([24, 46]);
  });

  it('returns the coordinates typed into its fields, not the ones it opened with', async () => {
    render(dialog(true));
    const [longitude, latitude] = await screen.findAllByRole('spinbutton');
    fireEvent.change(longitude, { target: { value: '10.5' } });
    fireEvent.change(latitude, { target: { value: '20.25' } });
    // A field cleared after a typed value keeps the typed one, not the opening one.
    fireEvent.change(latitude, { target: { value: '' } });
    fireEvent.click(screen.getByRole('button', { name: /Use this point/ }));
    expect(onPick).toHaveBeenCalledTimes(1);
    expect(onPick).toHaveBeenCalledWith([10.5, 20.25]);
  });
});
