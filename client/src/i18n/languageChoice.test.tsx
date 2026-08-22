// SPDX-License-Identifier: AGPL-3.0-or-later
import { App as AntApp } from 'antd';
import { renderHook, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import i18n from './index.ts';
import { useLanguageChoice } from './languageChoice.ts';

const saveLocale = vi.fn();
let me: { id: string; locale: string } | undefined;

vi.mock('../api/hooks.ts', () => ({
  useMe: () => ({ data: me }),
  useUpdateLocale: () => ({ mutate: saveLocale }),
}));

function mount() {
  return renderHook(() => useLanguageChoice(), {
    wrapper: ({ children }) => <AntApp>{children}</AntApp>,
  });
}

beforeEach(async () => {
  window.localStorage.clear();
  saveLocale.mockClear();
  me = undefined;
  await i18n.changeLanguage('en');
});

afterEach(async () => {
  window.localStorage.clear();
  await i18n.changeLanguage('en');
});

describe('the language a browser reads in', () => {
  it('takes the account’s language on a browser where nobody has chosen one', async () => {
    // The detector writes its own cache key during init, so "has this browser been told a
    // language" cannot be read from it — gating on that key is what made this branch dead, and
    // an account that reads Romanian stayed in English on every new machine.
    me = { id: 'a1', locale: 'ro' };

    mount();

    await waitFor(() => expect(i18n.resolvedLanguage).toBe('ro'));
  });

  it('leaves a browser alone once somebody has chosen here', async () => {
    me = { id: 'a1', locale: 'ro' };
    const { result, rerender } = mount();

    await waitFor(() => expect(i18n.resolvedLanguage).toBe('ro'));

    result.current.choose('en');
    await waitFor(() => expect(i18n.resolvedLanguage).toBe('en'));

    // A second account signing in to the same tab is offered its own language, but not over a
    // choice this browser has already been given.
    me = { id: 'a2', locale: 'ro' };
    rerender();

    expect(i18n.resolvedLanguage).toBe('en');
  });

  it('tells the server, so the messages this account is sent follow the choice', async () => {
    me = { id: 'a1', locale: 'en' };
    const { result } = mount();

    result.current.choose('ro');

    expect(saveLocale).toHaveBeenCalledTimes(1);
    expect(saveLocale.mock.calls[0][0]).toMatchObject({ language: 'ro' });
  });
});
