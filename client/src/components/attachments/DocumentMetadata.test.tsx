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
    metadataSchemaVersion: 1,
    metadataSchema:
      '{"type":"object","properties":{"cave_name":{"type":"string","title":"Cave"},' +
      '"surveyed_length_m":{"type":"number","title":"Surveyed length (m)","minimum":0}}}',
  },
];

// A stored document holding one key the current schema knows and one it does not — the
// second is what proves the merge rule rather than a hopeful comment about it.
const document = {
  id: 'doc-1',
  title: 'Ridicare topografică',
  documentTypeId: 1,
  documentTypeCode: 'survey_report',
  metadata: { cave_name: 'Peștera Urșilor', legacy_note: 'kept from an older schema' },
  metadataSchemaVersion: 1,
};

type DocumentUpdate = {
  id: string;
  title: string;
  documentTypeId: number | null;
  metadata: Record<string, unknown>;
};

const mutateAsync = vi.fn((update: DocumentUpdate) => Promise.resolve(update));

vi.mock('../../api/hooks.ts', () => ({
  useDocument: () => ({ data: document }),
  useDocumentTypes: () => ({ data: documentTypes }),
  useUpdateDocument: () => ({ mutateAsync, isPending: false }),
}));

const { default: DocumentMetadata } = await import('./DocumentMetadata.tsx');

describe('DocumentMetadata', () => {
  it('builds the form from the kind schema and keeps values the schema does not describe', async () => {
    render(
      <App>
        <DocumentMetadata documentId="doc-1" />
      </App>,
    );
    fireEvent.click(screen.getByRole('button'));

    // The fields come from the kind's schema, so a kind gaining a field needs no client change.
    expect(await screen.findByText('Cave')).toBeInTheDocument();
    expect(screen.getByText('Surveyed length (m)')).toBeInTheDocument();

    fireEvent.click(screen.getByText('Save'));

    await waitFor(() => expect(mutateAsync).toHaveBeenCalled());
    const sent = mutateAsync.mock.calls[0][0];
    expect(sent.id).toBe('doc-1');
    expect(sent.documentTypeId).toBe(1);
    expect(sent.metadata.cave_name).toBe('Peștera Urșilor');
    // The key the current schema has no field for survives the round trip: a kind that drops
    // a field must not silently erase what was written under the previous one.
    expect(sent.metadata.legacy_note).toBe('kept from an older schema');
    // A schema field nobody filled in is absent rather than sent as an empty value, so
    // "not filled in" and "filled in with nothing" stay distinct.
    expect('surveyed_length_m' in sent.metadata).toBe(false);
  });
});
