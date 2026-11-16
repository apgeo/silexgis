// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import i18n from '../../i18n';
import { ApiError } from '../../api/client.ts';

const t = i18n.getFixedT('en');

const createMutate = vi.fn();
const updateMutate = vi.fn();
const deleteMutate = vi.fn();
// The fresh read an editor or a delete makes first, and the version it filed.
const readChecklist = vi.fn();
const VERSION = 'W/"42"';
// What the permissions dialog was last handed; the dialog itself is somebody else's to test.
const permissionsModal = vi.fn();

const OWNER = '22222222-2222-2222-2222-222222222222';
const SOMEBODY_ELSE = '99999999-9999-9999-9999-999999999999';
// Who is reading the page, and whether they hold the right to manage every list's rules.
let reader = SOMEBODY_ELSE;
let managesAnyList = false;

const checklists = [
  {
    id: '11111111-1111-1111-1111-111111111111',
    title: 'Before we set off',
    description: 'The things that have to be settled.',
    ownerUserId: '22222222-2222-2222-2222-222222222222',
    cavingGroupId: null,
    visibility: 'private',
    items: [
      { id: '33333333-3333-3333-3333-333333333333', text: 'Permit obtained', sortOrder: 0 },
      { id: '44444444-4444-4444-4444-444444444444', text: 'Key collected', sortOrder: 1 },
    ],
    createdAt: '2026-08-22T10:00:00Z',
    updatedAt: '2026-08-22T10:00:00Z',
  },
];

vi.mock('../../api/hooks.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/hooks.ts')>('../../api/hooks.ts');
  return {
    ...actual,
    useChecklists: () => ({ data: checklists, isLoading: false }),
    useCavingGroups: () => ({ data: [] }),
    useCreateChecklist: () => ({ mutateAsync: createMutate, isPending: false }),
    useUpdateChecklist: () => ({ mutateAsync: updateMutate, isPending: false }),
    useDeleteChecklist: () => ({ mutateAsync: deleteMutate, isPending: false }),
    useReadChecklist: () => readChecklist,
    checklistVersion: () => VERSION,
    useMe: () => ({ data: { id: reader } }),
    useCan: () => managesAnyList,
  };
});

vi.mock('../../components/permissions/PermissionsModal.tsx', () => ({
  default: (props: { entityType: string; entityId: string; open: boolean }) => {
    permissionsModal(props);
    return null;
  },
}));

const { default: ChecklistsPage } = await import('./ChecklistsPage.tsx');

beforeEach(() => {
  createMutate.mockReset().mockResolvedValue({});
  updateMutate.mockReset().mockResolvedValue({});
  deleteMutate.mockReset().mockResolvedValue(undefined);
  readChecklist.mockReset().mockImplementation(() => Promise.resolve(checklists[0]));
  permissionsModal.mockReset();
  reader = SOMEBODY_ELSE;
  managesAnyList = false;
});
afterEach(cleanup);

function show() {
  return render(
    <App>
      <ChecklistsPage />
    </App>,
  );
}

describe('ChecklistsPage', () => {
  it('writes a new list with the lines somebody typed', async () => {
    show();
    fireEvent.click(screen.getByRole('button', { name: /New checklist/ }));

    fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Winter trips' } });
    fireEvent.change(screen.getByPlaceholderText('Something to settle before setting off'), {
      target: { value: 'Callout arranged' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'OK' }));

    await waitFor(() => expect(createMutate).toHaveBeenCalled());
    const body = createMutate.mock.calls[0][0];
    expect(body.title).toBe('Winter trips');
    expect(body.items).toEqual([{ id: null, text: 'Callout arranged' }]);

    // Its own audience and nothing else decides who reads it: a list starts shut, and there is
    // no token and no "publish" act anywhere on this page.
    expect(body.visibility).toBe('private');
  });

  it('keeps a line its identity when its wording is corrected', async () => {
    show();
    fireEvent.click(screen.getByRole('button', { name: 'Edit' }));

    const lines = await screen.findAllByPlaceholderText('Something to settle before setting off');
    fireEvent.change(lines[0], { target: { value: 'Permit obtained from the estate' } });
    fireEvent.click(screen.getByRole('button', { name: 'OK' }));

    await waitFor(() => expect(updateMutate).toHaveBeenCalled());
    const { body } = updateMutate.mock.calls[0][0];

    // The id travels with the reworded line. Sending it without one would write a new line and
    // strand every confirmation any trip had made against the old one.
    expect(body.items[0]).toEqual({
      id: '33333333-3333-3333-3333-333333333333',
      text: 'Permit obtained from the estate',
    });
    expect(body.items[1].id).toBe('44444444-4444-4444-4444-444444444444');
  });

  it('opens the editor on the list as stored now, and saves naming the version it opened', async () => {
    readChecklist.mockResolvedValue({ ...checklists[0], title: 'Before we set off, revised elsewhere' });
    show();
    fireEvent.click(screen.getByRole('button', { name: 'Edit' }));

    expect(await screen.findByDisplayValue('Before we set off, revised elsewhere')).toBeInTheDocument();
    expect(readChecklist).toHaveBeenCalledWith(checklists[0].id);
    fireEvent.click(screen.getByRole('button', { name: 'OK' }));

    await waitFor(() => expect(updateMutate).toHaveBeenCalled());
    expect(updateMutate.mock.calls[0][0]).toMatchObject({ id: checklists[0].id, version: VERSION });
  });

  it('says why a save was refused, in its own words, and keeps the editor open', async () => {
    updateMutate.mockRejectedValue(new ApiError(404, 'checklist.not_found', 'server prose'));
    show();
    fireEvent.click(screen.getByRole('button', { name: 'Edit' }));
    await screen.findAllByPlaceholderText('Something to settle before setting off');
    fireEvent.click(screen.getByRole('button', { name: 'OK' }));

    expect(await screen.findByText(t('checklists.problems.notFound'))).toBeInTheDocument();
    expect(screen.queryByText('server prose')).not.toBeInTheDocument();
    expect(screen.getAllByPlaceholderText('Something to settle before setting off')).toHaveLength(2);
  });

  it('says a save failed when it never reached the server', async () => {
    updateMutate.mockRejectedValue(new TypeError('Failed to fetch'));
    show();
    fireEvent.click(screen.getByRole('button', { name: 'Edit' }));
    await screen.findAllByPlaceholderText('Something to settle before setting off');
    fireEvent.click(screen.getByRole('button', { name: 'OK' }));

    expect(await screen.findByText(t('checklists.saveFailed'))).toBeInTheDocument();
  });

  it('leaves a save refused as a conflict to the prompt every write shares', async () => {
    updateMutate.mockRejectedValue(new ApiError(412, 'concurrency.conflict'));
    show();
    fireEvent.click(screen.getByRole('button', { name: 'Edit' }));
    await screen.findAllByPlaceholderText('Something to settle before setting off');
    fireEvent.click(screen.getByRole('button', { name: 'OK' }));

    await waitFor(() => expect(updateMutate).toHaveBeenCalled());
    expect(screen.queryByText(t('checklists.saveFailed'))).not.toBeInTheDocument();
  });

  it('deletes a list naming the version it was shown at', async () => {
    show();
    fireEvent.click(screen.getByRole('button', { name: t('checklists.delete') }));
    fireEvent.click(await screen.findByRole('button', { name: 'OK' }));

    await waitFor(() => expect(deleteMutate).toHaveBeenCalledWith({ id: checklists[0].id, version: VERSION }));
  });

  it('does not delete a list somebody changed since the page was shown, and says so', async () => {
    readChecklist.mockResolvedValue({ ...checklists[0], updatedAt: '2026-08-23T08:00:00Z' });
    show();
    fireEvent.click(screen.getByRole('button', { name: t('checklists.delete') }));
    fireEvent.click(await screen.findByRole('button', { name: 'OK' }));

    expect(await screen.findByText(t('checklists.changedSinceShown'))).toBeInTheDocument();
    expect(deleteMutate).not.toHaveBeenCalled();
  });

  it('says why a delete was refused', async () => {
    deleteMutate.mockRejectedValue(new ApiError(403, 'acl.forbidden'));
    show();
    fireEvent.click(screen.getByRole('button', { name: t('checklists.delete') }));
    fireEvent.click(await screen.findByRole('button', { name: 'OK' }));

    expect(await screen.findByText(t('checklists.problems.forbidden'))).toBeInTheDocument();
  });

  /**
   * Handing one list to one person by name goes through the object-permissions dialog, behind a
   * lock on the card. The listing does not say who may manage each list's rules, so the lock is
   * drawn for the owner and for somebody who holds that right over every list, and for nobody
   * else — a dialog that opened only to be refused would be a worse answer than no lock.
   */
  it("opens the permissions dialog on the list from its owner's lock", () => {
    reader = OWNER;
    show();

    fireEvent.click(screen.getByTestId('checklist-permissions-11111111-1111-1111-1111-111111111111'));

    expect(permissionsModal).toHaveBeenCalledWith(
      expect.objectContaining({
        entityType: 'checklist',
        entityId: '11111111-1111-1111-1111-111111111111',
        open: true,
      }),
    );
  });

  it('draws the lock for somebody who manages every list, and for nobody else', () => {
    show();
    expect(screen.queryByTestId('checklist-permissions-11111111-1111-1111-1111-111111111111')).toBeNull();
    expect(permissionsModal).not.toHaveBeenCalled();

    cleanup();
    managesAnyList = true;
    show();
    expect(screen.getByTestId('checklist-permissions-11111111-1111-1111-1111-111111111111')).toBeTruthy();
  });
});
