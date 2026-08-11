// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { ResLink } from '../../api/hooks.ts';

let items: ResLink[] = [];

vi.mock('../../api/hooks.ts', () => ({
  useResLinksForTarget: () => ({
    data: { items, page: 1, pageSize: 200, totalItems: items.length },
  }),
}));

// The field itself is tested where it lives. What this file is about is which of the ten
// are on the page at all, and what each one is told to be.
vi.mock('../../components/reslinks/RoleField.tsx', () => ({
  default: ({ relationCode, label, showNotes }: Record<string, unknown>) => (
    <div data-testid={`field-${String(relationCode)}`} data-notes={String(Boolean(showNotes))}>
      {String(label)}
    </div>
  ),
}));

const { default: TripRoleFields } = await import('./TripRoleFields.tsx');

function roleLink(code: string): ResLink {
  return {
    id: code,
    shortCode: 'Ab3xY9Zq',
    relationType: {
      id: 1,
      code,
      name: code,
      description: null,
      sortOrder: 10,
      directed: true,
      inverseName: null,
      seeded: true,
    },
    description: null,
    createdBy: null,
    mayEdit: true,
    createdAt: '2026-08-05T00:00:00Z',
    updatedAt: '2026-08-05T00:00:00Z',
    members: [],
  };
}

function show(canEdit: boolean) {
  return render(
    <MemoryRouter>
      <App>
        <TripRoleFields tripId="trip" tripTitle="Tura de sâmbătă" canEdit={canEdit} />
      </App>
    </MemoryRouter>,
  );
}

describe('TripRoleFields', () => {
  beforeEach(() => {
    items = [];
  });

  afterEach(cleanup);

  it('opens on the two fields a trip report is expected to state, and no more', () => {
    // The complaint this answers is a wall of empty boxes: ten of them is the difference
    // between a report written and a report not written.
    show(true);

    expect(screen.getByTestId('field-trip-work-area')).toBeInTheDocument();
    expect(screen.getByTestId('field-trip-objective')).toBeInTheDocument();
    expect(screen.queryByTestId('field-trip-dug')).not.toBeInTheDocument();
    expect(screen.queryByTestId('field-trip-lead')).not.toBeInTheDocument();
  });

  it('shows a role that holds something without being asked to', () => {
    items = [roleLink('trip-dug')];
    show(true);

    expect(screen.getByTestId('field-trip-dug')).toBeInTheDocument();
  });

  it('opens any other role from one control rather than standing them all up empty', () => {
    show(true);
    fireEvent.click(screen.getByText('Record something else'));
    fireEvent.click(screen.getByText('Leads left'));

    expect(screen.getByTestId('field-trip-lead')).toBeInTheDocument();
  });

  it('reads the ten in report order: where it worked, what it did, what it left', () => {
    items = ['trip-visited', 'trip-lead', 'trip-follows-on-from'].map(roleLink);
    show(true);

    const drawn = screen
      .getAllByTestId(/^field-/)
      .map((node) => node.dataset.testid?.replace('field-', ''));
    expect(drawn).toEqual([
      'trip-work-area',
      'trip-objective',
      'trip-visited',
      'trip-lead',
      'trip-follows-on-from',
    ]);
  });

  it('gives the lead field the note the others do not have', () => {
    items = [roleLink('trip-lead'), roleLink('trip-visited')];
    show(true);

    // A lead is a described continuation — the name of a cave alone would not tell the next
    // party where to go — while the rest name a thing and mean it.
    expect(screen.getByTestId('field-trip-lead').dataset.notes).toBe('true');
    expect(screen.getByTestId('field-trip-visited').dataset.notes).toBe('false');
  });

  it('shows a reader the roles that hold something and nothing else', () => {
    items = [roleLink('trip-surveyed')];
    show(false);

    // The two standing fields are an invitation to record; somebody who may not record has
    // no use for an invitation, and an empty labelled row tells them nothing.
    expect(screen.getByTestId('field-trip-surveyed')).toBeInTheDocument();
    expect(screen.queryByTestId('field-trip-work-area')).not.toBeInTheDocument();
    expect(screen.queryByText('Record something else')).not.toBeInTheDocument();
  });

  it('draws no section at all on a trip that recorded no role and a reader who may add none', () => {
    show(false);
    expect(screen.queryByText('What this trip did')).not.toBeInTheDocument();
  });
});
