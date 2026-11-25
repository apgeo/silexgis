// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { act, cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import i18n from '../../i18n';
import type { UserAddress } from '../../api/hooks.ts';
import { afterRejectionsAreReported } from '../../testEventLoop.ts';

/**
 * The account's addresses: what a new, an edited and a removed one send, and what is said when a
 * write fails. Places below are invented.
 */

const t = i18n.getFixedT('en');
const create = vi.fn();
const update = vi.fn();
const remove = vi.fn();

vi.mock('../../api/hooks.ts', () => ({
  useCreateAddress: () => ({ mutateAsync: create, isPending: false }),
  useUpdateAddress: () => ({ mutateAsync: update, isPending: false }),
  useDeleteAddress: () => ({ mutate: remove, isPending: false }),
}));
// The map picker is a dialog of its own; the list only carries its value.
vi.mock('./PointField.tsx', () => ({ default: () => null }));

const { default: AddressList } = await import('./AddressList.tsx');

const address = (id: string, label: string, sortOrder: number) =>
  ({ id, label, country: 'Invented Land', city: null, addressText: null, geom: null, sortOrder }) as unknown as UserAddress;

// The first of three was removed earlier: what is left sits at 1 and 2.
const left = [address('a-2', 'Cabin', 1), address('a-3', 'Field base', 2)];

function show(addresses: UserAddress[] = left) {
  render(
    <App>
      <AddressList addresses={addresses} />
    </App>,
  );
}

async function fillLabelAndSave(label: string) {
  fireEvent.change(await screen.findByLabelText(t('settings.profile.addressLabel')), { target: { value: label } });
  await act(async () => {
    fireEvent.click(screen.getByRole('button', { name: t('common.save') }));
  });
}

beforeEach(() => {
  create.mockReset().mockResolvedValue({});
  update.mockReset().mockResolvedValue({});
  remove.mockReset();
});
afterEach(cleanup);

describe('AddressList', () => {
  it('places a new address after the last one, not at the count, once one has been removed', async () => {
    show();
    fireEvent.click(screen.getByRole('button', { name: t('settings.profile.addAddress') }));
    await fillLabelAndSave('Home');

    expect(create).toHaveBeenCalledTimes(1);
    expect(create.mock.calls[0][0]).toMatchObject({ label: 'Home', sortOrder: 3, geom: null });
  });

  it('keeps an edited address in its place', async () => {
    show();
    fireEvent.click(screen.getAllByRole('button', { name: t('settings.profile.editAddress') })[1]);
    await fillLabelAndSave('Field base, north');

    expect(update).toHaveBeenCalledTimes(1);
    expect(update.mock.calls[0][0]).toMatchObject({ id: 'a-3', body: { label: 'Field base, north', sortOrder: 2 } });
  });

  it('removes an address only once the removal is confirmed', async () => {
    show();
    const card = screen.getByText('Cabin').closest('.ant-card') as HTMLElement;
    fireEvent.click(within(card).getByRole('button', { name: t('settings.profile.removeAddress') }));
    expect(remove).not.toHaveBeenCalled();

    fireEvent.click(await screen.findByRole('button', { name: /OK|Yes/ }));
    expect(remove).toHaveBeenCalledWith('a-2', expect.anything());
  });

  it('says a failed save, and keeps the editor open with what was typed', async () => {
    create.mockRejectedValue(new TypeError('Failed to fetch'));
    show();
    fireEvent.click(screen.getByRole('button', { name: t('settings.profile.addAddress') }));
    await fillLabelAndSave('Home');

    expect(await screen.findByText(t('common.saveFailed'))).toBeInTheDocument();
    expect(screen.getByLabelText(t('settings.profile.addressLabel'))).toHaveValue('Home');
  });

  it('offers no more than ten addresses', () => {
    show(Array.from({ length: 10 }, (_, i) => address(`a-${i}`, `Place ${i}`, i)));
    expect(screen.getByRole('button', { name: t('settings.profile.addAddress') })).toBeDisabled();
  });

  it('Save with no label marks the field, sends nothing and leaves no unhandled rejection', async () => {
    const unhandled: unknown[] = [];
    const record = (reason: unknown) => unhandled.push(reason);
    process.on('unhandledRejection', record);
    try {
      show();
      fireEvent.click(screen.getByRole('button', { name: t('settings.profile.addAddress') }));
      const name = await screen.findByLabelText(t('settings.profile.addressLabel'));
      fireEvent.click(screen.getByRole('button', { name: t('common.save') }));
      await vi.waitFor(() => expect(name.getAttribute('aria-invalid')).toBe('true'));
      await afterRejectionsAreReported();
      expect(create).not.toHaveBeenCalled();
      expect(unhandled).toEqual([]);
    } finally {
      process.off('unhandledRejection', record);
    }
  });
});
