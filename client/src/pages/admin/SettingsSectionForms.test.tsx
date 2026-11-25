// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ComponentType } from 'react';
import { App } from 'antd';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import i18n from '../../i18n';
import type { AdminSettings } from '../../api/hooks.ts';
import type { SectionProps } from './settingsSection.tsx';

/**
 * The four small sections of the installation settings page — sign-in policy, import trust,
 * notification retention and announcement spending — from the screen an administrator uses.
 *
 * What is pinned for each: the stored values are shown, a save sends the whole section back (a
 * value the form does not show included, since a save replaces the stored section and a value
 * left out would be reset), a refusal is said in words from its code, a dropped connection is
 * said rather than left unhandled, and an unsaved edit survives another section being saved.
 */

const put = vi.fn();

vi.mock('../../api/client.ts', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../api/client.ts')>()),
  api: {
    GET: vi.fn(),
    PUT: (...args: unknown[]) => put(...args),
  },
}));

const { default: PolicyForm } = await import('./PolicyForm.tsx');
const { default: ImportForm } = await import('./ImportForm.tsx');
const { default: NotificationsForm } = await import('./NotificationsForm.tsx');
const { default: AnnouncementsForm } = await import('./AnnouncementsForm.tsx');

interface Case {
  name: string;
  Form: ComponentType<SectionProps>;
  section: 'security' | 'import' | 'notifications' | 'announcements';
  path: string;
  stored: Record<string, unknown>;
  /** Changes one field on screen and says what the section then holds. */
  edit: () => Record<string, unknown>;
  /** Reads the edited field back off the screen. */
  shown: () => string | null;
}

const switchLabelled = (key: string) => screen.getByRole('switch', { name: i18n.t(key) });
const numberLabelled = (key: string) => screen.getByRole('spinbutton', { name: i18n.t(key) }) as HTMLInputElement;

const security = {
  requireConfirmedEmail: false,
  sendConfirmationOnRegistration: true,
  authenticatorTwoFactorEnabled: true,
  emailTwoFactorEnabled: false,
  smsTwoFactorEnabled: false,
  twoFactorCodeLifetimeMinutes: 10,
  twoFactorResendIntervalSeconds: 60,
};
const importSection = {
  allowCreateWithoutReview: false,
  duplicateRadiusMeters: 25,
  duplicateNameSimilarity: 0.8,
  photoProximityRadiusMeters: 150,
  photoClusterRadiusMeters: 40,
};

const cases: Case[] = [
  {
    name: 'PolicyForm',
    Form: PolicyForm,
    section: 'security',
    path: '/api/v1/admin/settings/security',
    stored: security,
    edit: () => {
      fireEvent.click(switchLabelled('admin.messaging.sendConfirmation'));
      return { ...security, sendConfirmationOnRegistration: false };
    },
    shown: () => switchLabelled('admin.messaging.sendConfirmation').getAttribute('aria-checked'),
  },
  {
    name: 'ImportForm',
    Form: ImportForm,
    section: 'import',
    path: '/api/v1/admin/settings/import',
    stored: importSection,
    edit: () => {
      fireEvent.click(switchLabelled('admin.messaging.allowWithoutReview'));
      return { ...importSection, allowCreateWithoutReview: true };
    },
    shown: () => switchLabelled('admin.messaging.allowWithoutReview').getAttribute('aria-checked'),
  },
  {
    name: 'NotificationsForm',
    Form: NotificationsForm,
    section: 'notifications',
    path: '/api/v1/admin/settings/notifications',
    stored: { retentionDays: 90 },
    edit: () => {
      const field = numberLabelled('admin.messaging.retentionDays');
      fireEvent.change(field, { target: { value: '120' } });
      fireEvent.blur(field);
      return { retentionDays: 120 };
    },
    shown: () => numberLabelled('admin.messaging.retentionDays').value,
  },
  {
    name: 'AnnouncementsForm',
    Form: AnnouncementsForm,
    section: 'announcements',
    path: '/api/v1/admin/settings/announcements',
    stored: { paidChannelsEnabled: false, dailyPaidMessageCap: 50 },
    edit: () => {
      fireEvent.click(switchLabelled('admin.messaging.paidChannelsEnabled'));
      return { paidChannelsEnabled: true, dailyPaidMessageCap: 50 };
    },
    shown: () => switchLabelled('admin.messaging.paidChannelsEnabled').getAttribute('aria-checked'),
  },
];

const onSaved = vi.fn();

function screenFor(c: Case, settings: Record<string, unknown>) {
  const { Form } = c;
  return (
    <QueryClientProvider client={new QueryClient()}>
      <App>
        <Form settings={settings as unknown as AdminSettings} onSaved={onSaved} />
      </App>
    </QueryClientProvider>
  );
}

beforeEach(() => {
  onSaved.mockReset();
  put.mockReset();
});
afterEach(cleanup);

describe.each(cases)('$name', (c) => {
  const settings = () => ({ [c.section]: c.stored, mailConfigured: true, smsConfigured: false });
  const save = () => fireEvent.click(screen.getByRole('button', { name: i18n.t('common.save') }));

  it('sends the whole section with the edit on save and hands the answer on', async () => {
    render(screenFor(c, settings()));
    const expected = c.edit();
    const answer = { [c.section]: expected };
    put.mockResolvedValue({ data: answer, error: undefined });

    save();

    await vi.waitFor(() => expect(put).toHaveBeenCalled());
    expect(put.mock.calls[0][0]).toBe(c.path);
    expect(put.mock.calls[0][1].body).toEqual(expected);
    await vi.waitFor(() => expect(onSaved).toHaveBeenCalledWith(answer));
    expect(await screen.findByText(i18n.t('common.saved'))).toBeTruthy();
  });

  it('says a refusal in words from its code and keeps the edit', async () => {
    render(screenFor(c, settings()));
    c.edit();
    const edited = c.shown();
    put.mockResolvedValue({ data: undefined, error: { code: 'access.forbidden', status: 403 } });

    save();

    expect(await screen.findByText(i18n.t('admin.settingsProblems.forbidden'))).toBeTruthy();
    expect(onSaved).not.toHaveBeenCalled();
    expect(c.shown()).toBe(edited);
  });

  it('says a dropped connection as a failed save', async () => {
    render(screenFor(c, settings()));
    put.mockRejectedValue(new TypeError('Failed to fetch'));

    save();

    expect(await screen.findByText(i18n.t('common.saveFailed'))).toBeTruthy();
    expect(onSaved).not.toHaveBeenCalled();
  });

  it('keeps an unsaved edit when another section of the settings is saved', () => {
    const view = render(screenFor(c, { ...settings(), mail: { host: 'a' } }));
    c.edit();
    const edited = c.shown();

    view.rerender(screenFor(c, { ...settings(), [c.section]: { ...c.stored }, mail: { host: 'b' } }));
    expect(c.shown()).toBe(edited);
  });
});
