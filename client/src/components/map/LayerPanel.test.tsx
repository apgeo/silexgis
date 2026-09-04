// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import LayerGroup from 'ol/layer/Group';
import '../../i18n';
import type { LibraryPhotoHealth, LibraryPhotoProvider } from '../../api/hooks.ts';
import type { LibraryPhotoLoadState, LibraryPhotoLoadStates } from '../../map/libraryPhotoLayer.ts';
import LayerPanel from './LayerPanel.tsx';

/**
 * The block of the layer panel that says what a neighbouring photo library is doing.
 *
 * Every case here is a state that draws the same empty patch of map: a library nobody connected, a
 * library that is not answering, one answering with a credential that lacks the rights this
 * integration needs, one answering and holding nothing in this rectangle, and one answering with
 * photographs. The panel is the only place any of them is a sentence rather than an absence, which
 * is why they are asserted as words.
 *
 * Every address, name, key permission and version below is invented.
 */

const recheck = vi.fn();
let recheckState = { mutate: recheck, isPending: false, isError: false, variables: undefined as string | undefined };

vi.mock('../../api/hooks.ts', () => ({
  useTags: () => ({ data: [] }),
  useRecheckPhotoLibrary: () => recheckState,
}));

let loadStates: LibraryPhotoLoadStates = {};

vi.mock('../../map/libraryPhotoLayer.ts', () => ({
  getLibraryPhotoLoadStates: () => loadStates,
  subscribeLibraryPhotoLoadStates: () => () => {},
  setLibraryPhotoPictures: vi.fn(),
  libraryPhotoSourceOf: () => undefined,
}));

vi.mock('../../map/mapContext.ts', () => ({
  getOverlayGroup: () => new LayerGroup({ layers: [] }),
}));

const healthy: LibraryPhotoHealth = {
  reach: 'reachable',
  version: '1.142.0',
  missingPermissions: [],
  picturesAvailable: true,
  failureCode: null,
  probedAt: '2026-09-04T10:11:12Z',
};

function library(health: LibraryPhotoHealth): LibraryPhotoProvider {
  return { source: 'immich', name: 'Immich', configured: true, health };
}

const answered: LibraryPhotoLoadState = {
  libraryName: 'Immich',
  shownCount: 0,
  truncated: false,
  omittedCount: 0,
  readAt: '2026-09-04T10:11:12Z',
  pictureUrlTemplate: '/api/v1/photo-libraries/immich/thumbnails/{reference}?size={size}',
  pictures: false,
  picturesSuppressed: false,
  reach: 'ok',
};

function renderPanel(
  overrides: {
    photoLibraries?: LibraryPhotoProvider[];
    unconfiguredPhotoLibraries?: LibraryPhotoProvider[];
    visibleLibraryPhotoSources?: string[];
  } = {},
) {
  render(
    <LayerPanel
      layers={[]}
      activeBaseId={undefined}
      onBaseChange={vi.fn()}
      baseOpacity={{}}
      onBaseOpacityChange={vi.fn()}
      geofiles={[]}
      visibleGeofileIds={[]}
      onGeofileVisibleChange={vi.fn()}
      visibleTileOverlayIds={[]}
      onTileOverlayVisibleChange={vi.fn()}
      tileOverlayOpacity={{}}
      onTileOverlayOpacityChange={vi.fn()}
      declutterLabels={false}
      onDeclutterLabelsChange={vi.fn()}
      rasters={[]}
      visibleRasterIds={[]}
      onRasterVisibleChange={vi.fn()}
      onOverlayVisibilityChanged={vi.fn()}
      photoLibraries={overrides.photoLibraries ?? [library(healthy)]}
      unconfiguredPhotoLibraries={overrides.unconfiguredPhotoLibraries ?? []}
      visibleLibraryPhotoSources={overrides.visibleLibraryPhotoSources ?? ['immich']}
      treeNonce={0}
      tagFilter={null}
      onTagFilterChange={vi.fn()}
      centerlinesVisible={false}
      onCenterlineLimitsChange={vi.fn()}
    />,
  );
}

beforeEach(() => {
  loadStates = { immich: answered };
  recheckState = { mutate: recheck, isPending: false, isError: false, variables: undefined };
  recheck.mockClear();
});

afterEach(cleanup);

describe('the photo-library block of the layer panel', () => {
  it('says a library is not answering rather than showing it as an area with nothing in it', () => {
    renderPanel({
      photoLibraries: [
        library({ ...healthy, reach: 'unreachable', version: null, failureCode: 'photo_library.unavailable' }),
      ],
    });

    const block = screen.getByTestId('library-photos-status-immich');
    expect(block).toHaveTextContent(/did not answer/i);

    // The sentence that must not be shown for a library that is down: it is a true statement about
    // the rectangle and a false one about why the map is empty.
    expect(block).not.toHaveTextContent(/no photographs in this area/i);
  });

  it('names a refused credential as a credential, not as a library that is down', () => {
    // Both facts at once, which is what a library behind something that refuses the credential
    // looks like: nothing answered the question, and the reason was the credential. The wording an
    // operator is given has to be the one that sends them to the right screen.
    renderPanel({
      photoLibraries: [
        library({
          ...healthy,
          reach: 'unreachable',
          version: null,
          failureCode: 'photo_library.unauthorized',
        }),
      ],
    });

    const block = screen.getByTestId('library-photos-status-immich');
    expect(block).toHaveTextContent(/would not accept the credential/i);
    expect(block).not.toHaveTextContent(/Check that it is running/i);
  });

  it('names the rights the credential does not carry, one by one', () => {
    renderPanel({
      photoLibraries: [library({ ...healthy, missingPermissions: ['map.read', 'asset.view'] })],
    });

    // The names themselves, because "your key is missing asset.view" is a fix and "the key is not
    // sufficient" is a search through a permission list of over a hundred entries.
    const block = screen.getByTestId('library-photos-status-immich');
    expect(block).toHaveTextContent('map.read, asset.view');
  });

  it('separates a library holding nothing here from a library that is not working', () => {
    renderPanel();

    const block = screen.getByTestId('library-photos-status-immich');
    expect(block).toHaveTextContent(/no photographs in this area/i);
    expect(block).not.toHaveTextContent(/did not answer/i);
  });

  it('counts the photographs when there are some', () => {
    loadStates = { immich: { ...answered, shownCount: 4 } };
    renderPanel();

    expect(screen.getByTestId('library-photos-status-immich')).toHaveTextContent('4 photographs shown');
  });

  it('says what the library is and when it was last asked', () => {
    renderPanel();

    // The version an operator quotes in a bug report, and the moment the line describes — without
    // which a health line read now and a health line read a minute ago look the same.
    expect(screen.getByTestId('library-photos-status-immich')).toHaveTextContent('1.142.0');
  });

  it('says out loud that this installation has stopped asking the library for pictures', () => {
    renderPanel({ photoLibraries: [library({ ...healthy, picturesAvailable: false })] });

    expect(screen.getByTestId('library-photos-status-immich')).toHaveTextContent(
      /answered a picture request without a picture/i,
    );
  });

  it('asks the server to check that one library again', () => {
    renderPanel();

    fireEvent.click(screen.getByTestId('library-photos-recheck-immich'));

    expect(recheck).toHaveBeenCalledWith('immich');
  });

  it('says when the check itself did not go through, rather than leaving the button looking dead', () => {
    recheckState = { mutate: recheck, isPending: false, isError: true, variables: 'immich' };
    renderPanel();

    expect(screen.getByTestId('library-photos-status-immich')).toHaveTextContent(
      /still did not answer/i,
    );
  });

  it('names a product nobody connected, which is the one thing an empty panel cannot say', () => {
    renderPanel({
      photoLibraries: [],
      visibleLibraryPhotoSources: [],
      unconfiguredPhotoLibraries: [
        {
          source: 'photoprism',
          name: 'PhotoPrism',
          configured: false,
          health: {
            reach: 'unknown',
            version: null,
            missingPermissions: [],
            picturesAvailable: false,
            failureCode: null,
            probedAt: null,
          },
        },
      ],
    });

    expect(screen.getByTestId('library-photos-absent-photoprism')).toHaveTextContent(
      /PhotoPrism is not connected/i,
    );
  });

  it('says nothing about unconnected products to an account the server sent none for', () => {
    // The server sends this list to a full administrator and to nobody else, so an empty list is
    // the whole of the rule reaching the panel — there is deliberately no second check here that
    // could disagree with it.
    renderPanel({ unconfiguredPhotoLibraries: [] });

    expect(screen.queryByTestId('library-photos-absent-photoprism')).toBeNull();
  });
});
