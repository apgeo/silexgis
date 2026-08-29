// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
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

vi.mock('../../api/hooks.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/hooks.ts')>('../../api/hooks.ts');
  return {
    ...actual,
    useNotificationPreferences: () => ({ data: prefs, isPending: false }),
    useUpdateNotificationPreferences: () => ({ mutateAsync: vi.fn(), isPending: false }),
  };
});

const { default: NotificationSettingsPage } = await import('./NotificationSettingsPage.tsx');

/** The row's own cell, whatever order the server listed the categories in. */
function row(category: string, channels: NotificationChannelCell[]): NotificationCategory {
  return { category, reachesNobody: false, channels } as NotificationCategory;
}

const alwaysOn = 'Always sent — this one cannot be switched off.';

beforeEach(() => {
  prefs = { configuredChannels: ['inApp', 'email', 'sms'], categories: [] };
});
afterEach(cleanup);

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
