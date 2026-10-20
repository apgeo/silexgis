// SPDX-License-Identifier: AGPL-3.0-or-later
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import { App as AntApp } from 'antd';
import { useEffect } from 'react';
import { MemoryRouter, useLocation, useNavigate } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import i18n from '../../i18n';
import {
  CAVE_REGISTRY_SCOPE,
  caveRegistryDocument,
  caveRegistryToken,
  readCaveRegistryAddress,
} from '../../filters/caveRegistry.ts';
import { whereOf, withWhere } from '../../filters/document.ts';
import type { FilterDocument, FilterNode } from '../../filters/types.ts';
import { decodeFilter, encodeFilter } from '../../filters/url.ts';

const { canSpy, doorSpy, cavesSpy, caveItems, downloadSpy, savedFilters, editorSpy, editorMounts, selectAllSpy, heldWrites } = vi.hoisted(() => ({
  caveItems: { current: [] as unknown[] },
  editorSpy: vi.fn(),
  editorMounts: { count: 0 },
  heldWrites: { on: false, queue: [] as (() => void)[] },
  selectAllSpy: vi.fn(),
  canSpy: vi.fn(),
  doorSpy: vi.fn(),
  cavesSpy: vi.fn(),
  downloadSpy: vi.fn(),
  savedFilters: { current: [] as unknown[] },
}));

// The browser's router changes the address bar at once and tells the page a moment later. Holding
// the page's writes stands for that moment, so what is opened inside it can be proved.
vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual<typeof import('react-router-dom')>('react-router-dom');
  return {
    ...actual,
    useSearchParams: (...args: Parameters<typeof actual.useSearchParams>) => {
      const [params, write] = actual.useSearchParams(...args);
      const held: typeof write = (...writeArgs) => {
        if (heldWrites.on) {
          heldWrites.queue.push(() => write(...writeArgs));
        } else {
          write(...writeArgs);
        }
      };
      return [params, held] as const;
    },
  };
});

vi.mock('../../api/hooks.ts', () => ({
  useCan: (domain: string, action: string) => canSpy(domain, action),
  useCreateDoor: (domain: string) => doorSpy(domain),
  useCaves: (params: unknown) => {
    cavesSpy(params);
    const items = caveItems.current;
    return { data: { items, page: 1, pageSize: 20, totalItems: items.length }, isFetching: false, error: null };
  },
  useCaveTypes: () => ({ data: [{ id: 3, name: 'Karst cave' }] }),
  useTags: () => ({ data: [{ id: 1, slug: 'bats', name: 'Bats' }] }),
}));

// The save dialogs' audience control reads the member's own caving groups from their access.
vi.mock('../../api/hooks/access.ts', () => ({
  useCapabilities: () => ({ data: {}, isLoading: false }),
  useCreateDoor: () => ({ canCreate: true, unbound: false, cavingGroups: [] }),
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

vi.mock('../../api/download.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/download.ts')>('../../api/download.ts');
  return {
    ...actual,
    downloadFile: (url: string) => {
      downloadSpy(url);
      return Promise.resolve();
    },
  };
});

// Neither is what this is about, and each reads the server on its own account.
vi.mock('../../components/statistics/CaveDistributionPanel.tsx', () => ({ default: () => null }));
vi.mock('../../components/caves/KarstLinkExportModal.tsx', () => ({ default: () => null }));

// The working-set fill is proved on its own; here what matters is which question it is handed.
vi.mock('../../components/filters/SelectResultButton.tsx', () => ({
  default: (props: { query: unknown }) => {
    selectAllSpy(props.query);
    return null;
  },
}));

// The editor is proved on its own; here it stands for "somebody wrote this and pressed Apply", so
// what is proved is the road between the page's address and the editor, both ways.
const EDITED_DEPTH: FilterNode = {
  node: 'condition',
  field: 'depth',
  op: 'lessOrEqual',
  values: [{ type: 'number', value: 50 }],
};
vi.mock('../../components/filters/FilterEditor.tsx', () => ({
  default: function StubEditor(props: {
    world: string;
    document: FilterDocument;
    fixed?: FilterNode;
    onApply: (document: FilterDocument) => void;
    onCancel?: () => void;
  }) {
    editorSpy(props);
    useEffect(() => {
      editorMounts.count += 1;
    }, []);
    const edited = withWhere(props.document, props.world, {
      node: 'allOf',
      of: [
        { node: 'condition', field: 'region', op: 'contains', values: [{ type: 'text', value: 'Alba' }] },
        EDITED_DEPTH,
      ],
    });
    return (
      <div>
        <button type="button" data-testid="stub-apply" onClick={() => props.onApply(edited)}>apply</button>
        <button type="button" data-testid="stub-cancel" onClick={() => props.onCancel?.()}>cancel</button>
      </div>
    );
  },
}));

const { default: CaveListPage } = await import('./CaveListPage.tsx');

function Address() {
  const location = useLocation();
  const navigate = useNavigate();
  return (
    <>
      <span data-testid="address">{location.pathname}</span>
      <span data-testid="search">{location.search}</span>
      {/* The browser's Back, so a test can tell one push from the same page pushed twice. */}
      <button type="button" data-testid="history-back" onClick={() => navigate(-1)} />
    </>
  );
}

function show(entry = '/caves') {
  return render(
    <AntApp>
      <MemoryRouter initialEntries={[entry]}>
        <CaveListPage />
        <Address />
      </MemoryRouter>
    </AntApp>,
  );
}

const NO_DOOR = { canCreate: false, unbound: false, cavingGroups: [] };

afterEach(cleanup);
beforeEach(async () => {
  await i18n.changeLanguage('en');
  canSpy.mockReset().mockReturnValue(false);
  doorSpy.mockReset().mockReturnValue(NO_DOOR);
  cavesSpy.mockReset();
  caveItems.current = [];
  downloadSpy.mockReset();
  savedFilters.current = [];
  editorSpy.mockReset();
  editorMounts.count = 0;
  heldWrites.on = false;
  heldWrites.queue = [];
  selectAllSpy.mockReset();
});

/**
 * The door to the deleted caves and features is offered to whoever could have deleted one. Which
 * deletions are on the page behind it is the server's decision, row by row; this only decides
 * whether the door is worth showing. An owner deletes their own cave by owning it, so "may record
 * a cave" is half of the answer — and that half is the create door's, not the right over caves as
 * such, because somebody who records caves only for their caving group owns them like anybody.
 */
describe('the door from the cave list to the deleted caves and features', () => {
  it('is not shown to somebody who may neither record a cave nor delete one', () => {
    show();

    expect(screen.queryByTestId('cave-list-deleted')).toBeNull();
  });

  it('is shown to somebody who records caves only for their caving group, and leads there', () => {
    doorSpy.mockReturnValue({ canCreate: true, unbound: false, cavingGroups: [{ id: 'g1', name: 'Speo Club' }] });
    show();

    fireEvent.click(screen.getByTestId('cave-list-deleted'));
    expect(screen.getByTestId('address').textContent).toBe('/features/deleted');
    expect(doorSpy).toHaveBeenCalledWith('features');
  });

  it('is shown to somebody who may delete caves and record none', () => {
    canSpy.mockImplementation((domain, action) => domain === 'features' && action === 'delete');
    show();

    expect(screen.getByTestId('cave-list-deleted')).toBeTruthy();
  });
});

describe('the registry read from its address', () => {
  it('fills the controls from a link and asks the list with that link’s filter', () => {
    const token = encodeFilter(caveRegistryDocument({ region: 'Bihor', search: 'urs', tag: 'bats' })!);
    show(`/caves?filter=${token}&page=2`);

    expect(screen.getByTestId('cave-registry-region')).toHaveValue('Bihor');
    expect(screen.getByDisplayValue('urs')).toBeInTheDocument();
    expect(cavesSpy).toHaveBeenLastCalledWith(expect.objectContaining({ filter: token, page: 2 }));
  });

  it('opens unfiltered, with a notice, when the link’s filter cannot be read', () => {
    show('/caves?filter=eyJ2ZXJzaW9uIjox');

    expect(screen.getByTestId('cave-registry-unreadable-link')).toBeInTheDocument();
    expect(cavesSpy).toHaveBeenLastCalledWith(expect.objectContaining({ filter: undefined }));
  });

  it('keeps a space typed at the end of the search while the next word is on its way', () => {
    vi.useFakeTimers();
    try {
      show('/caves');

      // The test id sits on the search control's wrapper; the box is the input inside it.
      const box = screen.getByTestId('cave-registry-search').querySelector('input')!;
      fireEvent.change(box, { target: { value: 'pestera ' } });
      act(() => {
        vi.advanceTimersByTime(1000);
      });

      // The address holds the trimmed words, and the box still holds what was typed.
      const state = readCaveRegistryAddress(new URLSearchParams(screen.getByTestId('search').textContent!));
      expect(state.controls).toEqual({ search: 'pestera' });
      expect(box).toHaveValue('pestera ');
    } finally {
      vi.useRealTimers();
    }
  });

  it('writes a changed control into the address, back on the first page', () => {
    show('/caves?page=3');

    const region = screen.getByTestId('cave-registry-region');
    fireEvent.change(region, { target: { value: 'Alba' } });
    fireEvent.blur(region);

    const state = readCaveRegistryAddress(new URLSearchParams(screen.getByTestId('search').textContent!));
    expect(state.controls).toEqual({ region: 'Alba' });
    expect(state.page).toBe(1);
  });

  it('takes a minimum length when Enter is pressed in its field', () => {
    show('/caves');

    const length = screen.getByTestId('cave-registry-min-length');
    fireEvent.change(length, { target: { value: '300' } });
    fireEvent.keyDown(length, { key: 'Enter', code: 'Enter', keyCode: 13 });

    const state = readCaveRegistryAddress(new URLSearchParams(screen.getByTestId('search').textContent!));
    expect(state.controls).toEqual({ length: { op: 'greaterOrEqual', values: [300], negated: false } });
  });

  it('takes a comparison typed with signs into the length, says what it read, and refuses what it cannot', () => {
    show('/caves');
    const length = screen.getByTestId('cave-registry-min-length');
    // The library takes Enter once until the key is let go.
    const press = (el: HTMLElement) => {
      fireEvent.keyDown(el, { key: 'Enter', code: 'Enter', keyCode: 13 });
      fireEvent.keyUp(el, { key: 'Enter', code: 'Enter', keyCode: 13 });
    };
    const controls = () =>
      readCaveRegistryAddress(new URLSearchParams(screen.getByTestId('search').textContent!)).controls;

    fireEvent.change(length, { target: { value: '300-500 m' } });
    expect(screen.getByTestId('cave-registry-length-understood')).toHaveTextContent('Read as: between 300 m and 500 m');
    press(length);
    expect(controls()).toEqual({ length: { op: 'between', values: [300, 500], negated: false } });

    fireEvent.change(length, { target: { value: '!=2,5' } });
    press(length);
    expect(controls()).toEqual({ length: { op: 'equals', values: [2.5], negated: true } });

    fireEvent.change(length, { target: { value: '300 km' } });
    expect(screen.getByTestId('cave-registry-length-unreadable')).toBeInTheDocument();
    press(length);
    expect(controls()).toEqual({ length: { op: 'equals', values: [2.5], negated: true } });
  });

  it('exports with the list’s own filter, region, tag and length included', async () => {
    const token = encodeFilter(caveRegistryDocument({ region: 'Bihor', tag: 'bats', length: { op: 'greaterOrEqual', values: [300], negated: false } })!);
    show(`/caves?filter=${token}`);

    fireEvent.mouseEnter(screen.getByRole('button', { name: /export/i }));
    fireEvent.click(await screen.findByText('CSV'));

    expect(downloadSpy).toHaveBeenCalledTimes(1);
    const url = new URL(downloadSpy.mock.calls[0]![0] as string, 'http://x');
    expect(url.pathname).toBe('/api/v1/export/caves');
    expect(url.searchParams.get('filter')).toBe(token);
  });

  it('withholds the interchange export while the list is cut to the map’s window', async () => {
    show('/caves?bbox=22.1,46.2,22.4,46.5');

    fireEvent.mouseEnter(screen.getByRole('button', { name: /export/i }));
    const withheld = await screen.findByTestId('cave-registry-karstlink-withheld');
    expect(withheld.closest('[role="menuitem"]')).toHaveAttribute('aria-disabled', 'true');

    // The ordinary formats still go, cut to the window as the list is.
    fireEvent.click(await screen.findByText('CSV'));
    const url = new URL(downloadSpy.mock.calls[0]![0] as string, 'http://x');
    expect(url.searchParams.get('bbox')).toBe('22.1,46.2,22.4,46.5');
  });

  it('offers the interchange export when the list has no window', async () => {
    show('/caves');

    fireEvent.mouseEnter(screen.getByRole('button', { name: /export/i }));
    const item = await screen.findByText('KarstLink (JSON-LD)');
    expect(item.closest('[role="menuitem"]')).not.toHaveAttribute('aria-disabled', 'true');
    expect(screen.queryByTestId('cave-registry-karstlink-withheld')).toBeNull();
  });

  it('marks the applied saved filter as changed once the controls move', () => {
    const document = caveRegistryDocument({ region: 'Bihor' })!;
    savedFilters.current = [{
      id: 'f-1',
      name: 'Bihor',
      description: null,
      document,
      state: 'ready',
      cavingGroupId: null,
      visibility: 'private',
      mine: true,
      createdAt: '',
      updatedAt: '',
    }];

    show(`/caves?filter=${encodeFilter(document)}&saved=f-1`);
    expect(screen.getByTestId('cave-registry-active-filter')).toHaveTextContent('Saved filter: Bihor');
    expect(screen.queryByTestId('cave-registry-active-changed')).toBeNull();

    const region = screen.getByTestId('cave-registry-region');
    fireEvent.change(region, { target: { value: 'Alba' } });
    fireEvent.blur(region);

    expect(screen.getByTestId('cave-registry-active-changed')).toBeInTheDocument();
  });
});

/**
 * The advanced filter edits the question the controls compose — the same document, read from the
 * address and written back to it — so what it writes reaches the list, the link, the exports and a
 * saved filter by the road a control's change takes.
 */
describe('the advanced filter on the cave registry', () => {
  const ODD_DEPTH: FilterNode = {
    node: 'condition',
    field: 'depth',
    op: 'greaterThan',
    values: [{ type: 'number', value: 10 }],
  };

  it('opens on the controls’ conditions and the extra ones, with the fixed “caves” kept out of the rows', async () => {
    const document = caveRegistryDocument({ region: 'Bihor', length: { op: 'greaterOrEqual', values: [300], negated: false } }, [ODD_DEPTH])!;
    show(`/caves?filter=${encodeFilter(document)}`);

    fireEvent.click(screen.getByTestId('cave-registry-advanced'));
    await screen.findByTestId('stub-apply');

    const props = editorSpy.mock.lastCall![0] as { world: string; document: FilterDocument; fixed?: FilterNode };
    expect(props.world).toBe('feature');
    expect(props.fixed).toEqual(CAVE_REGISTRY_SCOPE);
    expect(whereOf(props.document, 'feature')).toEqual({
      node: 'allOf',
      of: [
        { node: 'condition', field: 'region', op: 'contains', values: [{ type: 'text', value: 'Bihor' }] },
        { node: 'condition', field: 'surveyedLength', op: 'greaterOrEqual', values: [{ type: 'number', value: 300 }] },
        ODD_DEPTH,
      ],
    });
  });

  it('writes what was applied back to the controls and the address, the rest kept as extra conditions', async () => {
    show('/caves?page=4');

    fireEvent.click(screen.getByTestId('cave-registry-advanced'));
    fireEvent.click(await screen.findByTestId('stub-apply'));

    const state = readCaveRegistryAddress(new URLSearchParams(screen.getByTestId('search').textContent!));
    expect(state.controls).toEqual({ region: 'Alba' });
    expect(state.extra).toEqual([EDITED_DEPTH]);
    expect(state.page).toBe(1);
    expect(screen.getByTestId('cave-registry-region')).toHaveValue('Alba');
    expect(screen.getByTestId('cave-registry-extra')).toBeInTheDocument();
    expect(cavesSpy).toHaveBeenLastCalledWith(
      expect.objectContaining({ filter: caveRegistryToken({ controls: { region: 'Alba' }, extra: [EDITED_DEPTH] }) }),
    );
  });

  it('is reached from the notice about conditions the controls cannot show', async () => {
    const document = caveRegistryDocument({}, [ODD_DEPTH])!;
    show(`/caves?filter=${encodeFilter(document)}`);

    fireEvent.click(screen.getByTestId('cave-registry-edit-extra'));
    await screen.findByTestId('stub-apply');

    const props = editorSpy.mock.lastCall![0] as { document: FilterDocument };
    expect(whereOf(props.document, 'feature')).toEqual({ node: 'allOf', of: [ODD_DEPTH] });
  });

  it('opens on a control just written that the page has not yet been told of, and is not seeded again when it arrives', async () => {
    show('/caves');
    heldWrites.on = true;
    const region = screen.getByTestId('cave-registry-region');
    fireEvent.change(region, { target: { value: 'Bihor' } });
    fireEvent.blur(region);
    expect(heldWrites.queue).toHaveLength(1);

    fireEvent.click(screen.getByTestId('cave-registry-advanced'));
    await screen.findByTestId('stub-apply');
    const opened = editorSpy.mock.lastCall![0] as { document: FilterDocument };
    expect(whereOf(opened.document, 'feature')).toEqual({
      node: 'allOf',
      of: [{ node: 'condition', field: 'region', op: 'contains', values: [{ type: 'text', value: 'Bihor' }] }],
    });

    heldWrites.on = false;
    act(() => {
      heldWrites.queue.splice(0).forEach((apply) => apply());
    });
    expect(readCaveRegistryAddress(new URLSearchParams(screen.getByTestId('search').textContent!)).controls)
      .toEqual({ region: 'Bihor' });
    // Still the editor that was opened: whatever had been written in it is still there.
    expect(screen.getByTestId('stub-apply')).toBeInTheDocument();
    expect(editorMounts.count).toBe(1);
  });

  it('changes nothing when it is closed without applying', async () => {
    const token = encodeFilter(caveRegistryDocument({ region: 'Bihor' })!);
    show(`/caves?filter=${token}`);
    const before = screen.getByTestId('search').textContent;

    fireEvent.click(screen.getByTestId('cave-registry-advanced'));
    fireEvent.click(await screen.findByTestId('stub-cancel'));

    expect(screen.getByTestId('search').textContent).toBe(before);
  });
});

describe('the working set filled from a registry ordered nearest first', () => {
  it('is asked the list’s own request, with the place the order is measured from', () => {
    show('/caves?sort=proximity&near=22.5,46.25');
    const listed = cavesSpy.mock.calls.at(-1)?.[0] as { filter?: string; sort?: string };
    const asked = selectAllSpy.mock.calls.at(-1)?.[0] as { filter?: string; sort?: string };
    expect(listed.sort).toBe('proximity');
    expect(decodeFilter(listed.filter)?.anchor).toEqual({ anchor: 'point', longitude: 22.5, latitude: 46.25 });
    expect(asked).toMatchObject({ filter: listed.filter, sort: 'proximity' });
  });
});

describe('a row of the cave registry', () => {
  it('is opened from the keyboard by a link on the cave’s name, which pushes the page once', () => {
    caveItems.current = [
      { id: 'c-1', name: 'Ice Cave', caveTypeId: 3, entranceCount: 1, visibility: 'Public' },
    ];
    show();

    const link = screen.getByRole('link', { name: 'Ice Cave' });
    expect(link).toHaveAttribute('href', '/caves/c-1');
    fireEvent.click(link);
    expect(screen.getByTestId('address')).toHaveTextContent('/caves/c-1');
    // One Back returns to the registry: had the row's own click pushed the cave a second time,
    // Back would land on the same cave and seem to do nothing.
    fireEvent.click(screen.getByTestId('history-back'));
    expect(screen.getByTestId('address')).toHaveTextContent(/^\/caves$/);
  });
});
