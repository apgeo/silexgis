// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, expect, it, vi } from 'vitest';
import '../../i18n';

const created = { id: 'share-1', token: 'tok-00000000-0000-4000-8000-000000000002', mode: 'public' };
const createShare = vi.fn().mockResolvedValue(created);

vi.mock('../../api/hooks.ts', () => ({
  useFeatureShares: () => ({ data: [], isError: false }),
  useCreateFeatureShare: () => ({ mutateAsync: createShare, isPending: false }),
  useRevokeFeatureShare: () => ({ mutateAsync: vi.fn(), isPending: false }),
}));

const { default: ShareLinksModal } = await import('./ShareLinksModal.tsx');

afterEach(cleanup);

it('shows a freshly made link as text and as a square, for as long as the dialog is open', async () => {
  render(
    <App>
      <ShareLinksModal featureId="feature-1" open onClose={() => undefined} />
    </App>,
  );

  expect(screen.queryByRole('img', { name: 'The share link as a QR code' })).toBeNull();

  fireEvent.click(screen.getByRole('button', { name: 'Create link' }));

  expect(await screen.findByRole('img', { name: 'The share link as a QR code' })).toBeTruthy();
  expect(screen.getByRole('textbox', { name: 'Address' })).toHaveValue(
    `${window.location.origin}/shared/features/${created.token}`,
  );
  // The access choice is named, not only placeholdered.
  expect(screen.getByRole('combobox', { name: 'Access' })).toBeTruthy();
});
