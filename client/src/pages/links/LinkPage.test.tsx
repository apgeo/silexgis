// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { ApiError } from '../../api/client.ts';
import type { ResLink, ResLinkMember, ResLinkRelationType } from '../../api/hooks.ts';

const contains: ResLinkRelationType = {
  id: 2,
  code: 'contains',
  name: 'Contains',
  description: null,
  sortOrder: 30,
  directed: true,
  inverseName: 'Contained in',
  seeded: true,
};
const relatedTo: ResLinkRelationType = {
  id: 3,
  code: 'related-to',
  name: 'Related to',
  description: null,
  sortOrder: 10,
  directed: false,
  inverseName: null,
  seeded: true,
};
let relationTypes: ResLinkRelationType[] = [];

let linkState: { data?: ResLink; isLoading: boolean; isError: boolean; error?: unknown } = {
  isLoading: true,
  isError: false,
};
let meId = 'me';
let myGroups: { id: string; name: string; slug: string; isProtected: boolean }[] = [];
let creator: { id: string; label: string } | undefined;
const updateLink = vi.fn();
const updateMember = vi.fn();

vi.mock('../../api/hooks.ts', () => ({
  useResLink: () => linkState,
  useMe: () => ({ data: { id: meId } }),
  useMember: () => ({ data: creator }),
  useMyPermissionGroups: () => ({ data: myGroups }),
  useUpdateResLink: () => ({ mutateAsync: updateLink, isPending: false }),
  useUpdateResLinkMember: () => ({ mutateAsync: updateMember, isPending: false }),
  useDeleteResLinkMember: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useResLinkRelationTypes: () => ({ data: relationTypes, isLoading: false }),
  useCreateResLink: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useDeleteResLink: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useAddResLinkMember: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useResLinkTargets: () => ({ data: [], isFetching: false }),
  useResLinkPointDefault: () => ({ data: undefined }),
}));

const { default: LinkPage } = await import('./LinkPage.tsx');

function member(overrides: Partial<ResLinkMember> = {}): ResLinkMember {
  return {
    id: 'm1',
    targetType: 'feature',
    targetId: 'f1',
    isMain: false,
    sortOrder: 0,
    note: null,
    anchorKind: 'whole',
    anchor: null,
    anchorFileId: null,
    anchorState: 'exact',
    display: { title: 'Peștera Mare', subtitle: 'Cave', route: '/caves/f1', thumbnailUrl: null },
    ...overrides,
  };
}

function link(overrides: Partial<ResLink> = {}): ResLink {
  return {
    id: 'l1',
    shortCode: 'Ab3xY9Zq',
    relationType: {
      id: 1,
      code: 'documents',
      name: 'Documented by',
      description: null,
      sortOrder: 40,
      directed: true,
      inverseName: 'Documents',
      seeded: true,
    },
    description: 'Recorded after the 1987 survey.',
    createdBy: 'me',
    createdAt: '2026-08-05T09:30:00Z',
    updatedAt: '2026-08-05T09:30:00Z',
    members: [member()],
    ...overrides,
  };
}

function show(path = '/links/Ab3xY9Zq') {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <App>
        <Routes>
          <Route path="/links/:code" element={<LinkPage />} />
        </Routes>
      </App>
    </MemoryRouter>,
  );
}

describe('LinkPage', () => {
  beforeEach(() => {
    meId = 'me';
    myGroups = [];
    creator = undefined;
    relationTypes = [];
    updateLink.mockReset().mockResolvedValue(link());
    updateMember.mockReset().mockResolvedValue({});
    linkState = { data: link(), isLoading: false, isError: false };
  });

  afterEach(cleanup);

  it('stands on its own: relation, description, address and when it was recorded', () => {
    show();
    expect(screen.getByText('Documented by')).toBeInTheDocument();
    expect(screen.getByText('Recorded after the 1987 survey.')).toBeInTheDocument();
    expect(screen.getByText(/\/links\/Ab3xY9Zq$/)).toBeInTheDocument();
    expect(screen.getByText(/2026-08-05 .*by you/)).toBeInTheDocument();
  });

  it('says a dead short code leads nowhere instead of showing an empty link', () => {
    linkState = {
      isLoading: false,
      isError: true,
      error: new ApiError(404, 'reslink.code.unresolved'),
    };
    show();
    expect(screen.getAllByText('This link address does not lead anywhere.').length).toBeGreaterThan(0);
  });

  it('groups members by kind and offers the target the server routed', () => {
    linkState = {
      data: link({
        members: [
          member(),
          member({
            id: 'm2',
            targetType: 'document',
            targetId: 'd1',
            sortOrder: 1,
            display: { title: 'Survey report', subtitle: 'Report', route: null, thumbnailUrl: null },
          }),
        ],
      }),
      isLoading: false,
      isError: false,
    };
    show();

    expect(screen.getByText('Feature')).toBeInTheDocument();
    expect(screen.getByText('Document')).toBeInTheDocument();
    // The feature has a route; the document has none, so no button pretends it does.
    const openButtons = screen.getAllByText('Open');
    expect(openButtons).toHaveLength(1);
    expect(openButtons[0].closest('a')).toHaveAttribute('href', '/caves/f1');
  });

  it('marks an anchor measured against an older version, and says nothing about an exact one', () => {
    linkState = {
      data: link({
        members: [
          member({
            id: 'm2',
            targetType: 'document',
            anchorKind: 'page',
            anchor: { page: 7 },
            anchorState: 'degraded',
            display: { title: 'Survey report', subtitle: null, route: null, thumbnailUrl: null },
          }),
        ],
      }),
      isLoading: false,
      isError: false,
    };
    show();

    expect(screen.getByText('p. 7')).toBeInTheDocument();
    expect(screen.getByText(/measured against an older version/i)).toBeInTheDocument();

    cleanup();
    linkState = { data: link(), isLoading: false, isError: false };
    show();
    expect(screen.queryByText(/measured against an older version/i)).not.toBeInTheDocument();
  });

  it('shows a member it may not name as restricted, with nothing that narrows it down', () => {
    linkState = {
      data: link({
        members: [member({ id: 'm2', note: 'the sensitive one', anchorKind: 'page', display: null })],
      }),
      isLoading: false,
      isError: false,
    };
    show();

    expect(screen.getByText('Restricted item')).toBeInTheDocument();
    expect(screen.queryByText('part')).not.toBeInTheDocument();
    // The note travels with a withheld member and must not be shown: an author's words
    // beside a nameless card narrow down what the hidden thing is.
    expect(screen.queryByText('the sensitive one')).not.toBeInTheDocument();
  });

  it('offers editing to the creator and to a full administrator, and to nobody else', () => {
    show();
    expect(screen.getByRole('button', { name: /Edit link/ })).toBeInTheDocument();

    cleanup();
    meId = 'someone-else';
    show();
    expect(screen.queryByRole('button', { name: /Edit link/ })).not.toBeInTheDocument();

    cleanup();
    myGroups = [{ id: 'g', name: 'Full Administrators', slug: 'full-administrators', isProtected: true }];
    show();
    expect(screen.getByRole('button', { name: /Edit link/ })).toBeInTheDocument();
  });

  it('names who recorded the link, not only when', () => {
    meId = 'someone-else';
    creator = { id: 'me', label: 'Ana Pop' };
    show();
    expect(screen.getByText(/2026-08-05 .*by Ana Pop/)).toBeInTheDocument();
  });

  it('does not wear a dead-address face when the read itself failed', () => {
    linkState = { isLoading: false, isError: true, error: new ApiError(500, undefined) };
    show();
    expect(screen.queryByText('This link address does not lead anywhere.')).not.toBeInTheDocument();
    expect(screen.getByText('Could not load this. Please try again.')).toBeInTheDocument();
  });

  it('moves the marker with the relation when a link stops reading one way', async () => {
    relationTypes = [contains, relatedTo];
    linkState = {
      data: link({
        members: [member({ isMain: true }), member({ id: 'm2', targetId: 'f2', sortOrder: 1 })],
      }),
      isLoading: false,
      isError: false,
    };
    show('/links/Ab3xY9Zq?edit=1');

    fireEvent.mouseDown(screen.getByLabelText('Relation'));
    fireEvent.click(screen.getAllByText('Related to').at(-1)!);
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    // A relation that reads both ways refuses to keep an end it reads from, so the marker
    // has to come off in the very act that changes the relation.
    await vi.waitFor(() => expect(updateLink).toHaveBeenCalled());
    expect(updateLink.mock.calls[0][0].body).toMatchObject({ relationTypeId: 3, mainMemberId: null });
  });

  it('will not save a one-way relation until it is told which end it reads from', async () => {
    relationTypes = [contains, relatedTo];
    linkState = {
      data: link({
        relationType: relatedTo,
        members: [
          member(),
          member({
            id: 'm2',
            targetId: 'f2',
            sortOrder: 1,
            display: { title: 'Survey report', subtitle: null, route: null, thumbnailUrl: null },
          }),
        ],
      }),
      isLoading: false,
      isError: false,
    };
    show('/links/Ab3xY9Zq?edit=1');

    fireEvent.mouseDown(screen.getByLabelText('Relation'));
    fireEvent.click(screen.getAllByText('Contains').at(-1)!);
    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled();

    fireEvent.mouseDown(screen.getByLabelText('Main member'));
    fireEvent.click(screen.getAllByText('Peștera Mare').at(-1)!);
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    await vi.waitFor(() => expect(updateLink).toHaveBeenCalled());
    expect(updateLink.mock.calls[0][0].body).toMatchObject({ relationTypeId: 2, mainMemberId: 'm1' });
  });

  it('moves a member by writing positions that differ, not by exchanging equal ones', async () => {
    linkState = {
      data: link({
        members: [
          member({ id: 'm1', sortOrder: 0 }),
          member({ id: 'm2', targetId: 'f2', sortOrder: 0 }),
        ],
      }),
      isLoading: false,
      isError: false,
    };
    show('/links/Ab3xY9Zq?edit=1');

    fireEvent.click(screen.getAllByRole('button', { name: 'Move up' }).at(-1)!);

    // Both members hold 0, so exchanging what they hold would move nothing at all.
    await vi.waitFor(() => expect(updateMember).toHaveBeenCalled());
    const written = updateMember.mock.calls.map(([call]) => [call.memberId, call.body.sortOrder]);
    expect(written).toContainEqual(['m1', 1]);
  });

  it('opens straight into edit mode when the panel sent the reader there to edit', () => {
    show('/links/Ab3xY9Zq?edit=1');
    expect(screen.getByText('Edit this link')).toBeInTheDocument();
  });

  it('keeps the editing controls out of the way until asked for', () => {
    show();
    expect(screen.queryByText('Edit this link')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Move up' })).not.toBeInTheDocument();
  });
});
