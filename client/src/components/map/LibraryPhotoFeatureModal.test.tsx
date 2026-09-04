// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { LibraryPhotoFeatureCreated } from '../../api/hooks.ts';
import type { LibraryPhotoFeatureTarget } from '../../map/libraryPhotoPopup.ts';
import LibraryPhotoFeatureModal from './LibraryPhotoFeatureModal.tsx';

/**
 * The dialogue that turns a photograph in a neighbouring library into an object in this
 * installation's own registry.
 *
 * The load-bearing case is the first one: what leaves the browser. A coordinate in that request
 * would make the whole gesture a way of putting an object anywhere at all while it looked as though
 * a camera had measured it, and nothing further down the stack could tell the difference — so it is
 * asserted here as the exact body, not as a body containing the right fields.
 *
 * Every reference, name and rectangle below is invented.
 */

const mutateAsync = vi.fn();
const setSelection = vi.fn();

vi.mock('../../api/hooks.ts', () => ({
  useCreateFeatureFromLibraryPhoto: () => ({ mutateAsync }),
  useFeatureTypes: () => ({
    data: [
      {
        id: 12,
        code: 'spring',
        name: 'Spring',
        requiresParent: false,
        acceptedGeometryClasses: ['point'],
      },
      // Neither of these may be offered: one cannot hold a point, and one cannot exist without
      // something above it — and this dialogue creates with nothing above it.
      {
        id: 13,
        code: 'massif',
        name: 'Massif',
        requiresParent: false,
        acceptedGeometryClasses: ['polygon'],
      },
      {
        id: 14,
        code: 'in_cave_place',
        name: 'Place in a cave',
        requiresParent: true,
        acceptedGeometryClasses: ['point'],
      },
    ],
  }),
  useSearch: () => ({ data: { features: [] } }),
}));

vi.mock('../../map/entranceLayer.ts', () => ({ reloadEntrances: vi.fn() }));
vi.mock('../../workspace/surfaceFeatureRefresh.ts', () => ({ surfaceFeaturesChanged: vi.fn() }));
vi.mock('../../stores/workspaceStore.ts', () => ({
  useWorkspaceStore: (select: (state: { setSelection: unknown }) => unknown) =>
    select({ setSelection }),
}));

const target: LibraryPhotoFeatureTarget = {
  source: 'photoprism',
  reference: 'aa11bb22cc33',
  bbox: '21.5,45.125,24.25,46.75',
  title: 'muddy crawl.jpg',
};

const created: LibraryPhotoFeatureCreated = {
  featureId: '11111111-1111-4111-8111-111111111111',
  name: 'muddy crawl.jpg',
  kind: 'cave',
  nearby: [],
};

function show() {
  render(
    <App>
      <LibraryPhotoFeatureModal target={target} onClose={() => {}} />
    </App>,
  );
}

const confirm = () => fireEvent.click(screen.getByRole('button', { name: 'OK' }));

describe('LibraryPhotoFeatureModal', () => {
  beforeEach(() => {
    mutateAsync.mockReset().mockResolvedValue(created);
    setSelection.mockReset();
  });

  afterEach(cleanup);

  it('asks the server for a photograph and a rectangle, and never for a position', async () => {
    show();

    // The library's own title is offered as the name rather than imposed; confirming takes it.
    confirm();

    await waitFor(() => expect(mutateAsync).toHaveBeenCalledTimes(1));
    expect(mutateAsync).toHaveBeenCalledWith({
      source: 'photoprism',
      reference: 'aa11bb22cc33',
      bbox: '21.5,45.125,24.25,46.75',
      body: {
        kind: 'cave',
        name: 'muddy crawl.jpg',
        featureTypeId: null,
        caveFeatureId: null,
      },
    });
  });

  it('says what was created and offers to open it', async () => {
    show();
    confirm();

    await screen.findByText(/muddy crawl\.jpg was created/);
    fireEvent.click(screen.getByText('Open it'));

    expect(setSelection).toHaveBeenCalledWith({
      kind: 'cave',
      caveId: '11111111-1111-4111-8111-111111111111',
    });
  });

  it('warns about what was already standing there, having created the object anyway', async () => {
    // Forty photographs of one entrance would otherwise become forty caves — and refusing would
    // make a judgement only the person who took the picture can make.
    mutateAsync.mockResolvedValue({
      ...created,
      nearby: [
        {
          featureId: '22222222-2222-4222-8222-222222222222',
          name: 'Upper entrance',
          kind: 'caveEntrance',
          distanceMeters: 3.4,
        },
      ],
    });
    show();
    confirm();

    await screen.findByText('Something is already recorded near here');
    expect(screen.getByText('Upper entrance — 3.4 m away')).toBeTruthy();

    // Created all the same: the warning sits beside the success, never instead of it.
    expect(screen.getByText(/muddy crawl\.jpg was created/)).toBeTruthy();
  });

  it('offers only the kinds a photograph can actually become', async () => {
    show();

    fireEvent.mouseDown(screen.getByRole('combobox'));
    fireEvent.click(await screen.findByTitle('Another kind'));

    // The kind list appears only once "another kind" is chosen, so it is waited for rather than
    // reached for: the choice and the field that depends on it settle on different renders.
    await waitFor(() => expect(screen.getAllByRole('combobox')).toHaveLength(2));
    fireEvent.mouseDown(screen.getAllByRole('combobox')[1]);

    expect(await screen.findByTitle('Spring')).toBeTruthy();
    // A kind that holds no point, and one that cannot exist outside a container, would each be a
    // choice the server refuses on submission.
    expect(screen.queryByTitle('Massif')).toBeNull();
    expect(screen.queryByTitle('Place in a cave')).toBeNull();
  });
});
