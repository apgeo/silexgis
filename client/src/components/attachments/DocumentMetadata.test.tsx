// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
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
  visibility: 'private',
  cavingGroupId: null,
  // Filed nowhere: this is the state the tree cannot mend, since it only ever lists what
  // is already on a shelf.
  cabinetIds: [] as string[],
};

const cavingGroups = [{ id: 'cg-1', name: 'Speo Club' }];

// Two shelves sharing a name under different archives — which is legal, names being
// unique only among siblings, and why the picker names the whole path.
const cabinets = [
  { id: 'cab-1', parentId: null, name: 'Club archive', description: null, ancestorIds: ['cab-1'], documentCount: 0 },
  {
    id: 'cab-2', parentId: 'cab-1', name: '1987', description: null,
    ancestorIds: ['cab-1', 'cab-2'], documentCount: 0,
  },
  {
    id: 'cab-3', parentId: null, name: 'Peștera X', description: null,
    ancestorIds: ['cab-3'], documentCount: 0,
  },
];

type DocumentUpdate = {
  id: string;
  title: string;
  documentTypeId: number | null;
  metadata: Record<string, unknown>;
  visibility: string;
  cavingGroupId: string | null;
};

const mutateAsync = vi.fn((update: DocumentUpdate) => Promise.resolve(update));
const fileAsync = vi.fn((filing: { cabinetId: string; documentId: string; filed: boolean }) =>
  Promise.resolve(filing));
let documentActions = 'read, write';

vi.mock('../../api/hooks.ts', () => ({
  useDocument: () => ({ data: document }),
  useDocumentTypes: () => ({ data: documentTypes }),
  useCavingGroups: () => ({ data: cavingGroups }),
  useUpdateDocument: () => ({ mutateAsync, isPending: false }),
  useCabinets: () => ({ data: cabinets }),
  useCapabilities: () => ({ data: { domains: { documents: documentActions } } }),
  useFileDocument: () => ({ mutateAsync: fileAsync, isPending: false }),
  hasAccessAction: (actions: string | null | undefined, flag: string) =>
    (actions ?? '').split(',').map((a) => a.trim()).includes(flag),
}));

const { default: DocumentMetadata } = await import('./DocumentMetadata.tsx');

afterEach(cleanup);

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
    // The document's own read audience rides along unchanged: it is a field of the same
    // request, so a title correction must not quietly rewrite who may read the document.
    expect(sent.visibility).toBe('private');
    expect(sent.cavingGroupId).toBeNull();
  });

  it('asks which club a document belongs to only under club visibility, and drops the binding when it leaves', async () => {
    mutateAsync.mockClear();
    render(
      <App>
        <DocumentMetadata documentId="doc-1" />
      </App>,
    );
    fireEvent.click(screen.getByRole('button'));
    await screen.findByText('Cave');

    // Private to begin with: no club is asked for, because none decides anything.
    expect(screen.queryByText('Choose a caving group')).not.toBeInTheDocument();

    fireEvent.mouseDown(screen.getByText('Private'));
    fireEvent.click(await screen.findByTitle('Caving group'));

    // Now it is asked for, and saving is refused until it is answered — an unanswered
    // club binding would be a read audience that admits nobody.
    const picker = await screen.findByText('Choose a caving group');
    expect(screen.getByText('Save').closest('button')).toBeDisabled();

    fireEvent.mouseDown(picker);
    fireEvent.click(await screen.findByTitle('Speo Club'));
    fireEvent.click(screen.getByText('Save'));

    await waitFor(() => expect(mutateAsync).toHaveBeenCalled());
    const sent = mutateAsync.mock.calls[0][0];
    expect(sent.visibility).toBe('cavingGroup');
    expect(sent.cavingGroupId).toBe('cg-1');
  });

  it('files a document that is on no shelf yet, naming each cabinet by its whole path', async () => {
    fileAsync.mockClear();
    documentActions = 'read, write';
    render(
      <App>
        <DocumentMetadata documentId="doc-1" />
      </App>,
    );
    fireEvent.click(screen.getByRole('button'));

    const picker = await screen.findByText('Not filed anywhere');
    fireEvent.mouseDown(picker);

    // Named by path, because "1987" alone would not say which archive's 1987 it is.
    fireEvent.click(await screen.findByTitle('Club archive / 1987'));

    await waitFor(() => expect(fileAsync).toHaveBeenCalled());
    expect(fileAsync.mock.calls[0][0]).toEqual({
      cabinetId: 'cab-2',
      documentId: 'doc-1',
      filed: true,
    });
  });

  it('offers no filing control to someone who may not write documents', async () => {
    documentActions = 'read';
    render(
      <App>
        <DocumentMetadata documentId="doc-1" />
      </App>,
    );
    fireEvent.click(screen.getByRole('button'));

    // The same panel, same document, one right fewer: the control the previous test used
    // is gone rather than present-and-refused.
    await screen.findByText('Cave');
    expect(screen.queryByText('Not filed anywhere')).not.toBeInTheDocument();
    documentActions = 'read, write';
  });
});
