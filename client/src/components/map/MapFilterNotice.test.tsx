// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, expect, it, vi } from 'vitest';
import '../../i18n';
import { tagChipFilter } from '../../map/mapFilters.ts';

const savedFilterId = '0e6f2c4a-5b1d-4c39-9b7e-2f8a1d3c4e5f';

vi.mock('../../api/hooks/savedFilters.ts', () => ({
  useSavedFilters: () => ({ data: [{ id: savedFilterId, name: 'Winter leads' }] }),
}));

const { default: MapFilterNotice } = await import('./MapFilterNotice.tsx');

afterEach(cleanup);

const noop = () => undefined;

it('names the saved filter the map is drawn through, and clears it', () => {
  const onClear = vi.fn();
  render(
    <MapFilterNotice
      filter={{ kind: 'saved', savedFilterId }}
      unreadableLink={false}
      onDismissUnreadableLink={noop}
      refused={false}
      onClear={onClear}
    />,
  );

  expect(screen.getByText('Map filtered by Winter leads')).toBeTruthy();
  fireEvent.click(screen.getByRole('button', { name: "Clear the map's filter" }));
  expect(onClear).toHaveBeenCalledTimes(1);
});

it('says a refused filter shows nothing, in place of naming it', () => {
  render(
    <MapFilterNotice
      filter={{ kind: 'saved', savedFilterId }}
      unreadableLink={false}
      onDismissUnreadableLink={noop}
      refused
      onClear={noop}
    />,
  );

  expect(screen.getByTestId('map-filter-refused')).toBeTruthy();
  expect(screen.queryByTestId('map-filter-active')).toBeNull();
});

it('says a link held a filter it could not read, until dismissed', () => {
  const onDismiss = vi.fn();
  render(
    <MapFilterNotice filter={null} unreadableLink onDismissUnreadableLink={onDismiss} refused={false} onClear={noop} />,
  );

  expect(screen.getByTestId('map-filter-unreadable')).toBeTruthy();
  fireEvent.click(screen.getByRole('button', { name: 'Close' }));
  expect(onDismiss).toHaveBeenCalled();
});

it('stays out of the way for no filter and for a plain tag, which the layer panel shows', () => {
  const { container, rerender } = render(
    <MapFilterNotice filter={null} unreadableLink={false} onDismissUnreadableLink={noop} refused={false} onClear={noop} />,
  );
  expect(container.firstChild).toBeNull();

  rerender(
    <MapFilterNotice
      filter={tagChipFilter('leads')}
      unreadableLink={false}
      onDismissUnreadableLink={noop}
      refused={false}
      onClear={noop}
    />,
  );
  expect(container.firstChild).toBeNull();
});
