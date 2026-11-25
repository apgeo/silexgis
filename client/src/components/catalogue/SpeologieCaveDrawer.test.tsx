// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, expect, it, vi } from 'vitest';
import '../../i18n';
import type { SpeologieCave } from '../../api/hooks.ts';

const refetch = vi.fn();
vi.mock('../../api/hooks.ts', () => ({
  useSpeologieCave: () => ({
    data: undefined,
    isPending: false,
    isError: true,
    error: new TypeError('Failed to fetch'),
    refetch,
  }),
}));

const { default: SpeologieCaveDrawer } = await import('./SpeologieCaveDrawer.tsx');

afterEach(cleanup);

// An invented catalogue row, as the list carries it.
const listed = { id: 63, title: 'Peștera de probă', url: null } as unknown as SpeologieCave;

it('says the full record could not be read, not that the cave has no description', () => {
  render(<SpeologieCaveDrawer cave={listed} onClose={() => undefined} />);

  expect(screen.getByTestId('speologie-cave-load-failed')).toBeTruthy();
  expect(screen.queryByText('The catalogue records no description for this cave.')).toBeNull();
  fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
  expect(refetch).toHaveBeenCalledTimes(1);
});
