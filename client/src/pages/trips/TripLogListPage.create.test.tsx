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

const { doorSpy, formSpy } = vi.hoisted(() => ({ doorSpy: vi.fn(), formSpy: vi.fn() }));

/** Somebody who holds the right to record trips over the domain as such. */
const holdsTheRight = { canCreate: true, unbound: true, cavingGroups: [] };
/** Somebody whose right to record trips reaches only through the one caving group they are in. */
const byClubOnly = {
  canCreate: true,
  unbound: false,
  cavingGroups: [{ id: 'g-1', name: 'Silex' }],
};
/** Somebody who may record no trip anywhere. */
const mayNot = { canCreate: false, unbound: false, cavingGroups: [] };

vi.mock('../../api/hooks.ts', () => ({
  useTripLogs: () => ({
    data: { items: [], page: 1, pageSize: 20, totalItems: 0 },
    isFetching: false,
    isError: false,
  }),
  useTripLogFacets: () => ({ data: undefined }),
  useTripLogGrouping: () => ({ data: undefined }),
  useTripTypes: () => ({ data: [] }),
  // The vocabulary links beside the control are a different right, and not what is tested here.
  useCan: () => false,
  useCreateDoor: () => doorSpy(),
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
  doorSpy.mockReset().mockReturnValue(holdsTheRight);
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

  it('offers both doors to somebody who may record trips only for their caving group', async () => {
    // The right held over trips as such is not what the control asks. A member whose club lets
    // them record the club's trips holds no such right and is still served by both doors: the
    // server accepts a trip of theirs that belongs to the club, and the form binds it.
    doorSpy.mockReturnValue(byClubOnly);
    show();

    fireEvent.click(screen.getByRole('button', { name: /New trip log/ }));
    expect(form().dataset.open).toBe('true');
    expect(form().dataset.intent).toBe('report');

    fireEvent.click(screen.getByTestId('trip-create-menu'));
    fireEvent.click(await screen.findByText('Plan a trip'));
    expect(form().dataset.intent).toBe('plan');
  });

  it('offers neither door to somebody who may not create a trip', () => {
    doorSpy.mockReturnValue(mayNot);
    show();

    expect(screen.queryByRole('button', { name: /New trip log/ })).toBeNull();
    expect(screen.queryByTestId('trip-create-menu')).toBeNull();
    // Not merely closed: the form is not mounted at all, so the dashboard's "open on arrival"
    // state cannot hand a create form to somebody the server would refuse.
    expect(screen.queryByTestId('trip-form')).toBeNull();
  });
});
