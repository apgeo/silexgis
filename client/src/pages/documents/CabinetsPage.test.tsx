// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import i18n from '../../i18n';

/**
 * What a shelf says about whatever lands on it. Every cabinet carries this — the server
 * always sends it, resolved — so the fixtures do too rather than the page defending against
 * a shape it will never be given.
 */
const noDefaults = {
  documentTypeId: null,
  visibility: null,
  tagIds: [],
  requiredMetadataKeys: [],
  effectiveDocumentTypeId: null,
  effectiveVisibility: null,
  effectiveTagIds: [],
  effectiveRequiredMetadataKeys: [],
};

const cabinets = [
  {
    id: 'c1',
    parentId: null,
    name: 'Club archive',
    description: 'Everything the club keeps.',
    ancestorIds: ['c1'],
    documentCount: 3,
    defaults: noDefaults,
  },
  {
    id: 'c2',
    parentId: 'c1',
    name: 'Surveys',
    description: null,
    ancestorIds: ['c1', 'c2'],
    documentCount: 2,
    defaults: noDefaults,
  },
  {
    // A shelf with settings of its own, and a name that repeats elsewhere in the tree — the
    // two things a careless write or a bare-name picker would lose.
    id: 'c3',
    parentId: null,
    name: 'Bulletins',
    description: null,
    ancestorIds: ['c3'],
    documentCount: 0,
    defaults: {
      ...noDefaults,
      documentTypeId: 7,
      visibility: 'cavingGroup',
      tagIds: [4],
      requiredMetadataKeys: ['author'],
    },
  },
  {
    id: 'c4',
    parentId: 'c1',
    name: 'Bulletins',
    description: null,
    ancestorIds: ['c1', 'c4'],
    documentCount: 0,
    defaults: noDefaults,
  },
];

// Two documents on the shelf and a count of two, so the page is exercised on the case
// where the listing is the caller's readable subset rather than the whole shelf.
const shelf = {
  items: [
    {
      id: 'd1',
      title: 'Cave survey 2026',
      documentTypeId: 1,
      visibility: 'private',
      cavingGroupId: null,
      currentFileId: 'f1',
      kind: 'document',
      mimeType: 'application/pdf',
      sizeBytes: 2048,
      missingMetadataKeys: [],
      updatedAt: '2026-08-01T00:00:00Z',
    },
  ],
  page: 1,
  pageSize: 20,
  totalItems: 1,
};

const createMutate = vi.fn(() => Promise.resolve(cabinets[1]));
const updateMutate = vi.fn(() => Promise.resolve(cabinets[1]));
const deleteMutate = vi.fn(() => Promise.resolve());
const fileMutate = vi.fn(() => Promise.resolve());

// A caller who may read documents but not write them: the tree and its contents are
// readable, none of the controls that move access are offered.
let capabilities = { domains: { documents: 'read' } };

const refetchCabinets = vi.fn();
const cabinetsLoaded = () => ({ data: cabinets, isPending: false, isError: false, refetch: refetchCabinets });
let cabinetsRead: () => unknown = cabinetsLoaded;

vi.mock('../../api/hooks.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/hooks.ts')>('../../api/hooks.ts');
  return {
    hasAccessAction: actual.hasAccessAction,
    useCapabilities: () => ({ data: capabilities }),
    useCabinets: () => cabinetsRead(),
    useCabinetDocuments: (id: string | undefined) => ({
      data: id ? shelf : undefined,
      isFetching: false,
    }),
    useCreateCabinet: () => ({ mutateAsync: createMutate, isPending: false }),
    useUpdateCabinet: () => ({ mutateAsync: updateMutate, isPending: false }),
    useDeleteCabinet: () => ({ mutateAsync: deleteMutate, isPending: false }),
    useFileDocument: () => ({ mutateAsync: fileMutate, isPending: false }),
    useDocumentTypes: () => ({ data: [] }),
    useTags: () => ({ data: [] }),
  };
});

// Width is the only thing this flips; jsdom reports none, so the phone layout is only ever
// reached by saying so.
let mobile = false;
vi.mock('../../hooks/useIsMobile.ts', () => ({ useIsMobile: () => mobile }));

const { default: CabinetsPage } = await import('./CabinetsPage.tsx');

afterEach(() => {
  cleanup();
  cabinetsRead = cabinetsLoaded;
  refetchCabinets.mockReset();
});

function renderPage() {
  return render(
    <MemoryRouter>
      <App>
        <CabinetsPage />
      </App>
    </MemoryRouter>,
  );
}

describe('CabinetsPage', () => {
  it('points an editor, and nobody who can only read, to the deleted documents', () => {
    capabilities = { domains: { documents: 'read, write' } };
    renderPage();
    expect(screen.getByTestId('cabinets-deleted-link')).toHaveAttribute('href', '/documents/deleted');
    cleanup();

    capabilities = { domains: { documents: 'read' } };
    renderPage();
    expect(screen.queryByTestId('cabinets-deleted-link')).not.toBeInTheDocument();
  });

  it('draws the tree with each shelf\'s size and asks for a selection before listing anything', () => {
    capabilities = { domains: { documents: 'read' } };
    renderPage();

    // Both cabinets appear, each carrying how many documents filed on it this caller may
    // read — the server decides that by the same rule as the listing, so the label on a
    // shelf and what opens when it is picked describe the same documents.
    expect(screen.getByText('Club archive (3)')).toBeInTheDocument();
    expect(screen.getByText('Surveys (2)')).toBeInTheDocument();
    expect(screen.getByText('Choose a cabinet to see what is filed in it.')).toBeInTheDocument();
    expect(screen.queryByText('Cave survey 2026')).not.toBeInTheDocument();
  });

  it('lists a selected cabinet with its ancestry, and offers no write control to a reader', () => {
    capabilities = { domains: { documents: 'read' } };
    renderPage();

    fireEvent.click(screen.getByText('Surveys (2)'));

    expect(screen.getByText('Cave survey 2026')).toBeInTheDocument();
    // The breadcrumb is drawn from the flat list already fetched: root first, this last.
    expect(screen.getByText('Club archive')).toBeInTheDocument();

    // A reader gets no control that would move access — no filing, no unfiling, no edit.
    expect(screen.queryByText('New')).not.toBeInTheDocument();
    expect(screen.queryByText('File elsewhere')).not.toBeInTheDocument();
  });

  it('offers filing to a writer, and files rather than moving so a document can sit on several shelves', async () => {
    capabilities = { domains: { documents: 'read, write' } };
    fileMutate.mockClear();
    renderPage();

    fireEvent.click(screen.getByText('Surveys (2)'));
    // The same caller who was refused these controls above is offered them here — the
    // difference is the right, not the page.
    expect(screen.getByText('New')).toBeInTheDocument();

    fireEvent.click(screen.getByText('File elsewhere'));
    fireEvent.mouseDown(screen.getByText('Choose a cabinet'));
    await waitFor(() => expect(screen.getAllByText('Club archive').length).toBeGreaterThan(0));
    fireEvent.click(screen.getAllByText('Club archive').at(-1)!);
    fireEvent.click(screen.getByText('OK'));

    // One request, adding a shelf. Nothing is unfiled: filing elsewhere leaves the
    // document where it already is.
    await waitFor(() => expect(fileMutate).toHaveBeenCalledTimes(1));
    expect(fileMutate).toHaveBeenCalledWith({
      cabinetId: 'c1',
      documentId: 'd1',
      filed: true,
    });
  });

  it('stacks the shelf above the documents on a phone instead of beside them, and still opens one', () => {
    capabilities = { domains: { documents: 'read' } };
    mobile = false;
    const { container, unmount } = renderPage();
    // On a screen with room for both, the tree keeps its own column beside the listing.
    expect(container.querySelector('.ant-layout-sider')).not.toBeNull();
    unmount();

    mobile = true;
    const phone = renderPage();
    // The same page on a phone: no fixed column eating the width, the shelf folded into a
    // panel above the documents, and picking one still gets to the documents.
    expect(phone.container.querySelector('.ant-layout-sider')).toBeNull();
    expect(phone.container.querySelector('.ant-collapse')).not.toBeNull();

    fireEvent.click(screen.getByText('Surveys (2)'));
    expect(screen.getByText('Cave survey 2026')).toBeInTheDocument();
    mobile = false;
  });

  it('moves a cabinet in the tree without clearing the settings it carries', async () => {
    capabilities = { domains: { documents: 'read, write' } };
    updateMutate.mockClear();
    renderPage();

    // The cabinet route replaces what it is sent, so a move that sends only the new parent
    // would wipe this shelf's own type, audience, tags and required keys.
    // The root-level one: roots follow the club archive's subtree in the drawn tree.
    const dragged = screen.getAllByText('Bulletins').at(-1)!.closest('.ant-tree-treenode')!;
    const target = screen.getByText('Surveys (2)').closest('.ant-tree-treenode')!;
    fireEvent.dragStart(dragged);
    fireEvent.dragEnter(target);
    fireEvent.dragOver(target);
    fireEvent.drop(target);

    await waitFor(() => expect(updateMutate).toHaveBeenCalledTimes(1));
    expect(updateMutate).toHaveBeenCalledWith({
      id: 'c3',
      name: 'Bulletins',
      description: null,
      parentId: 'c2',
      defaultDocumentTypeId: 7,
      defaultVisibility: 'cavingGroup',
      defaultTagIds: [4],
      requiredMetadataKeys: ['author'],
    });
  });

  it('OK with no name marks the field, sends nothing and leaves no unhandled rejection', async () => {
    capabilities = { domains: { documents: 'read, write' } };
    createMutate.mockClear();
    const unhandled: unknown[] = [];
    const record = (reason: unknown) => unhandled.push(reason);
    process.on('unhandledRejection', record);
    try {
      renderPage();
      fireEvent.click(screen.getByText('New'));
      const name = await screen.findByLabelText('Name');
      fireEvent.click(screen.getByRole('button', { name: 'OK' }));

      await waitFor(() => expect(name.getAttribute('aria-invalid')).toBe('true'));
      // Long enough for a rejection with no handler to be reported.
      await new Promise((resolve) => setTimeout(resolve, 50));
      expect(createMutate).not.toHaveBeenCalled();
      expect(unhandled).toEqual([]);
    } finally {
      process.off('unhandledRejection', record);
    }
  });

  it('offers a cabinet the four audiences the server knows, each named', async () => {
    capabilities = { domains: { documents: 'read, write' } };
    renderPage();

    fireEvent.click(screen.getByText('New'));
    const field = await screen.findByLabelText('Who can read it');
    fireEvent.mouseDown(field);

    for (const label of ['Private', 'Caving group', 'Authenticated users', 'Public']) {
      await waitFor(() => expect(screen.getAllByTitle(label).length).toBeGreaterThan(0));
    }
    expect(screen.queryByText(/visibilityValues/)).not.toBeInTheDocument();
  });

  it('names every filing target by its whole path, so two shelves of one name can be told apart', async () => {
    capabilities = { domains: { documents: 'read, write' } };
    renderPage();

    fireEvent.click(screen.getByText('Surveys (2)'));
    fireEvent.click(screen.getByText('File elsewhere'));
    fireEvent.mouseDown(screen.getByText('Choose a cabinet'));

    await waitFor(() => expect(screen.getAllByTitle('Club archive / Bulletins').length).toBeGreaterThan(0));
    expect(screen.getAllByTitle('Bulletins').length).toBeGreaterThan(0);
  });

  // A tree that could not be read is not an empty one: drawn as nothing but the inbox, it reads as
  // an archive with no shelves in it.
  it('says the cabinets could not be read, keeps the inbox, and asks again on Retry', () => {
    cabinetsRead = () => ({ data: undefined, isPending: false, isError: true, error: new Error('offline'), refetch: refetchCabinets });
    renderPage();
    expect(screen.getByTestId('cabinets-load-failed')).toBeTruthy();
    expect(screen.getByText(i18n.t('cabinets.unfiled'))).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: i18n.t('common.retry') }));
    expect(refetchCabinets).toHaveBeenCalledTimes(1);
  });

  it('keeps a tree already shown when a later read fails, and says it may be out of date', () => {
    cabinetsRead = () => ({ data: cabinets, isPending: false, isError: true, error: new Error('offline'), refetch: refetchCabinets });
    renderPage();
    expect(screen.getByText('Club archive (3)')).toBeTruthy();
    expect(screen.getByText(i18n.t('common.refreshFailed'))).toBeTruthy();
  });
});
