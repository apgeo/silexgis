// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import i18n from '../../i18n';
import { ApiError } from '../../api/client.ts';

/**
 * The caving-group directory: the list everybody reads, and the create dialog offered to whoever
 * may create a group. Pinned: the button follows the right, a dialog with no name stays open with
 * the field marked and sends nothing, a create sends what was typed, and a refusal is said in
 * words from its code with the dialog kept open.
 */

const createSpy = vi.fn();
let canCreate = true;

vi.mock('../../api/hooks.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/hooks.ts')>('../../api/hooks.ts');
  return {
    ...actual,
    useCan: (domain: string, action: string) => domain === 'cavingGroups' && action === 'create' && canCreate,
    useCavingGroups: () => ({
      data: [
        { id: 'g-1', name: 'Club A', type: 'cavingClub', description: null, memberCount: 3, canAnnounce: false },
        { id: 'g-2', name: 'Group B', type: 'group', description: 'Sample', memberCount: 1, canAnnounce: false },
      ],
      isFetching: false,
    }),
    useCreateCavingGroup: () => ({ mutateAsync: createSpy, isPending: false }),
  };
});
vi.mock('../../components/statistics/TripStatisticsPanel.tsx', () => ({ default: () => null }));

const { default: CavingGroupsPage } = await import('./CavingGroupsPage.tsx');

const unhandled: unknown[] = [];
const record = (reason: unknown) => unhandled.push(reason);

beforeEach(() => {
  canCreate = true;
  createSpy.mockReset();
  unhandled.length = 0;
  process.on('unhandledRejection', record);
});
afterEach(() => {
  cleanup();
  process.off('unhandledRejection', record);
});

function show() {
  render(
    <MemoryRouter>
      <App>
        <CavingGroupsPage />
      </App>
    </MemoryRouter>,
  );
}

async function openCreate() {
  fireEvent.click(screen.getByRole('button', { name: new RegExp(i18n.t('cavingGroups.new')) }));
  return screen.findByRole('dialog');
}

function nameField(dialog: HTMLElement) {
  return within(dialog).getByLabelText(i18n.t('cavingGroups.name'));
}

describe('CavingGroupsPage', () => {
  it('lists the groups and offers create only to whoever may create one', () => {
    const createButton = () => screen.queryByRole('button', { name: new RegExp(i18n.t('cavingGroups.new')) });
    // The same query finds the button when the right is there, so its absence below is the gating.
    show();
    expect(createButton()).not.toBeNull();
    cleanup();

    canCreate = false;
    show();
    expect(screen.getByText('Club A')).toBeTruthy();
    expect(screen.getByText('Group B')).toBeTruthy();
    expect(createButton()).toBeNull();
  });

  it('keeps a dialog with no name open, marks the field and sends nothing', async () => {
    show();
    const dialog = await openCreate();
    fireEvent.click(within(dialog).getByRole('button', { name: i18n.t('common.ok') }));

    await waitFor(() => expect(nameField(dialog).getAttribute('aria-invalid')).toBe('true'));
    // Long enough for a rejection with no handler to be reported.
    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(createSpy).not.toHaveBeenCalled();
    expect(unhandled).toEqual([]);
  });

  it('creates a group from what was typed', async () => {
    createSpy.mockResolvedValue({ id: 'g-3' });
    show();
    const dialog = await openCreate();
    fireEvent.change(nameField(dialog), { target: { value: 'Club C' } });
    fireEvent.click(within(dialog).getByRole('button', { name: i18n.t('common.ok') }));

    await waitFor(() => expect(createSpy).toHaveBeenCalled());
    expect(createSpy.mock.calls[0][0]).toEqual({
      name: 'Club C',
      type: 'cavingClub',
      description: null,
      website: null,
    });
    expect(await screen.findByText(i18n.t('common.saved'))).toBeTruthy();
  });

  it('says a refused create in words from its code and keeps the dialog', async () => {
    createSpy.mockRejectedValue(new ApiError(409, 'caving_group.name_taken'));
    show();
    const dialog = await openCreate();
    fireEvent.change(nameField(dialog), { target: { value: 'Club A' } });
    fireEvent.click(within(dialog).getByRole('button', { name: i18n.t('common.ok') }));

    expect(await screen.findByText(i18n.t('cavingGroupPicker.nameTaken'))).toBeTruthy();
    expect((nameField(dialog) as HTMLInputElement).value).toBe('Club A');
  });
});
