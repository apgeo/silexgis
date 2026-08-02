// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import '../../i18n';

const documentTypes = [
  {
    id: 1,
    code: 'survey_report',
    name: 'Survey report',
    description: null,
    sortOrder: 10,
    metadataSchemaVersion: 2,
    metadataSchema:
      '{"type":"object","required":["cave_name"],"properties":' +
      '{"cave_name":{"type":"string","title":"Cave"},' +
      // A nested object: JSON Schema allows it, the flat form does not render it. The
      // preview has to leave it out or an author would expect a field that never appears.
      '"survey":{"type":"object","title":"Survey block"}}}',
  },
  {
    id: 2,
    code: 'permit',
    name: 'Permit',
    description: null,
    sortOrder: 20,
    metadataSchemaVersion: 1,
    metadataSchema: null,
  },
];

type DocumentTypeUpdate = {
  id: number;
  code: string;
  name: string;
  description: string | null;
  sortOrder: number;
  metadataSchema: string | null;
};

const updateMutate = vi.fn((write: DocumentTypeUpdate) => Promise.resolve(write));
const createMutate = vi.fn((write: Omit<DocumentTypeUpdate, 'id'>) => Promise.resolve(write));

// A caller holding taxonomy read *and* write — the page's own read/write split is asserted
// through what it renders, not through a comment.
const capabilities = { domains: { taxonomies: 'read, write' } };

vi.mock('../../api/hooks.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/hooks.ts')>('../../api/hooks.ts');
  return {
    hasAccessAction: actual.hasAccessAction,
    useCapabilities: () => ({ data: capabilities }),
    useDocumentTypes: () => ({ data: documentTypes, isLoading: false }),
    useCreateDocumentType: () => ({ mutateAsync: createMutate, isPending: false }),
    useUpdateDocumentType: () => ({ mutateAsync: updateMutate, isPending: false }),
  };
});

const { default: DocumentTypesPage } = await import('./DocumentTypesPage.tsx');

describe('DocumentTypesPage', () => {
  it('lists each kind with the fields its schema actually renders, and its schema version', () => {
    render(
      <App>
        <DocumentTypesPage />
      </App>,
    );

    expect(screen.getByText('Survey report')).toBeInTheDocument();
    expect(screen.getByText('survey_report')).toBeInTheDocument();
    // The renderable field is listed...
    expect(screen.getByText('Cave')).toBeInTheDocument();
    // ...and the one the flat form cannot render is not, so the list never promises a
    // field the document form will not offer.
    expect(screen.queryByText('Survey block')).not.toBeInTheDocument();

    // A kind carrying no schema says so rather than showing an empty cell.
    expect(screen.getByText('Permit')).toBeInTheDocument();
    expect(screen.getByText('No details')).toBeInTheDocument();

    // The published schema version is visible: it is what stored documents stamp.
    expect(screen.getByText('v2')).toBeInTheDocument();
  });

  it('sends a rewritten schema as raw text, and an emptied box as "no schema"', async () => {
    render(
      <App>
        <DocumentTypesPage />
      </App>,
    );

    fireEvent.click(screen.getAllByText('Edit')[0]);
    const schemaBox = await screen.findByLabelText('Details schema (JSON Schema)');
    fireEvent.change(schemaBox, { target: { value: '   ' } });
    fireEvent.click(screen.getByText('Save'));

    await waitFor(() => expect(updateMutate).toHaveBeenCalled());
    const sent = updateMutate.mock.calls[0][0];
    expect(sent.id).toBe(1);
    expect(sent.code).toBe('survey_report');
    // Whitespace is not a schema. Sending "" or "{}" would mean something different —
    // an empty object accepts anything and would publish a new version for no reason.
    expect(sent.metadataSchema).toBeNull();
  });
});
