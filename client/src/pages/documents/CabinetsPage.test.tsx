// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import '../../i18n';

const cabinets = [
  {
    id: 'c1',
    parentId: null,
    name: 'Club archive',
    description: 'Everything the club keeps.',
    ancestorIds: ['c1'],
    documentCount: 3,
  },
  {
    id: 'c2',
    parentId: 'c1',
    name: 'Surveys',
    description: null,
    ancestorIds: ['c1', 'c2'],
    documentCount: 2,
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

vi.mock('../../api/hooks.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/hooks.ts')>('../../api/hooks.ts');
  return {
    hasAccessAction: actual.hasAccessAction,
    useCapabilities: () => ({ data: capabilities }),
    useCabinets: () => ({ data: cabinets, isPending: false }),
    useCabinetDocuments: (id: string | undefined) => ({
      data: id ? shelf : undefined,
      isFetching: false,
    }),
    useCreateCabinet: () => ({ mutateAsync: createMutate, isPending: false }),
    useUpdateCabinet: () => ({ mutateAsync: updateMutate, isPending: false }),
    useDeleteCabinet: () => ({ mutateAsync: deleteMutate, isPending: false }),
    useFileDocument: () => ({ mutateAsync: fileMutate, isPending: false }),
  };
});

// Width is the only thing this flips; jsdom reports none, so the phone layout is only ever
// reached by saying so.
let mobile = false;
vi.mock('../../hooks/useIsMobile.ts', () => ({ useIsMobile: () => mobile }));

const { default: CabinetsPage } = await import('./CabinetsPage.tsx');

afterEach(cleanup);

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
  it('draws the tree with each shelf\'s size and asks for a selection before listing anything', () => {
    capabilities = { domains: { documents: 'read' } };
    renderPage();

    // Both cabinets appear, each carrying how full it is — the count is the whole shelf,
    // not the caller's subset, which is why it is shown on the tree and not on the list.
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
});
