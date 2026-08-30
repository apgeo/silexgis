// SPDX-License-Identifier: AGPL-3.0-or-later
import { MemoryRouter } from 'react-router-dom';
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { NotificationItem, NotificationListParams } from '../../api/hooks.ts';

const markReadMutate = vi.fn();
const markAllMutate = vi.fn();
const asked: NotificationListParams[] = [];

function line(overrides: Partial<NotificationItem> & { id: number }): NotificationItem {
  return {
    category: 'permissionGranted',
    templateKey: 'notify.permission-granted',
    title: 'Ana shared Bat Cave with you',
    url: '/caves/00000000-0000-0000-0000-000000000001',
    targetWithheld: false,
    createdAt: '2026-08-20T09:00:00Z',
    readAt: null,
    ...overrides,
  };
}

// The whole page's data, so a filter change is observable as the question the page asks next.
let items: NotificationItem[] = [];
// What the envelope claims exists, which is not what this page is holding.
let totalItems = 0;

vi.mock('../../api/hooks.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/hooks.ts')>('../../api/hooks.ts');
  return {
    ...actual,
    useNotifications: (params: NotificationListParams) => {
      asked.push(params);
      return {
        data: {
          items,
          page: params.page ?? 1,
          pageSize: params.pageSize ?? 20,
          totalItems: totalItems || items.length,
        },
        isFetching: false,
      };
    },
    useMarkNotificationRead: () => ({ mutate: markReadMutate, isPending: false }),
    useMarkAllNotificationsRead: () => ({ mutate: markAllMutate, isPending: false }),
  };
});

const { default: NotificationsPage } = await import('./NotificationsPage.tsx');

beforeEach(() => {
  markReadMutate.mockReset();
  markAllMutate.mockReset();
  asked.length = 0;
  items = [];
  totalItems = 0;
});
afterEach(cleanup);

function show() {
  return render(
    <MemoryRouter>
      <NotificationsPage />
    </MemoryRouter>,
  );
}

/** The most recent set of query parameters the page asked the server for. */
function lastAsked() {
  return asked[asked.length - 1];
}

describe('NotificationsPage', () => {
  it('lists what happened, with a link to the thing it happened to', () => {
    items = [line({ id: 7 })];
    show();
    const link = screen.getByRole('link', { name: 'Ana shared Bat Cave with you' });
    expect(link.getAttribute('href')).toBe('/caves/00000000-0000-0000-0000-000000000001');
    // Unread until it is opened: the mark is on the row, not only in the header count.
    expect(document.querySelectorAll('[data-testid="notification-unread"]')).toHaveLength(1);
  });

  it('shows a line whose subject the reader has lost as deliberate rather than broken', () => {
    // What the server sends for a notification about something this reader may no longer see:
    // the category and the date survive, the name and the link do not.
    items = [
      line({ id: 11, targetWithheld: true, title: null, url: null, category: 'permissionGranted' }),
    ];
    show();

    // It says, in words, that the thing is gone — rather than rendering an empty cell.
    expect(screen.getByTestId('notification-withheld').textContent)
      .toBe('The thing this is about is no longer available to you.');
    // And it offers nowhere to go, so there is no dead link to click.
    expect(screen.queryByRole('link')).toBeNull();
    // The category and the date are still shown: that it happened is not the secret.
    expect(screen.getByText('Someone shares something with me')).toBeTruthy();
    const row = screen.getByTestId('notification-withheld').closest('tr');
    expect(row).not.toBeNull();
    expect(within(row!).getByText(/2026/)).toBeTruthy();
  });

  it('separates wording this installation no longer has from a subject it has withheld', () => {
    // Both arrive with no title; only one of them is about protection, and they read differently.
    items = [line({ id: 12, title: null, url: null, targetWithheld: false })];
    show();
    expect(screen.queryByTestId('notification-withheld')).toBeNull();
    expect(screen.getByTestId('notification-unrenderable').textContent)
      .toBe('This notification can no longer be shown.');
  });

  it('asks for one category when one is chosen, and starts again at the first page', () => {
    // Deep in the inbox before the filter changes: on page one the reset is invisible, and a
    // test that never leaves it passes with the reset deleted. A reader who has paged in and
    // then narrows the list is asking a new question, and page five of the old answer is not
    // where it begins — it is very likely past the end of the new one, which shows as an empty
    // table under a pager insisting the rows are on page one.
    items = [line({ id: 1 })];
    totalItems = 95;
    show();
    fireEvent.click(screen.getByTitle('2'));
    expect(lastAsked().page).toBe(2);

    // By accessible name: the pager's own size chooser is a combobox too.
    fireEvent.mouseDown(screen.getByRole('combobox', { name: 'About' }));
    fireEvent.click(
      document.querySelector('.ant-select-item-option[title="I am added to a trip"]')!,
    );

    expect(lastAsked().category).toBe('tripParticipation');
    expect(lastAsked().page).toBe(1);
  });

  it('asks for the unread ones only when the reader narrows to them, from the first page', () => {
    items = [line({ id: 1 })];
    totalItems = 95;
    show();
    expect(lastAsked().unreadOnly).toBeUndefined();
    fireEvent.click(screen.getByTitle('2'));
    expect(lastAsked().page).toBe(2);

    fireEvent.click(screen.getByRole('radio', { name: 'Unread' }));

    expect(lastAsked().unreadOnly).toBe(true);
    // Narrowing to the unread is the same new question as choosing a category, and starts
    // where that one does.
    expect(lastAsked().page).toBe(1);
  });

  it('marks one line read when it is opened, and only if it was unread', () => {
    items = [line({ id: 7 }), line({ id: 8, readAt: '2026-08-20T10:00:00Z', title: 'Already read' })];
    show();

    fireEvent.click(screen.getByRole('link', { name: 'Ana shared Bat Cave with you' }));
    expect(markReadMutate).toHaveBeenCalledWith(7);

    // Reading an already-read line again would rewrite nothing on the server, but asking is noise.
    markReadMutate.mockReset();
    fireEvent.click(screen.getByText('Already read'));
    expect(markReadMutate).not.toHaveBeenCalled();
  });

  it('marks a withheld line read when it is opened, because it has nothing else to open', () => {
    items = [line({ id: 21, targetWithheld: true, title: null, url: null })];
    show();
    fireEvent.click(screen.getByTestId('notification-withheld'));
    expect(markReadMutate).toHaveBeenCalledWith(21);
  });

  it('marks everything read in one act', () => {
    items = [line({ id: 1 }), line({ id: 2 })];
    show();
    fireEvent.click(screen.getByRole('button', { name: /Mark all as read/ }));
    expect(markAllMutate).toHaveBeenCalledTimes(1);
  });

  it('binds paging to the envelope the server sent rather than to the rows it is holding', () => {
    // One row in hand, ninety-five in the account: the pager follows the envelope, so a second
    // page is offered and asking for it asks the server rather than re-slicing what is here.
    items = [line({ id: 1 })];
    totalItems = 95;
    show();

    fireEvent.click(screen.getByTitle('2'));

    expect(lastAsked().page).toBe(2);
    expect(lastAsked().pageSize).toBe(20);
  });
});
