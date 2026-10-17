// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { MemoryRouter } from 'react-router-dom';
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { SAVED_FILTER_AUDIENCE_SETTINGS_PATH } from '../../filters/savedFilterAudience.ts';
import { useUiPrefsStore } from '../../stores/uiPrefsStore.ts';

const savePreferences = vi.fn();
let groups: { id: string; name: string }[] | undefined = [];

vi.mock('../../api/hooks/accountPrefs.ts', () => ({
  useSaveAccountPreferences: () => ({ mutate: savePreferences }),
}));

vi.mock('../../api/hooks/access.ts', () => ({
  useCapabilities: () => ({ data: groups === undefined ? undefined : {}, isLoading: groups === undefined }),
  useCreateDoor: () => ({ canCreate: true, unbound: true, cavingGroups: groups ?? [] }),
}));

const { default: SavedFilterSettingsPage } = await import('./SavedFilterSettingsPage.tsx');

function show(at = '/settings/filters') {
  return render(
    <App>
      <MemoryRouter initialEntries={[at]}>
        <SavedFilterSettingsPage />
      </MemoryRouter>
    </App>,
  );
}

function control() {
  return screen.getByTestId('saved-filter-default-audience');
}

function shown() {
  return control().querySelector('.ant-select-selection-item, .ant-select-content')?.textContent;
}

function pick(label: string) {
  fireEvent.mouseDown(screen.getByRole('combobox', { name: 'Share new filters with' }));
  const option = Array.from(document.querySelectorAll('.ant-select-item-option')).find(
    (node) => node.textContent === label,
  );
  if (!option) {
    throw new Error(`no option "${label}"`);
  }
  fireEvent.click(option);
}

function explanation() {
  return screen.getByTestId('saved-filter-default-audience-explanation').textContent;
}

beforeEach(() => {
  savePreferences.mockReset();
  groups = [];
  useUiPrefsStore.getState().releaseAccount();
  useUiPrefsStore.getState().adoptAccount('account-a');
});

afterEach(() => {
  cleanup();
  useUiPrefsStore.getState().releaseAccount();
  localStorage.removeItem('silexgis.uiPrefs');
});

describe('the default audience of saved filters, in Settings', () => {
  it('is private for an account that has chosen nothing, and says so', () => {
    groups = [{ id: 'g-1', name: 'Speo Alpha' }];
    show();

    expect(shown()).toBe('Only me');
    expect(explanation()).toBe('Only you can see this filter.');
    expect(savePreferences).not.toHaveBeenCalled();
  });

  it('stores a choice of everyone signed in with the account, through the one writer', () => {
    show();

    pick('Everyone signed in');

    expect(useUiPrefsStore.getState().savedFilterAudience).toEqual({ visibility: 'authenticated' });
    expect(savePreferences).toHaveBeenCalledTimes(1);
    expect(explanation()).toMatch(/^Everyone signed in can use it — it shows each of them only what they can already see\.$/);
  });

  it('offers each of a member’s own clubs, and keeps the one they pick', () => {
    groups = [
      { id: 'g-1', name: 'Speo Alpha' },
      { id: 'g-2', name: 'Speo Beta' },
    ];
    show();

    pick('Members of Speo Beta');

    expect(useUiPrefsStore.getState().savedFilterAudience).toEqual({ visibility: 'cavingGroup', cavingGroupId: 'g-2' });
    expect(explanation()).toBe('Members of Speo Beta can use it — it shows each of them only what they can already see.');
    expect(shown()).toBe('Members of Speo Beta');
  });

  it('offers no club to a member who belongs to none', () => {
    show();

    fireEvent.mouseDown(screen.getByRole('combobox', { name: 'Share new filters with' }));
    const labels = Array.from(document.querySelectorAll('.ant-select-item-option'), (node) => node.textContent);
    expect(labels).toEqual(['Only me', 'Everyone signed in']);
  });

  it('keeps each account its own choice: the next member on this browser starts private', () => {
    groups = [{ id: 'g-1', name: 'Speo Alpha' }];
    show();
    pick('Members of Speo Alpha');
    cleanup();

    useUiPrefsStore.getState().adoptAccount('account-b');
    show();

    expect(shown()).toBe('Only me');
    expect(useUiPrefsStore.getState().savedFilterAudience).toEqual({ visibility: 'private' });
  });

  it('says a club the member has left is no longer the default, and treats it as private', () => {
    useUiPrefsStore.getState().setSavedFilterAudience({ visibility: 'cavingGroup', cavingGroupId: 'g-gone' });
    groups = [{ id: 'g-1', name: 'Speo Alpha' }];
    show();

    expect(shown()).toBe('Only me');
    expect(explanation()).toBe('Only you can see this filter.');
    expect(screen.getByRole('alert')).toHaveTextContent('no longer one you can share with');
  });

  it('puts the keyboard on the choice when reached by the “change your default” link', () => {
    show(SAVED_FILTER_AUDIENCE_SETTINGS_PATH);

    const combobox = screen.getByRole('combobox', { name: 'Share new filters with' });
    expect(document.activeElement).toBe(combobox);
    expect(within(document.getElementById('default-audience')!).getByRole('combobox')).toBe(combobox);
  });
});
