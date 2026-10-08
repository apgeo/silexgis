// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { App as AntApp } from 'antd';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { ApiError } from '../../api/client.ts';
import type { DeletedFeature } from '../../api/hooks.ts';
import { onSurfaceFeaturesChanged } from '../../workspace/surfaceFeatureRefresh.ts';

const { listSpy, restoreSpy } = vi.hoisted(() => ({
  listSpy: vi.fn(),
  restoreSpy: vi.fn(),
}));

vi.mock('../../api/hooks.ts', () => ({
  useDeletedFeatures: (page: number) => listSpy(page),
  useFeatureTypes: () => ({ data: [{ id: 7, code: 'sinkhole', name: 'Sinkhole' }] }),
  useRestoreFeature: () => ({ mutateAsync: restoreSpy, isPending: false, variables: undefined }),
}));

const { default: DeletedFeaturesPage } = await import('./DeletedFeaturesPage.tsx');

const CAVE = '11111111-1111-1111-1111-111111111111';
const ENTRANCE = '22222222-2222-2222-2222-222222222222';
const SINKHOLE = '33333333-3333-3333-3333-333333333333';

function deleted(overrides: Partial<DeletedFeature> = {}): DeletedFeature {
  return {
    id: CAVE,
    kind: 'cave',
    featureTypeCode: null,
    name: 'Coiba Mare',
    deletedAt: '2026-10-05T09:30:00Z',
    entranceCount: 1,
    otherCount: 0,
    parents: [],
    ...overrides,
  };
}

function answer(items: DeletedFeature[], totalItems = items.length) {
  listSpy.mockReturnValue({
    data: { items, page: 1, pageSize: 50, totalItems },
    isFetching: false,
    isError: false,
  });
}

function show() {
  return render(
    <AntApp>
      <MemoryRouter initialEntries={['/features/deleted']}>
        <Routes>
          <Route path="/features/deleted" element={<DeletedFeaturesPage />} />
          <Route path="/caves/:id" element={<div data-testid="cave-page" />} />
          <Route path="/features/:id" element={<div data-testid="feature-page" />} />
        </Routes>
      </MemoryRouter>
    </AntApp>,
  );
}

/** Presses Restore on the one row and then the confirmation's own button. */
async function restoreTheRow() {
  fireEvent.click(screen.getByTestId('deleted-feature-restore'));
  const dialog = await screen.findByRole('tooltip');
  fireEvent.click(within(dialog).getByRole('button', { name: 'Restore' }));
}

afterEach(cleanup);
beforeEach(() => {
  listSpy.mockReset();
  restoreSpy.mockReset().mockResolvedValue({ kind: 'cave', feature: { id: CAVE } });
  answer([deleted()]);
});

describe('deleted caves and features', () => {
  it('lists a deletion with what it is and what went with it', () => {
    show();

    const table = screen.getByTestId('deleted-features');
    expect(within(table).getByText('Coiba Mare')).toBeTruthy();
    expect(within(table).getByText('Cave')).toBeTruthy();
    expect(within(table).getByTestId('deleted-feature-took').textContent).toBe('1 entrance');
  });

  /**
   * One row is one deletion, and what it took is said in words a reader can check against what
   * they remember deleting: singular where there was one, both kinds where there were both, and
   * a dash rather than "0 entrances" where the feature went alone.
   */
  it.each([
    [{ entranceCount: 0, otherCount: 0 }, '—'],
    [{ entranceCount: 3, otherCount: 0 }, '3 entrances'],
    [{ entranceCount: 0, otherCount: 1 }, '1 other object'],
    [{ entranceCount: 2, otherCount: 4 }, '2 entrances, 4 other objects'],
  ])('says what went with a deletion (%#)', (counts, text) => {
    answer([deleted(counts)]);
    show();

    expect(screen.getByTestId('deleted-feature-took').textContent).toBe(text);
  });

  it('names a surface feature by its type, and an entrance by where it sat', () => {
    answer([
      deleted({ id: SINKHOLE, kind: 'generic', featureTypeCode: 'sinkhole', name: 'Dolina 4', entranceCount: 0 }),
      deleted({
        id: ENTRANCE,
        kind: 'caveEntrance',
        name: 'Intrarea de sus',
        entranceCount: 0,
        parents: [
          { id: 'aaaaaaaa-0000-0000-0000-000000000001', name: 'Padurea Craiului' },
          { id: CAVE, name: 'Coiba Mare' },
        ],
      }),
    ]);
    show();

    const table = screen.getByTestId('deleted-features');
    expect(within(table).getByText('Sinkhole')).toBeTruthy();
    expect(within(table).getByText('Entrance')).toBeTruthy();
    expect(within(table).getByText('Padurea Craiului › Coiba Mare')).toBeTruthy();
  });

  it('restores only after the confirmation, tells the map, and opens the cave', async () => {
    const mapHeard = vi.fn();
    const stop = onSurfaceFeaturesChanged(mapHeard);
    show();

    fireEvent.click(screen.getByTestId('deleted-feature-restore'));
    expect(restoreSpy).not.toHaveBeenCalled();

    const dialog = await screen.findByRole('tooltip');
    expect(within(dialog).getByText('Restore this?')).toBeTruthy();
    fireEvent.click(within(dialog).getByRole('button', { name: 'Restore' }));

    await waitFor(() => expect(restoreSpy).toHaveBeenCalledWith(CAVE));
    expect(await screen.findByTestId('cave-page')).toBeTruthy();
    expect(mapHeard).toHaveBeenCalledTimes(1);
    stop();
  });

  /** An entrance has no page of its own: it is read on its cave's, so that is what opens. */
  it('opens the cave of a restored entrance, and the feature page of anything else', async () => {
    answer([deleted({ id: ENTRANCE, kind: 'caveEntrance', entranceCount: 0 })]);
    restoreSpy.mockResolvedValue({
      kind: 'caveEntrance',
      feature: { id: ENTRANCE },
      entrance: { caveFeatureId: CAVE },
    });
    const first = show();
    await restoreTheRow();
    expect(await screen.findByTestId('cave-page')).toBeTruthy();
    first.unmount();

    answer([deleted({ id: SINKHOLE, kind: 'generic', featureTypeCode: 'sinkhole', entranceCount: 0 })]);
    restoreSpy.mockResolvedValue({ kind: 'generic', feature: { id: SINKHOLE } });
    show();
    await restoreTheRow();
    expect(await screen.findByTestId('feature-page')).toBeTruthy();
  });

  /**
   * Each refusal the route names is said in its own words and leaves the reader where they are.
   * What is in the way is named only when the server named it: a container this reader could not
   * see blocks the restore all the same and is not named to them for doing so.
   */
  it.each([
    [
      new ApiError(409, 'feature.restore_container_deleted', undefined, {
        restoreFirst: { id: 'aaaaaaaa-0000-0000-0000-000000000001', kind: 'generic', name: 'Padurea Craiului' },
      }),
      'It is inside Padurea Craiului, which is deleted. Restore that first.',
    ],
    [
      new ApiError(409, 'feature.restore_container_deleted', undefined, { restoreFirst: null }),
      'It is inside something that is deleted, which has to be restored first.',
    ],
    [new ApiError(409, 'feature.not_deleted'), 'That is no longer among the deleted ones.'],
    [new ApiError(404, 'feature.not_found'), 'That is no longer among the deleted ones.'],
    [new ApiError(403, 'acl.forbidden'), 'You may not restore that.'],
  ])('says why a restore was refused and stays on the list (%#)', async (error, sentence) => {
    restoreSpy.mockRejectedValue(error);
    show();

    await restoreTheRow();

    expect(await screen.findByText(sentence)).toBeTruthy();
    expect(screen.queryByTestId('cave-page')).toBeNull();
  });

  it('tells an empty list from one that could not be read', () => {
    answer([]);
    const first = show();
    expect(screen.getByTestId('deleted-features-empty').textContent).toContain('Nothing here');
    first.unmount();

    listSpy.mockReturnValue({ data: undefined, isFetching: false, isError: true });
    show();
    expect(screen.getByTestId('deleted-features-empty').textContent).toContain('could not be loaded');
  });
});
