// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { AdminSettings } from '../../api/hooks.ts';
import en from '../../i18n/locales/en.json';
import ro from '../../i18n/locales/ro.json';

/**
 * The phone-sync settings, from the screen an administrator sets them on.
 *
 * What is pinned: the section is saved whole — a field the form forgot would be put back to its
 * default by every save — the scope warning shows only while it can matter, and every setting has
 * a help text in both languages.
 */

const put = vi.fn();

vi.mock('../../api/client.ts', () => ({
  api: {
    GET: vi.fn(),
    PUT: (...args: unknown[]) => put(...args),
  },
}));

const { default: SyncSettingsForm } = await import('./SyncSettingsForm.tsx');

const onSaved = vi.fn();

const stored = {
  peopleDirectoryEnabled: true,
  peopleDirectoryScope: 'myGroups',
  convertTripsWithProblems: true,
  stagedUploadRetentionDays: 7,
  rejectedHeldBytesRetentionDays: 30,
};

function show(sync = stored) {
  return render(
    <App>
      <SyncSettingsForm settings={{ sync } as unknown as AdminSettings} onSaved={onSaved} />
    </App>,
  );
}

beforeEach(() => {
  onSaved.mockReset();
  put.mockReset().mockResolvedValue({ data: { sync: stored }, error: undefined });
});
afterEach(cleanup);

describe('SyncSettingsForm', () => {
  it('shows the stored values and saves all five together', async () => {
    show({ ...stored, convertTripsWithProblems: false, stagedUploadRetentionDays: 3 });

    const convert = screen.getByRole('switch', { name: /Make trips from recordings that have problems/ });
    expect(convert.getAttribute('aria-checked')).toBe('false');
    expect(screen.getByRole('switch', { name: /Give phones the list of people/ }).getAttribute('aria-checked')).toBe('true');
    expect((screen.getByLabelText(/Keep photos sent ahead of an upload/) as HTMLInputElement).value).toBe('3');

    fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(put).toHaveBeenCalledTimes(1));
    expect(put.mock.calls[0]![0]).toBe('/api/v1/admin/settings/sync');
    expect(put.mock.calls[0]![1]).toEqual({
      body: { ...stored, convertTripsWithProblems: false, stagedUploadRetentionDays: 3 },
    });
    await waitFor(() => expect(onSaved).toHaveBeenCalledTimes(1));
  });

  it('says so when the save is refused, and does not report it saved', async () => {
    put.mockResolvedValue({ data: undefined, error: { code: 'validation' } });
    show();

    fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    expect(await screen.findByText('The operation failed. Please try again.')).toBeInTheDocument();
    expect(onSaved).not.toHaveBeenCalled();
  });

  it('warns about listing everyone only while the list is on and set to everyone', async () => {
    const { unmount } = show({ ...stored, peopleDirectoryScope: 'everyone' });
    expect(await screen.findByTestId('sync-scope-everyone-warning')).toBeInTheDocument();
    unmount();

    // The switch is read back from the form before the absence means anything.
    show({ ...stored, peopleDirectoryScope: 'everyone', peopleDirectoryEnabled: false });
    await waitFor(() =>
      expect(
        screen.getByRole('switch', { name: /Give phones the list of people/ }).getAttribute('aria-checked'),
      ).toBe('false'),
    );
    expect(screen.queryByTestId('sync-scope-everyone-warning')).toBeNull();
    cleanup();

    show();
    await waitFor(() =>
      expect(
        screen.getByRole('switch', { name: /Give phones the list of people/ }).getAttribute('aria-checked'),
      ).toBe('true'),
    );
    expect(screen.queryByTestId('sync-scope-everyone-warning')).toBeNull();
  });

  it('has help for every setting in both languages', () => {
    const SETTINGS = [
      'PeopleDirectoryEnabled',
      'PeopleDirectoryScope',
      'ConvertTripsWithProblems',
      'StagedUploadRetentionDays',
      'RejectedHeldBytesRetentionDays',
    ];
    for (const locale of [en, ro]) {
      const messaging = locale.admin.messaging as Record<string, string>;
      for (const setting of SETTINGS) {
        expect(messaging[`sync${setting}`], setting).toBeTruthy();
        expect(messaging[`sync${setting}Hint`], `${setting} help`).toBeTruthy();
      }
    }
  });
});
