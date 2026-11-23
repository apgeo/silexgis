// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import i18n from '../../i18n';
import { ApiError } from '../../api/client.ts';

/**
 * The profile form and the read it is filled from: a failed first read is named with a way to
 * ask again, a failed re-read behind an open form does not take the form (and what was typed into
 * it) away, and a refused save is worded by what the server said. Every name below is invented.
 */

const t = i18n.getFixedT('en');
const update = vi.fn();
const refetchMe = vi.fn();

const me = {
  id: 'u1',
  userName: 'ana.invented',
  email: 'ana@example.invalid',
  firstName: 'Ana',
  lastName: 'Inventată',
  displayName: null,
  bio: null,
  cavingClubId: null,
  phoneNumber: null,
  addresses: [],
  visibility: {
    email: 'private',
    realName: 'private',
    bio: 'private',
    phone: 'private',
    cavingClub: 'private',
    address: 'private',
    addressPoint: 'private',
  },
};

let meRead: Record<string, unknown>;

vi.mock('../../api/hooks.ts', () => ({
  useMe: () => meRead,
  useUpdateProfile: () => ({ mutateAsync: update, isPending: false }),
}));
vi.mock('../../components/cavingGroups/CavingGroupPicker.tsx', () => ({ default: () => null }));
vi.mock('../../components/settings/AddressList.tsx', () => ({ default: () => null }));
vi.mock('../../components/settings/AvatarPicker.tsx', () => ({ default: () => null }));
vi.mock('../../components/settings/FieldVisibilityToggle.tsx', () => ({ default: () => null }));

const { default: ProfileSettingsPage } = await import('./ProfileSettingsPage.tsx');

function show() {
  return render(
    <MemoryRouter>
      <App>
        <ProfileSettingsPage />
      </App>
    </MemoryRouter>,
  );
}

beforeEach(() => {
  update.mockReset();
  refetchMe.mockReset();
  meRead = { data: me, isPending: false, isError: false, error: null, refetch: refetchMe };
});

afterEach(cleanup);

describe('the profile page', () => {
  it('fills the form from the account it read', () => {
    show();
    expect(screen.getByText('ana.invented')).toBeInTheDocument();
    expect(screen.getByLabelText(/First name/)).toHaveValue('Ana');
  });

  it('names a failed first read and reads again when asked', () => {
    meRead = { data: undefined, isPending: false, isError: true, error: new TypeError('Failed to fetch'), refetch: refetchMe };
    show();

    expect(screen.getByText(t('common.loadFailed'))).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: t('common.retry') }));
    expect(refetchMe).toHaveBeenCalledTimes(1);
  });

  it('keeps the open form, and what was typed into it, when a later re-read fails', () => {
    const view = show();
    fireEvent.change(screen.getByLabelText(/First name/), { target: { value: 'Typed, not saved' } });

    meRead = { data: me, isPending: false, isError: true, error: new TypeError('Failed to fetch'), refetch: refetchMe };
    view.rerender(
      <MemoryRouter>
        <App>
          <ProfileSettingsPage />
        </App>
      </MemoryRouter>,
    );

    expect(screen.getByLabelText(/First name/)).toHaveValue('Typed, not saved');
    expect(screen.getByText(t('common.refreshFailed'))).toBeInTheDocument();
  });

  it('offers no retry when the server refused the read', () => {
    meRead = { data: undefined, isPending: false, isError: true, error: new ApiError(403, 'auth.forbidden'), refetch: refetchMe };
    show();

    expect(screen.getByText(t('common.readRefused'))).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: t('common.retry') })).toBeNull();
  });

  it('says a refused save in the server-coded words and does not report it saved', async () => {
    update.mockRejectedValue(new ApiError(400, 'me.caving_club_unknown'));
    show();

    fireEvent.click(screen.getByRole('button', { name: t('common.save') }));

    await waitFor(() => expect(update).toHaveBeenCalledTimes(1));
    expect(await screen.findByText(t('settings.problems.cavingClubUnknown'))).toBeInTheDocument();
    expect(screen.queryByText(t('common.saved'))).toBeNull();
  });
});
