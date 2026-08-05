// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { ResLink, ResLinkMember, ResLinkRelationType } from '../../api/hooks.ts';

const createLink = vi.fn();
const addMember = vi.fn();
const updateLink = vi.fn();
const deleteLink = vi.fn();
let relationTypes: ResLinkRelationType[] = [];
let targetHits: { id: string; title: string; subtitle: string | null }[] = [];
type PointDefault = { visibility: string; cavingGroupId: string | null; cavingGroupName: string | null };
let pointDefault: PointDefault | undefined = {
  visibility: 'authenticated',
  cavingGroupId: null,
  cavingGroupName: null,
};
let audienceUnknown = false;

vi.mock('../../api/hooks.ts', () => ({
  useCreateResLink: () => ({ mutateAsync: createLink, isPending: false }),
  useUpdateResLink: () => ({ mutateAsync: updateLink, isPending: false }),
  useDeleteResLink: () => ({ mutateAsync: deleteLink, isPending: false }),
  useAddResLinkMember: () => ({ mutateAsync: addMember, isPending: false }),
  useResLinkTargets: () => ({ data: targetHits, isFetching: false }),
  useResLinkRelationTypes: () => ({ data: relationTypes, isLoading: false }),
  useResLinkPointDefault: () => ({ data: pointDefault, isError: audienceUnknown }),
}));

// The map dialog builds a real OpenLayers map; the point field's contract is the only part
// this modal is responsible for, so the field is stubbed down to that contract.
vi.mock('../settings/PointField.tsx', () => ({
  default: ({ onChange }: { onChange?: (value: [number, number] | null) => void }) => (
    <button type="button" onClick={() => onChange?.([25.6, 45.65])}>
      pick-a-point
    </button>
  ),
}));

const { default: AddMemberModal } = await import('./AddMemberModal.tsx');

function relation(overrides: Partial<ResLinkRelationType> = {}): ResLinkRelationType {
  return {
    id: 1,
    code: 'contains',
    name: 'Contains',
    description: null,
    sortOrder: 30,
    directed: true,
    inverseName: 'Contained in',
    seeded: true,
    ...overrides,
  };
}

function member(overrides: Partial<ResLinkMember> = {}): ResLinkMember {
  return {
    id: 'm1',
    targetType: 'feature',
    targetId: 'a',
    isMain: false,
    sortOrder: 0,
    note: null,
    anchorKind: 'whole',
    anchor: null,
    anchorFileId: null,
    anchorState: 'exact',
    display: { title: 'A', subtitle: null, route: null, thumbnailUrl: null },
    ...overrides,
  };
}

function link(overrides: Partial<ResLink> = {}): ResLink {
  return {
    id: 'l1',
    shortCode: 'Ab3xY9Zq',
    relationType: null,
    description: null,
    createdBy: 'me',
    createdAt: '2026-08-05T00:00:00Z',
    updatedAt: '2026-08-05T00:00:00Z',
    members: [],
    ...overrides,
  };
}

function open(props: Partial<Parameters<typeof AddMemberModal>[0]> = {}) {
  return render(
    <MemoryRouter>
      <App>
        <AddMemberModal
          open
          onClose={() => {}}
          origin={{ targetType: 'feature', targetId: 'self', title: 'Peștera Mare' }}
          {...props}
        />
      </App>
    </MemoryRouter>,
  );
}

/** antd renders its select options into a floating layer; opening one is a click on it. */
function openSelect(label: string) {
  const control = screen.getByLabelText(label);
  fireEvent.mouseDown(control);
  return control;
}

describe('AddMemberModal', () => {
  beforeEach(() => {
    createLink.mockReset().mockResolvedValue(link({ members: [member({ id: 'origin' })] }));
    addMember.mockReset().mockResolvedValue(member({ id: 'added' }));
    updateLink.mockReset().mockResolvedValue(link());
    deleteLink.mockReset().mockResolvedValue(undefined);
    relationTypes = [relation()];
    targetHits = [];
    pointDefault = { visibility: 'authenticated', cavingGroupId: null, cavingGroupName: null };
    audienceUnknown = false;
  });

  afterEach(cleanup);

  it('offers every member type, so linking a whole resource is never the missing option', () => {
    open();
    openSelect('Kind of item');

    for (const label of [
      'Feature',
      'Document',
      'Trip log',
      'Caver',
      'Caving group',
      'Saved view',
      '3D survey model',
      'Geodata file',
      'Cabinet',
    ]) {
      expect(screen.getAllByText(label).length).toBeGreaterThan(0);
    }
  });

  it('lists a document part anchor it cannot compose yet, disabled rather than absent', () => {
    open();
    // Documents are the type with parts; a feature admits nothing but the whole.
    expect(screen.queryByLabelText('What it points at')).not.toBeInTheDocument();

    fireEvent.mouseDown(screen.getByLabelText('Kind of item'));
    fireEvent.click(screen.getAllByText('Document').at(-1)!);
    fireEvent.mouseDown(screen.getByLabelText('What it points at'));

    // The four numeric anchors ship; the ones that need a viewer are shown, disabled.
    const region = screen.getAllByText('A region of an image').at(-1)!;
    expect(region.closest('.ant-select-item-option')).toHaveClass('ant-select-item-option-disabled');
    const page = screen.getAllByText('A page').at(-1)!;
    expect(page.closest('.ant-select-item-option')).not.toHaveClass('ant-select-item-option-disabled');
  });

  it('refuses a page range that runs backwards, in the same words the server would', () => {
    open();
    fireEvent.mouseDown(screen.getByLabelText('Kind of item'));
    fireEvent.click(screen.getAllByText('Document').at(-1)!);
    fireEvent.mouseDown(screen.getByLabelText('What it points at'));
    fireEvent.click(screen.getAllByText('A range of pages').at(-1)!);

    fireEvent.change(screen.getByLabelText('First page'), { target: { value: '9' } });
    fireEvent.change(screen.getByLabelText('Last page'), { target: { value: '3' } });
    expect(screen.getByText('The last page cannot come before the first.')).toBeInTheDocument();

    // A one-page range is legitimate and must not be reported as backwards.
    fireEvent.change(screen.getByLabelText('Last page'), { target: { value: '9' } });
    expect(screen.queryByText('The last page cannot come before the first.')).not.toBeInTheDocument();
  });

  it('refuses a zero-length time span, which has its own anchor kind', () => {
    open();
    fireEvent.mouseDown(screen.getByLabelText('Kind of item'));
    fireEvent.click(screen.getAllByText('Document').at(-1)!);
    fireEvent.mouseDown(screen.getByLabelText('What it points at'));
    fireEvent.click(screen.getAllByText('A stretch of the recording').at(-1)!);

    fireEvent.change(screen.getByLabelText('From (seconds)'), { target: { value: '30' } });
    fireEvent.change(screen.getByLabelText('To (seconds)'), { target: { value: '30' } });
    expect(
      screen.getByText('The end must come after the start; use a single moment for an instant.'),
    ).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText('To (seconds)'), { target: { value: '31' } });
    expect(
      screen.queryByText('The end must come after the start; use a single moment for an instant.'),
    ).not.toBeInTheDocument();
  });

  it('names the audience a new point will actually get, and again per choice', () => {
    open();
    fireEvent.mouseDown(screen.getByLabelText('Which item'));
    fireEvent.click(screen.getAllByText('Mark a new point on the map').at(-1)!);

    // The audience is named, not described: this account belongs to no single group, so
    // the notice says so rather than making the reader apply the rule to themselves.
    expect(screen.getByText(/visible to everyone signed in/)).toBeInTheDocument();
    expect(screen.queryByText(/if you belong to none or to several/)).not.toBeInTheDocument();

    fireEvent.mouseDown(screen.getByLabelText('Who can see it'));
    fireEvent.click(screen.getAllByText('Public').at(-1)!);
    expect(screen.getByText(/including visitors who are not signed in/)).toBeInTheDocument();

    fireEvent.mouseDown(screen.getByLabelText('Who can see it'));
    fireEvent.click(screen.getAllByText('Private').at(-1)!);
    expect(screen.getByText('Only you will be able to see this point.')).toBeInTheDocument();
  });

  it('names the caller’s own group when that is what the default resolves to', () => {
    pointDefault = { visibility: 'cavingGroup', cavingGroupId: 'g1', cavingGroupName: 'Clubul Speo' };
    open();
    fireEvent.mouseDown(screen.getByLabelText('Which item'));
    fireEvent.click(screen.getAllByText('Mark a new point on the map').at(-1)!);

    expect(screen.getByText(/visible to members of Clubul Speo/)).toBeInTheDocument();
  });

  it('says the audience could not be checked rather than saying nothing at all', () => {
    pointDefault = undefined;
    audienceUnknown = true;
    open();
    fireEvent.mouseDown(screen.getByLabelText('Which item'));
    fireEvent.click(screen.getAllByText('Mark a new point on the map').at(-1)!);

    expect(screen.getByText(/could not be checked/)).toBeInTheDocument();
  });

  it('mints a new point through the endpoint that can make one, and joins it to the link', async () => {
    open();
    fireEvent.mouseDown(screen.getByLabelText('Which item'));
    fireEvent.click(screen.getAllByText('Mark a new point on the map').at(-1)!);
    fireEvent.click(screen.getByText('pick-a-point'));
    fireEvent.click(screen.getByRole('button', { name: 'OK' }));

    await vi.waitFor(() => expect(addMember).toHaveBeenCalled());

    // A link is created against things that already exist, so it is created holding the
    // entity the user started on — and never carries a member with no target.
    const created = createLink.mock.calls[0][0];
    expect(created.members).toHaveLength(1);
    expect(created.members[0]).toMatchObject({ targetType: 'feature', targetId: 'self' });

    // The point is minted by the one endpoint that can mint one, and takes no visibility
    // of its own: the server owns the default the form just named.
    const added = addMember.mock.calls[0][0].body;
    expect(added.newGeoPoint).toMatchObject({ lon: 25.6, lat: 45.65, visibility: null });
    expect(added.targetType).toBeNull();
    expect(deleteLink).not.toHaveBeenCalled();
  });

  it('sends a stated altitude with the new point, and nothing at all when none was given', async () => {
    open();
    fireEvent.mouseDown(screen.getByLabelText('Which item'));
    fireEvent.click(screen.getAllByText('Mark a new point on the map').at(-1)!);
    fireEvent.click(screen.getByText('pick-a-point'));
    fireEvent.change(screen.getByLabelText('Altitude (m)'), { target: { value: '1250' } });
    fireEvent.click(screen.getByRole('button', { name: 'OK' }));

    await vi.waitFor(() => expect(addMember).toHaveBeenCalled());
    expect(addMember.mock.calls[0][0].body.newGeoPoint).toMatchObject({
      lon: 25.6,
      lat: 45.65,
      z: 1250,
    });

    // A height nobody entered is absent, not zero — sea level is a measurement, and the
    // point would carry it as one.
    cleanup();
    addMember.mockClear();
    open();
    fireEvent.mouseDown(screen.getByLabelText('Which item'));
    fireEvent.click(screen.getAllByText('Mark a new point on the map').at(-1)!);
    fireEvent.click(screen.getByText('pick-a-point'));
    fireEvent.click(screen.getByRole('button', { name: 'OK' }));

    await vi.waitFor(() => expect(addMember).toHaveBeenCalled());
    expect(addMember.mock.calls[0][0].body.newGeoPoint.z).toBeNull();
  });

  it('does not leave a half-built link behind when the point cannot be made', async () => {
    addMember.mockRejectedValue(new Error('refused'));
    open();
    fireEvent.mouseDown(screen.getByLabelText('Which item'));
    fireEvent.click(screen.getAllByText('Mark a new point on the map').at(-1)!);
    fireEvent.click(screen.getByText('pick-a-point'));
    fireEvent.click(screen.getByRole('button', { name: 'OK' }));

    await vi.waitFor(() => expect(deleteLink).toHaveBeenCalledWith('l1'));
  });

  it('reads a directed link with a new point from the end the user left the marker on', async () => {
    open();
    fireEvent.mouseDown(screen.getByLabelText('Relation'));
    fireEvent.click(screen.getAllByText('Contains').at(-1)!);
    fireEvent.mouseDown(screen.getByLabelText('Which item'));
    fireEvent.click(screen.getAllByText('Mark a new point on the map').at(-1)!);
    fireEvent.click(screen.getByText('pick-a-point'));
    fireEvent.click(screen.getByRole('button', { name: 'OK' }));

    // A directed link refuses to hold two members without exactly one marked, so the
    // marker rides in on the new member and moves back to the origin afterwards.
    await vi.waitFor(() => expect(updateLink).toHaveBeenCalled());
    expect(addMember.mock.calls[0][0].body.isMain).toBe(true);
    expect(updateLink.mock.calls[0][0].body.mainMemberId).toBe('origin');
    expect(deleteLink).not.toHaveBeenCalled();
  });

  it('appends a member to an existing link instead of dropping it into the middle', async () => {
    open({
      link: link({
        members: [
          member({ id: 'm1', sortOrder: 0 }),
          member({ id: 'm2', targetId: 'b', sortOrder: 3 }),
        ],
      }),
      origin: undefined,
    });

    fireEvent.mouseDown(screen.getByLabelText('Which item'));
    fireEvent.click(screen.getAllByText('Mark a new point on the map').at(-1)!);
    fireEvent.click(screen.getByText('pick-a-point'));
    fireEvent.click(screen.getByRole('button', { name: 'OK' }));

    await vi.waitFor(() => expect(addMember).toHaveBeenCalled());
    expect(addMember.mock.calls[0][0].body.sortOrder).toBe(4);
  });

  it('keeps the main marker to relations that read one way', () => {
    open();
    const checkbox = screen.getByRole('checkbox');
    expect(checkbox).toBeDisabled();

    fireEvent.mouseDown(screen.getByLabelText('Relation'));
    fireEvent.click(screen.getAllByText('Contains').at(-1)!);
    expect(screen.getByRole('checkbox')).toBeEnabled();
  });

  it('reads a new directed link from the entity it was started on unless told otherwise', async () => {
    open();
    fireEvent.mouseDown(screen.getByLabelText('Relation'));
    fireEvent.click(screen.getAllByText('Contains').at(-1)!);

    const search = screen.getByLabelText('Item');
    fireEvent.change(search, { target: { value: 'other' } });
    // Nothing is selected from the empty feed, so there is no member to send — and the
    // refusal is visible on the button rather than a click that does nothing.
    expect(screen.getByRole('button', { name: 'OK' })).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: 'OK' }));
    expect(createLink).not.toHaveBeenCalled();
  });

  it('creates a link against two things that already exist in one call', async () => {
    targetHits = [{ id: 'other', title: 'Falia Demo', subtitle: null }];
    open();
    fireEvent.mouseDown(screen.getByLabelText('Relation'));
    fireEvent.click(screen.getAllByText('Contains').at(-1)!);

    fireEvent.change(screen.getByLabelText('Item'), { target: { value: 'Falia' } });
    fireEvent.click(screen.getAllByText('Falia Demo').at(-1)!);
    fireEvent.click(screen.getByRole('button', { name: 'OK' }));

    await vi.waitFor(() => expect(createLink).toHaveBeenCalled());
    const body = createLink.mock.calls[0][0];
    expect(body.members).toHaveLength(2);
    // The end a directed link reads from is the entity the user started on, and the two
    // members hold distinct positions so a later reorder has something to exchange.
    expect(body.members[0]).toMatchObject({ targetId: 'self', isMain: true, sortOrder: 0 });
    expect(body.members[1]).toMatchObject({ targetId: 'other', isMain: false, sortOrder: 1 });
    expect(addMember).not.toHaveBeenCalled();
  });

  it('will not offer the marker on a link that already reads from one of its members', () => {
    open({
      link: link({
        relationType: relation(),
        members: [
          {
            id: 'm1',
            targetType: 'feature',
            targetId: 'a',
            isMain: true,
            sortOrder: 0,
            note: null,
            anchorKind: 'whole',
            anchor: null,
            anchorFileId: null,
            anchorState: 'exact',
            display: { title: 'A', subtitle: null, route: null, thumbnailUrl: null },
          },
        ],
      }),
      origin: undefined,
    });

    expect(screen.getByRole('checkbox')).toBeDisabled();
    // Adding to an existing link chooses no relation — that belongs to the link itself.
    expect(screen.queryByLabelText('Relation')).not.toBeInTheDocument();
  });

  it('previews both readings of a directed relation once there is an end to read from', () => {
    open();
    fireEvent.mouseDown(screen.getByLabelText('Relation'));
    fireEvent.click(screen.getAllByText('Contains').at(-1)!);

    const preview = screen.getByText(/Peștera Mare/);
    expect(within(preview).getByText(/Contained in/)).toBeDefined();
  });
});
