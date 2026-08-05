// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { ApiError } from '../../api/client.ts';
import type { ResLinkRelationType } from '../../api/hooks.ts';

const createMutate = vi.fn();
const updateMutate = vi.fn();
const deleteMutate = vi.fn();
let groups: { slug: string }[] = [{ slug: 'full-administrators' }];

const relationTypes: ResLinkRelationType[] = [
  {
    id: 1,
    code: 'contains',
    name: 'Contains',
    description: null,
    sortOrder: 30,
    directed: true,
    inverseName: 'Contained in',
    seeded: true,
  },
  {
    id: 7,
    // A row this installation added: no translation exists for its code, so the page has to
    // show the stored wording rather than a missing-key placeholder.
    code: 'resurveyed-by',
    name: 'Resurveyed by',
    description: 'The later survey that replaced this one',
    sortOrder: 100,
    directed: true,
    inverseName: 'Resurvey of',
    seeded: false,
  },
];

vi.mock('../../api/hooks.ts', () => ({
  useMe: () => ({ data: { id: 'me' } }),
  useMyPermissionGroups: () => ({ data: groups }),
  useResLinkRelationTypes: () => ({ data: relationTypes, isLoading: false }),
  useCreateResLinkRelationType: () => ({ mutateAsync: createMutate, isPending: false }),
  useUpdateResLinkRelationType: () => ({ mutateAsync: updateMutate, isPending: false }),
  useDeleteResLinkRelationType: () => ({ mutateAsync: deleteMutate, isPending: false }),
}));

const { default: RelationTypesPage } = await import('./RelationTypesPage.tsx');

function show() {
  return render(
    <App>
      <RelationTypesPage />
    </App>,
  );
}

describe('RelationTypesPage', () => {
  beforeEach(() => {
    groups = [{ slug: 'full-administrators' }];
    createMutate.mockReset().mockResolvedValue(undefined);
    updateMutate.mockReset().mockResolvedValue(undefined);
    deleteMutate.mockReset().mockResolvedValue(undefined);
  });

  afterEach(cleanup);

  it('translates the shipped vocabulary, shows a custom row as written, and edits only the custom one', () => {
    show();

    // The shipped row is rendered from its code in the reader's language, both readings.
    expect(screen.getByText('Contains')).toBeInTheDocument();
    expect(screen.getByText('Contained in')).toBeInTheDocument();
    // The installation's own row keeps the wording its author stored.
    expect(screen.getByText('Resurveyed by')).toBeInTheDocument();
    expect(screen.getByText('Resurvey of')).toBeInTheDocument();

    // Shipped rows are the exchange vocabulary: listed, labelled, and left alone.
    expect(screen.getByText('Not editable')).toBeInTheDocument();
    expect(screen.getAllByRole('button', { name: 'Edit' })).toHaveLength(1);
    expect(screen.getAllByRole('button', { name: 'Delete' })).toHaveLength(1);
  });

  it('offers nothing to change to a caller without the rank the server demands', () => {
    groups = [];
    show();

    expect(screen.getByText('Contains')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Add a relation' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Edit' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Delete' })).not.toBeInTheDocument();
  });

  it('records a new relation with both of its readings', async () => {
    show();
    fireEvent.click(screen.getByRole('button', { name: /Add a relation/ }));

    fireEvent.change(await screen.findByLabelText('Reads as'), { target: { value: 'Rigged by' } });
    fireEvent.change(screen.getByLabelText('Code'), { target: { value: ' rigged-by ' } });
    // The second reading only exists once the relation reads one way, so the box only
    // appears then — a relation with two identical ends has nothing to put in it.
    expect(screen.queryByLabelText('The other way round')).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('switch'));
    fireEvent.change(await screen.findByLabelText('The other way round'), {
      target: { value: 'Rigging for' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(createMutate).toHaveBeenCalled());
    expect(createMutate.mock.calls[0][0]).toMatchObject({
      name: 'Rigged by',
      code: 'rigged-by',
      directed: true,
      inverseName: 'Rigging for',
    });
  });

  it('sends a position for a relation whose position box was emptied', async () => {
    show();
    fireEvent.click(screen.getByRole('button', { name: /Add a relation/ }));

    fireEvent.change(await screen.findByLabelText('Reads as'), { target: { value: 'Rigged by' } });
    fireEvent.change(screen.getByLabelText('Code'), { target: { value: 'rigged-by' } });
    // An emptied number box reads back as null, which the wire has no room for: the position
    // is not nullable there, so a null would be refused while the body was still being read
    // and the administrator would be told only that something failed.
    fireEvent.change(screen.getByLabelText('Sort order'), { target: { value: '' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(createMutate).toHaveBeenCalled());
    expect(createMutate.mock.calls[0][0].sortOrder).toBe(0);
  });

  it('drops a second reading that the direction switch has just made meaningless', async () => {
    show();
    fireEvent.click(screen.getAllByRole('button', { name: 'Edit' })[0]);

    // The custom row arrives directed and carrying an inverse; turning the direction off
    // must send no inverse at all — the server refuses one, and keeping it in the form
    // would earn that refusal on the user's behalf.
    expect(await screen.findByLabelText('The other way round')).toHaveValue('Resurvey of');
    fireEvent.click(screen.getByRole('switch'));
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(updateMutate).toHaveBeenCalled());
    expect(updateMutate.mock.calls[0][0].id).toBe(7);
    expect(updateMutate.mock.calls[0][0].body).toMatchObject({
      directed: false,
      inverseName: null,
    });
  });

  it('says why a relation still in use cannot be deleted, rather than failing silently', async () => {
    deleteMutate.mockRejectedValue(new ApiError(409, 'reslink.relation.in_use'));
    show();

    fireEvent.click(screen.getByRole('button', { name: 'Delete' }));
    // The confirmation's own button carries the same word; the one in the popover is the
    // second to appear.
    const confirm = await screen.findAllByRole('button', { name: 'Delete' });
    fireEvent.click(confirm[confirm.length - 1]);

    expect(
      await screen.findByText(
        'Links already record this relation, so it cannot be changed that way or deleted.',
      ),
    ).toBeInTheDocument();
  });

  it('confirms a deletion that the server accepts', async () => {
    show();

    fireEvent.click(screen.getByRole('button', { name: 'Delete' }));
    const confirm = await screen.findAllByRole('button', { name: 'Delete' });
    fireEvent.click(confirm[confirm.length - 1]);

    await waitFor(() => expect(deleteMutate).toHaveBeenCalledWith(7));
  });
});
