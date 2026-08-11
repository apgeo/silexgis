// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { ResLink, ResLinkMember, ResLinkRelationType } from '../../api/hooks.ts';

interface PanelPage {
  items: ResLink[];
  page: number;
  pageSize: number;
  totalItems: number;
}

let panel: PanelPage = { items: [], page: 1, pageSize: 200, totalItems: 0 };
let relationTypes: ResLinkRelationType[] = [];
const deleteMember = vi.fn();
const updateMember = vi.fn();
const askedFor: { relation?: string }[] = [];
let dialogProps: Record<string, unknown> = {};

vi.mock('../../api/hooks.ts', () => ({
  useResLinksForTarget: (
    _type: string,
    _id: string,
    params: { relation?: string },
  ) => {
    askedFor.push(params);
    return { data: panel };
  },
  useResLinkRelationTypes: () => ({ data: relationTypes }),
  useDeleteResLinkMember: () => ({ mutateAsync: deleteMember, isPending: false }),
  useUpdateResLinkMember: () => ({ mutateAsync: updateMember, isPending: false }),
  useMyPermissionGroups: () => ({ data: [] }),
}));

// The dialog is the existing add-a-member flow, tested where it lives. What matters here is
// which of its two authoring shapes this field opens it in — amend an existing link, or open
// a new one with the relation already decided.
vi.mock('./AddMemberModal.tsx', () => ({
  default: (props: Record<string, unknown>) => {
    dialogProps = props;
    return <div data-testid="add-dialog" />;
  },
}));

const { default: RoleField } = await import('./RoleField.tsx');

function member(overrides: Partial<ResLinkMember> = {}): ResLinkMember {
  return {
    id: 'm',
    targetType: 'feature',
    targetId: 'other',
    isMain: false,
    sortOrder: 0,
    note: null,
    anchorKind: 'whole',
    anchor: null,
    anchorFileId: null,
    anchorState: 'exact',
    display: { title: 'Other thing', subtitle: null, route: null, thumbnailUrl: null },
    ...overrides,
  };
}

function link(overrides: Partial<ResLink> = {}): ResLink {
  return {
    id: 'l1',
    shortCode: 'Ab3xY9Zq',
    relationType: {
      id: 7,
      code: 'trip-surveyed',
      name: 'Surveyed',
      description: null,
      sortOrder: 90,
      directed: true,
      inverseName: 'Surveyed on trip',
      seeded: true,
    },
    description: null,
    createdBy: 'somebody',
    mayEdit: true,
    createdAt: '2026-08-05T00:00:00Z',
    updatedAt: '2026-08-05T00:00:00Z',
    members: [member({ id: 'trip', targetType: 'tripLog', targetId: 'self', isMain: true })],
    ...overrides,
  };
}

function show(items: ResLink[], props: { canAdd?: boolean; showNotes?: boolean; showPath?: boolean } = {}, total?: number) {
  panel = { items, page: 1, pageSize: 200, totalItems: total ?? items.length };
  return render(
    <MemoryRouter>
      <App>
        <RoleField
          entityType="tripLog"
          entityId="self"
          entityTitle="Tura de sâmbătă"
          relationCode="trip-surveyed"
          label="Surveyed"
          {...props}
        />
      </App>
    </MemoryRouter>,
  );
}

describe('RoleField', () => {
  beforeEach(() => {
    deleteMember.mockReset().mockResolvedValue(undefined);
    updateMember.mockReset().mockResolvedValue(undefined);
    relationTypes = [
      {
        id: 7,
        code: 'trip-surveyed',
        name: 'Surveyed',
        description: null,
        sortOrder: 90,
        directed: true,
        inverseName: 'Surveyed on trip',
        seeded: true,
      },
    ];
    askedFor.length = 0;
    dialogProps = {};
  });

  afterEach(cleanup);

  it('asks for one role rather than reading every link and sorting them itself', () => {
    show([]);
    expect(askedFor[0]).toMatchObject({ relation: 'trip-surveyed' });
  });

  it('shows what two authors recorded separately as one role, not the first of them', () => {
    // Two links of the same relation on the same trip: nothing forbids it, and a field that
    // drew the first would hide the second author's work entirely.
    show([
      link({ id: 'l1', members: [member({ id: 'a', targetId: 'a', display: { title: 'Galeria A', subtitle: null, route: null, thumbnailUrl: null } })] }),
      link({ id: 'l2', members: [member({ id: 'b', targetId: 'b', display: { title: 'Galeria B', subtitle: null, route: null, thumbnailUrl: null } })] }),
    ]);

    expect(screen.getByText('Galeria A')).toBeInTheDocument();
    expect(screen.getByText('Galeria B')).toBeInTheDocument();
  });

  it('leaves out the entity the field belongs to, which speaks for itself', () => {
    show([link({ members: [member({ id: 'trip', targetType: 'tripLog', targetId: 'self', isMain: true }), member({ id: 'a' })] })]);

    expect(screen.getByText('Other thing')).toBeInTheDocument();
    expect(screen.queryByText('Tura de sâmbătă')).not.toBeInTheDocument();
  });

  it('reads a member it may not resolve as restricted, not as a failure', () => {
    show([link({ members: [member({ id: 'a', display: null }), member({ id: 'b' })] })]);

    // The withheld member is the disclosure rule working: it is named as restricted, beside
    // the sibling that was not withheld, and nothing about it is reported as an error.
    expect(screen.getByText('Restricted item')).toBeInTheDocument();
    expect(screen.getByText('Other thing')).toBeInTheDocument();
  });

  it('never puts an author’s note beside a chip whose target was withheld', () => {
    show(
      [link({ members: [member({ id: 'a', display: null, note: 'Continues north past the squeeze' })] })],
      { showNotes: true },
    );

    expect(screen.getByText('Restricted item')).toBeInTheDocument();
    expect(screen.queryByText(/Continues north/)).not.toBeInTheDocument();
  });

  it('shows the note a role is described by when the field asks for it', () => {
    show([link({ members: [member({ id: 'a', note: 'Continues north past the squeeze' })] })], {
      showNotes: true,
    });

    expect(screen.getByText('Continues north past the squeeze')).toBeInTheDocument();
  });

  it('writes a note in place, resending the marker and the order it must not disturb', async () => {
    show([link({ mayEdit: true, members: [member({ id: 'a', isMain: false, sortOrder: 3 })] })], {
      showNotes: true,
    });

    fireEvent.click(screen.getByLabelText('Describe what was left'));
    fireEvent.change(screen.getByRole('textbox'), {
      target: { value: 'Continues north past the squeeze' },
    });
    fireEvent.blur(screen.getByRole('textbox'));

    await vi.waitFor(() => expect(updateMember).toHaveBeenCalled());
    expect(updateMember).toHaveBeenCalledWith({
      id: 'l1',
      memberId: 'a',
      body: { isMain: false, sortOrder: 3, note: 'Continues north past the squeeze' },
    });
  });

  it('offers nobody but a curator the pen, and shows a reader no invitation to write', () => {
    show([link({ mayEdit: false, members: [member({ id: 'a', note: 'Continues north' })] })], {
      showNotes: true,
    });

    expect(screen.getByText('Continues north')).toBeInTheDocument();
    expect(screen.queryByLabelText('Describe what was left')).not.toBeInTheDocument();
    expect(screen.queryByText('Describe it')).not.toBeInTheDocument();
  });

  it('offers removal only where the link says this caller may curate it', () => {
    show([
      link({ id: 'l1', mayEdit: false, members: [member({ id: 'a' })] }),
      link({
        id: 'l2',
        mayEdit: true,
        members: [member({ id: 'b', display: { title: 'Mine to fix', subtitle: null, route: null, thumbnailUrl: null } })],
      }),
    ]);

    // One control, for the one link whose own answer admits it — not one per chip, and not
    // none because the caller happens not to have created either.
    expect(screen.getAllByLabelText('Remove from link')).toHaveLength(1);
  });

  it('removes a chip from its own link, which need not be its neighbour’s', async () => {
    show([
      link({ id: 'l1', mayEdit: false, members: [member({ id: 'a' })] }),
      link({
        id: 'l2',
        mayEdit: true,
        members: [member({ id: 'b', display: { title: 'Mine to fix', subtitle: null, route: null, thumbnailUrl: null } })],
      }),
    ]);

    fireEvent.click(screen.getByLabelText('Remove from link'));
    fireEvent.click(await screen.findByRole('button', { name: 'OK' }));
    await vi.waitFor(() => expect(deleteMember).toHaveBeenCalled());
    expect(deleteMember).toHaveBeenCalledWith({ id: 'l2', memberId: 'b' });
  });

  it('asks before striking a membership out, which is a hard delete of somebody’s record', async () => {
    // The close icon is a small target inside a chip that is often a navigation link, and
    // there is no undo — the link's own page guards the same act the same way.
    show([link({ id: 'l2', mayEdit: true, members: [member({ id: 'b' })] })]);

    fireEvent.click(screen.getByLabelText('Remove from link'));
    expect(await screen.findByText('Remove this item from the link?')).toBeInTheDocument();
    expect(deleteMember).not.toHaveBeenCalled();
  });

  it('says which of the identically-named passages a work area is', () => {
    // The whole reason the path is drawn: two caves of one massif each have a "Galeria
    // Mare", and the name on its own picks neither.
    show(
      [link({ members: [member({ id: 'a', display: {
        title: 'Galeria Mare',
        subtitle: 'Passage',
        route: null,
        thumbnailUrl: null,
        path: ['Piatra Craiului', 'Peștera Mare'],
      } })] })],
      { showPath: true },
    );

    expect(screen.getByText('Piatra Craiului › Peștera Mare ›')).toBeInTheDocument();
    expect(screen.getByText('Galeria Mare')).toBeInTheDocument();
  });

  it('leaves the path out of the fields whose names do not repeat', () => {
    show(
      [link({ members: [member({ id: 'a', display: {
        title: 'Galeria Mare',
        subtitle: null,
        route: null,
        thumbnailUrl: null,
        path: ['Piatra Craiului'],
      } })] })],
    );

    expect(screen.queryByText(/Piatra Craiului/)).not.toBeInTheDocument();
    expect(screen.getByText('Galeria Mare')).toBeInTheDocument();
  });

  it('records the first of a role by opening a link with the relation already decided', () => {
    show([], { canAdd: true });
    fireEvent.click(screen.getByText('Add'));

    // Nothing to amend, so a link is opened from the entity itself — and the relation is
    // not a question the recorder is asked, which is what keeps the entity the end it
    // reads from.
    expect(dialogProps.link).toBeNull();
    expect(dialogProps.origin).toMatchObject({ targetType: 'tripLog', targetId: 'self' });
    expect(dialogProps.relation).toMatchObject({ typeId: 7, directed: true });
  });

  it('records the next of a role into the link already carrying it', () => {
    show([link({ id: 'l2', mayEdit: true, members: [member({ id: 'a' })] })], { canAdd: true });
    fireEvent.click(screen.getByText('Add'));

    // A second target joins the link that already states this role rather than opening a
    // rival one, so the union stays one link wherever the rules allow it.
    expect((dialogProps.link as ResLink).id).toBe('l2');
    expect(dialogProps.origin).toBeUndefined();
  });

  it('opens a second link when the one recorded is not this caller’s to amend', () => {
    show([link({ id: 'l1', mayEdit: false, members: [member({ id: 'a' })] })], { canAdd: true });
    fireEvent.click(screen.getByText('Add'));

    expect(dialogProps.link).toBeNull();
    expect(dialogProps.origin).toMatchObject({ targetId: 'self' });
  });

  it('draws nothing at all for a role with nothing recorded and nothing to record with', () => {
    show([]);
    // Ten roles, most of them empty on most trips: a field with nothing in it and no way to
    // put anything there is a box that only makes the page harder to read.
    expect(screen.queryByTestId('role-field-trip-surveyed')).not.toBeInTheDocument();
    expect(screen.queryByText('Surveyed')).not.toBeInTheDocument();
  });

  it('says so when the server counted more links of this role than it returned', () => {
    show([link({ members: [member({ id: 'a' })] })], {}, 3);
    expect(screen.getByText(/Showing the first 1 of 3/)).toBeInTheDocument();
  });
});
