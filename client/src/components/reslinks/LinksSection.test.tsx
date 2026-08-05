// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { ResLink, ResLinkMember } from '../../api/hooks.ts';

interface PanelPage {
  items: ResLink[];
  page: number;
  pageSize: number;
  totalItems: number;
}

let panel: PanelPage = { items: [], page: 1, pageSize: 50, totalItems: 0 };

vi.mock('../../api/hooks.ts', () => ({
  useResLinksForTarget: () => ({ data: panel }),
  useMe: () => ({ data: { id: 'me' } }),
  useMyPermissionGroups: () => ({ data: [] }),
  useDeleteResLink: () => ({ mutateAsync: vi.fn(), isPending: false }),
}));

// Imported after the mock so the section binds to the stubbed queries.
const { default: LinksSection } = await import('./LinksSection.tsx');

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
      id: 1,
      code: 'documents',
      name: 'Documented by',
      description: null,
      sortOrder: 40,
      directed: true,
      inverseName: 'Documents',
      seeded: true,
    },
    description: null,
    createdBy: 'me',
    createdAt: '2026-08-05T00:00:00Z',
    updatedAt: '2026-08-05T00:00:00Z',
    members: [],
    ...overrides,
  };
}

function show(total: number, items: ResLink[], canAdd?: boolean) {
  panel = { items, page: 1, pageSize: 50, totalItems: total };
  return render(
    <MemoryRouter>
      <App>
        <LinksSection entityType="feature" entityId="self" canAdd={canAdd} entityTitle="This cave" />
      </App>
    </MemoryRouter>,
  );
}

describe('LinksSection', () => {
  beforeEach(() => {
    panel = { items: [], page: 1, pageSize: 50, totalItems: 0 };
  });

  // Renders are not torn down between cases here, and several of these assert on absence.
  afterEach(cleanup);

  it('renders a counted section with the relation phrase and the sibling members', () => {
    show(1, [
      link({
        members: [
          member({ id: 'self', targetId: 'self', isMain: true }),
          member({ id: 'm2', targetType: 'document', targetId: 'd1', anchorKind: 'page', anchor: { page: 7 },
            display: { title: 'Survey report', subtitle: 'Report', route: null, thumbnailUrl: null } }),
        ],
      }),
    ]);

    expect(screen.getByText('Links (1)')).toBeInTheDocument();
    // The page's own entity is the main member, so the relation reads forward from it.
    expect(screen.getByText('Documented by')).toBeInTheDocument();
    expect(screen.getByText('Survey report')).toBeInTheDocument();
    expect(screen.getByText('p. 7')).toBeInTheDocument();
    // The entity whose page this is does not repeat itself in its own row.
    expect(screen.queryByText('Other thing')).not.toBeInTheDocument();
  });

  it('shows nothing but the add action when the panel counts no links', () => {
    show(0, [], true);
    expect(screen.queryByText(/^Links \(/)).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Link/ })).toBeInTheDocument();
  });

  it('stays out of the way entirely when there is nothing to show and no way to add', () => {
    const { container } = show(0, []);
    expect(container.textContent).toBe('');
    expect(screen.queryByRole('button')).not.toBeInTheDocument();
  });

  it('renders a withheld member as an anonymous chip, with none of what still travels', () => {
    show(1, [
      link({
        relationType: null,
        members: [
          member({ id: 'self', targetId: 'self' }),
          // What the server sends for a target this caller may not read: no display, no
          // anchor payload — but the note and the anchor kind still arrive.
          member({
            id: 'm2',
            targetType: 'document',
            targetId: 'd1',
            note: 'the sensitive one',
            anchorKind: 'page',
            anchor: null,
            display: null,
          }),
        ],
      }),
    ]);

    expect(screen.getByText('Restricted item')).toBeInTheDocument();
    expect(screen.queryByText('the sensitive one')).not.toBeInTheDocument();
    expect(screen.queryByText('part')).not.toBeInTheDocument();
  });

  it('says so when it counted more links than the page it was given holds', () => {
    show(3, [link()]);
    expect(screen.getByText('Links (3)')).toBeInTheDocument();
    expect(screen.getByText('Showing the first 1 of 3.')).toBeInTheDocument();

    cleanup();
    show(1, [link()]);
    expect(screen.queryByText(/Showing the first/)).not.toBeInTheDocument();
  });

  it('collapses members past the fourth into a count that opens the link page', () => {
    show(1, [
      link({
        members: [
          member({ id: 'self', targetId: 'self' }),
          ...Array.from({ length: 6 }, (_, index) =>
            member({
              id: `m${index}`,
              targetId: `t${index}`,
              display: { title: `Thing ${index}`, subtitle: null, route: null, thumbnailUrl: null },
            }),
          ),
        ],
      }),
    ]);

    expect(screen.getByText('Thing 3')).toBeInTheDocument();
    expect(screen.queryByText('Thing 4')).not.toBeInTheDocument();
    const overflow = screen.getByText('+2');
    expect(overflow.closest('a')).toHaveAttribute('href', '/links/Ab3xY9Zq');
  });
});
