// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';

/**
 * The one home for creating a trip, and the two doors behind it.
 *
 * Kept apart from the listing's own tests on purpose: those stand the create right down and are
 * about narrowing, grouping and exporting. This file is only about what the create control
 * offers and which door it tells the form to write for.
 */

const { canSpy, formSpy } = vi.hoisted(() => ({ canSpy: vi.fn(), formSpy: vi.fn() }));

vi.mock('../../api/hooks.ts', () => ({
  useTripLogs: () => ({
    data: { items: [], page: 1, pageSize: 20, totalItems: 0 },
    isFetching: false,
    isError: false,
  }),
  useTripLogFacets: () => ({ data: undefined }),
  useTripLogGrouping: () => ({ data: undefined }),
  useTripTypes: () => ({ data: [] }),
  useCan: () => canSpy(),
}));

// The form is a probe of its props: which door it was opened on is the whole claim here, and the
// form's own behaviour on that door is asserted where the form is tested.
vi.mock('./TripFormModal.tsx', () => ({
  default: (props: { open: boolean; intent?: string }) => {
    formSpy(props);
    return <div data-testid="trip-form" data-open={String(props.open)} data-intent={props.intent} />;
  },
}));

const { default: TripLogListPage } = await import('./TripLogListPage.tsx');

function show() {
  return render(
    <MemoryRouter initialEntries={['/trip-logs']}>
      <TripLogListPage />
    </MemoryRouter>,
  );
}

const form = () => screen.getByTestId('trip-form');

afterEach(cleanup);
beforeEach(() => {
  canSpy.mockReset().mockReturnValue(true);
  formSpy.mockReset();
});

describe('creating a trip from the list', () => {
  it('files a report from the button, as it always did', () => {
    show();
    expect(form().dataset.open).toBe('false');

    fireEvent.click(screen.getByRole('button', { name: /New trip log/ }));

    expect(form().dataset.open).toBe('true');
    expect(form().dataset.intent).toBe('report');
  });

  it('opens a plan from the menu beside the button, on the same form', async () => {
    show();

    fireEvent.click(screen.getByTestId('trip-create-menu'));
    fireEvent.click(await screen.findByText('Plan a trip'));

    expect(form().dataset.open).toBe('true');
    expect(form().dataset.intent).toBe('plan');
  });

  it('offers neither door to somebody who may not create a trip', () => {
    canSpy.mockReturnValue(false);
    show();

    expect(screen.queryByRole('button', { name: /New trip log/ })).toBeNull();
    expect(screen.queryByTestId('trip-create-menu')).toBeNull();
    // Not merely closed: the form is not mounted at all, so the dashboard's "open on arrival"
    // state cannot hand a create form to somebody the server would refuse.
    expect(screen.queryByTestId('trip-form')).toBeNull();
  });
});
