// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, expect, it, vi } from 'vitest';
import '../../i18n';
import ViewShareDialog from './ViewShareDialog.tsx';

afterEach(cleanup);

const url = 'https://silexgis.invalid/shared/view/00000000-0000-4000-8000-000000000001';

it('shows the link to copy and the same link as a square', async () => {
  const writeText = vi.fn().mockResolvedValue(undefined);
  Object.assign(navigator, { clipboard: { writeText } });
  render(
    <App>
      <ViewShareDialog shared={{ name: 'Valea Demo', url }} onClose={() => undefined} />
    </App>,
  );

  const dialog = await screen.findByRole('dialog', { name: 'Share the view Valea Demo' });
  expect(dialog).toBeTruthy();
  expect(screen.getByRole('img', { name: 'The link to the view Valea Demo as a QR code' })).toBeTruthy();
  expect(screen.getByRole('textbox', { name: 'Address' })).toHaveValue(url);

  fireEvent.click(screen.getByRole('button', { name: 'Copy address' }));
  await waitFor(() => expect(writeText).toHaveBeenCalledWith(url));
});

it('stays shut while nothing is being shared', () => {
  render(
    <App>
      <ViewShareDialog shared={null} onClose={() => undefined} />
    </App>,
  );
  expect(screen.queryByRole('dialog')).toBeNull();
});

it('says a view narrowed beyond a tag is shared without its data layers', async () => {
  render(
    <App>
      <ViewShareDialog
        shared={{ name: 'Valea Demo', url, leavesOut: { dataLayers: true, filterLayers: true } }}
        onClose={() => undefined}
      />
    </App>,
  );
  await screen.findByRole('dialog', { name: 'Share the view Valea Demo' });
  expect(screen.getByTestId('view-share-without-data').textContent).toMatch(/without the caves and features/);
  expect(screen.queryByTestId('view-share-without-filter-layers')).toBeNull();
});

it('says filter layers stay off a link when the rest of the view is shared', async () => {
  render(
    <App>
      <ViewShareDialog
        shared={{ name: 'Valea Demo', url, leavesOut: { dataLayers: false, filterLayers: true } }}
        onClose={() => undefined}
      />
    </App>,
  );
  await screen.findByRole('dialog', { name: 'Share the view Valea Demo' });
  expect(screen.queryByTestId('view-share-without-data')).toBeNull();
  expect(screen.getByTestId('view-share-without-filter-layers')).toBeTruthy();
});
