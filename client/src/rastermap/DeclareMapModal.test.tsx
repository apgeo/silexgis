// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { App } from 'antd';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../i18n';
import DeclareMapModal from './DeclareMapModal.tsx';
import type { RasterMapDeclaration } from './rasterMaps.ts';

/**
 * Declaring a map and amending a declaration: the one POST with the document as the main
 * member, the image gate in front of it, the view-kind PATCH that keeps everything else,
 * and the removal that deletes the declaration alone.
 */
const createLink = vi.fn();
const updateLink = vi.fn();
const deleteLink = vi.fn();

/** What the picked document's current file is, per test. */
let pickedFileMime = 'image/png';

vi.mock('../api/hooks.ts', () => ({
  useCreateResLink: () => ({ mutateAsync: createLink, isPending: false }),
  useUpdateResLink: () => ({ mutateAsync: updateLink, isPending: false }),
  useDeleteResLink: () => ({ mutateAsync: deleteLink, isPending: false }),
  useResLinkRelationTypes: () => ({
    data: [
      { id: 3, code: 'map-plan-of', name: 'x', directed: true, inverseName: 'x' },
      { id: 4, code: 'map-profile-of', name: 'x', directed: true, inverseName: 'x' },
      { id: 5, code: 'map-other-of', name: 'x', directed: true, inverseName: 'x' },
    ],
  }),
  useDocument: (id: string | undefined) => ({
    data: id === undefined ? undefined : { id, currentFileId: 'file-1' },
  }),
  useFile: (id: string | undefined) => ({
    data:
      id === undefined
        ? undefined
        : {
            id,
            mimeType: pickedFileMime,
            contentUrl: 'http://files.local/f1',
            thumbnailUrl: null,
            mayDownloadOriginal: true,
          },
  }),
}));

// The picker is its own component with its own server feed; here only its contract.
vi.mock('../components/reslinks/ResLinkTargetPicker.tsx', () => ({
  default: function FakePicker({ onChange }: { onChange: (id: string, title: string) => void }) {
    return <button data-testid="fake-pick-document" onClick={() => onChange('doc-1', 'Sheet A')} />;
  },
}));

const declaration = (overrides: Partial<RasterMapDeclaration> = {}): RasterMapDeclaration => ({
  linkId: 'map-link',
  documentId: 'doc-1',
  viewKind: 'plan',
  title: 'Plan sheet',
  createdAt: '2026-08-01T10:00:00Z',
  description: 'northern branch',
  mayEdit: true,
  ...overrides,
});

function show(editing: RasterMapDeclaration | null = null) {
  return render(
    <App>
      <DeclareMapModal open onClose={vi.fn()} surveyModelId="model-1" editing={editing} />
    </App>,
  );
}

const submitButton = () => screen.getByTestId('rastermap-declare-submit');

beforeEach(() => {
  pickedFileMime = 'image/png';
  createLink.mockReset().mockResolvedValue({ id: 'made' });
  updateLink.mockReset().mockResolvedValue({ id: 'map-link' });
  deleteLink.mockReset().mockResolvedValue(undefined);
});

afterEach(() => {
  cleanup();
});

describe('declaring', () => {
  it('refuses to submit until a document is picked, then writes the one designed POST', async () => {
    show();
    expect(submitButton()).toBeDisabled();

    fireEvent.click(screen.getByTestId('fake-pick-document'));
    await waitFor(() => expect(submitButton()).toBeEnabled());
    fireEvent.click(submitButton());

    await waitFor(() =>
      expect(createLink).toHaveBeenCalledWith({
        relationTypeId: 3,
        description: null,
        members: [
          {
            targetType: 'document',
            targetId: 'doc-1',
            isMain: true,
            sortOrder: 0,
            note: null,
            anchorKind: 'whole',
            anchor: null,
            anchorFileId: null,
          },
          {
            targetType: 'surveyModel',
            targetId: 'model-1',
            isMain: false,
            sortOrder: 1,
            note: null,
            anchorKind: 'whole',
            anchor: null,
            anchorFileId: null,
          },
        ],
      }),
    );
  });

  it('writes the chosen view kind, which is nothing but the relation code', async () => {
    show();
    fireEvent.click(screen.getByTestId('fake-pick-document'));
    fireEvent.click(screen.getByText('Profile'));
    await waitFor(() => expect(submitButton()).toBeEnabled());
    fireEvent.click(submitButton());

    await waitFor(() => expect(createLink).toHaveBeenCalled());
    expect(createLink.mock.calls[0][0]).toMatchObject({ relationTypeId: 4 });
  });

  it('warns and refuses when the picked document is not an image', async () => {
    pickedFileMime = 'application/pdf';
    show();
    fireEvent.click(screen.getByTestId('fake-pick-document'));

    expect(await screen.findByTestId('rastermap-not-an-image')).toBeInTheDocument();
    expect(submitButton()).toBeDisabled();
    expect(createLink).not.toHaveBeenCalled();
  });

  it('never offers removal while declaring — there is nothing to remove yet', () => {
    show();
    expect(screen.queryByTestId('rastermap-undeclare')).not.toBeInTheDocument();
  });
});

describe('amending', () => {
  it('asks no document question — a different document would be a different map', () => {
    show(declaration());
    expect(screen.queryByTestId('fake-pick-document')).not.toBeInTheDocument();
  });

  it('re-kinding PATCHes the relation and nothing else the link carries', async () => {
    show(declaration());
    // Unchanged kind is nothing to save.
    expect(submitButton()).toBeDisabled();

    fireEvent.click(screen.getByText('Profile'));
    await waitFor(() => expect(submitButton()).toBeEnabled());
    fireEvent.click(submitButton());

    await waitFor(() =>
      expect(updateLink).toHaveBeenCalledWith({
        id: 'map-link',
        // The description rides back unchanged; no mainMemberId keeps the marker on the
        // document, where the edit-rights rule reads it from.
        body: { description: 'northern branch', relationTypeId: 4, mainMemberId: null },
      }),
    );
    expect(createLink).not.toHaveBeenCalled();
  });

  it('removal deletes the declaration link alone, behind a confirmation', async () => {
    show(declaration());
    fireEvent.click(screen.getByTestId('rastermap-undeclare'));
    // The Popconfirm's own OK, not the dialog's (whose OK is the disabled submit).
    await screen.findByText('Remove this map from the model?');
    const confirms = screen
      .getAllByRole('button', { name: 'OK' })
      .filter((button) => !button.hasAttribute('data-testid'));
    expect(confirms).toHaveLength(1);
    fireEvent.click(confirms[0]);

    await waitFor(() => expect(deleteLink).toHaveBeenCalledWith('map-link'));
  });
});
