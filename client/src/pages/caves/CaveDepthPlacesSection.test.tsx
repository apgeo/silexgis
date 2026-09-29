// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';

const write = vi.fn();
const remove = vi.fn();
const places = vi.fn();

vi.mock('../../api/hooks.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/hooks.ts')>('../../api/hooks.ts');
  return {
    ...actual,
    useCaveDepthPlaces: () => ({ data: places() }),
    useWriteCaveDepthPlace: () => ({ mutateAsync: write, isPending: false }),
    useDeleteCaveDepthPlace: () => ({ mutateAsync: remove, isPending: false }),
  };
});

const { default: CaveDepthPlacesSection } = await import('./CaveDepthPlacesSection.tsx');

const DECLARED = [
  { id: 'p-1', depthM: 96, stationName: 'upper.2', placeLabel: 'Meandru' },
  { id: 'p-2', depthM: 150, stationName: 'deep.3', placeLabel: null },
];

beforeEach(() => {
  write.mockReset().mockResolvedValue(DECLARED[0]);
  remove.mockReset().mockResolvedValue(undefined);
  places.mockReset().mockReturnValue(DECLARED);
});
afterEach(cleanup);

/**
 * Declaring what a cave's depths mean.
 *
 * What is worth testing here is not the table but the two things a reader could be misled about: a
 * cave that has declared nothing must say what happens instead, and somebody without write access
 * must not be offered controls that would be refused.
 */
describe('CaveDepthPlacesSection', () => {
  it('shows each declaration as a depth, a station and a name', () => {
    render(
      <App>
        <CaveDepthPlacesSection caveId="cave-1" canEdit />
      </App>,
    );
    expect(screen.getByText('96 m')).toBeInTheDocument();
    expect(screen.getByText('upper.2')).toBeInTheDocument();
    expect(screen.getByText('Meandru')).toBeInTheDocument();
    // A declaration with no name is still a declaration; it is said to be unnamed rather than
    // drawn as a blank cell somebody would read as a loading row.
    expect(screen.getByText(/Unnamed|Fără nume/)).toBeInTheDocument();
  });

  it('says what happens to a depth when the cave has declared nothing', () => {
    // An empty table would read as "this feature is broken" rather than "every depth here lands
    // on whichever station is nearest", which is the fact somebody needs.
    places.mockReturnValue([]);
    render(
      <App>
        <CaveDepthPlacesSection caveId="cave-1" canEdit />
      </App>,
    );
    const empty = screen.getByTestId('cave-depth-places-empty');
    expect(empty).toBeInTheDocument();
    // Asserted on the empty notice itself: the card's own help text also says "nearest", and a
    // whole-document match would pass on that sentence with the notice missing entirely.
    expect(empty.textContent).toMatch(/nearest|apropiat/);
  });

  it('declares a depth with the station and the name that were typed', async () => {
    render(
      <App>
        <CaveDepthPlacesSection caveId="cave-1" canEdit />
      </App>,
    );
    fireEvent.click(screen.getByTestId('cave-depth-place-add'));

    fireEvent.change(screen.getByTestId('cave-depth-place-depth'), { target: { value: '110' } });
    fireEvent.change(screen.getByTestId('cave-depth-place-station'), { target: { value: 'deep.4' } });
    fireEvent.change(screen.getByTestId('cave-depth-place-label'), { target: { value: 'Sala Mare' } });
    fireEvent.click(screen.getByTestId('cave-depth-place-save'));

    await waitFor(() => expect(write).toHaveBeenCalledOnce());
    expect(write.mock.calls[0][0]).toEqual({
      depthM: 110,
      stationName: 'deep.4',
      placeLabel: 'Sala Mare',
    });
  });

  it('declares a depth without a name rather than with an empty one', async () => {
    // Null and "" mean different things to the reader of a declaration: one is "this place has no
    // name", the other is a name that is a blank string and would be offered in a chooser.
    render(
      <App>
        <CaveDepthPlacesSection caveId="cave-1" canEdit />
      </App>,
    );
    fireEvent.click(screen.getByTestId('cave-depth-place-add'));
    fireEvent.change(screen.getByTestId('cave-depth-place-depth'), { target: { value: '110' } });
    fireEvent.change(screen.getByTestId('cave-depth-place-station'), { target: { value: 'deep.4' } });
    fireEvent.click(screen.getByTestId('cave-depth-place-save'));

    await waitFor(() => expect(write).toHaveBeenCalledOnce());
    expect(write.mock.calls[0][0].placeLabel).toBeNull();
  });

  it('will not declare a depth with no station, because that is not a declaration', async () => {
    render(
      <App>
        <CaveDepthPlacesSection caveId="cave-1" canEdit />
      </App>,
    );
    fireEvent.click(screen.getByTestId('cave-depth-place-add'));
    fireEvent.change(screen.getByTestId('cave-depth-place-depth'), { target: { value: '110' } });
    fireEvent.click(screen.getByTestId('cave-depth-place-save'));

    await waitFor(() => expect(screen.getByText(/which station|care stație/)).toBeInTheDocument());
    expect(write).not.toHaveBeenCalled();
  });

  it('offers no controls at all without write access', () => {
    render(
      <App>
        <CaveDepthPlacesSection caveId="cave-1" canEdit={false} />
      </App>,
    );
    // The list is still worth reading — it says where reported depths land — so it is shown; what
    // is absent is every way of changing it, rather than a button that would be refused.
    expect(screen.getByText('Meandru')).toBeInTheDocument();
    expect(screen.queryByTestId('cave-depth-place-add')).not.toBeInTheDocument();
    expect(screen.queryByTestId('cave-depth-place-save')).not.toBeInTheDocument();
  });
});
