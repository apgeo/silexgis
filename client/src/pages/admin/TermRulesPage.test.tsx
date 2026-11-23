// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import i18n from '../../i18n';
import { ApiError } from '../../api/client.ts';
import type { TermRuleSetInfo } from '../../api/hooks.ts';

/**
 * The page listing the club's naming rule sets: the list itself, a read that failed, and each
 * write it makes — copying a set to edit, deleting, promoting and importing — with the sentence
 * each refusal is said in. The sets below are invented.
 */

const create = vi.fn();
const remove = vi.fn();
const setScope = vi.fn();
const importSet = vi.fn();
const refetch = vi.fn();
let setsRead: Record<string, unknown>;

vi.mock('../../api/hooks.ts', () => ({
  useTermRuleSets: () => setsRead,
  useCreateTermRuleSet: () => ({ mutateAsync: create, isPending: false }),
  useDeleteTermRuleSet: () => ({ mutateAsync: remove, isPending: false }),
  useSetTermRuleSetScope: () => ({ mutateAsync: setScope, isPending: false }),
  useImportTermRuleSet: () => ({ mutateAsync: importSet, isPending: false }),
}));

// The editor has tests of its own; here only which set it was asked to open.
vi.mock('../../components/import/TermRuleSetEditor.tsx', () => ({
  default: ({ setId }: { setId: string | null }) => <div data-testid="editor" data-set={setId ?? ''} />,
}));

const { default: TermRulesPage } = await import('./TermRulesPage.tsx');

const t = i18n.getFixedT('en');

function set(overrides: Partial<TermRuleSetInfo>): TermRuleSetInfo {
  return {
    id: 's1',
    name: 'Invented club habits',
    description: null,
    scope: 'user',
    isDefault: false,
    isSeeded: false,
    ruleCount: 4,
    canEdit: true,
    canDelete: true,
    ...overrides,
  } as TermRuleSetInfo;
}

function show() {
  render(
    <App>
      <TermRulesPage />
    </App>,
  );
}

function rowOf(name: string) {
  return screen.getByText(name).closest('tr') as HTMLElement;
}

async function pickFile(content: string, name = 'rules.json') {
  const input = screen.getByLabelText(t('termRules.importFile'), { selector: 'input' });
  await act(async () => {
    fireEvent.change(input, { target: { files: [new File([content], name, { type: 'application/json' })] } });
  });
}

beforeEach(async () => {
  await i18n.changeLanguage('en');
  for (const spy of [create, remove, setScope, importSet, refetch]) {
    spy.mockReset();
  }
  setsRead = {
    data: [set({}), set({ id: 's2', name: 'Shipped defaults', scope: 'installation', isSeeded: true, canEdit: false })],
    isLoading: false,
    isError: false,
    error: null,
    refetch,
  };
});
afterEach(cleanup);

describe('TermRulesPage', () => {
  it('lists every set with its scope and how many rules it holds', () => {
    show();
    expect(within(rowOf('Invented club habits')).getByText('4')).toBeInTheDocument();
    expect(within(rowOf('Shipped defaults')).getByText(t('termRules.shipped'))).toBeInTheDocument();
    expect(screen.queryByTestId('term-rule-sets-load-failed')).toBeNull();
  });

  it('names a failed read of the sets instead of an empty list, and reads again on Retry', () => {
    setsRead = { data: undefined, isLoading: false, isError: true, error: new TypeError('Failed to fetch'), refetch };
    show();
    expect(screen.getByTestId('term-rule-sets-load-failed')).toHaveTextContent(t('admin.listLoadFailed'));
    fireEvent.click(screen.getByRole('button', { name: t('common.retry') }));
    expect(refetch).toHaveBeenCalledTimes(1);
  });

  it('opens the copy it made of a set the reader may not change, and says why a copy was refused', async () => {
    create
      .mockResolvedValueOnce({ set: { id: 'copy-1' } })
      .mockRejectedValueOnce(new ApiError(404, 'term_rules.not_found', 'Not found.'));
    show();

    fireEvent.click(within(rowOf('Shipped defaults')).getByRole('button', { name: t('termRules.editCopy') }));
    await waitFor(() => expect(screen.getByTestId('editor')).toHaveAttribute('data-set', 'copy-1'));
    expect(create.mock.calls[0][0]).toMatchObject({ copyFromId: 's2' });

    fireEvent.click(within(rowOf('Shipped defaults')).getByRole('button', { name: t('termRules.editCopy') }));
    expect(await screen.findByText(t('termRules.errors.notFound'))).toBeInTheDocument();
  });

  it('says why a deletion was refused, in the words for its code', async () => {
    remove.mockRejectedValueOnce(new ApiError(409, 'term_rules.seeded_undeletable', 'Shipped.'));
    show();
    fireEvent.click(within(rowOf('Invented club habits')).getByRole('button', { name: t('iconButtons.delete') }));
    fireEvent.click((await screen.findAllByRole('button', { name: 'OK' })).at(-1)!);

    expect(await screen.findByText(t('termRules.errors.seededUndeletable'))).toBeInTheDocument();
    expect(remove).toHaveBeenCalledWith('s1');
    expect(screen.queryByText(t('common.deleted'))).toBeNull();
  });

  it('says why making a set the installation default was refused', async () => {
    setScope.mockRejectedValueOnce(new ApiError(403, 'term_rules.promotion_forbidden', 'Forbidden.'));
    show();
    fireEvent.click(within(rowOf('Invented club habits')).getByRole('button', { name: t('termRules.promote') }));

    expect(await screen.findByText(t('termRules.errors.promotionForbidden'))).toBeInTheDocument();
    expect(setScope.mock.calls[0][0]).toMatchObject({ id: 's1', body: { scope: 'installation', isDefault: true } });
  });

  it('opens an imported set, and says what the server found wrong with a refused one', async () => {
    importSet
      .mockResolvedValueOnce({ set: { id: 'imported-1' } })
      .mockRejectedValueOnce(new ApiError(400, 'term_rules.invalid', 'Rule 3: the pattern "([" cannot be compiled.'));
    show();

    await pickFile(JSON.stringify({ name: 'From the neighbours', rules: [] }));
    await waitFor(() => expect(screen.getByTestId('editor')).toHaveAttribute('data-set', 'imported-1'));
    expect(importSet.mock.calls[0][0]).toMatchObject({ name: 'From the neighbours' });

    await pickFile(JSON.stringify({ name: 'Broken', rules: [] }));
    expect(await screen.findByText('Rule 3: the pattern "([" cannot be compiled.')).toBeInTheDocument();
  });

  it('says a file that is not a rule set could not be read, without sending it', async () => {
    show();
    await pickFile('this is not JSON', 'notes.txt');
    expect(await screen.findByText(t('termRules.importFailed'))).toBeInTheDocument();
    expect(importSet).not.toHaveBeenCalled();
  });
});
