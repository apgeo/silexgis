// SPDX-License-Identifier: AGPL-3.0-or-later
import { act, cleanup, render, screen, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { App } from 'antd';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import i18n from '../../i18n';
import { queryKeys } from '../../api/hooks/queryKeys.ts';
import { useDuplicatePrompt, type DuplicateQuestion } from './useDuplicatePrompt.ts';

const EXISTING = '0195f4a2-0000-7000-8000-00000000d0c1';
const CABINET = '0195f4a2-0000-7000-8000-0000000cab01';

let ask: ((question: DuplicateQuestion) => Promise<boolean>) | undefined;

function Host() {
  ask = useDuplicatePrompt();
  return null;
}

function mount() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: Infinity } } });
  // The existing copy and the cabinets, as the reads would answer them: invented.
  client.setQueryData(queryKeys.document(EXISTING), {
    id: EXISTING,
    title: 'Invented survey notes',
    cabinetIds: [CABINET],
  });
  client.setQueryData(queryKeys.cabinets, [
    { id: CABINET, parentId: null, name: 'Invented field notes', description: null, ancestorIds: [], documentCount: 1 },
  ]);
  render(
    <QueryClientProvider client={client}>
      <App>
        <Host />
      </App>
    </QueryClientProvider>,
  );
}

beforeEach(async () => {
  await i18n.changeLanguage('en');
  ask = undefined;
});
afterEach(cleanup);

describe('the question a duplicate upload asks', () => {
  it('names the file dropped and where the copy already stored is filed, with a way to open it', async () => {
    mount();
    void act(() => {
      void ask!({ fileName: 'notes-2026.txt', documentId: EXISTING });
    });

    const dialog = await screen.findByRole('dialog');
    await waitFor(() => expect(dialog).toHaveTextContent('Invented field notes'));
    // Three files may have been dropped: the question says which one it is about.
    expect(dialog).toHaveTextContent('notes-2026.txt');
    expect(dialog).toHaveTextContent('Invented survey notes');
    // "Already here" was wrong when the copy sits in another cabinet: it says the archive.
    expect(dialog).not.toHaveTextContent('already here');
    expect(screen.getByRole('link', { name: /Invented survey notes/ })).toHaveAttribute(
      'href',
      `/documents/${EXISTING}`,
    );
  });

  it('still names the file when the copy is one this person cannot open', async () => {
    mount();
    void act(() => {
      void ask!({ fileName: 'notes-2026.txt' });
    });

    const dialog = await screen.findByRole('dialog');
    expect(dialog).toHaveTextContent('notes-2026.txt');
    expect(screen.queryByRole('link')).toBeNull();
  });
});
