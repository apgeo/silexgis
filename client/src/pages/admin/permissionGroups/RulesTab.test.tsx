// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import i18n from '../../../i18n';
import type { AccessCatalog, PermissionGroup } from '../../../api/hooks.ts';

const t = i18n.getFixedT('en');

type Entry = {
  id: number;
  effect: 'allow' | 'deny';
  domain: 'features' | 'tripLogs';
  actions: string;
  scopeKind: 'all' | 'own';
  scopeId: null;
  scopeLabel: null;
  featureKind: null;
  featureTypeId: null;
};

// What the group's rules request answers right now: still out, failed, or arrived.
let entriesQuery: { data: Entry[] | undefined; isPending: boolean; isError: boolean };
const refetch = vi.fn();
const replace = vi.fn();

vi.mock('../../../api/hooks.ts', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../../api/hooks.ts')>();
  return {
    parseAccessActions: actual.parseAccessActions,
    usePermissionGroupEntries: () => ({ ...entriesQuery, refetch }),
    useReplacePermissionGroupEntries: () => ({ mutateAsync: replace, isPending: false }),
    useGenericFeatureTypes: () => ({ data: [] }),
  };
});

const { default: RulesTab } = await import('./RulesTab.tsx');

const catalog = {
  domains: [
    {
      domain: 'features',
      name: 'features',
      supportsKindNarrowing: false,
      scopes: [
        { scopeKind: 'all', requiresAnchor: false, actions: ['read', 'update'] },
        { scopeKind: 'own', requiresAnchor: false, actions: ['read'] },
      ],
    },
    {
      domain: 'tripLogs',
      name: 'tripLogs',
      supportsKindNarrowing: false,
      scopes: [{ scopeKind: 'all', requiresAnchor: false, actions: ['read', 'delete'] }],
    },
  ],
  featureSets: [],
} as unknown as AccessCatalog;

const group = {
  id: 'group-1',
  name: 'Surveyors',
  slug: 'surveyors',
  description: null,
  isProtected: false,
  isSeeded: false,
} as unknown as PermissionGroup;

const stored: Entry[] = [
  {
    id: 1,
    effect: 'allow',
    domain: 'features',
    actions: 'read, update',
    scopeKind: 'all',
    scopeId: null,
    scopeLabel: null,
    featureKind: null,
    featureTypeId: null,
  },
];

const rulesChanged = vi.fn();

function tab(previewSeen = false) {
  return (
    <App>
      <RulesTab
        group={group}
        catalog={catalog}
        previewSeen={previewSeen}
        onRulesChanged={rulesChanged}
        onRequestPreview={() => {}}
      />
    </App>
  );
}

const saveButton = () => screen.getByTestId('save-rules');
const addButton = () => screen.getByRole('button', { name: t('permissionGroups.addRule') });

beforeEach(() => {
  entriesQuery = { data: stored, isPending: false, isError: false };
  refetch.mockReset();
  // The read made just before a save answers whatever is stored at that moment.
  refetch.mockImplementation(() => Promise.resolve({ data: entriesQuery.data }));
  rulesChanged.mockReset();
  replace.mockReset();
  replace.mockResolvedValue([]);
});

afterEach(cleanup);

describe('RulesTab before the stored rules are known', () => {
  // Saving is a full replace: a save sent before the rules arrived would replace the group's
  // real rules with an empty list, or with only the row just added.
  it('offers neither Save nor Add rule while the rules are still on their way', () => {
    entriesQuery = { data: undefined, isPending: true, isError: false };
    render(tab());

    expect(saveButton()).toBeDisabled();
    expect(addButton()).toBeDisabled();
  });

  it('names the failed read, offers a retry, and keeps the editor shut', () => {
    entriesQuery = { data: undefined, isPending: false, isError: true };
    render(tab());

    expect(screen.getByText(t('permissionGroups.rulesLoadFailed'))).toBeInTheDocument();
    expect(saveButton()).toBeDisabled();
    expect(addButton()).toBeDisabled();

    fireEvent.click(screen.getByRole('button', { name: t('common.retry') }));
    expect(refetch).toHaveBeenCalledTimes(1);
  });

  it('saves the stored rules plus the added one once the rules have arrived', async () => {
    render(tab());

    // Nothing changed yet: there is nothing to save.
    expect(saveButton()).toBeDisabled();
    fireEvent.click(addButton());
    expect(saveButton()).toBeEnabled();

    await act(async () => {
      fireEvent.click(saveButton());
    });
    expect(replace).toHaveBeenCalledTimes(1);
    const sent = replace.mock.calls[0][0] as { actions: string }[];
    expect(sent).toHaveLength(2);
  });
});

describe('RulesTab while rules are being edited', () => {
  it('keeps an unsaved edit when the stored rules are read again behind it', () => {
    const { rerender } = render(tab());
    fireEvent.click(addButton());
    expect(screen.getAllByRole('row')).toHaveLength(3);

    // A background read (a focus refetch, another tab's save) answers a new list.
    entriesQuery = { data: [...stored], isPending: false, isError: false };
    rerender(tab());

    expect(screen.getAllByRole('row')).toHaveLength(3);
    expect(saveButton()).toBeEnabled();
  });
});

/** Another administrator's save: the stored rules now hold one more row. */
const movedOn: Entry[] = [
  ...stored,
  { ...stored[0], id: 2, domain: 'tripLogs', actions: 'read', scopeKind: 'all' },
];

/** Picks an option of the antd Select labelled `label` in the first rule row. */
function choose(label: string, option: string) {
  fireEvent.mouseDown(screen.getAllByRole('combobox', { name: label })[0]);
  const item = Array.from(document.querySelectorAll('.ant-select-item-option')).find(
    (node) => node.textContent === option && !node.closest('.ant-select-dropdown-hidden'),
  );
  if (!item) {
    throw new Error(`no option "${option}" in ${label}`);
  }
  fireEvent.click(item);
}

describe('RulesTab when the stored rules move under an open edit', () => {
  it('says so, holds the save, and loads the stored rules when asked', () => {
    const { rerender } = render(tab());
    fireEvent.click(addButton());

    entriesQuery = { data: movedOn, isPending: false, isError: false };
    rerender(tab());

    expect(screen.getByTestId('rules-stored-moved')).toHaveTextContent(t('permissionGroups.storedRulesMoved'));
    expect(saveButton()).toBeDisabled();
    // The edit is still there: nothing is dropped without the administrator's say.
    expect(screen.getAllByRole('row')).toHaveLength(3);

    fireEvent.click(screen.getByRole('button', { name: t('permissionGroups.loadStoredRules') }));
    expect(screen.queryByTestId('rules-stored-moved')).not.toBeInTheDocument();
    expect(screen.getAllByRole('row')).toHaveLength(3); // header + the two stored rules
    expect(saveButton()).toBeDisabled(); // nothing of the administrator's left to save
  });

  it('saves over the other change only once the administrator says to', async () => {
    const { rerender } = render(tab());
    fireEvent.click(addButton());
    entriesQuery = { data: movedOn, isPending: false, isError: false };
    rerender(tab());

    fireEvent.click(screen.getByRole('button', { name: t('permissionGroups.keepMyRules') }));
    expect(saveButton()).toBeEnabled();
    await act(async () => {
      fireEvent.click(saveButton());
    });
    expect(replace).toHaveBeenCalledTimes(1);
  });

  it('reads the stored rules again just before saving, and does not save over a change it finds', async () => {
    render(tab());
    fireEvent.click(addButton());
    // Nothing refetched on its own; the save's own read is the first to see the other change.
    refetch.mockResolvedValue({ data: movedOn });

    await act(async () => {
      fireEvent.click(saveButton());
    });

    expect(refetch).toHaveBeenCalledTimes(1);
    expect(replace).not.toHaveBeenCalled();
    expect(screen.getByTestId('rules-stored-moved')).toBeInTheDocument();
    expect(saveButton()).toBeDisabled();
  });
});

describe('RulesTab with a deny in the ruleset', () => {
  it('holds the save until the preview has been opened, and says what the preview does and does not show', () => {
    const { rerender } = render(tab(false));
    choose(t('access.effect'), t('access.effects.deny'));

    expect(screen.getByText(t('permissionGroups.previewRequired'))).toBeInTheDocument();
    expect(saveButton()).toBeDisabled();
    expect(rulesChanged).toHaveBeenCalled();

    rerender(tab(true));
    expect(screen.queryByText(t('permissionGroups.previewRequired'))).not.toBeInTheDocument();
    expect(saveButton()).toBeEnabled();
  });

  it('tells the administrator to look at the preview again once a deny is saved', async () => {
    render(tab(true));
    choose(t('access.effect'), t('access.effects.deny'));

    await act(async () => {
      fireEvent.click(saveButton());
    });
    expect(replace).toHaveBeenCalledTimes(1);
    expect(await screen.findByText(t('permissionGroups.savedWithDeny'))).toBeInTheDocument();
  });
});

describe('RulesTab keeping a rule valid as its domain or scope changes', () => {
  it('drops the actions the new scope does not accept', async () => {
    render(tab(true));
    choose(t('access.scope'), t('access.scopes.own'));
    await act(async () => {
      fireEvent.click(saveButton());
    });
    const sent = replace.mock.calls[0][0] as { actions: string; scopeKind: string }[];
    expect(sent[0]).toMatchObject({ scopeKind: 'own', actions: 'read' });
  });

  it('drops the actions the new domain does not offer, keeping those it does', async () => {
    render(tab(true));
    choose(t('access.domain'), t('access.domains.tripLogs'));
    await act(async () => {
      fireEvent.click(saveButton());
    });
    const sent = replace.mock.calls[0][0] as { actions: string; domain: string }[];
    expect(sent[0]).toMatchObject({ domain: 'tripLogs', actions: 'read' });
  });
});
