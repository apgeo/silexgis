// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { ExpeditionLeads } from '../../api/hooks.ts';

const { leadsSpy } = vi.hoisted(() => ({ leadsSpy: vi.fn() }));

vi.mock('../../api/hooks.ts', () => ({ useExpeditionLeads: () => leadsSpy() }));

const { default: ExpeditionLeadsTab } = await import('./ExpeditionLeadsTab.tsx');

const CAMP = '11111111-2222-3333-4444-555555555555';

function board(overrides: Partial<ExpeditionLeads> = {}): ExpeditionLeads {
  return {
    expeditionId: CAMP,
    groups: [
      {
        state: 'open',
        leads: [
          { id: 'lead-1', name: 'Windy crawl', grade: 'A', note: 'Needs two people and a hammer.' },
          { id: 'lead-2', name: 'Sandy tube', grade: null, note: null },
        ],
      },
      { state: 'dead-end', leads: [{ id: 'lead-3', name: 'Boulder choke', grade: 'D', note: null }] },
    ],
    leads: 3,
    truncated: false,
    ...overrides,
  };
}

function show() {
  return render(
    <MemoryRouter>
      <ExpeditionLeadsTab expeditionId={CAMP} />
    </MemoryRouter>,
  );
}

afterEach(cleanup);
beforeEach(() => leadsSpy.mockReturnValue({ data: board(), isPending: false, error: null }));

describe('what a camp left open', () => {
  it('shows each way on under the state it is in, as the server grouped them', () => {
    show();

    const open = screen.getByTestId('expedition-leads-group-open');
    expect(within(open).getByText('Windy crawl')).toBeTruthy();
    expect(within(open).getByText('Sandy tube')).toBeTruthy();
    expect(within(open).getByText('Needs two people and a hammer.')).toBeTruthy();

    const dead = screen.getByTestId('expedition-leads-group-dead-end');
    expect(within(dead).getByText('Boulder choke')).toBeTruthy();

    // The count is what came back, not what the client worked out from the rows: a board is
    // answered as the reader may see it, and a second sum here could disagree with the first.
    expect(screen.getByTestId('expedition-leads-count').textContent).toContain('3');
  });

  it('shows how promising a graded lead looked and says nothing about an ungraded one', () => {
    show();

    expect(screen.getByTestId('expedition-lead-grade-lead-1').textContent).toContain('A');
    expect(screen.queryByTestId('expedition-lead-grade-lead-2')).toBeNull();
  });

  /**
   * A camp with nothing open and a camp whose leads this reader may not be told about look the
   * same from here, on purpose — so the pane says which of the two it might be rather than
   * leaving a blank that reads as a page that failed to load.
   */
  it('says a camp left nothing open rather than drawing an empty pane', () => {
    leadsSpy.mockReturnValue({
      data: board({ groups: [], leads: 0 }),
      isPending: false,
      error: null,
    });
    show();

    expect(screen.getByTestId('expedition-leads-empty')).toBeTruthy();
    expect(screen.queryByTestId('expedition-leads-count')).toBeNull();
  });

  it('says so when the camp has more leads than the board carries', () => {
    leadsSpy.mockReturnValue({
      data: board({ truncated: true }),
      isPending: false,
      error: null,
    });
    show();

    expect(screen.getByTestId('expedition-leads-truncated')).toBeTruthy();
  });

  /**
   * An installation may edit the vocabulary this kind of place carries. A state this client ships
   * no wording for is shown as the word it was stored as — dropping it would take the lead off the
   * board with it, and a lead missing from the only list that would show it is missed by nobody.
   */
  it('shows a state it has no wording for as the word it was stored as', () => {
    leadsSpy.mockReturnValue({
      data: board({
        groups: [{ state: 'flooded', leads: [{ id: 'lead-9', name: 'Sump', grade: null, note: null }] }],
        leads: 1,
      }),
      isPending: false,
      error: null,
    });
    show();

    const group = screen.getByTestId('expedition-leads-group-flooded');
    expect(within(group).getByText('flooded')).toBeTruthy();
  });

  it('groups the leads nobody recorded a state for under a heading of their own', () => {
    leadsSpy.mockReturnValue({
      data: board({
        groups: [{ state: null, leads: [{ id: 'lead-8', name: 'Draughting slot', grade: null, note: null }] }],
        leads: 1,
      }),
      isPending: false,
      error: null,
    });
    show();

    expect(screen.getByTestId('expedition-leads-group-unrecorded')).toBeTruthy();
  });

  it('says the leads could not be read rather than showing an empty board', () => {
    leadsSpy.mockReturnValue({ data: undefined, isPending: false, error: new Error('nope') });
    show();

    expect(screen.queryByTestId('expedition-leads-tab')).toBeNull();
  });
});
