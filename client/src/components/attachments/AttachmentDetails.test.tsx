// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { AttachmentInfo } from '../../api/hooks.ts';

const { updateAttachmentSpy, updateFileSpy, defaultSetSpy } = vi.hoisted(() => ({
  updateAttachmentSpy: vi.fn(() => Promise.resolve()),
  updateFileSpy: vi.fn(() => Promise.resolve()),
  defaultSetSpy: vi.fn((_: { id: string; included: boolean }) => Promise.resolve()),
}));

vi.mock('../../api/hooks.ts', () => ({
  useUpdateAttachment: () => ({ mutateAsync: updateAttachmentSpy, isPending: false }),
  useUpdateFile: () => ({ mutateAsync: updateFileSpy, isPending: false }),
  useSetDeviceDefaultSet: () => ({ mutateAsync: defaultSetSpy, isPending: false }),
  // TagChips (rendered inside the details popover) reaches for these.
  useTaggings: () => ({ data: [] }),
  useTags: () => ({ data: [] }),
  useCreateTagging: () => ({ mutateAsync: vi.fn(() => Promise.resolve()) }),
  useDeleteTagging: () => ({ mutateAsync: vi.fn(() => Promise.resolve()) }),
}));

const { default: AttachmentDetails } = await import('./AttachmentDetails.tsx');

const attachment: AttachmentInfo = {
  id: 'att-1',
  fileId: 'file-1',
  entityType: 'cave',
  entityId: 'cave-1',
  role: 'document',
  caption: 'Main entrance',
  sortOrder: 3,
  isPrimary: false,
  inDeviceDefaultSet: false,
  addedBy: null,
  file: {
    id: 'file-1',
    documentId: 'doc-1',
    originalName: 'report.pdf',
    mimeType: 'application/pdf',
    sizeBytes: 2048,
    sha256: 'abc',
    kind: 'document',
    versionNumber: 1,
    documentDate: '2019-08-01',
    createdAt: '2026-07-15T10:00:00Z',
    contentUrl: '/c/file-1',
    mayDownloadOriginal: true,
    thumbnailUrl: null,
  pagesUrl: null,
  pageCount: null,
  conversion: 'notApplicable',
  contentCreatedAt: null,
  photo: null,
  position: null,
  locationRemoved: false,
  },
};

function open() {
  render(
    <App>
      <AttachmentDetails attachment={attachment} />
    </App>,
  );
  fireEvent.click(screen.getByRole('button', { name: 'Details' }));
}

afterEach(() => {
  vi.clearAllMocks();
  cleanup();
}); // popover content renders into a portal — clear it between cases

describe('AttachmentDetails', () => {
  it('shows current metadata and keeps Save disabled until something changes', async () => {
    open();
    // Caption prefilled; role label + document date present.
    expect(await screen.findByDisplayValue('Main entrance')).toBeInTheDocument();
    expect(screen.getByDisplayValue('2019-08-01')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled();
  });

  it('saves a caption edit through the attachment endpoint only (date untouched)', async () => {
    open();
    const caption = await screen.findByDisplayValue('Main entrance');
    fireEvent.change(caption, { target: { value: 'Winter view' } });

    const save = screen.getByRole('button', { name: 'Save' });
    expect(save).toBeEnabled();
    fireEvent.click(save);

    await waitFor(() =>
      expect(updateAttachmentSpy).toHaveBeenCalledWith({
        id: 'att-1',
        role: 'document',
        caption: 'Winter view',
        sortOrder: 3,
      }),
    );
    // The document date did not change, so the file endpoint is not called.
    expect(updateFileSpy).not.toHaveBeenCalled();
  });

  it('flips the phones\' default download on its own route, without touching the other fields', async () => {
    open();
    const toggle = await screen.findByRole('switch', { name: "Include in the phones' default download" });
    expect(toggle).toHaveAttribute('aria-checked', 'false');
    fireEvent.click(toggle);

    await waitFor(() => expect(defaultSetSpy).toHaveBeenCalledWith({ id: 'att-1', included: true }));
    expect(updateAttachmentSpy).not.toHaveBeenCalled();
    expect(updateFileSpy).not.toHaveBeenCalled();
  });
});
