// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { EventInfo, EventInvitationInfo, EventInvitationList } from '../../api/hooks.ts';

const list = vi.fn();
const invite = vi.fn();
const answer = vi.fn();
const select = vi.fn();
const removeRow = vi.fn();

const EVENT = '55555555-5555-5555-5555-555555555555';
const ANA = '11111111-1111-1111-1111-111111111111';
const BOGDAN = '22222222-2222-2222-2222-222222222222';
const UNNAMED = '44444444-4444-4444-4444-444444444444';

vi.mock('../../api/hooks.ts', () => ({
  useEventInvitations: () => list(),
  useCavers: () => ({ data: [{ id: ANA, name: 'Ana Popescu' }] }),
  useInviteToEvent: () => ({ mutateAsync: invite, isPending: false }),
  useAnswerEventInvitation: () => ({ mutateAsync: answer, isPending: false }),
  useSelectForEvent: () => ({ mutateAsync: select, isPending: false }),
  useRemoveEventInvitation: () => ({ mutateAsync: removeRow, isPending: false }),
}));

const { default: EventResponsesTab } = await import('./EventResponsesTab.tsx');

function row(overrides: Partial<EventInvitationInfo> = {}): EventInvitationInfo {
  return {
    id: 1,
    eventId: EVENT,
    caverId: ANA,
    caverName: 'Ana Popescu',
    response: 'yes',
    invitedByUserId: null,
    invitedAt: null,
    respondedAt: '2026-09-01T10:00:00Z',
    respondedByUserId: null,
    selectedAt: null,
    note: null,
    mayAnswer: false,
    place: 1,
    attending: true,
    createdAt: '2026-09-01T09:00:00Z',
    updatedAt: '2026-09-01T10:00:00Z',
    ...overrides,
  };
}

function answers(overrides: Partial<EventInvitationList> = {}): EventInvitationList {
  return {
    eventId: EVENT,
    maxParticipants: null,
    attendingCount: 0,
    waitingCount: 0,
    invitations: [],
    ...overrides,
  };
}

const anEvent = () => ({ id: EVENT, title: 'Committee night', kind: 'clubMeeting' }) as EventInfo;

function show(canEdit = true) {
  return render(
    <App>
      <EventResponsesTab event={anEvent()} canEdit={canEdit} />
    </App>,
  );
}

beforeEach(() => {
  for (const spy of [list, invite, answer, select, removeRow]) {
    spy.mockReset();
  }
  for (const spy of [invite, answer, select, removeRow]) {
    spy.mockResolvedValue({});
  }
  list.mockReturnValue({ data: answers(), isPending: false, error: null, refetch: vi.fn() });
});

afterEach(cleanup);

describe('EventResponsesTab', () => {
  /**
   * The order rows arrive in is the order people answered in, and the place beside each is the
   * server's conclusion about that order against a limit. Re-deriving either here would be a
   * second copy of a rule free to disagree with the one enforced — quietly, because a wrong place
   * still looks like a place. So the rows are drawn as received, in the order received.
   */
  it('draws the answers in the order they arrive, with the place the server gave each', () => {
    list.mockReturnValue({
      data: answers({
        maxParticipants: 1,
        attendingCount: 1,
        waitingCount: 1,
        invitations: [
          row({ caverId: ANA, caverName: 'Ana Popescu', place: 1, attending: true }),
          row({ id: 2, caverId: BOGDAN, caverName: 'Bogdan Ionescu', place: 2, attending: false }),
        ],
      }),
      isPending: false,
      error: null,
      refetch: vi.fn(),
    });
    show();

    expect(screen.getByTestId(`event-invitation-place-${ANA}`).textContent).toBe('#1');
    expect(screen.getByTestId(`event-invitation-place-${BOGDAN}`).textContent).toBe('#2');
    // The counts are the server's too: a hand-picked person holds a place wherever they stand in
    // the order, so these cannot be recovered by counting the rows.
    expect(screen.getByTestId('event-invitations-limit').textContent).toBe(
      '1 of 1 places taken, 1 waiting.',
    );
  });

  /** An event without a limit turns nobody away, and the line beside the list says so. */
  it('says an event with no limit has none', () => {
    list.mockReturnValue({
      data: answers({ attendingCount: 3, invitations: [row()] }),
      isPending: false,
      error: null,
      refetch: vi.fn(),
    });
    show();

    expect(screen.getByTestId('event-invitations-limit').textContent).toBe(
      '3 coming. This event sets no limit on how many.',
    );
  });

  /**
   * A person is asked by their entry in the club's records and never by a bare name: a list of
   * people to be told about something that could hold text nobody can resolve would be a list
   * nobody can act on. Typing a name and pressing the button names nobody, so nothing is sent.
   */
  it('refuses to ask somebody the club has no record of, and sends nothing', () => {
    show();

    fireEvent.change(screen.getByTestId('event-invite-name'), {
      target: { value: 'Someone from the pub' },
    });
    fireEvent.click(screen.getByTestId('event-invite'));

    expect(invite).not.toHaveBeenCalled();
  });

  /**
   * A name this caller may not be given arrives as no name at all. Such a row is counted and
   * never drawn — a blank row still says somebody is there and where they stand in the order —
   * so the shortfall is written out as a number rather than left as a list that quietly shortens
   * itself.
   */
  it('counts a person it may not name instead of drawing a blank row', () => {
    list.mockReturnValue({
      data: answers({
        attendingCount: 2,
        invitations: [row(), row({ id: 2, caverId: UNNAMED, caverName: '' })],
      }),
      isPending: false,
      error: null,
      refetch: vi.fn(),
    });
    show();

    expect(screen.queryByTestId(`event-invitation-${UNNAMED}`)).toBeNull();
    expect(screen.getByTestId('event-invitations-withheld').textContent).toContain('1');
  });

  /**
   * An event keeps no list of who turned up: the answers are the whole record of who was coming.
   * So the one act a trip offers here — writing the people holding places into a roster — has no
   * event analogue, and must not appear on this surface where a reader would expect it to work.
   */
  it('offers nothing that would turn the answers into a list of who was there', () => {
    list.mockReturnValue({
      data: answers({ attendingCount: 1, invitations: [row()] }),
      isPending: false,
      error: null,
      refetch: vi.fn(),
    });
    show();

    expect(screen.queryByTestId('event-invitations-promote')).toBeNull();
    expect(screen.queryByTestId('trip-invitations-promote')).toBeNull();
  });

  /** A list that could not be read says so rather than drawing an empty one, which would be a lie. */
  it('says the list could not be read rather than drawing an empty one', () => {
    list.mockReturnValue({
      data: undefined,
      isPending: false,
      error: new Error('nope'),
      refetch: vi.fn(),
    });
    show();

    expect(screen.getByTestId('event-invitations-unavailable')).toBeTruthy();
  });
});
