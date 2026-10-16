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
