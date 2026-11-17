// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import i18n from '../../i18n';
import { ApiError } from '../../api/client.ts';
import type { TermRule } from '../../api/hooks.ts';

const updateMutate = vi.fn();
const createMutate = vi.fn();

const rule: TermRule = {
  id: 'rule-1',
  name: 'Caves',
  enabled: true,
  matchMode: 'wholeWord',
  matchName: true,
  matchDescription: false,
  terms: { ro: ['pestera'] },
  target: 'cave',
  strip: 'none',
};

const detailOf = (name: string) => ({
  set: { id: 'set-1', name, description: null },
  rules: [rule],
});

let detail: ReturnType<typeof detailOf> | undefined = detailOf('Club rules');
let detailFailed = false;
const refetchDetail = vi.fn();

vi.mock('../../api/hooks.ts', () => ({
  useTermRuleSet: () => ({
    data: detail,
    isPending: detail === undefined && !detailFailed,
    isError: detailFailed,
    refetch: refetchDetail,
  }),
  useCreateTermRuleSet: () => ({ mutateAsync: createMutate, isPending: false }),
  useUpdateTermRuleSet: () => ({ mutateAsync: updateMutate, isPending: false }),
  useCaveTypes: () => ({ data: [] }),
  useEntranceTypes: () => ({ data: [] }),
  useGenericFeatureTypes: () => ({ data: [] }),
}));

const { default: TermRuleSetEditor } = await import('./TermRuleSetEditor.tsx');

function editor(onClose: () => void) {
  return (
    <App>
      <TermRuleSetEditor setId="set-1" creating={false} onClose={onClose} />
    </App>
  );
}

const nameInput = () => screen.getByDisplayValue<HTMLInputElement>(/Club rules|Mine/);

afterEach(cleanup);

beforeEach(() => {
  updateMutate.mockReset();
  createMutate.mockReset();
  detail = detailOf('Club rules');
  detailFailed = false;
  refetchDetail.mockReset();
});

describe('a set that has not been read', () => {
  it('offers no save while the set is still being read', async () => {
    detail = undefined;
    render(editor(vi.fn()));

    const save = await screen.findByTestId('term-rules-save');
    expect(save).toBeDisabled();
    fireEvent.click(save);
    await new Promise((resolve) => setTimeout(resolve, 20));
    expect(updateMutate).not.toHaveBeenCalled();
  });

  it('says the read failed, offers a retry, and never saves an empty set over the stored one', async () => {
    detail = undefined;
    detailFailed = true;
    render(editor(vi.fn()));

    expect(
      await screen.findByText('The rule set could not be read, so it cannot be changed yet.'),
    ).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
    expect(refetchDetail).toHaveBeenCalledTimes(1);

    const save = screen.getByTestId('term-rules-save');
    expect(save).toBeDisabled();
    fireEvent.click(save);
    await new Promise((resolve) => setTimeout(resolve, 20));
    expect(updateMutate).not.toHaveBeenCalled();
  });
});

describe('saving a rule set the server refuses', () => {
  it("shows the server's sentence about the set, not a status line", async () => {
    updateMutate.mockRejectedValue(
      new ApiError(400, 'term_rules.invalid', 'Rule 1: the pattern "(" cannot be compiled.'),
    );
    render(editor(vi.fn()));

    fireEvent.click(await screen.findByTestId('term-rules-save'));

    expect(await screen.findByText('Rule 1: the pattern "(" cannot be compiled.')).toBeInTheDocument();
    expect(screen.queryByText(/API error/)).toBeNull();
  });

  it('words a refusal it knows the code of', async () => {
    updateMutate.mockRejectedValue(new ApiError(403, 'term_rules.not_editable'));
    render(editor(vi.fn()));

    fireEvent.click(await screen.findByTestId('term-rules-save'));

    expect(await screen.findByText(i18n.t('termRules.errors.notEditable'))).toBeInTheDocument();
  });

  it('says the save failed when the server said nothing usable', async () => {
    updateMutate.mockRejectedValue(new ApiError(500));
    render(editor(vi.fn()));

    fireEvent.click(await screen.findByTestId('term-rules-save'));

    expect(await screen.findByText(i18n.t('common.saveFailed'))).toBeInTheDocument();
    expect(screen.queryByText(/API error/)).toBeNull();
  });
});

describe('closing the rule editor', () => {
  it('closes straight away when nothing was changed', async () => {
    const onClose = vi.fn();
    render(editor(onClose));
    await screen.findByDisplayValue('Club rules');

    fireEvent.click(screen.getByText(i18n.t('common.cancel')));

    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('asks before throwing away changes, and keeps them if the answer is to stay', async () => {
    const onClose = vi.fn();
    render(editor(onClose));
    fireEvent.change(await screen.findByDisplayValue('Club rules'), { target: { value: 'Mine' } });

    fireEvent.click(screen.getByText(i18n.t('common.cancel')));

    // The dialog names itself for assistive technology as well, so the title is found twice.
    expect((await screen.findAllByText(i18n.t('termRules.discardTitle'))).length).toBeGreaterThan(0);
    expect(onClose).not.toHaveBeenCalled();

    fireEvent.click(screen.getByText(i18n.t('termRules.keepEditing')));
    // The dialog leaves through an animation that never ends here, so what is asserted is the
    // outcome: nothing closed, and the change is still in the field.
    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(onClose).not.toHaveBeenCalled();
    expect(nameInput().value).toBe('Mine');
  });

  it('closes once the reader agrees to lose the changes', async () => {
    const onClose = vi.fn();
    render(editor(onClose));
    fireEvent.change(await screen.findByDisplayValue('Club rules'), { target: { value: 'Mine' } });

    fireEvent.click(screen.getByText(i18n.t('common.cancel')));
    fireEvent.click(await screen.findByText(i18n.t('termRules.discardOk')));

    await waitFor(() => expect(onClose).toHaveBeenCalledTimes(1));
  });
});

describe('the set being read again while it is edited', () => {
  it('keeps what was typed when the same set arrives again', async () => {
    const { rerender } = render(editor(vi.fn()));
    fireEvent.change(await screen.findByDisplayValue('Club rules'), { target: { value: 'Mine' } });

    // A refetch — the window regained focus, or another change invalidated the sets — hands the
    // editor a new copy of the same set.
    detail = detailOf('Club rules');
    rerender(editor(vi.fn()));

    expect(nameInput().value).toBe('Mine');
  });
});
