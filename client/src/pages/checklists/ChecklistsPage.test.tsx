// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';

const createMutate = vi.fn();
const updateMutate = vi.fn();
const deleteMutate = vi.fn();
const readForChange = vi.fn();
const refetchLists = vi.fn();
// What the page's list read answers: the lists, or a failure to read them.
let listRead: { data?: unknown; isLoading: boolean; isError?: boolean; error?: unknown } = { isLoading: false };
// What the permissions dialog was last handed; the dialog itself is somebody else's to test.
const permissionsModal = vi.fn();

const OWNER = '22222222-2222-2222-2222-222222222222';
const SOMEBODY_ELSE = '99999999-9999-9999-9999-999999999999';
// Who is reading the page, and whether they hold the right to manage every list's rules.
let reader = SOMEBODY_ELSE;
let managesAnyList = false;
// What the server says this reader may do to the list itself, for a list that is not theirs.
let heldOnList = 'read,write,delete';
const effectiveAccess = vi.fn();

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
    useChecklists: () => ({ ...listRead, refetch: refetchLists }),
    useReadChecklistForChange: () => readForChange,
    useCavingGroups: () => ({ data: [] }),
    useCreateChecklist: () => ({ mutateAsync: createMutate, isPending: false }),
    useUpdateChecklist: () => ({ mutateAsync: updateMutate, isPending: false }),
    useDeleteChecklist: () => ({ mutate: deleteMutate, isPending: false }),
    useMe: () => ({ data: { id: reader } }),
    useCan: () => managesAnyList,
    useEffectiveAccess: (entityType: string, id: string, options?: { enabled?: boolean }) => {
      effectiveAccess(entityType, id, options);
      return options?.enabled === false ? { data: undefined } : { data: { actions: heldOnList } };
    },
  };
});

vi.mock('../../components/permissions/PermissionsModal.tsx', () => ({
  default: (props: { entityType: string; entityId: string; open: boolean }) => {
    permissionsModal(props);
    return null;
  },
}));

const { default: ChecklistsPage } = await import('./ChecklistsPage.tsx');
const { ApiError } = await import('../../api/client.ts');

// The list as the server holds it now, read on its own when the page is about to change it; the
// version is what that read was answered with.
const heldNow = (overrides: Partial<(typeof checklists)[number]> = {}) => ({
  list: { ...checklists[0], ...overrides },
  version: '"5"',
});

beforeEach(() => {
  createMutate.mockReset().mockResolvedValue({});
  updateMutate.mockReset().mockResolvedValue({});
  deleteMutate.mockReset();
  readForChange.mockReset().mockResolvedValue(heldNow());
  refetchLists.mockReset();
  listRead = { data: checklists, isLoading: false };
  permissionsModal.mockReset();
  reader = SOMEBODY_ELSE;
  managesAnyList = false;
  heldOnList = 'read,write,delete';
  effectiveAccess.mockReset();
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

    // The dialog opens once the list has been read on its own.
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

  it('offers Edit and Delete only to somebody who may change the list, and always to its owner', () => {
    // A reader of a list published to everybody may read it and nothing more.
    heldOnList = 'read';
    show();
    expect(screen.queryByRole('button', { name: 'Edit' })).toBeNull();
    expect(screen.queryByRole('button', { name: 'Delete this checklist' })).toBeNull();
    expect(effectiveAccess).toHaveBeenCalledWith('checklist', checklists[0].id, expect.anything());

    // The list's owner, in the same test: the buttons are there.
    cleanup();
    reader = OWNER;
    show();
    expect(screen.getByRole('button', { name: 'Edit' })).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Delete this checklist' })).toBeTruthy();

    // Somebody handed the list to change but not to delete gets the one and not the other.
    cleanup();
    reader = SOMEBODY_ELSE;
    heldOnList = 'read,write';
    show();
    expect(screen.getByRole('button', { name: 'Edit' })).toBeTruthy();
    expect(screen.queryByRole('button', { name: 'Delete this checklist' })).toBeNull();
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
  /**
   * The page's listing carries no version per list, so the dialog reads the one list it is about
   * to rewrite and fills itself from that answer. The rewrite is whole — every line it does not
   * name is taken off — so a dialog filled from an older listing would quietly drop a line
   * somebody added since, and the version it carries has to be the one its lines came from.
   */
  it('fills the dialog from the list as held now and saves against that version', async () => {
    readForChange.mockResolvedValue(
      heldNow({
        items: [
          ...checklists[0].items,
          { id: '55555555-5555-5555-5555-555555555555', text: 'Added since', sortOrder: 2 },
        ],
      }),
    );
    show();
    fireEvent.click(screen.getByRole('button', { name: 'Edit' }));

    await screen.findByDisplayValue('Added since');
    expect(readForChange).toHaveBeenCalledWith('11111111-1111-1111-1111-111111111111');
    fireEvent.click(screen.getByRole('button', { name: 'OK' }));

    await waitFor(() => expect(updateMutate).toHaveBeenCalled());
    const { id, body, ifMatch } = updateMutate.mock.calls[0][0];
    expect(id).toBe('11111111-1111-1111-1111-111111111111');
    expect(ifMatch).toBe('"5"');
    expect(body.items.map((item: { id: string }) => item.id)).toContain(
      '55555555-5555-5555-5555-555555555555',
    );
  });

  it('says the list could not be opened when reading it fails, and opens nothing', async () => {
    readForChange.mockRejectedValue(new TypeError('Failed to fetch'));
    show();
    fireEvent.click(screen.getByRole('button', { name: 'Edit' }));

    expect(await screen.findByText('That checklist could not be opened. Try again.')).toBeTruthy();
    expect(screen.queryByText('Edit checklist')).toBeNull();
  });

  it('deletes against the version of the list the page showed', async () => {
    show();
    fireEvent.click(screen.getByRole('button', { name: 'Delete this checklist' }));
    fireEvent.click(await screen.findByRole('button', { name: 'OK' }));

    await waitFor(() => expect(deleteMutate).toHaveBeenCalled());
    expect(deleteMutate.mock.calls[0][0]).toEqual({
      id: '11111111-1111-1111-1111-111111111111',
      ifMatch: '"5"',
    });
  });

  it('deletes nothing when the list was changed after the page showed it', async () => {
    readForChange.mockResolvedValue(heldNow({ updatedAt: '2026-08-23T09:00:00Z' }));
    show();
    fireEvent.click(screen.getByRole('button', { name: 'Delete this checklist' }));
    fireEvent.click(await screen.findByRole('button', { name: 'OK' }));

    expect(
      await screen.findByText(
        'Somebody changed this checklist since the page showed it. Look at it again before deleting it.',
      ),
    ).toBeTruthy();
    expect(deleteMutate).not.toHaveBeenCalled();
    expect(refetchLists).toHaveBeenCalled();
  });

  it('says why a save was refused, in its own words', async () => {
    updateMutate.mockRejectedValue(
      new ApiError(403, 'access.caving_group_binding_forbidden', 'You may not bind this group.'),
    );
    show();
    fireEvent.click(screen.getByRole('button', { name: 'Edit' }));
    await screen.findByText('Edit checklist');
    fireEvent.click(screen.getByRole('button', { name: 'OK' }));

    expect(
      await screen.findByText('You may not hand a checklist to that caving group.'),
    ).toBeTruthy();
  });

  // Whether a new list may be made depends on the group chosen, so a refusal that said only
  // "could not be saved" would be retried as it stands and refused the same way each time.
  it('says a new list may not be made under the group chosen, rather than that it failed', async () => {
    createMutate.mockRejectedValue(
      new ApiError(403, 'access.create_forbidden', 'You may not create checklists here.'),
    );
    show();
    fireEvent.click(screen.getByRole('button', { name: /New checklist/ }));
    fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Winter trips' } });
    fireEvent.change(screen.getByPlaceholderText('Something to settle before setting off'), {
      target: { value: 'Callout arranged' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'OK' }));

    await waitFor(() => expect(createMutate).toHaveBeenCalled());
    expect(
      await screen.findByText(
        'You may not create a checklist for that caving group. Choose another group, or ask its administrator.',
      ),
    ).toBeTruthy();
  });

  it('says a save failed when the server could not be reached', async () => {
    updateMutate.mockRejectedValue(new TypeError('Failed to fetch'));
    show();
    fireEvent.click(screen.getByRole('button', { name: 'Edit' }));
    await screen.findByText('Edit checklist');
    fireEvent.click(screen.getByRole('button', { name: 'OK' }));

    expect(await screen.findByText('That checklist could not be saved.')).toBeTruthy();
  });

  /**
   * A stale version is worded once for every save, by the application's own conflict warning. A
   * page message beside it would contradict it, and closing the dialog would throw away what was
   * typed.
   */
  it('leaves a stale-version refusal to the conflict warning and keeps the dialog open', async () => {
    updateMutate.mockRejectedValue(new ApiError(412, 'concurrency.version_mismatch', 'Stale.'));
    show();
    fireEvent.click(screen.getByRole('button', { name: 'Edit' }));
    await screen.findByText('Edit checklist');
    fireEvent.click(screen.getByRole('button', { name: 'OK' }));

    await waitFor(() => expect(updateMutate).toHaveBeenCalled());
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(screen.queryByText('That checklist could not be saved.')).toBeNull();
    expect(screen.queryByText('Stale.')).toBeNull();
    expect(screen.getByText('Edit checklist')).toBeTruthy();
  });

  it('says a delete failed as a delete, not as a save', async () => {
    deleteMutate.mockImplementation((_variables, options: { onError: (error: unknown) => void }) =>
      options.onError(new TypeError('Failed to fetch')),
    );
    show();
    fireEvent.click(screen.getByRole('button', { name: 'Delete this checklist' }));
    fireEvent.click(await screen.findByRole('button', { name: 'OK' }));

    expect(await screen.findByText('That checklist could not be deleted.')).toBeTruthy();
    expect(screen.queryByText('That checklist could not be saved.')).toBeNull();
  });

  it('says a list somebody else deleted is gone, rather than that the delete failed', async () => {
    deleteMutate.mockImplementation((_variables, options: { onError: (error: unknown) => void }) =>
      options.onError(new ApiError(404, 'checklist.not_found', 'Not found.')),
    );
    show();
    fireEvent.click(screen.getByRole('button', { name: 'Delete this checklist' }));
    fireEvent.click(await screen.findByRole('button', { name: 'OK' }));

    expect(
      await screen.findByText('That checklist is no longer there, or you may no longer see it.'),
    ).toBeTruthy();
  });

  it('words a refused save itself rather than in the server prose, and keeps the editor open', async () => {
    updateMutate.mockRejectedValue(new ApiError(404, 'checklist.not_found', 'server prose'));
    show();
    fireEvent.click(screen.getByRole('button', { name: 'Edit' }));
    await screen.findAllByPlaceholderText('Something to settle before setting off');
    fireEvent.click(screen.getByRole('button', { name: 'OK' }));

    expect(
      await screen.findByText('That checklist is no longer there, or you may no longer see it.'),
    ).toBeTruthy();
    expect(screen.queryByText('server prose')).toBeNull();
    expect(screen.getAllByPlaceholderText('Something to settle before setting off')).toHaveLength(2);
  });

  it('says why a delete was refused', async () => {
    deleteMutate.mockImplementation((_variables, options: { onError: (error: unknown) => void }) =>
      options.onError(new ApiError(403, 'acl.forbidden')),
    );
    show();
    fireEvent.click(screen.getByRole('button', { name: 'Delete this checklist' }));
    fireEvent.click(await screen.findByRole('button', { name: 'OK' }));

    expect(await screen.findByText('You may not change this checklist.')).toBeTruthy();
  });

  it('leaves a delete refused as a conflict to the prompt every write shares', async () => {
    deleteMutate.mockImplementation((_variables, options: { onError: (error: unknown) => void }) =>
      options.onError(new ApiError(412, 'concurrency.version_mismatch', 'Stale.')),
    );
    show();
    fireEvent.click(screen.getByRole('button', { name: 'Delete this checklist' }));
    fireEvent.click(await screen.findByRole('button', { name: 'OK' }));

    await waitFor(() => expect(deleteMutate).toHaveBeenCalled());
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(screen.queryByText('That checklist could not be deleted.')).toBeNull();
    expect(screen.queryByText('Stale.')).toBeNull();
  });

  /** A list read that failed is not an installation with no lists. */
  it('shows a failed read as a failure with a retry, not as no lists', () => {
    listRead = { isLoading: false, isError: true };
    show();

    expect(screen.queryByText('No checklists yet.')).toBeNull();
    expect(screen.getByText('The checklists could not be loaded.')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
    expect(refetchLists).toHaveBeenCalled();
  });

  it('keeps the lists on screen when a later read fails, under a may-be-out-of-date notice', () => {
    listRead = { data: checklists, isLoading: false, isError: true, error: new TypeError('Failed to fetch') };
    show();

    expect(screen.queryByText('The checklists could not be loaded.')).toBeNull();
    expect(screen.getByText('Before we set off')).toBeTruthy();
    expect(screen.getByTestId('checklists-refresh-failed')).toBeTruthy();
  });
});

describe('leaving the checklist dialog with typed lines', () => {
  it('asks before throwing typed lines away, and a later opening starts with nothing typed', async () => {
    show();
    fireEvent.click(screen.getByRole('button', { name: /New checklist/ }));
    fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Winter trips' } });
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    fireEvent.click((await screen.findAllByTestId('discard-confirm')).at(-1)!);
    await waitFor(() => expect(document.querySelector('.ant-modal-wrap:not([style*="display: none"]) [data-testid="checklist-form"]')).toBeNull());

    // Opened again and left untouched, it closes without a question.
    const asked = screen.queryAllByTestId('discard-confirm').length;
    fireEvent.click(screen.getByRole('button', { name: /New checklist/ }));
    await screen.findByLabelText('Title');
    fireEvent.click(screen.getAllByRole('button', { name: 'Cancel' }).at(-1)!);
    expect(screen.queryAllByTestId('discard-confirm').length).toBe(asked);
    expect(createMutate).not.toHaveBeenCalled();
  });
});
