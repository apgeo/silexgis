// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { EntityType } from '../../api/hooks.ts';
import { ACTION_ORDER } from './accessDisplay.ts';

const replace = vi.fn();
// The camp a cascade row names, as the reader's own read of it answers: a name when they may
// open it, nothing when it is not theirs to read.
let campRead: { data?: { id: string; name: string } } = { data: undefined };
let rules: {
  subjectKind: 'user' | 'cavingGroup';
  subjectId: string;
  subjectName: string | null;
  effect: 'allow' | 'deny';
  scopeKind: string;
  actions: string;
  grantedViaExpeditionId: string | null;
}[] = [];
// Whether the object's rules have come back yet; false is the moment before they do.
let rulesArrived = true;
// The accounts the reader's own lookup answers, by id. An id that is not here names nobody.
let members: Record<string, { id: string; label: string }> = {};
// Every id the dialog asked the directory about.
const memberLookups = vi.fn();

vi.mock('../../api/hooks.ts', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../api/hooks.ts')>();
  return {
    parseAccessActions: actual.parseAccessActions,
    useObjectAccess: () => ({ data: rulesArrived ? rules : undefined, isError: false }),
    // An empty id is the hook's own "ask nothing", exactly as the real one treats it.
    useMember: (id: string) => {
      if (id) {
        memberLookups(id);
      }
      return { data: id ? members[id] : undefined };
    },
    useReplaceObjectAccess: () => ({ mutateAsync: replace, isPending: false }),
    useCavingGroups: () => ({ data: [{ id: 'club-1', name: 'Speo Club' }] }),
    useEffectiveAccess: () => ({ data: undefined }),
    useUserSearch: () => ({ data: [] }),
    useExpedition: () => campRead,
  };
});

const { default: PermissionsModal } = await import('./PermissionsModal.tsx');

function dialog(entityType: EntityType, grantTo?: string) {
  return (
    <App>
      <MemoryRouter>
        <PermissionsModal
          entityType={entityType}
          entityId="trip-1"
          open
          onClose={() => {}}
          grantTo={grantTo}
        />
      </MemoryRouter>
    </App>
  );
}

function show(entityType: EntityType = 'tripLog', grantTo?: string) {
  return render(dialog(entityType, grantTo));
}

/** The dialog's own save button, which antd draws as the modal's OK. */
function saveButton() {
  return screen.getAllByRole('button', { name: /OK/i })[0];
}

beforeEach(() => {
  replace.mockReset();
  replace.mockResolvedValue(undefined);
  memberLookups.mockReset();
  rules = [];
  rulesArrived = true;
  members = {};
  campRead = { data: undefined };
});
afterEach(cleanup);

describe('PermissionsModal on a trip', () => {

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

    campRead = { data: { id: 'camp-1', name: 'Bihor summer camp' } };
    show();

    // Shown — the point of the list is that it is the whole list of who may read this trip —
    // and shown as somebody else's: it is changed where the camp is, and the save below
    // replaces only the rules written here, so offering it as editable would be a lie. The
    // mark names the camp and leads to it, which is where the rule is changed.
    const mark = screen.getByRole('link', { name: 'Granted through camp Bihor summer camp' });
    expect(mark.getAttribute('href')).toBe('/expeditions/camp-1');
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

  it('says only that a camp wrote a rule when the camp is not the reader\'s to open', () => {
    // A camp the reader may not open answers as one that does not exist, so a link would lead
    // to a page saying "no such camp": the mark then carries no name and no link.
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

    expect(screen.getByText('From a camp')).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /Granted through camp/ })).not.toBeInTheDocument();
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

/**
 * The dialog opened about one account — what an address in a message does, so that whoever is
 * told somebody cannot open a cave does not have to find that person by hand.
 *
 * Everything here turns on the difference between drafting and granting. The dialog may put a
 * row in its own table; nothing may reach the server until the person looking at it says so,
 * and what reaches it then is Read on this object and nothing they did not tick themselves.
 */
describe('PermissionsModal opened about an account', () => {
  const ana = '0b6f2c1e-5a51-4c0e-9d1b-3f6a8a2d7c11';
  const nobody = '7d9d1f0a-0c55-4b7e-8a43-5e1f2b3c4d5e';

  beforeEach(() => {
    members = { [ana]: { id: ana, label: 'Ana Pop' } };
  });

  /** One rule of the object's own for Ana, as the server sends it. */
  function anasRule(actions: string, scopeKind = 'object', grantedViaExpeditionId: string | null = null) {
    return {
      subjectKind: 'user' as const,
      subjectId: ana,
      subjectName: 'Ana Pop',
      effect: 'allow' as const,
      scopeKind,
      actions,
      grantedViaExpeditionId,
    };
  }

  /** The table rows that name a subject, header left out. */
  function rowsNaming(name: RegExp) {
    return screen.getAllByRole('row').filter((row) => name.test(row.textContent ?? '') && within(row).queryAllByRole('checkbox').length > 0);
  }

  /** Which actions a row has ticked, by name, in the order the columns are drawn. */
  function ticked(row: HTMLElement) {
    const boxes = within(row).getAllByRole('checkbox') as HTMLInputElement[];
    // A feature's row draws every action; the columns are the display order itself.
    expect(boxes).toHaveLength(ACTION_ORDER.length);
    return ACTION_ORDER.filter((_, index) => boxes[index].checked);
  }

  it('drafts Read on this object alone for the account, marks the row, and says why it is there', () => {
    show('feature', ana);

    const [row] = rowsNaming(/Ana Pop/);
    expect(within(row).getByText('proposed')).toBeInTheDocument();
    // Read and nothing else. In particular not the exact position of a protected cave, which
    // is a decision of its own and never one an address makes for the person reading it.
    expect(ticked(row)).toEqual(['read']);
    expect(within(row).getByText('This object only')).toBeInTheDocument();

    const why = screen.getByTestId('permissions-proposed');
    expect(why).toHaveTextContent('Ana Pop');
    expect(why).toHaveTextContent('Nothing is granted until you press OK');

    // Opening the dialog wrote nothing.
    expect(replace).not.toHaveBeenCalled();
  });

  it('grants when the reader confirms, and then what was proposed beside what was already there', async () => {
    rules = [
      {
        subjectKind: 'cavingGroup',
        subjectId: 'club-1',
        subjectName: 'Speo Club',
        effect: 'allow',
        scopeKind: 'object',
        actions: 'read, write',
        grantedViaExpeditionId: null,
      },
    ];
    show('feature', ana);

    fireEvent.click(saveButton());

    // One confirmation, one save: the drafted rule and the club's own, which a full replace
    // would otherwise have dropped.
    await vi.waitFor(() => expect(replace).toHaveBeenCalledTimes(1));
    expect(replace).toHaveBeenCalledWith([
      { subjectKind: 'user', subjectId: ana, effect: 'allow', actions: 'read', scopeKind: 'object' },
      { subjectKind: 'cavingGroup', subjectId: 'club-1', effect: 'allow', actions: 'read, write', scopeKind: 'object' },
    ]);
  });

  it('grants nothing once the drafted row is removed, and stops saying anything is proposed', async () => {
    show('feature', ana);

    const [row] = rowsNaming(/Ana Pop/);
    fireEvent.click(within(row).getByRole('button'));

    expect(rowsNaming(/Ana Pop/)).toHaveLength(0);
    expect(screen.queryByTestId('permissions-proposed')).not.toBeInTheDocument();

    fireEvent.click(saveButton());
    await vi.waitFor(() => expect(replace).toHaveBeenCalledTimes(1));
    expect(replace).toHaveBeenCalledWith([]);
  });

  it('drafts nothing for an id that names nobody, and says nothing about it', () => {
    show('feature', nobody);

    // The directory was asked, so the silence below is the dialog's own and not a lookup that
    // never happened.
    expect(memberLookups).toHaveBeenCalledWith(nobody);
    expect(screen.queryByText('proposed')).not.toBeInTheDocument();
    expect(screen.queryByTestId('permissions-proposed')).not.toBeInTheDocument();
    expect(screen.queryByTestId('permissions-proposed-already')).not.toBeInTheDocument();
    // Neither the id nor any word about an account that could not be found.
    expect(document.body.textContent).not.toContain(nobody);
    expect(rowsNaming(/./)).toHaveLength(0);
  });

  it('does not ask the directory about something that is not an account id', () => {
    show('feature', "ana' or 1=1 --");

    expect(memberLookups).not.toHaveBeenCalled();
    expect(screen.queryByText('proposed')).not.toBeInTheDocument();
  });

  it('drafts nothing when the account already has a rule here with Read, and says so', async () => {
    // The same message reaches the cave's owner and every full administrator, so the second
    // of them to follow the link finds the first one's rule already in force.
    rules = [anasRule('read')];
    show('feature', ana);

    expect(rowsNaming(/Ana Pop/)).toHaveLength(1);
    expect(screen.queryByText('proposed')).not.toBeInTheDocument();
    expect(screen.queryByTestId('permissions-proposed')).not.toBeInTheDocument();
    expect(screen.getByTestId('permissions-proposed-already')).toHaveTextContent('Ana Pop');

    fireEvent.click(saveButton());
    await vi.waitFor(() => expect(replace).toHaveBeenCalledTimes(1));
    expect(replace).toHaveBeenCalledWith([
      { subjectKind: 'user', subjectId: ana, effect: 'allow', actions: 'read', scopeKind: 'object' },
    ]);
  });

  it('counts a rule reaching everything inside this object as already giving Read on it', () => {
    rules = [anasRule('read, write', 'subtree')];
    show('feature', ana);

    expect(rowsNaming(/Ana Pop/)).toHaveLength(1);
    expect(screen.queryByText('proposed')).not.toBeInTheDocument();
    expect(screen.getByTestId('permissions-proposed-already')).toBeInTheDocument();
  });

  it('proposes Read on the rule the account already has here rather than a second one', async () => {
    // A subject has one rule per effect and reach; a second for the same account would be
    // refused as a duplicate and take the whole save with it.
    rules = [anasRule('write')];
    show('feature', ana);

    const rows = rowsNaming(/Ana Pop/);
    expect(rows).toHaveLength(1);
    expect(within(rows[0]).getByText('proposed')).toBeInTheDocument();
    expect(ticked(rows[0])).toEqual(['read', 'write']);
    expect(screen.getByTestId('permissions-proposed')).toBeInTheDocument();

    fireEvent.click(saveButton());
    await vi.waitFor(() => expect(replace).toHaveBeenCalledTimes(1));
    expect(replace).toHaveBeenCalledWith([
      { subjectKind: 'user', subjectId: ana, effect: 'allow', actions: 'read, write', scopeKind: 'object' },
    ]);
  });

  it('does not rely on a rule a camp wrote for the account', async () => {
    // A camp's rule is withdrawn from the camp, by somebody else, without this object being
    // asked. So the account is still drafted a rule of the object's own, and only that travels.
    rules = [anasRule('read', 'object', 'camp-1')];
    show('feature', ana);

    const rows = rowsNaming(/Ana Pop/);
    expect(rows).toHaveLength(2);
    expect(within(rows[0]).getByText('proposed')).toBeInTheDocument();
    expect(screen.queryByTestId('permissions-proposed-already')).not.toBeInTheDocument();

    fireEvent.click(saveButton());
    await vi.waitFor(() => expect(replace).toHaveBeenCalledTimes(1));
    expect(replace).toHaveBeenCalledWith([
      { subjectKind: 'user', subjectId: ana, effect: 'allow', actions: 'read', scopeKind: 'object' },
    ]);
  });

  it('drafts once the rules arrive when the account was known first', () => {
    rulesArrived = false;
    const view = show('feature', ana);
    expect(screen.queryByText('proposed')).not.toBeInTheDocument();

    rulesArrived = true;
    view.rerender(dialog('feature', ana));

    expect(rowsNaming(/Ana Pop/)).toHaveLength(1);
    expect(screen.getByTestId('permissions-proposed')).toBeInTheDocument();
  });

  it('adds the draft to what is on screen when the account arrives second', async () => {
    // The lookup is one request and the rules another, and either may land first. Whatever the
    // reader has already done to the table in between is theirs and stays.
    members = {};
    const view = show('feature', ana);

    fireEvent.mouseDown(screen.getAllByRole('combobox')[0]);
    fireEvent.click(await screen.findByTitle('Caving group'));
    fireEvent.mouseDown(screen.getAllByRole('combobox')[1]);
    fireEvent.click(await screen.findByTitle('Speo Club'));
    fireEvent.click(screen.getByRole('button', { name: /Add/i }));
    expect(rowsNaming(/Speo Club/)).toHaveLength(1);

    members = { [ana]: { id: ana, label: 'Ana Pop' } };
    view.rerender(dialog('feature', ana));

    expect(rowsNaming(/Ana Pop/)).toHaveLength(1);
    expect(rowsNaming(/Speo Club/)).toHaveLength(1);
  });

  it('drafts nothing on a dialog opened about nobody', () => {
    show('feature');

    expect(memberLookups).not.toHaveBeenCalled();
    expect(screen.queryByText('proposed')).not.toBeInTheDocument();
    expect(screen.queryByTestId('permissions-proposed')).not.toBeInTheDocument();
  });
});
