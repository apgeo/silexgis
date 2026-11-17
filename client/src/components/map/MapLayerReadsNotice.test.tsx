// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, expect, it, vi } from 'vitest';
import '../../i18n';
import MapLayerReadsNotice from './MapLayerReadsNotice.tsx';

afterEach(cleanup);

const fine = () => ({ isError: false, error: null, refetch: vi.fn() });

it('says nothing while every layer read answered', () => {
  render(<MapLayerReadsNotice reads={[fine(), fine()]} />);
  expect(screen.queryByTestId('map-layer-reads-failed')).toBeNull();
});

it('says the layers could not all be read, and asks again for exactly the ones that failed', () => {
  const catalogue = { isError: true, error: new TypeError('Failed to fetch'), refetch: vi.fn() };
  const files = fine();
  render(<MapLayerReadsNotice reads={[catalogue, files]} />);

  expect(screen.getByText("Some of the map's layers could not be read; the list may be incomplete.")).toBeTruthy();
  fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
  expect(catalogue.refetch).toHaveBeenCalledTimes(1);
  expect(files.refetch).not.toHaveBeenCalled();
});
