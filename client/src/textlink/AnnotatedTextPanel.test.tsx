// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { App } from 'antd';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../i18n';
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

vi.mock('../api/hooks.ts', async () => {
  const actual = await vi.importActual<typeof import('../api/hooks.ts')>('../api/hooks.ts');
  return {
    ...actual,
    useAnnotatedText: () => ({ data: annotatedText, isPending: false, isError: false, error: null }),
    useResLinksForTarget: () => ({ data: { items: [link], page: 1, pageSize: 200, totalItems: 1 } }),
    useDeleteResLink: () => ({ mutateAsync: vi.fn() }),
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
