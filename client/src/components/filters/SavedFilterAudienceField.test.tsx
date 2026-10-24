// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { App as AntApp } from 'antd';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import i18n from '../../i18n';
import {
  DEFAULT_SAVED_FILTER_AUDIENCE,
  SAVED_FILTER_AUDIENCE_SETTINGS_PATH,
  type SavedFilterAudience,
} from '../../filters/savedFilterAudience.ts';
import type { FilterNode } from '../../filters/types.ts';
import { useUiPrefsStore } from '../../stores/uiPrefsStore.ts';

const { createSpy, door } = vi.hoisted(() => ({
  createSpy: vi.fn(),
  door: {
    groups: [] as { id: string; name: string }[],
    // Every club of the installation, the member's own among them.
    all: [] as { id: string; name: string; slug: string }[],
  },
}));

vi.mock('../../api/hooks/access.ts', () => ({
  useCapabilities: () => ({ data: {}, isLoading: false }),
  useCreateDoor: () => ({ canCreate: true, unbound: false, cavingGroups: door.groups }),
}));

vi.mock('../../api/hooks/people.ts', () => ({
  useCavingGroups: () => ({ data: door.all }),
}));

vi.mock('../../api/hooks/savedFilters.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/hooks/savedFilters.ts')>(
    '../../api/hooks/savedFilters.ts',
  );
  return {
    documentOf: actual.documentOf,
    useSavedFilters: () => ({ data: [], isSuccess: true }),
    useCreateSavedFilter: () => ({ mutateAsync: createSpy, isPending: false }),
  };
});

const { default: CaveRegistrySavedFilters } = await import('../../pages/caves/CaveRegistrySavedFilters.tsx');
const { default: GallerySavedFilters } = await import('../../pages/gallery/GallerySavedFilters.tsx');

const TITLED: FilterNode = {
  node: 'condition',
  field: 'title',
  op: 'contains',
  values: [{ type: 'text', value: 'entrance' }],
};

function Address() {
  const location = useLocation();
  return <span data-testid="address">{`${location.pathname}${location.hash}`}</span>;
}

function showRegistry() {
  return render(
    <AntApp>
      <MemoryRouter initialEntries={['/caves']}>
        <CaveRegistrySavedFilters
          reading={{ controls: { region: 'Bihor' }, extra: [] }}
          onApply={vi.fn()}
          onSaved={vi.fn()}
          onClearExtra={vi.fn()}
          onRelease={vi.fn()}
          onForget={vi.fn()}
        />
        <Address />
      </MemoryRouter>
    </AntApp>,
  );
}

function showGallery() {
  return render(
    <AntApp>
      <MemoryRouter initialEntries={['/gallery']}>
        <GallerySavedFilters where={TITLED} onApply={vi.fn()} onSaved={vi.fn()} onForget={vi.fn()} />
        <Address />
      </MemoryRouter>
    </AntApp>,
  );
}

function shownAudience(dialog: HTMLElement, testId: string) {
  return within(dialog)
    .getByTestId(testId)
    .querySelector('.ant-select-selection-item, .ant-select-content')?.textContent;
}

function storeDefault(audience: SavedFilterAudience) {
  useUiPrefsStore.setState({ savedFilterAudience: audience });
}

afterEach(cleanup);
beforeEach(async () => {
  await i18n.changeLanguage('en');
  createSpy.mockReset().mockResolvedValue({ id: 'new', name: 'Mine' });
  door.groups = [{ id: 'g-1', name: 'Speo Club' }];
  door.all = [
    { id: 'g-2', name: 'Clubul Vecin', slug: 'clubul-vecin' },
    { id: 'g-1', name: 'Speo Club', slug: 'speo-club' },
  ];
  storeDefault(DEFAULT_SAVED_FILTER_AUDIENCE);
});

async function openRegistryDialog() {
  showRegistry();
  fireEvent.click(screen.getByTestId('cave-registry-save-filter'));
  return screen.findByRole('dialog');
}

async function saveAs(dialog: HTMLElement, nameTestId: string) {
  fireEvent.change(within(dialog).getByTestId(nameTestId), { target: { value: 'Mine' } });
  fireEvent.click(within(dialog).getByRole('button', { name: 'Save' }));
  await waitFor(() => expect(createSpy).toHaveBeenCalledTimes(1));
  return createSpy.mock.calls[0]![0] as { visibility: string; cavingGroupId: string | null };
}

describe('the audience where a filter is saved from the cave registry', () => {
  it('starts private and says so, for a member who chose no default', async () => {
    const dialog = await openRegistryDialog();

    expect(shownAudience(dialog, 'saved-filter-audience')).toBe('Only me');
    expect(within(dialog).getByTestId('saved-filter-audience-explanation')).toHaveTextContent(
      'Only you can see this filter.',
    );
    const sent = await saveAs(dialog, 'saved-filter-name');
    expect(sent).toMatchObject({ visibility: 'private', cavingGroupId: null });
  });

  it('starts with the default chosen in Settings and sends it', async () => {
    storeDefault({ visibility: 'authenticated' });
    const dialog = await openRegistryDialog();

    expect(shownAudience(dialog, 'saved-filter-audience')).toBe('Everyone signed in');
    expect(within(dialog).getByTestId('saved-filter-audience-explanation')).toHaveTextContent(
      'Everyone signed in can use it — it shows each of them only what they can already see.',
    );
    const sent = await saveAs(dialog, 'saved-filter-name');
    expect(sent).toMatchObject({ visibility: 'authenticated', cavingGroupId: null });
  });

  it('starts with a default club the member can still share with, and names it', async () => {
    storeDefault({ visibility: 'cavingGroup', cavingGroupId: 'g-1' });
    const dialog = await openRegistryDialog();

    expect(within(dialog).getByTestId('saved-filter-audience-explanation')).toHaveTextContent(
      'Members of Speo Club can use it',
    );
    const sent = await saveAs(dialog, 'saved-filter-name');
    expect(sent).toMatchObject({ visibility: 'cavingGroup', cavingGroupId: 'g-1' });
  });

  it('falls back to private when the default club is no longer one the member can share with', async () => {
    storeDefault({ visibility: 'cavingGroup', cavingGroupId: 'g-gone' });
    const dialog = await openRegistryDialog();

    expect(shownAudience(dialog, 'saved-filter-audience')).toBe('Only me');
    const sent = await saveAs(dialog, 'saved-filter-name');
    expect(sent).toMatchObject({ visibility: 'private', cavingGroupId: null });
  });

  it('offers you, the member’s clubs, everyone signed in, then every other club — never anonymous', async () => {
    const dialog = await openRegistryDialog();
    fireEvent.mouseDown(within(dialog).getByRole('combobox', { name: 'Who can use it' }));

    await screen.findByTitle('Everyone signed in');
    const labels = Array.from(document.querySelectorAll('.ant-select-item-option-content'), (option) => option.textContent);
    expect(labels).toEqual(['Only me', 'Members of Speo Club', 'Everyone signed in', 'Members of Clubul Vecin']);
    // The other clubs sit under a heading that says the server may refuse them.
    expect(document.querySelector('.ant-select-item-group')).toHaveTextContent(
      'Other clubs — you may not be allowed to share with these',
    );
  });

  it('says in words when the server refuses a club the member may not share with', async () => {
    const { ApiError } = await import('../../api/client.ts');
    createSpy.mockReset().mockRejectedValue(new ApiError(403, 'access.caving_group_binding_forbidden'));
    const dialog = await openRegistryDialog();
    fireEvent.mouseDown(within(dialog).getByRole('combobox', { name: 'Who can use it' }));
    fireEvent.click(await screen.findByTitle('Members of Clubul Vecin'));

    await saveAs(dialog, 'saved-filter-name');
    expect(createSpy.mock.calls[0]![0]).toMatchObject({ visibility: 'cavingGroup', cavingGroupId: 'g-2' });
    expect(await screen.findByText(/You may not share filters with this club/)).toBeTruthy();
    // The dialog stays open, so another audience can be chosen.
    expect(screen.getByRole('dialog')).toBeTruthy();
  });

  it('lets a choice made in the dialog win over the default, and explains the new choice', async () => {
    storeDefault({ visibility: 'authenticated' });
    const dialog = await openRegistryDialog();
    fireEvent.mouseDown(within(dialog).getByRole('combobox', { name: 'Who can use it' }));
    fireEvent.click(await screen.findByTitle('Only me'));

    expect(within(dialog).getByTestId('saved-filter-audience-explanation')).toHaveTextContent(
      'Only you can see this filter.',
    );
    const sent = await saveAs(dialog, 'saved-filter-name');
    expect(sent).toMatchObject({ visibility: 'private', cavingGroupId: null });
  });

  it('ties the explanation to the choice and links to the default in Settings', async () => {
    const dialog = await openRegistryDialog();
    const combobox = within(dialog).getByRole('combobox', { name: 'Who can use it' });
    const explanation = within(dialog).getByTestId('saved-filter-audience-explanation');
    expect(combobox.getAttribute('aria-describedby')).toContain(explanation.id);

    const link = within(dialog).getByRole('link', { name: /Change your default/ });
    expect(link).toHaveAttribute('href', SAVED_FILTER_AUDIENCE_SETTINGS_PATH);
    fireEvent.click(link);
    expect(screen.getByTestId('address')).toHaveTextContent(SAVED_FILTER_AUDIENCE_SETTINGS_PATH);
  });

  it('says it in Romanian too', async () => {
    await i18n.changeLanguage('ro');
    storeDefault({ visibility: 'authenticated' });
    const dialog = await openRegistryDialog();

    expect(within(dialog).getByTestId('saved-filter-audience-explanation')).toHaveTextContent(
      'Toți cei autentificați îl pot folosi',
    );
    expect(within(dialog).getByRole('link', { name: /Schimbă alegerea implicită/ })).toBeInTheDocument();
  });
});

describe('the audience where a gallery view is saved', () => {
  it('starts with the default chosen in Settings and sends it', async () => {
    storeDefault({ visibility: 'cavingGroup', cavingGroupId: 'g-1' });
    showGallery();
    fireEvent.click(screen.getByTestId('gallery-save-filter'));
    const dialog = await screen.findByRole('dialog');

    expect(within(dialog).getByTestId('gallery-saved-filter-audience-explanation')).toHaveTextContent(
      'Members of Speo Club can use it',
    );
    expect(within(dialog).getByRole('link', { name: /Change your default/ })).toHaveAttribute(
      'href',
      SAVED_FILTER_AUDIENCE_SETTINGS_PATH,
    );
    const sent = await saveAs(dialog, 'gallery-saved-filter-name');
    expect(sent).toMatchObject({ visibility: 'cavingGroup', cavingGroupId: 'g-1' });
  });
});

