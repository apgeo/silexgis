// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { EntityType } from '../../api/hooks.ts';

const replace = vi.fn();
let rules: {
  subjectKind: 'user' | 'cavingGroup';
  subjectId: string;
  subjectName: string | null;
  effect: 'allow' | 'deny';
  scopeKind: string;
  actions: string;
  grantedViaExpeditionId: string | null;
}[] = [];

vi.mock('../../api/hooks.ts', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../api/hooks.ts')>();
  return {
    parseAccessActions: actual.parseAccessActions,
    useObjectAccess: () => ({ data: rules, isError: false }),
    useReplaceObjectAccess: () => ({ mutateAsync: replace, isPending: false }),
    useCavingGroups: () => ({ data: [{ id: 'club-1', name: 'Speo Club' }] }),
    useEffectiveAccess: () => ({ data: undefined }),
    useUserSearch: () => ({ data: [] }),
  };
});

const { default: PermissionsModal } = await import('./PermissionsModal.tsx');

function show(entityType: EntityType = 'tripLog') {
  return render(
    <App>
      <PermissionsModal entityType={entityType} entityId="trip-1" open onClose={() => {}} />
    </App>,
  );
}

/** The dialog's own save button, which antd draws as the modal's OK. */
function saveButton() {
  return screen.getAllByRole('button', { name: /OK/i })[0];
}

describe('PermissionsModal on a trip', () => {
  beforeEach(() => {
    replace.mockReset();
    replace.mockResolvedValue(undefined);
    rules = [];
  });
  afterEach(cleanup);

  it('offers this trip alone, because a trip contains nothing to reach into', () => {
    show();

    // The two reaches are a feature's, and only a feature's. Neither the picker above the
    // table nor a per-row control may offer to grant over "everything inside" a trip.
    expect(screen.queryByText('This object and everything inside')).not.toBeInTheDocument();
    expect(screen.queryByText('Applies to')).not.toBeInTheDocument();
    // Create belongs to that same reach, so its column is absent while Read is present.
    expect(screen.getAllByText('Read').length).toBeGreaterThan(0);
    expect(screen.queryByText('Create')).not.toBeInTheDocument();
  });

  it('names a group of accounts as one subject and a person as another', async () => {
    show();

    // A named list of people is N of the second kind rather than a list object of its own,
    // and a whole club is one of the first. Both are offered on a trip, unchanged from the
    // world this dialog came from.
    fireEvent.mouseDown(screen.getAllByRole('combobox')[0]);

    expect(await screen.findByText('Caving group')).toBeInTheDocument();
    expect(screen.getAllByText('User').length).toBeGreaterThan(0);
  });

  it('shows a rule a camp wrote, and neither lets it be changed nor sends it back', async () => {
    rules = [
      {
        subjectKind: 'cavingGroup',
        subjectId: 'club-1',
        subjectName: 'Speo Club',
        effect: 'allow',
        scopeKind: 'object',
        actions: 'read',
        grantedViaExpeditionId: 'camp-1',
      },
      {
        subjectKind: 'user',
        subjectId: 'user-9',
        subjectName: 'Ana',
        effect: 'allow',
        scopeKind: 'object',
        actions: 'read, write',
        grantedViaExpeditionId: null,
      },
    ];

    show();

    // Shown — the point of the list is that it is the whole list of who may read this trip —
    // and shown as somebody else's: it is changed where the camp is, and the save below
    // replaces only the rules written here, so offering it as editable would be a lie.
    expect(screen.getByText('From a camp')).toBeInTheDocument();
    const removeButtons = Array.from(document.querySelectorAll<HTMLButtonElement>('td button'));
    expect(removeButtons[0]).toBeDisabled();
    expect(removeButtons[1]).not.toBeDisabled();

    fireEvent.click(saveButton());

    // The rule the trip owns travels; the camp's does not, in the same save.
    await vi.waitFor(() => expect(replace).toHaveBeenCalledTimes(1));
    expect(replace).toHaveBeenCalledWith([
      { subjectKind: 'user', subjectId: 'user-9', effect: 'allow', actions: 'read, write', scopeKind: 'object' },
    ]);
  });

  it('lets a subject a camp already named be given a rule the trip owns itself', async () => {
    // The camp's rule is shown but is not this trip's to edit, so it must not stand in the way
    // of the trip granting the same club something itself — otherwise the club can be given no
    // right here at all, and the Add button simply does nothing.
    rules = [
      {
        subjectKind: 'cavingGroup',
        subjectId: 'club-1',
        subjectName: 'Speo Club',
        effect: 'allow',
        scopeKind: 'object',
        actions: 'read',
        grantedViaExpeditionId: 'camp-1',
      },
    ];

    show();

    // The table already names the camp's subject, so the kind is picked from the dropdown's own
    // option rather than by matching the word anywhere on screen.
    fireEvent.mouseDown(screen.getAllByRole('combobox')[0]);
    const kindOption = await screen.findByTitle('Caving group');
    fireEvent.click(kindOption);
    fireEvent.mouseDown(screen.getAllByRole('combobox')[1]);
    fireEvent.click(await screen.findByTitle('Speo Club'));
    fireEvent.click(screen.getByRole('button', { name: /Add/i }));

    fireEvent.click(saveButton());

    // One rule saved, and it is the trip's own — the camp's is still left where the camp is.
    await vi.waitFor(() => expect(replace).toHaveBeenCalledTimes(1));
    expect(replace).toHaveBeenCalledWith([
      { subjectKind: 'cavingGroup', subjectId: 'club-1', effect: 'allow', actions: 'read', scopeKind: 'object' },
    ]);
  });
});
