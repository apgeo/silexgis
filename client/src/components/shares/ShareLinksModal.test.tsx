// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import '../../i18n';

const created = { id: 'share-1', token: 'tok-00000000-0000-4000-8000-000000000002', mode: 'public' };
const createShare = vi.fn();
const revokeShare = vi.fn();
let listed: unknown[] = [];

vi.mock('../../api/hooks.ts', () => ({
  useFeatureShares: () => ({ data: listed, isError: false }),
  useCreateFeatureShare: () => ({ mutateAsync: createShare, isPending: false }),
  useRevokeFeatureShare: () => ({ mutateAsync: revokeShare, isPending: false }),
}));

const { default: ShareLinksModal } = await import('./ShareLinksModal.tsx');

beforeEach(() => {
  createShare.mockReset().mockResolvedValue(created);
  revokeShare.mockReset().mockResolvedValue(undefined);
  listed = [];
});

afterEach(cleanup);

function renderModal() {
  render(
    <App>
      <ShareLinksModal featureId="feature-1" open onClose={() => undefined} />
    </App>,
  );
}

const QR = { name: 'The share link as a QR code' };

it('shows a freshly made link as text and as a square, for as long as the dialog is open', async () => {
  renderModal();

  expect(screen.queryByRole('img', QR)).toBeNull();

  fireEvent.click(screen.getByRole('button', { name: 'Create link' }));

  expect(await screen.findByRole('img', QR)).toBeTruthy();
  expect(screen.getByRole('textbox', { name: 'Address' })).toHaveValue(
    `${window.location.origin}/shared/features/${created.token}`,
  );
  // The access choice is named, not only placeholdered.
  expect(screen.getByRole('combobox', { name: 'Access' })).toBeTruthy();
});

it('mints a public link over the contained records unless told otherwise', async () => {
  renderModal();

  fireEvent.click(screen.getByRole('button', { name: 'Create link' }));

  await screen.findByRole('img', QR);
  expect(createShare).toHaveBeenCalledWith({
    id: 'feature-1',
    body: { mode: 'public', includeSubtree: true },
  });
});

it('sends the access and the extent that were chosen', async () => {
  renderModal();

  fireEvent.mouseDown(screen.getByRole('combobox', { name: 'Access' }));
  fireEvent.click(await screen.findByTitle('Requires sign-in'));
  fireEvent.click(screen.getByRole('checkbox', { name: 'Include contained records' }));
  fireEvent.click(screen.getByRole('button', { name: 'Create link' }));

  await screen.findByRole('img', QR);
  expect(createShare).toHaveBeenCalledWith({
    id: 'feature-1',
    body: { mode: 'requiresLogin', includeSubtree: false },
  });
});

it('says a refused link was not made and shows no address for it', async () => {
  createShare.mockRejectedValue(new Error('feature.not_found'));
  renderModal();

  fireEvent.click(screen.getByRole('button', { name: 'Create link' }));

  expect(await screen.findByText('The operation failed. Please try again.')).toBeInTheDocument();
  expect(screen.queryByRole('img', QR)).toBeNull();
  expect(screen.queryByRole('textbox', { name: 'Address' })).toBeNull();
});

const active = {
  id: 'share-7',
  mode: 'requiresLogin',
  includeSubtree: false,
  createdAt: '2026-11-01T10:00:00Z',
  revokedAt: null,
};

async function revokeTheListedLink() {
  const row = screen.getByText('Requires sign-in', { selector: '.ant-tag' }).closest('tr');
  if (!row) {
    throw new Error('the listed link has no row');
  }
  fireEvent.click(within(row).getByRole('button', { name: 'Revoke' }));
  const confirm = await screen.findByText('Revoke this share link?');
  const popover = confirm.closest('.ant-popover');
  if (!(popover instanceof HTMLElement)) {
    throw new Error('the confirmation has no popover');
  }
  fireEvent.click(within(popover).getByRole('button', { name: /OK|Revoke/ }));
}

it('revokes the link it was asked to, and says so', async () => {
  listed = [active];
  renderModal();

  await revokeTheListedLink();

  expect(await screen.findByText('Share link revoked.')).toBeInTheDocument();
  expect(revokeShare).toHaveBeenCalledWith({ id: 'feature-1', shareId: 'share-7' });
});

it('says a refused revocation failed and does not claim the link was revoked', async () => {
  listed = [active];
  revokeShare.mockRejectedValue(new Error('share.not_found'));
  renderModal();

  await revokeTheListedLink();

  expect(await screen.findByText('The operation failed. Please try again.')).toBeInTheDocument();
  await waitFor(() => expect(revokeShare).toHaveBeenCalledTimes(1));
  expect(screen.queryByText('Share link revoked.')).toBeNull();
});
