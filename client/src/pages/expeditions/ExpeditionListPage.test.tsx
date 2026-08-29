// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { ExpeditionInfo, ExpeditionListParams } from '../../api/hooks.ts';

const { listSpy } = vi.hoisted(() => ({ listSpy: vi.fn() }));

vi.mock('../../api/hooks.ts', () => ({
  useExpeditions: (params: ExpeditionListParams) => listSpy(params),
}));

const { default: ExpeditionListPage } = await import('./ExpeditionListPage.tsx');

function camp(overrides: Partial<ExpeditionInfo> = {}): ExpeditionInfo {
  return {
    id: '11111111-1111-1111-1111-111111111111',
    name: 'Bihor summer camp',
    description: null,
    startDate: '2026-07-18',
    endDate: '2026-08-01',
    geom: null,
    ownerUserId: null,
    cavingGroupId: null,
    visibility: 'private',
    state: 'planned',
    publishedAt: null,
    createdAt: '2026-07-01T00:00:00Z',
    updatedAt: '2026-07-01T00:00:00Z',
    ...overrides,
  } as ExpeditionInfo;
}

/** What the page last asked the server for. */
function lastParams(): ExpeditionListParams {
  return listSpy.mock.calls.at(-1)![0] as ExpeditionListParams;
}

function show() {
  return render(
    <MemoryRouter>
      <ExpeditionListPage />
    </MemoryRouter>,
  );
}

afterEach(cleanup);
beforeEach(() => {
  listSpy.mockReset();
  listSpy.mockReturnValue({
    data: { items: [camp()], page: 1, pageSize: 20, totalItems: 60 },
    isFetching: false,
  });
});

describe('the camps', () => {
  it('lists a camp with its days, its state and who may see it', () => {
    show();

    expect(screen.getByText('Bihor summer camp')).toBeTruthy();
    expect(screen.getByTestId('trip-state').textContent).toContain('Planned');
  });

  it('asks for a page of camps and nothing else until somebody narrows it', () => {
    show();

    expect(lastParams().page).toBe(1);
    expect(lastParams().state).toBeUndefined();
    expect(lastParams().from).toBeUndefined();
    expect(lastParams().search).toBeUndefined();
  });

  /**
   * The state goes to the server as the word the contract spells it with. Sending anything else
   * is refused rather than ignored, so a mistranslation here would empty the list with a refusal
   * behind it rather than quietly showing everything.
   */
  it('narrows by lifecycle state, in the words the contract uses', () => {
    show();

    fireEvent.mouseDown(within(screen.getByTestId('expedition-state-filter')).getByRole('combobox'));
    fireEvent.click(screen.getByText('Confirmed'));

    expect(lastParams().state).toBe('confirmed');
  });

  /**
   * Narrowing returns to the first page. Staying on page four of a list that now has one is an
   * empty table nobody asked for, and it reads as a filter that found nothing.
   */
  it('goes back to the first page whenever the list is narrowed', () => {
    show();

    // Second page first, the way somebody paging through would get there.
    fireEvent.click(screen.getByTitle('2'));
    expect(lastParams().page).toBe(2);

    fireEvent.mouseDown(within(screen.getByTestId('expedition-state-filter')).getByRole('combobox'));
    fireEvent.click(screen.getByText('Done'));

    expect(lastParams().page).toBe(1);
  });
});
