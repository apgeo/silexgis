// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi, type Mock } from 'vitest';
import i18n from '../../i18n';
import type { CabinetInfo } from '../../api/hooks.ts';

/**
 * Filing many documents at once: what is sent for a copy and for a move, and what is said for a
 * whole, a partial and a refused answer. A move takes documents off the shelf being looked at, so a
 * request that sent the wrong shelves would lose or duplicate an archive without a word.
 */

const t = i18n.getFixedT('en');
const refile = vi.fn();

vi.mock('../../api/hooks.ts', () => ({
  useBulkFiling: () => ({ mutateAsync: refile, isPending: false }),
}));

const { default: BulkFilingBar } = await import('./BulkFilingBar.tsx');

const cabinet = (id: string, name: string): CabinetInfo => ({ id, name, ancestorIds: [id] }) as unknown as CabinetInfo;
const CABINETS = [cabinet('cab-old', 'Old shelf'), cabinet('cab-new', 'New shelf')];
const DOCUMENTS = ['doc-1', 'doc-2', 'doc-3'];

let onDone: Mock<() => void>;

beforeEach(() => {
  refile.mockReset();
  onDone = vi.fn();
});

afterEach(cleanup);

function show(currentCabinetId?: string) {
  render(
    <App>
      <BulkFilingBar documentIds={DOCUMENTS} cabinets={CABINETS} currentCabinetId={currentCabinetId} onDone={onDone} />
    </App>,
  );
}

async function fileInto(action: string, label: string) {
  fireEvent.click(screen.getByRole('button', { name: action }));
  const dialog = (await screen.findAllByText(action)).map((node) => node.closest('.ant-modal')).find(Boolean) as HTMLElement;
  fireEvent.mouseDown(within(dialog).getByRole('combobox'));
  const option = await waitFor(() => {
    const found = document.querySelector(`.ant-select-dropdown [title="${label}"]`);
    expect(found).not.toBeNull();
    return found as HTMLElement;
  });
  fireEvent.click(option);
  await act(async () => {
    fireEvent.click(within(dialog).getByRole('button', { name: 'OK' }));
  });
  return dialog;
}

describe('filing a selection', () => {
  it('moves the selection off the shelf being looked at and onto the one chosen', async () => {
    refile.mockResolvedValue({ filed: DOCUMENTS, refused: {} });
    show('cab-old');

    await fileInto(t('cabinets.moveTo'), 'New shelf');

    expect(refile).toHaveBeenCalledWith({
      documentIds: DOCUMENTS,
      fileIntoCabinetIds: ['cab-new'],
      unfileFromCabinetIds: ['cab-old'],
    });
    expect(await screen.findByText(t('cabinets.bulkFiled', { count: 3 }))).toBeInTheDocument();
    expect(onDone).toHaveBeenCalledTimes(1);
  });

  it('copies without taking anything off any shelf', async () => {
    refile.mockResolvedValue({ filed: DOCUMENTS, refused: {} });
    show('cab-old');

    await fileInto(t('cabinets.copyTo'), 'New shelf');

    expect(refile).toHaveBeenCalledWith({
      documentIds: DOCUMENTS,
      fileIntoCabinetIds: ['cab-new'],
      unfileFromCabinetIds: undefined,
    });
  });

  it('does not offer the shelf being looked at as the destination, nor a move from the inbox', async () => {
    show();
    expect(screen.queryByRole('button', { name: t('cabinets.moveTo') })).not.toBeInTheDocument();
    cleanup();

    show('cab-old');
    fireEvent.click(screen.getByRole('button', { name: t('cabinets.moveTo') }));
    fireEvent.mouseDown(await screen.findByRole('combobox'));
    await waitFor(() => expect(document.querySelector('.ant-select-dropdown [title="New shelf"]')).not.toBeNull());
    expect(document.querySelector('.ant-select-dropdown [title="Old shelf"]')).toBeNull();
  });

  it('says how many were filed and how many were not when the answer is partial', async () => {
    // One refused by name, one neither filed nor refused: both count as not filed.
    refile.mockResolvedValue({ filed: ['doc-1'], refused: { 'doc-2': 'document.forbidden' } });
    show('cab-old');

    await fileInto(t('cabinets.moveTo'), 'New shelf');

    expect(await screen.findByText(t('cabinets.bulkPartly', { filed: 1, refused: 2 }))).toBeInTheDocument();
    expect(onDone).toHaveBeenCalledTimes(1);
  });

  it('says a refused request failed, and keeps the dialog and the selection', async () => {
    refile.mockRejectedValue(new TypeError('Failed to fetch'));
    show('cab-old');

    const dialog = await fileInto(t('cabinets.moveTo'), 'New shelf');

    expect(await screen.findByText(t('common.saveFailed'))).toBeInTheDocument();
    expect(onDone).not.toHaveBeenCalled();
    expect(dialog).toBeInTheDocument();
    expect(within(dialog).getByText(t('cabinets.moveHint'))).toBeInTheDocument();
  });
});
