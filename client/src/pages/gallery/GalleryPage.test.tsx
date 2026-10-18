// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { App as AntApp } from 'antd';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import i18n from '../../i18n';
import { galleryDocument, galleryToken, galleryWhere, readGalleryFilter } from '../../filters/gallery.ts';
import { emptyDocument, withWhere } from '../../filters/document.ts';
import type { FilterDocument, FilterNode } from '../../filters/types.ts';
import { encodeFilter } from '../../filters/url.ts';

const { photosSpy, editorSpy, savedFilters, extentCutShort } = vi.hoisted(() => ({
  photosSpy: vi.fn(),
  editorSpy: vi.fn(),
  savedFilters: { current: [] as unknown[] },
  extentCutShort: { current: false },
}));

vi.mock('../../api/hooks.ts', () => ({
  hasAccessAction: () => true,
  useCapabilities: () => ({ data: { domains: { documents: ['read'] } } }),
  useCavers: () => ({ data: [] }),
  useTags: () => ({ data: [] }),
  // A chosen tag is found by its slug even when the first page of names does not hold it.
  useTagsBySlug: (slugs: string[]) => ({
    data: slugs.map((slug) => ({ id: 1, slug, name: `Tag ${slug}` })),
  }),
  useAlbums: () => ({ data: { items: [] } }),
  usePhotoBulk: () => ({ mutateAsync: vi.fn() }),
  usePhotos: (params: unknown) => {
    photosSpy(params);
    return {
      data: { items: [], page: 1, pageSize: 60, totalItems: 0, extentCutShort: extentCutShort.current },
      isFetching: false,
      isError: false,
    };
  },
}));

vi.mock('../../api/hooks/savedFilters.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/hooks/savedFilters.ts')>(
    '../../api/hooks/savedFilters.ts',
  );
  return {
    documentOf: actual.documentOf,
    useSavedFilters: () => ({ data: savedFilters.current, isSuccess: true }),
    useCreateSavedFilter: () => ({ mutateAsync: vi.fn(), isPending: false }),
  };
});

vi.mock('../../components/gallery/Lightbox.tsx', () => ({ default: () => null }));
vi.mock('../../components/gallery/PhotoCreditDrawer.tsx', () => ({ default: () => null }));
vi.mock('../../components/gallery/PhotoGrid.tsx', () => ({ default: () => null }));

// The editor is proved on its own; here it stands for "somebody wrote this and pressed Apply".
const TAKEN_SINCE: FilterNode = {
  node: 'condition',
  field: 'takenAt',
  op: 'greaterOrEqual',
  values: [{ type: 'instant', value: '2024-01-01T00:00:00Z' }],
};
vi.mock('../../components/filters/FilterEditor.tsx', () => ({
  default: (props: { world: string; document: FilterDocument; onApply: (document: FilterDocument) => void }) => {
    editorSpy(props);
    return (
      <button
        type="button"
        data-testid="stub-apply"
        onClick={() => props.onApply(withWhere(props.document, props.world, TAKEN_SINCE))}
      >
        apply
      </button>
    );
  },
}));

const { default: GalleryPage } = await import('./GalleryPage.tsx');

function Address() {
  const location = useLocation();
  return <span data-testid="search">{location.search}</span>;
}

function show(entry = '/gallery') {
  return render(
    <AntApp>
      <MemoryRouter initialEntries={[entry]}>
        <GalleryPage />
        <Address />
      </MemoryRouter>
    </AntApp>,
  );
}

function address() {
  return new URLSearchParams(screen.getByTestId('search').textContent!);
}

const TITLED: FilterNode = {
  node: 'condition',
  field: 'title',
  op: 'contains',
  values: [{ type: 'text', value: 'entrance' }],
};

afterEach(cleanup);
beforeEach(async () => {
  await i18n.changeLanguage('en');
  photosSpy.mockReset();
  editorSpy.mockReset();
  savedFilters.current = [];
  extentCutShort.current = false;
});

/**
 * The gallery reads a filter: the photograph world's conditions travel in its address beside its own
 * controls, reach the list as one token, and can be saved and applied as a gallery view.
 */
describe('the gallery filter', () => {
  it('asks the list with the conditions in the address, beside the controls', () => {
    show(`/gallery?caverId=c-1&filter=${galleryToken(TITLED)}`);

    expect(photosSpy).toHaveBeenLastCalledWith(
      expect.objectContaining({ caverId: 'c-1', filter: galleryToken(TITLED) }),
    );
  });

  it('asks the list without a filter when the address has none', () => {
    show('/gallery?caverId=c-1');

    expect(photosSpy).toHaveBeenLastCalledWith(expect.objectContaining({ filter: undefined }));
  });

  it('writes what the condition editor applies into the address and the request, keeping the controls', () => {
    show(`/gallery?caverId=c-1&filter=${galleryToken(TITLED)}`);

    fireEvent.click(screen.getByTestId('gallery-advanced'));
    expect(editorSpy).toHaveBeenLastCalledWith(expect.objectContaining({ world: 'photograph' }));
    fireEvent.click(screen.getByTestId('stub-apply'));

    expect(readGalleryFilter(address()).where).toEqual(TAKEN_SINCE);
    expect(address().get('caverId')).toBe('c-1');
    expect(photosSpy).toHaveBeenLastCalledWith(
      expect.objectContaining({ caverId: 'c-1', filter: galleryToken(TAKEN_SINCE) }),
    );
  });

  it('writes the search box into the document, not beside it, keeping the other conditions', () => {
    show(`/gallery?filter=${galleryToken(TITLED)}`);

    const box = screen.getByTestId('gallery-search').querySelector('input') ?? screen.getByTestId('gallery-search');
    fireEvent.change(box, { target: { value: 'cascade' } });
    fireEvent.keyDown(box, { key: 'Enter', code: 'Enter', keyCode: 13 });

    const where = galleryWhere({ search: 'cascade' }, [TITLED]);
    expect(readGalleryFilter(address()).where).toEqual(where);
    expect(address().has('search')).toBe(false);
    const asked = photosSpy.mock.lastCall?.[0] as Record<string, unknown>;
    expect(asked.filter).toBe(galleryToken(where));
    expect(asked).not.toHaveProperty('search');
    expect(asked).not.toHaveProperty('tagId');
  });

  it('shows the tag and the dates a link or a saved view carries in the controls', () => {
    show(`/gallery?filter=${galleryToken(galleryWhere({ tag: 'entrance', from: '2024-03-01', to: '2024-03-31' }))}`);

    expect(screen.getByTestId('gallery-tag')).toHaveTextContent('Tag entrance');
    expect(screen.getByDisplayValue('2024-03-01')).toBeInTheDocument();
    expect(screen.getByDisplayValue('2024-03-31')).toBeInTheDocument();
    // Nothing beyond the controls, so the conditions button says nothing extra is on.
    expect(screen.getByTestId('gallery-advanced')).not.toHaveTextContent('·');
  });

  it('says which controls a saved view does not keep, only while one of them is set', () => {
    show('/gallery?caverId=c-1');
    expect(screen.getByTestId('gallery-unsaved-controls')).toBeInTheDocument();

    cleanup();
    show(`/gallery?filter=${galleryToken(galleryWhere({ tag: 'entrance' }))}`);
    expect(screen.queryByTestId('gallery-unsaved-controls')).toBeNull();
  });

  it('applies a saved gallery view and marks it changed once the conditions move', () => {
    savedFilters.current = [{
      id: 'f-1',
      name: 'Entrances',
      description: null,
      document: galleryDocument(TITLED),
      state: 'ready',
      cavingGroupId: null,
      visibility: 'private',
      mine: true,
      createdAt: '',
      updatedAt: '',
    }];

    show(`/gallery?filter=${galleryToken(TITLED)}&saved=f-1`);
    expect(screen.getByTestId('gallery-active-filter')).toHaveTextContent('Saved view: Entrances');
    expect(screen.queryByTestId('gallery-active-changed')).toBeNull();

    fireEvent.click(screen.getByTestId('gallery-advanced'));
    fireEvent.click(screen.getByTestId('stub-apply'));

    expect(address().get('saved')).toBe('f-1');
    expect(screen.getByTestId('gallery-active-changed')).toBeInTheDocument();
  });

  it('opens a link whose conditions it cannot read without them, and says so', () => {
    const aboutCaves = withWhere(emptyDocument(['feature']), 'feature', TITLED);
    show(`/gallery?filter=${encodeFilter(aboutCaves)}`);

    expect(screen.getByTestId('gallery-unreadable-link')).toBeInTheDocument();
    expect(photosSpy).toHaveBeenLastCalledWith(expect.objectContaining({ filter: undefined }));
  });
});

describe('a map extent with more photographs than one search carries', () => {
  it('says that not every photograph of the extent is listed, only when the answer was cut short', () => {
    show('/gallery?bbox=25,45,26,46');
    expect(screen.queryByTestId('gallery-extent-cut-short')).not.toBeInTheDocument();
    cleanup();

    extentCutShort.current = true;
    show('/gallery?bbox=25,45,26,46');
    expect(screen.getByTestId('gallery-extent-cut-short')).toHaveTextContent(
      i18n.t('gallery.mapExtentCutShort'),
    );
  });

  it('says nothing about an extent when the gallery is not narrowed to one', () => {
    extentCutShort.current = true;
    show('/gallery');
    expect(screen.queryByTestId('gallery-extent-cut-short')).not.toBeInTheDocument();
  });
});
