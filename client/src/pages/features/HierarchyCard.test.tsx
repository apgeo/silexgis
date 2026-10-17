// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { App as AntApp } from 'antd';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { ApiError } from '../../api/client.ts';
import type { FeatureKind } from '../../api/hooks.ts';

const { setParentsSpy } = vi.hoisted(() => ({ setParentsSpy: vi.fn() }));

const CAVE = '11111111-1111-1111-1111-111111111111';
const FEATURE = '22222222-2222-2222-2222-222222222222';
const AREA = '33333333-3333-3333-3333-333333333333';
const CHILD = '44444444-4444-4444-4444-444444444444';

const { childItems } = vi.hoisted(() => ({ childItems: [] as { id: string; name: string; kind: string; isPrimary: boolean }[] }));

const { queryCalls } = vi.hoisted(() => ({ queryCalls: [] as { body: unknown; enabled: boolean }[] }));

const hit = (id: string, title: string, subtitle: string) =>
  ({ world: 'feature', id, title, subtitle, symbol: null, placeable: false });

vi.mock('../../api/hooks.ts', () => ({
  useFeatureParents: () => ({ data: [{ id: CAVE, name: 'Peștera Urșilor', isPrimary: true }] }),
  useFeatureChildren: () => ({ data: { items: childItems, page: 1, pageSize: 10, totalItems: childItems.length } }),
  useSetFeatureParents: () => ({ mutateAsync: setParentsSpy, isPending: false }),
  useFilterVocabulary: () => ({ data: { worlds: [{ world: 'feature', labelKey: 'x', fields: [], sorts: [] }] } }),
  // The search answers with the feature itself, its parent already on the list, one of its
  // children (when the card lists it as one) and one other:
  // what the picker offers out of that is what these tests are about.
  useFilterQuery: (body: unknown, enabled: boolean) => {
    queryCalls.push({ body, enabled });
    return enabled
      ? {
          data: {
            worlds: [{
              world: 'feature',
              hits: [
                hit(CAVE, 'Peștera Urșilor', 'Cave'),
                hit(FEATURE, 'Galeria Mare', 'Passage'),
                hit(AREA, 'Zona Bihor', 'Karst area'),
                hit(CHILD, 'Sala Cristalelor', 'Chamber'),
              ],
              total: null,
            }],
            page: 1, pageSize: 6, counted: false,
          },
          isFetching: false,
        }
      : { data: undefined, isFetching: false };
  },
  useFilterResolve: () => ({ data: [] }),
}));

const { default: HierarchyCard } = await import('./HierarchyCard.tsx');

function show(kind: FeatureKind) {
  return render(
    <AntApp>
      <MemoryRouter>
        <HierarchyCard featureId={FEATURE} kind={kind} canEdit />
      </MemoryRouter>
    </AntApp>,
  );
}

afterEach(cleanup);
beforeEach(() => {
  setParentsSpy.mockReset().mockResolvedValue([]);
  queryCalls.length = 0;
  childItems.length = 0;
  childItems.push({ id: CHILD, name: 'Sala Cristalelor', kind: 'generic', isPrimary: true });
});

async function openEditorAndSearch() {
  fireEvent.click(screen.getByRole('button', { name: /Edit parents/ }));
  fireEvent.change(await screen.findByTestId('hierarchy-parent-picker-input'), { target: { value: 'zona' } });
  // The search is debounced; a loaded machine can take longer than the default second to answer.
  await screen.findByRole('button', { name: /Zona Bihor/ }, { timeout: 3000 });
}

describe('the hierarchy card', () => {
  it.each<FeatureKind>(['caveEntrance', 'centerline'])(
    'offers no parent editor for a %s, whose one parent is its own cave, and says why',
    (kind) => {
      show(kind);

      expect(screen.queryByRole('button', { name: /Edit parents/ })).toBeNull();
      expect(screen.getByTestId('hierarchy-parent-fixed').textContent).toContain('sits under its own cave');
      // The parent is still shown, as a link to it.
      expect(screen.getByRole('link', { name: 'Peștera Urșilor' })).toBeTruthy();
    },
  );

  it('offers the editor for a feature that may be filed anywhere', () => {
    show('generic');

    expect(screen.getByRole('button', { name: /Edit parents/ })).toBeTruthy();
    expect(screen.queryByTestId('hierarchy-parent-fixed')).toBeNull();
  });

  it('searches the whole feature world, unnarrowed by kind', async () => {
    show('generic');
    await openEditorAndSearch();

    const asked = queryCalls.filter((c) => c.enabled).at(-1)!;
    const document = JSON.stringify(asked.body);
    expect(document).toContain('"feature"');
    expect(document).toContain('zona');
    // No kind narrowing: a parent may be any kind of feature, as it could be before.
    expect(document).not.toContain('"kind"');
  });

  it('offers neither the feature itself, nor a parent it already has, nor a child it lists', async () => {
    show('generic');
    await openEditorAndSearch();

    const offered = screen.getAllByTestId('hierarchy-parent-picker-row').map((r) => r.textContent);
    expect(offered).toEqual([expect.stringContaining('Zona Bihor')]);
  });

  it('offers the same feature once the card no longer lists it as a child', async () => {
    // The positive case of the one above: the search row is the same, only the card's children
    // differ, so its absence there is the exclusion and not the search.
    childItems.length = 0;
    show('generic');
    await openEditorAndSearch();

    const offered = screen.getAllByTestId('hierarchy-parent-picker-row').map((r) => r.textContent);
    expect(offered).toEqual([
      expect.stringContaining('Zona Bihor'),
      expect.stringContaining('Sala Cristalelor'),
    ]);
  });

  it('adds a picked feature as a further parent under the name the row gave it, and saves both', async () => {
    show('generic');
    await openEditorAndSearch();

    fireEvent.click(screen.getByRole('button', { name: /Zona Bihor/ }));

    const dialog = screen.getByRole('dialog');
    expect(within(dialog).getByRole('cell', { name: 'Zona Bihor' })).toBeTruthy();
    // Once added it is no longer offered, and the picker holds no value of its own.
    await waitFor(() => expect(screen.queryByTestId('hierarchy-parent-picker-row')).toBeNull());

    fireEvent.click(screen.getByRole('button', { name: 'OK' }));
    await waitFor(() => expect(setParentsSpy).toHaveBeenCalled());
    expect(setParentsSpy.mock.calls[0][0]).toEqual({
      id: FEATURE,
      parents: [
        { parentId: CAVE, isPrimary: true },
        { parentId: AREA, isPrimary: false },
      ],
    });
  });

  it('adds a parent from the keyboard alone', async () => {
    show('generic');
    await openEditorAndSearch();

    fireEvent.keyDown(screen.getByTestId('hierarchy-parent-picker-input'), { key: 'ArrowDown' });
    const row = screen.getByRole('button', { name: /Zona Bihor/ });
    expect(row).toHaveFocus();
    fireEvent.keyDown(row, { key: 'Enter' });

    expect(within(screen.getByRole('dialog')).getByRole('cell', { name: 'Zona Bihor' })).toBeTruthy();
  });

  it('names the fixed-parent refusal in words rather than as a failed save', async () => {
    setParentsSpy.mockRejectedValue(new ApiError(400, 'feature.parent_fixed'));
    show('generic');

    fireEvent.click(screen.getByRole('button', { name: /Edit parents/ }));
    fireEvent.click(await screen.findByRole('button', { name: 'OK' }));

    expect(await screen.findByText(/sits under its own cave and nowhere else\. To protect it/)).toBeTruthy();
    expect(screen.queryByText('The operation failed. Please try again.')).toBeNull();
  });

  it('keeps the general failure for every other refusal', async () => {
    setParentsSpy.mockRejectedValue(new ApiError(400, 'feature.parent_cycle'));
    show('generic');

    fireEvent.click(screen.getByRole('button', { name: /Edit parents/ }));
    fireEvent.click(await screen.findByRole('button', { name: 'OK' }));

    expect(await screen.findByText('The operation failed. Please try again.')).toBeTruthy();
  });
});
