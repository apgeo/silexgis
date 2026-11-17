// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { App } from 'antd';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import i18n from '../i18n';
import { ApiError } from '../api/client.ts';
import { useWorkspaceStore } from '../stores/workspaceStore.ts';
import { registerViewControl, resetViewControlsForTests } from '../viewlinks/viewTargets.ts';
import type { ResourceRef } from '../viewlinks/resourceRef.ts';

const annotatedText = {
  documentId: 'doc-1',
  fileId: 'file-1',
  title: 'Notes',
  visibility: 'public',
  cavingGroupId: null,
  versionNumber: 1,
  canonicalLength: 40,
  mayWrite: false,
  blocks: [{ type: 'p', text: 'The passage past Dolina Demo widens.' }],
};

const link = {
  id: 'link-1',
  shortCode: 'abcd1234',
  relationType: null,
  description: null,
  createdBy: null,
  createdAt: '2026-08-29T00:00:00Z',
  updatedAt: '2026-08-29T00:00:00Z',
  mayEdit: false,
  members: [
    {
      id: 'm-passage',
      targetType: 'document',
      targetId: 'doc-1',
      isMain: false,
      sortOrder: 0,
      note: null,
      anchorKind: 'textRange',
      anchor: { start: 17, end: 28, quote: 'Dolina Demo' },
      anchorFileId: 'file-1',
      anchorState: 'exact',
      display: null,
    },
    {
      id: 'm-target',
      targetType: 'feature',
      targetId: 'feature-1',
      isMain: true,
      sortOrder: 1,
      note: null,
      anchorKind: 'whole',
      anchor: null,
      anchorFileId: null,
      anchorState: 'exact',
      display: { title: 'Dolina Demo', subtitle: null, route: null, thumbnailUrl: null },
    },
  ],
};

// How the document's link read answers: how many links the document has in all, and whether the
// read failed.
const linkRead = { totalItems: 1, isError: false };
const refetchLinks = vi.fn();
const deleteLinkMutateAsync = vi.fn();

vi.mock('../api/hooks.ts', async () => {
  const actual = await vi.importActual<typeof import('../api/hooks.ts')>('../api/hooks.ts');
  return {
    ...actual,
    useAnnotatedText: () => ({ data: annotatedText, isPending: false, isError: false, error: null }),
    useResLinksForTarget: () =>
      linkRead.isError
        ? { data: undefined, isError: true, refetch: refetchLinks }
        : {
            data: { items: [link], page: 1, pageSize: 200, totalItems: linkRead.totalItems },
            isError: false,
            refetch: refetchLinks,
          },
    useDeleteResLink: () => ({ mutateAsync: deleteLinkMutateAsync }),
  };
});

const { default: AnnotatedTextPanel } = await import('./AnnotatedTextPanel.tsx');

function renderPanel() {
  return render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <MemoryRouter>
        <App>
          <AnnotatedTextPanel documentId="doc-1" />
        </App>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

beforeEach(() => {
  linkRead.totalItems = 1;
  linkRead.isError = false;
  refetchLinks.mockReset();
  deleteLinkMutateAsync.mockReset();
  annotatedText.mayWrite = false;
  link.mayEdit = false;
  resetViewControlsForTests();
  useWorkspaceStore.setState({ selection: null });
});

afterEach(cleanup);

describe('following a passage', () => {
  it('sends the target to the views that are listening', async () => {
    const shown: ResourceRef[] = [];
    registerViewControl({
      id: 'map',
      kind: 'map2d',
      labelKey: 'viewLinks.controls.map2d',
      canReveal: () => true,
      reveal: (ref) => shown.push(ref),
    });

    renderPanel();
    (await screen.findByTestId('tl-passage')).click();

    await waitFor(() => expect(shown).toHaveLength(1));
    expect(shown[0]).toMatchObject({ targetType: 'feature', targetId: 'feature-1' });
  });

  it('selects the target even where no view is listening', async () => {
    // The panel is also mounted on a document's own page, where there is no map at all, and a
    // reader may have muted every view. The selection is workspace state that panels read — it
    // is not a fact about any view — so making it depend on one leaves a click doing nothing
    // whatsoever in exactly those two places.
    renderPanel();
    (await screen.findByTestId('tl-passage')).click();

    await waitFor(() =>
      expect(useWorkspaceStore.getState().selection).toEqual({ kind: 'feature', featureId: 'feature-1' }),
    );
  });

  it('draws the passage where its quote is, not where its offsets say', async () => {
    renderPanel();
    const passage = await screen.findByTestId('tl-passage');
    expect(passage).toHaveTextContent('Dolina Demo');
  });
});

describe('the links the reader could not draw', () => {
  it('says when the document has more links than were read, and says nothing when it has not', async () => {
    renderPanel();
    await screen.findByTestId('tl-passage');
    expect(screen.queryByTestId('tl-links-cut-off')).toBeNull();
    cleanup();

    // More links than one read brings back: a passage among the rest would show no highlight,
    // which reads as "nothing is linked here".
    linkRead.totalItems = 250;
    renderPanel();
    expect(await screen.findByTestId('tl-links-cut-off')).toHaveTextContent(
      'Only the first 1 of 250 links are highlighted in this text.',
    );
  });

  it('says the links could not be read rather than showing the text as unlinked, and offers a retry', async () => {
    renderPanel();
    await screen.findByTestId('tl-passage');
    expect(screen.queryByTestId('tl-links-failed')).toBeNull();
    cleanup();

    linkRead.isError = true;
    renderPanel();
    const notice = await screen.findByTestId('tl-links-failed');
    expect(notice).toHaveTextContent("The links of this text could not be loaded");
    fireEvent.click(within(notice).getByRole('button', { name: 'Retry' }));
    expect(refetchLinks).toHaveBeenCalled();
  });
});

describe('deleting a link', () => {
  it('says why when the deletion is refused, rather than leaving the rejection unhandled', async () => {
    annotatedText.mayWrite = true;
    link.mayEdit = true;
    deleteLinkMutateAsync.mockRejectedValue(new ApiError(503, undefined));
    renderPanel();

    fireEvent.click(await screen.findByRole('button', { name: 'Edit links' }));
    fireEvent.mouseEnter(await screen.findByTestId('tl-passage'));
    fireEvent.click(await screen.findByRole('button', { name: 'Delete link' }, { timeout: 3000 }));
    fireEvent.click(await screen.findByRole('button', { name: 'OK' }));

    await waitFor(() => expect(deleteLinkMutateAsync).toHaveBeenCalledWith('link-1'));
    // The refusal is said in words. Before, the rejected promise escaped the dialog as an unhandled
    // rejection (which fails this run by itself) and nothing was said at all.
    expect(await screen.findByText(i18n.t('common.deleteFailed'))).toBeTruthy();
  });
});
