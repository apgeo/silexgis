// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { TripInvitationInfo, TripInvitationList, TripLogInfo } from '../../api/hooks.ts';

const list = vi.fn();
const invite = vi.fn();
const answer = vi.fn();
const select = vi.fn();
const removeRow = vi.fn();
const promote = vi.fn();

const ANA = '11111111-1111-1111-1111-111111111111';
const BOGDAN = '22222222-2222-2222-2222-222222222222';
const CARMEN = '33333333-3333-3333-3333-333333333333';
const UNNAMED = '44444444-4444-4444-4444-444444444444';

vi.mock('../../api/hooks.ts', () => ({
  useTripInvitations: () => list(),
  useCavers: () => ({ data: [{ id: ANA, name: 'Ana Popescu' }] }),
  useInviteToTrip: () => ({ mutateAsync: invite, isPending: false }),
  useAnswerTripInvitation: () => ({ mutateAsync: answer, isPending: false }),
  useSelectForTrip: () => ({ mutateAsync: select, isPending: false }),
  useRemoveTripInvitation: () => ({ mutateAsync: removeRow, isPending: false }),
  usePromoteTripInvitations: () => ({ mutateAsync: promote, isPending: false }),
}));

const { default: TripInvitationsTab } = await import('./TripInvitationsTab.tsx');

function row(overrides: Partial<TripInvitationInfo> = {}): TripInvitationInfo {
  return {
    id: 1,
    tripLogId: 'trip-1',
    caverId: ANA,
    caverName: 'Ana Popescu',
    response: 'yes',
    invitedByUserId: null,
    invitedAt: null,
    respondedAt: '2026-03-01T10:00:00Z',
    respondedByUserId: null,
    selectedAt: null,
    note: null,
    mayAnswer: false,
    place: 1,
    attending: true,
    createdAt: '2026-03-01T09:00:00Z',
    updatedAt: '2026-03-01T10:00:00Z',
    ...overrides,
  };
}

function answers(overrides: Partial<TripInvitationList> = {}): TripInvitationList {
  return {
    tripLogId: 'trip-1',
    maxParticipants: null,
    attendingCount: 0,
    waitingCount: 0,
    invitations: [],
    ...overrides,
  };
}

function trip(overrides: Partial<TripLogInfo> = {}): TripLogInfo {
  return { id: 'trip-1', title: 'Digging weekend', state: 'planned', ...overrides } as unknown as TripLogInfo;
}

function show(subject: TripLogInfo = trip(), canEdit = true) {
  return render(
    <App>
      <TripInvitationsTab trip={subject} canEdit={canEdit} />
    </App>,
  );
}

beforeEach(() => {
  for (const spy of [list, invite, answer, select, removeRow, promote]) {
    spy.mockReset();
  }
  for (const spy of [invite, answer, select, removeRow]) {
    spy.mockResolvedValue({});
  }
  promote.mockResolvedValue({ tripLogId: 'trip-1', attending: 0, promoted: 0, alreadyNamed: 0 });
  list.mockReturnValue({ data: answers(), isPending: false, error: null, refetch: vi.fn() });
});

afterEach(cleanup);

describe('TripInvitationsTab', () => {
  it('writes an unanswered row’s control in words rather than in the stored token', () => {
    // Not answered is where every row starts, and the picker has no option for it because
    // nobody may set it back. A control given a value none of its options carry falls back to
    // drawing the value itself — which would put an untranslated word beside a person's name,
    // on the default path of the surface's central control.
    list.mockReturnValue({
      data: answers({
        invitations: [row({ response: 'pending', mayAnswer: true, place: null, attending: false })],
      }),
      isPending: false,
      error: null,
      refetch: vi.fn(),
    });
    show();

    const control = screen.getByTestId(`trip-invitation-answer-${ANA}`);
    expect(control).toHaveTextContent('Not answered');
    expect(control).not.toHaveTextContent('pending');
  });

  /**
   * The order rows arrive in *is* the order people answered in, worked out by the server over the
   * whole list. Sorting here — by name, by answer, by when the row was created — would be a second
   * ordering free to disagree with the places drawn beside the rows, and it would disagree
   * silently, because a wrongly ordered list still looks like a list.
   */
  it('draws the queue in the order the server sent it', () => {
    list.mockReturnValue({
      data: answers({
        attendingCount: 2,
        invitations: [
          row({ caverId: CARMEN, caverName: 'Carmen Zaharia', place: 1, id: 30 }),
          row({ caverId: ANA, caverName: 'Ana Popescu', place: 2, id: 10 }),
          row({ caverId: BOGDAN, caverName: 'Bogdan Ionescu', place: 3, id: 20, attending: false }),
        ],
      }),
      isPending: false,
      error: null,
      refetch: vi.fn(),
    });
    show();

    const drawn = screen
      .getAllByText(/Carmen Zaharia|Ana Popescu|Bogdan Ionescu/)
      .map((node) => node.textContent);
    expect(drawn).toEqual(['Carmen Zaharia', 'Ana Popescu', 'Bogdan Ionescu']);
  });

  /**
   * The counts beside the list are the server's, and they cannot be recovered by counting rows: a
   * hand-picked person holds a place wherever they stand in the order, so a list of three people
   * two of whom are coming is not "two because two rows say yes".
   */
  it('shows the limit as the server counted it rather than recounting the rows', () => {
    list.mockReturnValue({
      data: answers({
        maxParticipants: 6,
        attendingCount: 4,
        waitingCount: 2,
        invitations: [row()],
      }),
      isPending: false,
      error: null,
      refetch: vi.fn(),
    });
    show();

    expect(screen.getByTestId('trip-invitations-limit').textContent).toBe(
      '4 of 6 places taken, 2 waiting.',
    );
  });

  it('says the trip sets no limit when it sets none', () => {
    list.mockReturnValue({
      data: answers({ maxParticipants: null, attendingCount: 3, invitations: [row()] }),
      isPending: false,
      error: null,
      refetch: vi.fn(),
    });
    show();

    expect(screen.getByTestId('trip-invitations-limit').textContent).toContain('no limit');
  });

  /**
   * Writing the list onto the trip only means anything once the trip has happened, and the server
   * refuses it otherwise. The negative case is constructed rather than assumed — a trip genuinely
   * still ahead — and the positive one is asserted beside it so that a control that disappeared
   * for some unrelated reason could not pass this test.
   */
  it('offers writing people onto the trip only once the trip has happened', () => {
    show(trip({ state: 'planned' }));
    expect(screen.queryByTestId('trip-invitations-promote')).toBeNull();

    cleanup();
    show(trip({ state: 'done' }));
    expect(screen.getByTestId('trip-invitations-promote')).toBeTruthy();
  });

  /** Nobody who cannot write the trip is offered the acts that belong to whoever runs it. */
  it("offers none of the proposer's controls to a reader", () => {
    list.mockReturnValue({
      data: answers({ invitations: [row()] }),
      isPending: false,
      error: null,
      refetch: vi.fn(),
    });
    show(trip({ state: 'done' }), false);

    expect(screen.queryByTestId('trip-invite')).toBeNull();
    expect(screen.queryByTestId(`trip-invitation-select-${ANA}`)).toBeNull();
    expect(screen.queryByTestId(`trip-invitation-remove-${ANA}`)).toBeNull();
    expect(screen.queryByTestId('trip-invitations-promote')).toBeNull();
  });

  /**
   * Whether this caller may write an answer in somebody's name is the server's answer and has
   * three ways into it — being that person, being able to write the trip, being an administrator.
   * The client renders the answer and never works it out, so both cases are proved off the same
   * list: one row the server said may be answered for and one it did not.
   */
  it('offers answering for somebody only where the server says it may be', () => {
    list.mockReturnValue({
      data: answers({
        invitations: [
          row({ caverId: ANA, caverName: 'Ana Popescu', mayAnswer: true }),
          row({ caverId: BOGDAN, caverName: 'Bogdan Ionescu', mayAnswer: false, place: 2 }),
        ],
      }),
      isPending: false,
      error: null,
      refetch: vi.fn(),
    });
    show();

    expect(screen.getByTestId(`trip-invitation-answer-${ANA}`)).toBeTruthy();
    expect(screen.queryByTestId(`trip-invitation-answer-${BOGDAN}`)).toBeNull();
  });

  /**
   * A name the caller may not be given arrives as no name at all. Drawing that row blank would
   * still say somebody is there and where they stand; the shortfall is written as a number
   * instead, and the person is named nowhere.
   */
  it('counts somebody it may not name, and lets nothing of theirs onto the page', () => {
    list.mockReturnValue({
      data: answers({
        attendingCount: 2,
        invitations: [
          row({ caverId: ANA, caverName: 'Ana Popescu' }),
          row({ caverId: UNNAMED, caverName: '', place: 2, note: 'bringing the drill' }),
        ],
      }),
      isPending: false,
      error: null,
      refetch: vi.fn(),
    });
    const { container } = show();

    // The readable half of the same list, asserted here rather than in a test of its own: a
    // surface that drew nothing at all would pass every assertion below and be useless.
    expect(screen.getByTestId(`trip-invitation-${ANA}`)).toBeTruthy();
    expect(screen.getByText('Ana Popescu')).toBeTruthy();

    expect(screen.getByTestId('trip-invitations-withheld').textContent).toContain('1');

    // Not merely undrawn as a row: the reference the row is keyed by identifies the person to
    // anybody who can ask the directory about it, and a note they wrote is their words. Neither
    // may reach the page in any form — a hidden row, a key, an attribute — so the whole rendered
    // markup is what is searched, not the parts of it this surface remembers drawing.
    expect(screen.queryByTestId(`trip-invitation-${UNNAMED}`)).toBeNull();
    expect(container.innerHTML).not.toContain(UNNAMED);
    expect(container.innerHTML).not.toContain('bringing the drill');
  });

  /**
   * Every row withheld is still a list somebody is on. Saying nobody has been asked would be
   * false rather than reticent, and it is the one wording that turns the refusal back into a gap.
   */
  it('does not say nobody was asked when everybody asked is withheld', () => {
    list.mockReturnValue({
      data: answers({
        attendingCount: 2,
        invitations: [
          row({ caverId: UNNAMED, caverName: '' }),
          row({ caverId: BOGDAN, caverName: '   ', place: 2 }),
        ],
      }),
      isPending: false,
      error: null,
      refetch: vi.fn(),
    });
    show();

    expect(screen.queryByText('Nobody has been asked on this trip yet.')).toBeNull();
    expect(
      screen.getByText("Everybody on this trip's list is somebody you may not be told about."),
    ).toBeTruthy();
    expect(screen.getByTestId('trip-invitations-withheld').textContent).toContain('2');
  });

  /**
   * The list can only hold people the club already knows, so text that names nobody it has
   * chosen is refused rather than sent. Typing a name that happens to match somebody is not
   * choosing them: the reference is what a write is read by, and inventing one here would ask
   * whoever the server guessed at under a right-looking name.
   */
  it('refuses to ask on a name that was typed rather than chosen', () => {
    show();

    fireEvent.change(screen.getByPlaceholderText('Somebody the club knows'), {
      target: { value: 'Ana Popescu' },
    });
    fireEvent.click(screen.getByTestId('trip-invite'));

    expect(invite).not.toHaveBeenCalled();
  });

  it('says nothing has been asked yet when the list is empty', () => {
    show();
    expect(screen.getByText('Nobody has been asked on this trip yet.')).toBeTruthy();
  });

  /** A list that could not be read is a state of the surface, not an empty one. */
  it('says so when the list could not be read', () => {
    list.mockReturnValue({
      data: undefined,
      isPending: false,
      error: new Error('nope'),
      refetch: vi.fn(),
    });
    show();

    expect(screen.getByTestId('trip-invitations-unavailable')).toBeTruthy();
  });
});
