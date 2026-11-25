// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import i18n from '../../i18n';
import { ApiError } from '../../api/client.ts';
import type {
  NotificationCategory,
  NotificationChannelCell,
  NotificationChannelName,
  NotificationPreferences,
} from '../../api/hooks.ts';

function cell(
  channel: NotificationChannelName,
  overrides: Partial<NotificationChannelCell> = {},
): NotificationChannelCell {
  return {
    channel,
    choice: 'immediate',
    locked: false,
    canDefer: false,
    available: true,
    ...overrides,
  };
}

let prefs: NotificationPreferences = { configuredChannels: [], categories: [] };
let readFailed = false;
const refetch = vi.fn();
const save = vi.fn();

vi.mock('../../api/hooks.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/hooks.ts')>('../../api/hooks.ts');
  return {
    ...actual,
    useNotificationPreferences: () =>
      readFailed
        ? { data: undefined, isPending: false, isError: true, error: new TypeError('offline'), refetch }
        : { data: prefs, isPending: false, isError: false, refetch },
    useUpdateNotificationPreferences: () => ({ mutateAsync: save, isPending: false }),
  };
});

const { default: NotificationSettingsPage } = await import('./NotificationSettingsPage.tsx');

/** The row's own cell, whatever order the server listed the categories in. */
function row(category: string, channels: NotificationChannelCell[]): NotificationCategory {
  return { category, reachesNobody: false, channels } as NotificationCategory;
}

const alwaysOn = 'Always sent — this one cannot be switched off.';

beforeEach(() => {
  save.mockReset();
  prefs = { configuredChannels: ['inApp', 'email', 'sms'], categories: [] };
});
afterEach(cleanup);

describe('a failed read of the preferences', () => {
  it('says so and offers Retry instead of spinning for ever', () => {
    readFailed = true;
    try {
      render(<NotificationSettingsPage />);
      expect(screen.getByTestId('notification-settings-load-failed')).toBeTruthy();
      fireEvent.click(screen.getByRole('button', { name: i18n.t('common.retry') }));
      expect(refetch).toHaveBeenCalledTimes(1);
    } finally {
      readFailed = false;
      refetch.mockReset();
    }
  });
});

describe('the sentence that explains a switch somebody cannot move', () => {
  it('is shown while every channel of the row is held on', () => {
    prefs.categories = [
      row('tripCallout', [cell('inApp', { locked: true }), cell('email', { locked: true })]),
    ];

    render(<NotificationSettingsPage />);

    expect(screen.getByText(alwaysOn)).toBeTruthy();
  });

  it('is still shown when the row also offers a channel the account pays for and chooses', () => {
    // The regression this exists for. An installation that agreed to pay for texts gets a third
    // cell on the overdue alarm, and that one is deliberately the account's own choice rather than
    // held on. Reading the row as "locked only if all of it is locked" would take the explanation
    // away from the two switches it explains — on the one category where a greyed-out switch with
    // no reason beside it is worst.
    prefs.categories = [
      row('tripCallout', [
        cell('inApp', { locked: true }),
        cell('email', { locked: true }),
        cell('sms', { locked: false, choice: 'off' }),
      ]),
    ];

    render(<NotificationSettingsPage />);

    expect(screen.getByText(alwaysOn)).toBeTruthy();
  });

  it('is absent from a row nothing is held on for', () => {
    prefs.categories = [row('commentReply', [cell('inApp'), cell('email')])];

    render(<NotificationSettingsPage />);

    expect(screen.queryByText(alwaysOn)).toBeNull();
  });
});

describe('saving the preferences', () => {
  function showAndSave() {
    prefs.categories = [row('commentReply', [cell('inApp'), cell('email')])];
    render(
      <App>
        <NotificationSettingsPage />
      </App>,
    );
    fireEvent.click(screen.getByRole('button', { name: i18n.t('common.save') }));
  }

  it('says which kind of refusal it was when the server will not allow a choice', async () => {
    save.mockRejectedValue(
      new ApiError(400, 'me.notification_choice_refused', 'That kind of notification cannot be set that way on that channel.'),
    );
    showAndSave();

    expect(await screen.findByText(i18n.t('settings.notifications.choiceRefused'))).toBeTruthy();
    expect(save).toHaveBeenCalledTimes(1);
    expect(screen.queryByText(i18n.t('common.saved'))).toBeNull();
  });

  it('names a save that did not land as failed, not as saved', async () => {
    save.mockRejectedValue(new TypeError('Failed to fetch'));
    showAndSave();

    expect(await screen.findByText(i18n.t('common.saveFailed'))).toBeTruthy();
    expect(screen.queryByText(i18n.t('common.saved'))).toBeNull();
  });
});
