// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, expect, it, vi } from 'vitest';
import '../../i18n';

const refetch = vi.fn();
let read: Record<string, unknown> = {};
vi.mock('../../api/hooks.ts', () => ({
  useUnfiledDocuments: () => ({ ...read, refetch }),
}));
vi.mock('./BulkFilingBar.tsx', () => ({ default: () => null }));
vi.mock('./UploadDrawer.tsx', () => ({ default: () => null }));

const { default: UnfiledDocuments } = await import('./UnfiledDocuments.tsx');

afterEach(() => {
  cleanup();
  refetch.mockReset();
});

function show() {
  return render(
    <MemoryRouter>
      <App>
        <UnfiledDocuments cabinets={[]} canWrite={false} />
      </App>
    </MemoryRouter>,
  );
}

it('says the inbox could not be read, rather than that nothing is waiting to be filed', () => {
  read = { data: undefined, isFetching: false, isError: true, error: new TypeError('Failed to fetch') };
  show();

  expect(screen.getByTestId('unfiled-load-failed')).toBeTruthy();
  expect(screen.queryByText('Nothing waiting to be filed.')).toBeNull();
  fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
  expect(refetch).toHaveBeenCalledTimes(1);
});
