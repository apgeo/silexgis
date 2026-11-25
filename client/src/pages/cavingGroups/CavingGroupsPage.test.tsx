// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import i18n from '../../i18n';
import { ApiError } from '../../api/client.ts';
import { afterRejectionsAreReported } from '../../testEventLoop.ts';

/**
 * The caving-group directory: the list everybody reads, and the create dialog offered to whoever
 * may create a group. Pinned: the button follows the right, a dialog with no name stays open with
 * the field marked and sends nothing, a create sends what was typed, and a refusal is said in
 * words from its code with the dialog kept open.
 */

const createSpy = vi.fn();
let canCreate = true;
/** What the read of the groups failed with, when a test makes it fail. */
let groupsFailure: unknown;
/** What the read of a roster, or of an announcement's audience, failed with, when a test makes it fail. */
let membersFailure: unknown;
let audienceFailure: unknown;
const sideRefetch = vi.fn();
const upsertSpy = vi.fn();

vi.mock('../../api/hooks.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/hooks.ts')>('../../api/hooks.ts');
  return {
    ...actual,
    useCan: (domain: string, action: string) => domain === 'cavingGroups' && action === 'create' && canCreate,
    useCavingGroups: () => groupsFailure !== undefined
      ? { data: undefined, isFetching: false, isError: true, error: groupsFailure, refetch: vi.fn() }
      : ({
      data: [
        { id: 'g-1', name: 'Club A', type: 'cavingClub', description: null, memberCount: 3, canAnnounce: true },
        { id: 'g-2', name: 'Group B', type: 'group', description: 'Sample', memberCount: 1, canAnnounce: false },
      ],
      isFetching: false,
    }),
    useCreateCavingGroup: () => ({ mutateAsync: createSpy, isPending: false }),
    useCavingGroupMembers: () => membersFailure !== undefined
      ? { data: undefined, isError: true, error: membersFailure, refetch: sideRefetch }
      : { data: [], isError: false, refetch: sideRefetch },
    useCavers: () => ({ data: [{ id: 'cv-1', name: 'Ion A.' }] }),
    useUpsertCavingGroupMember: () => ({ mutateAsync: upsertSpy, isPending: false }),
    useRemoveCavingGroupMember: () => ({ mutateAsync: vi.fn(), isPending: false }),
    useCavingGroupAnnouncementAudience: () => audienceFailure !== undefined
      ? { data: undefined, isPending: false, isError: true, error: audienceFailure, refetch: sideRefetch }
      : { data: { recipients: 2 }, isPending: false, isError: false, refetch: sideRefetch },
    useAnnounceToCavingGroup: () => ({ mutateAsync: vi.fn(), isPending: false }),
  };
});
vi.mock('../../components/statistics/TripStatisticsPanel.tsx', () => ({ default: () => null }));

const { default: CavingGroupsPage } = await import('./CavingGroupsPage.tsx');

const unhandled: unknown[] = [];
const record = (reason: unknown) => unhandled.push(reason);

beforeEach(() => {
  canCreate = true;
  createSpy.mockReset();
  membersFailure = undefined;
  audienceFailure = undefined;
  sideRefetch.mockReset();
  upsertSpy.mockReset();
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
  it('says the groups could not be read, rather than drawing an empty directory', () => {
    groupsFailure = new TypeError('Failed to fetch');
    try {
      show();
      expect(screen.getByTestId('caving-groups-load-failed')).toBeTruthy();
    } finally {
      groupsFailure = undefined;
    }
  });

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
    await afterRejectionsAreReported();
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

  /**
   * A roster that could not be read is not an empty roster: the drawer would say nobody is a
   * member, and adding somebody "as a member" could quietly change the role of a person already
   * on it. The failure is named, with a way to ask again, and nobody is added until it is read.
   */
  it('says a roster could not be read, offers to read it again and adds nobody meanwhile', async () => {
    membersFailure = new TypeError('Failed to fetch');
    show();
    fireEvent.click(within(screen.getByRole('row', { name: /Club A/ })).getByRole('button', { name: i18n.t('cavingGroups.manage') }));

    const alert = await screen.findByTestId('caving-group-members-load-failed');
    expect(alert.textContent).toContain(i18n.t('cavingGroups.membersLoadFailed'));
    expect(within(screen.getByRole('dialog')).queryByText(/No data/)).toBeNull();
    expect(within(screen.getByRole('dialog')).getByRole('button', { name: new RegExp(i18n.t('cavingGroups.addMember')) })).toBeDisabled();
    fireEvent.click(within(alert).getByRole('button', { name: 'Retry' }));
    expect(sideRefetch).toHaveBeenCalledTimes(1);
  });

  /**
   * "Nobody on this roster holds an account" is a claim about the roster; a count that never
   * arrived has not made it. The failure is named and the announcement cannot go on unseen.
   */
  it('says an announcement\'s audience could not be counted instead of saying there is nobody to tell', async () => {
    audienceFailure = new TypeError('Failed to fetch');
    show();
    fireEvent.click(within(screen.getByRole('row', { name: /Club A/ })).getByRole('button', { name: new RegExp(i18n.t('cavingGroups.announce.open')) }));

    const alert = await screen.findByTestId('announcement-audience-failed');
    expect(alert.textContent).toContain(i18n.t('cavingGroups.announce.audienceFailed'));
    expect(screen.queryByText(i18n.t('cavingGroups.announce.nobody'))).toBeNull();
    expect(screen.getByRole('button', { name: i18n.t('common.continue') })).toBeDisabled();
    fireEvent.click(within(alert).getByRole('button', { name: 'Retry' }));
    expect(sideRefetch).toHaveBeenCalledTimes(1);
  });
});
