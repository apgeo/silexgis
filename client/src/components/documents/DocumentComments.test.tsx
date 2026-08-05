// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';

function comment(over: Record<string, unknown> = {}) {
  return {
    id: 'c-1',
    documentId: 'doc-1',
    parentId: null as string | null,
    body: 'The third sketch does not match the passage.',
    authorId: 'u-1',
    authorName: 'A. Speolog',
    anchorKind: 'whole',
    anchor: null,
    anchorFileId: null,
    mayEdit: false,
    mayDelete: false,
    editedAt: null as string | null,
    createdAt: '2026-02-03T11:00:00Z',
    updatedAt: '2026-02-03T11:00:00Z',
    ...over,
  };
}

let items: ReturnType<typeof comment>[] = [comment()];

vi.mock('../../api/hooks.ts', () => ({
  useDocumentComments: () => ({
    data: { items, page: 1, pageSize: 200, totalItems: items.length },
    isPending: false,
    isError: false,
  }),
  useCreateDocumentComment: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useUpdateDocumentComment: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useDeleteDocumentComment: () => ({ mutateAsync: vi.fn(), isPending: false }),
}));

const { default: DocumentComments } = await import('./DocumentComments.tsx');

function draw() {
  render(
    <App>
      <DocumentComments documentId="doc-1" />
    </App>,
  );
}

afterEach(() => {
  cleanup();
  items = [comment()];
});

describe('DocumentComments', () => {
  it('shows a remark with who wrote it, and says so when nobody has', () => {
    draw();
    expect(screen.getByText('The third sketch does not match the passage.')).toBeTruthy();
    expect(screen.getByText(/A\. Speolog/)).toBeTruthy();

    cleanup();
    items = [];
    draw();
    expect(screen.queryByText('The third sketch does not match the passage.')).toBeNull();
    expect(screen.getByText('Nothing has been said about this document yet.')).toBeTruthy();
  });

  it('offers editing and deleting to the caller the server said may, and to nobody else', () => {
    // The rights are the server's per-comment answer. The negative case is a caller the
    // server refused both — not merely a caller we forgot to give buttons to — and it must
    // still be able to read the remark and reply to it.
    items = [comment({ mayEdit: true, mayDelete: true })];
    draw();
    expect(screen.getByText('Edit')).toBeTruthy();
    expect(screen.getByText('Delete')).toBeTruthy();

    cleanup();
    items = [comment({ mayEdit: false, mayDelete: false })];
    draw();
    expect(screen.queryByText('Edit')).toBeNull();
    expect(screen.queryByText('Delete')).toBeNull();
    expect(screen.getByText('The third sketch does not match the passage.')).toBeTruthy();
    expect(screen.getByText('Reply')).toBeTruthy();
  });

  it('renders a remark containing markup as the characters that were typed, never as elements', () => {
    // The one place in this application where prose one member typed is shown to another.
    items = [comment({ body: '<img src=x onerror=alert(1)>\nsecond line' })];
    const { container } = render(
      <App>
        <DocumentComments documentId="doc-1" />
      </App>,
    );
    expect(container.querySelector('img')).toBeNull();
    expect(screen.getByText(/<img src=x onerror=alert\(1\)>/)).toBeTruthy();
  });

  it('offers replying on a remark but not on a reply, so a thread stays one level deep', () => {
    items = [
      comment(),
      comment({ id: 'c-2', parentId: 'c-1', body: 'Checked against the 1987 sheet.' }),
    ];
    draw();
    expect(screen.getByText('Checked against the 1987 sheet.')).toBeTruthy();
    // One Reply button in total: the root has one, the reply beneath it does not.
    expect(screen.getAllByText('Reply').length).toBe(1);
  });

  it('marks a remark that has been rewritten', () => {
    items = [comment({ editedAt: '2026-02-04T09:00:00Z' })];
    draw();
    expect(screen.getByText(/edited/)).toBeTruthy();

    cleanup();
    items = [comment()];
    draw();
    expect(screen.queryByText(/edited/)).toBeNull();
  });
});
