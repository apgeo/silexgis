// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { App as AntApp } from 'antd';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { ApiError } from '../../api/client.ts';
import type { DeletedDocument } from '../../api/hooks.ts';

const { listSpy, configSpy, restoreSpy } = vi.hoisted(() => ({
  listSpy: vi.fn(),
  configSpy: vi.fn(),
  restoreSpy: vi.fn(),
}));

vi.mock('../../api/hooks.ts', () => ({
  useDeletedDocuments: (page: number) => listSpy(page),
  useDocumentConfig: () => configSpy(),
  useRestoreDocument: () => ({ mutateAsync: restoreSpy, isPending: false, variables: undefined }),
}));

const { default: DeletedDocumentsPage } = await import('./DeletedDocumentsPage.tsx');

const DAY = 24 * 60 * 60 * 1000;
const ID = '11111111-1111-1111-1111-111111111111';

function deleted(overrides: Partial<DeletedDocument> = {}): DeletedDocument {
  return {
    id: ID,
    title: 'Ridicare topografică',
    documentTypeId: null,
    deletedAt: new Date(Date.now() - 2 * DAY).toISOString(),
    // Half a day past the round figure, so the count does not turn on the millisecond between
    // building this row and drawing it.
    restorableUntil: new Date(Date.now() + 28 * DAY + DAY / 2).toISOString(),
    deletedByUserId: '22222222-2222-2222-2222-222222222222',
    deletedByName: 'Ana Popescu',
    ...overrides,
  };
}

function answer(items: DeletedDocument[]) {
  listSpy.mockReturnValue({
    data: { items, page: 1, pageSize: 50, totalItems: items.length },
    isFetching: false,
    isError: false,
  });
}

function show() {
  return render(
    <AntApp>
      <MemoryRouter initialEntries={['/documents/deleted']}>
        <Routes>
          <Route path="/documents/deleted" element={<DeletedDocumentsPage />} />
          <Route path="/documents/:id" element={<div data-testid="document-page" />} />
        </Routes>
      </MemoryRouter>
    </AntApp>,
  );
}

async function restoreTheRow() {
  fireEvent.click(screen.getByTestId('deleted-document-restore'));
  const dialog = await screen.findByRole('tooltip');
  fireEvent.click(within(dialog).getByRole('button', { name: 'Restore' }));
}

afterEach(cleanup);
beforeEach(() => {
  listSpy.mockReset();
  configSpy.mockReset().mockReturnValue({ data: { deletedRetentionDays: 30 } });
  restoreSpy.mockReset().mockResolvedValue(undefined);
  answer([deleted()]);
});

describe('deleted documents', () => {
  it('lists a deleted document with who deleted it and how long it has left', () => {
    show();

    const table = screen.getByTestId('deleted-documents');
    expect(within(table).getByText('Ridicare topografică')).toBeTruthy();
    expect(within(table).getByText('Ana Popescu')).toBeTruthy();
    expect(within(table).getByText('in 28 days')).toBeTruthy();
  });

  it('says the window the server reported, and nothing before it has answered', () => {
    configSpy.mockReturnValue({ data: { deletedRetentionDays: 7 } });
    const first = show();
    expect(screen.getByText(/kept for 7 days and then removed for good/)).toBeTruthy();
    first.unmount();

    configSpy.mockReturnValue({ data: undefined });
    show();
    expect(screen.queryByText(/removed for good\./)).toBeNull();
    expect(screen.queryByText(/until an administrator decides otherwise/)).toBeNull();
  });

  it('says a document is not scheduled to go where the installation keeps deleted ones', () => {
    configSpy.mockReturnValue({ data: { deletedRetentionDays: null } });
    answer([deleted({ restorableUntil: null })]);
    show();

    expect(screen.getByText(/until an administrator decides otherwise/)).toBeTruthy();
    expect(within(screen.getByTestId('deleted-documents')).getByText('Not scheduled')).toBeTruthy();
  });

  it('restores only after the confirmation, then opens the document', async () => {
    show();

    fireEvent.click(screen.getByTestId('deleted-document-restore'));
    expect(restoreSpy).not.toHaveBeenCalled();

    const dialog = await screen.findByRole('tooltip');
    expect(within(dialog).getByText('Restore this document?')).toBeTruthy();
    fireEvent.click(within(dialog).getByRole('button', { name: 'Restore' }));

    await waitFor(() => expect(restoreSpy).toHaveBeenCalledWith(ID));
    expect(await screen.findByTestId('document-page')).toBeTruthy();
  });

  it.each([
    [new ApiError(409, 'document.restore_window_passed'), 'That document was deleted too long ago to be restored.'],
    [new ApiError(409, 'document.not_deleted'), 'That document is no longer among the deleted ones.'],
    [new ApiError(404, 'document.not_found'), 'That document is no longer among the deleted ones.'],
    [new ApiError(403, 'document.write_forbidden'), 'You may not restore that document.'],
  ])('says why a restore was refused and stays on the list (%#)', async (error, sentence) => {
    restoreSpy.mockRejectedValue(error);
    show();

    await restoreTheRow();

    expect(await screen.findByText(sentence)).toBeTruthy();
    expect(screen.queryByTestId('document-page')).toBeNull();
  });

  it('tells an empty list from one that could not be read', () => {
    answer([]);
    const first = show();
    expect(screen.getByTestId('deleted-documents-empty').textContent).toContain('Nothing here');
    first.unmount();

    listSpy.mockReturnValue({ data: undefined, isFetching: false, isError: true });
    show();
    expect(screen.getByTestId('deleted-documents-empty').textContent).toContain('could not be loaded');
  });
});
