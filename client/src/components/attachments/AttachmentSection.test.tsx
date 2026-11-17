// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { ApiError } from '../../api/client.ts';
import type { AttachmentInfo } from '../../api/hooks.ts';

function attachment(over: Partial<AttachmentInfo> & { kind?: string; name?: string } = {}): AttachmentInfo {
  const { kind = 'image', name = 'photo-1.jpg', ...rest } = over;
  return {
    id: 'att-1',
    fileId: 'file-1',
    entityType: 'feature',
    entityId: 'feature-1',
    role: kind === 'image' ? 'photoSurface' : 'document',
    caption: null,
    sortOrder: 1,
    isPrimary: false,
    inDeviceDefaultSet: false,
    addedBy: null,
    file: {
      id: 'file-1',
      documentId: 'doc-1',
      originalName: name,
      mimeType: kind === 'image' ? 'image/jpeg' : 'application/pdf',
      sizeBytes: 2048,
      sha256: 'abc',
      kind,
      versionNumber: 1,
      documentDate: null,
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
    ...rest,
  } as AttachmentInfo;
}

let attachments: AttachmentInfo[] = [];
let setPrimaryAnswer: () => Promise<void> = () => Promise.resolve();
const uploadSpy = vi.fn();
const setPrimarySpy = vi.fn((_: { id: string; primary: boolean }) => setPrimaryAnswer());

/** A mutation whose pending flag and variables follow its request, as TanStack Query's do. */
function useFakeMutation<V>(spy: (variables: V) => Promise<void>) {
  const [state, setState] = useState<{ isPending: boolean; variables?: V }>({ isPending: false });
  return {
    ...state,
    mutateAsync: async (variables: V) => {
      setState({ isPending: true, variables });
      try {
        await spy(variables);
      } finally {
        setState({ isPending: false });
      }
    },
  };
}

vi.mock('../../api/hooks.ts', () => ({
  useAttachments: () => ({ data: attachments }),
  useFileConfig: () => ({ data: { maxUploadBytes: 1024 * 1024 } }),
  useUploadFile: () => ({ mutateAsync: uploadSpy }),
  useCreateAttachment: () => ({ mutateAsync: vi.fn() }),
  useDeleteAttachment: () => ({ mutateAsync: vi.fn() }),
  useSetPrimaryAttachment: () => useFakeMutation(setPrimarySpy),
}));

// The per-row controls each fetch what they need; this section only places them.
vi.mock('../documents/OpenDocument.tsx', () => ({ default: () => <span>open</span> }));
vi.mock('./AttachmentDetails.tsx', () => ({ default: () => <span>details</span> }));
vi.mock('./DocumentMetadata.tsx', () => ({ default: () => <span>metadata</span> }));
vi.mock('./FileVersions.tsx', () => ({ default: () => <span>versions</span> }));
vi.mock('./PhotoFactsPanel.tsx', () => ({ default: () => null }));
vi.mock('./PhotoPositionAction.tsx', () => ({ default: () => null }));
vi.mock('../../rastermap/PlaceMapAction.tsx', () => ({ default: () => null }));

const { default: AttachmentSection } = await import('./AttachmentSection.tsx');

function draw(canEdit = true, reportSlot = false) {
  render(
    <App>
      <AttachmentSection entityType="feature" entityId="feature-1" canEdit={canEdit} reportSlot={reportSlot} />
    </App>,
  );
}

afterEach(() => {
  cleanup();
  vi.clearAllMocks();
  attachments = [];
  setPrimaryAnswer = () => Promise.resolve();
});

describe('AttachmentSection', () => {
  it('names the headline star by what pressing it will do, and says whether it is pressed', () => {
    attachments = [attachment()];
    draw();
    const star = screen.getByRole('button', { name: 'Use as the headline picture' });
    expect(star).toHaveAttribute('aria-pressed', 'false');

    cleanup();
    attachments = [attachment({ isPrimary: true })];
    draw();
    const pressed = screen.getByRole('button', { name: 'Stop using as the headline picture' });
    expect(pressed).toHaveAttribute('aria-pressed', 'true');
  });

  it('says that starring worked, and asks the server for the opposite of what is shown', async () => {
    attachments = [attachment()];
    draw();
    fireEvent.click(screen.getByRole('button', { name: 'Use as the headline picture' }));
    expect(setPrimarySpy).toHaveBeenCalledWith({ id: 'att-1', primary: true });
    expect(await screen.findByText('This is now the headline picture.')).toBeInTheDocument();
  });

  it('says so when the server refuses the star, instead of letting the refusal go unseen', async () => {
    attachments = [attachment()];
    setPrimaryAnswer = () => Promise.reject(new Error('refused'));
    draw();
    fireEvent.click(screen.getByRole('button', { name: 'Use as the headline picture' }));
    expect(await screen.findByText('The operation failed. Please try again.')).toBeInTheDocument();
  });

  it('says why the server refused an upload instead of a bare "save failed"', async () => {
    uploadSpy.mockRejectedValue(new ApiError(400, 'file.type_not_accepted'));
    draw();
    const input = document.querySelector('input[type="file"]')!;
    fireEvent.change(input, { target: { files: [new File([new Uint8Array(10)], 'tool.exe')] } });

    await waitFor(() => expect(uploadSpy).toHaveBeenCalled());
    expect(await screen.findByText('This file type is not accepted')).toBeInTheDocument();
  });

  it('draws a report and a document with the same controls, in their own lists', () => {
    attachments = [
      attachment({ id: 'att-r', kind: 'document', name: 'report.pdf', role: 'report' }),
      attachment({ id: 'att-d', kind: 'document', name: 'notes.pdf' }),
    ];
    draw(true, true);
    const report = screen.getByText('report.pdf').closest('li')!;
    const document = screen.getByText('notes.pdf').closest('li')!;
    for (const row of [report, document]) {
      expect(within(row).getByText('open')).toBeInTheDocument();
      expect(within(row).getByText('details')).toBeInTheDocument();
      expect(within(row).getByText('metadata')).toBeInTheDocument();
      expect(within(row).getByText('versions')).toBeInTheDocument();
      expect(within(row).getByRole('link', { name: 'Download' })).toBeInTheDocument();
      expect(within(row).getByRole('button', { name: 'Delete' })).toBeInTheDocument();
    }

    // A reader who may not edit gets the reading controls on both rows and none of the others.
    cleanup();
    draw(false, true);
    for (const name of ['report.pdf', 'notes.pdf']) {
      const row = screen.getByText(name).closest('li')!;
      expect(within(row).getByText('open')).toBeInTheDocument();
      expect(within(row).getByRole('link', { name: 'Download' })).toBeInTheDocument();
      expect(within(row).queryByText('details')).not.toBeInTheDocument();
      expect(within(row).queryByRole('button', { name: 'Delete' })).not.toBeInTheDocument();
    }
  });
});
