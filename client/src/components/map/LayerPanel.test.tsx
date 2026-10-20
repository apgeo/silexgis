// SPDX-License-Identifier: AGPL-3.0-or-later
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import LayerGroup from 'ol/layer/Group';
import '../../i18n';
import { ApiError } from '../../api/client.ts';
import type { LibraryPhotoHealth, LibraryPhotoProvider, MapLayerInfo } from '../../api/hooks.ts';
import type { LibraryPhotoLoadState, LibraryPhotoLoadStates } from '../../map/libraryPhotoLayer.ts';
import LayerPanel from './LayerPanel.tsx';
import type { FilterLayerRowInfo } from './FilterLayerRow.tsx';
import { CENTERLINE_STATUS_ID } from './centerlineStatus.ts';
import { addFilterLayer, MAX_FILTER_LAYERS } from '../../map/filterLayerList.ts';
import type Map from 'ol/Map';
import { fromLonLat } from 'ol/proj';
import type { PhotoFeatureCollection } from '../../api/hooks.ts';
import { attachPhotoLoader, setPhotosEnabled } from '../../map/photoLayer.ts';

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
let recheckState = {
  mutate: recheck,
  isPending: false,
  isError: false,
  variables: undefined as string | undefined,
  error: null as unknown,
};

vi.mock('../../api/hooks.ts', () => ({
  useTags: () => ({ data: [] }),
  // The trips overlay's own filter reads the purposes a trip can have. None configured here: the
  // mock replaces the module wholesale, so a hook left out of it is undefined at the call site and
  // every case in this file dies on the render rather than on what it was written to check.
  useTripTypes: () => ({ data: [] }),
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
  return {
    source: 'immich',
    name: 'Immich',
    search: 'meaning',
    configured: true,
    suspended: false,
    health,
  };
}

const answered: LibraryPhotoLoadState = {
  libraryName: 'Immich',
  shownCount: 0,
  truncated: false,
  omittedCount: 0,
  readAt: '2026-09-04T10:11:12Z',
  bbox: '21.5,45.125,24.25,46.75',
  pictureUrlTemplate: '/api/v1/photo-libraries/immich/thumbnails/{reference}?size={size}',
  pictures: false,
  picturesSuppressed: false,
  reach: 'ok',
};

interface PanelOverrides {
  layers?: MapLayerInfo[];
  activeBaseId?: number;
  photoLibraries?: LibraryPhotoProvider[];
  unconfiguredPhotoLibraries?: LibraryPhotoProvider[];
  suspendedPhotoLibraries?: LibraryPhotoProvider[];
  visibleLibraryPhotoSources?: string[];
  centerlinesVisible?: boolean;
  filterLayers?: FilterLayerRowInfo[];
  filterLayerCandidates?: { id: string; name: string }[];
  onAddFilterLayer?: (savedFilterId: string) => void;
}

function renderPanel(overrides: PanelOverrides = {}) {
  return render(panel(overrides));
}

function panel(overrides: PanelOverrides = {}) {
  return (
    <LayerPanel
      layers={overrides.layers ?? []}
      activeBaseId={overrides.activeBaseId}
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
      terrainDerivatives={[]}
      visibleTerrainDerivativeIds={[]}
      onTerrainDerivativeVisibleChange={vi.fn()}
      onOverlayVisibilityChanged={vi.fn()}
      photoLibraries={overrides.photoLibraries ?? [library(healthy)]}
      unconfiguredPhotoLibraries={overrides.unconfiguredPhotoLibraries ?? []}
      suspendedPhotoLibraries={overrides.suspendedPhotoLibraries ?? []}
      visibleLibraryPhotoSources={overrides.visibleLibraryPhotoSources ?? ['immich']}
      treeNonce={0}
      tagFilter={null}
      onTagFilterChange={vi.fn()}
      filterLayers={overrides.filterLayers}
      filterLayerCandidates={overrides.filterLayerCandidates}
      onAddFilterLayer={overrides.onAddFilterLayer}
      centerlinesVisible={overrides.centerlinesVisible ?? false}
      onCenterlineLimitsChange={vi.fn()}
      tripsVisible={false}
      karstDensityVisible={false}
      karstDensityMode="density"
      onKarstDensityModeChange={() => {}}
      karstIndexVisible={false}
      tripFilter={{}}
      onTripFilterChange={vi.fn()}
      unappliedTripFilters={0}
    />
  );
}

beforeEach(() => {
  loadStates = { immich: answered };
  recheckState = { mutate: recheck, isPending: false, isError: false, variables: undefined, error: null };
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
    recheckState = {
      mutate: recheck,
      isPending: false,
      isError: true,
      variables: 'immich',
      error: new ApiError(503, 'photo_library.unavailable'),
    };
    renderPanel();

    expect(screen.getByTestId('library-photos-status-immich')).toHaveTextContent(
      /still did not answer/i,
    );
  });

  it('does not report a refused credential as a library that is still down', () => {
    // The two failures the button can come back with are the two the whole block exists to
    // separate. A library that answered in milliseconds and refused the credential is not a
    // library that did not answer, and being told it is down sends an operator to the container
    // while what needs fixing is on the library's own settings screen.
    recheckState = {
      mutate: recheck,
      isPending: false,
      isError: true,
      variables: 'immich',
      error: new ApiError(503, 'photo_library.unauthorized'),
    };
    renderPanel();

    const block = screen.getByTestId('library-photos-status-immich');
    expect(block).toHaveTextContent(/would not accept the credential/i);
    expect(block).not.toHaveTextContent(/still did not answer/i);
  });

  it('says a viewport was cut short at the limit, which on a map looks like a viewport that ended', () => {
    loadStates = { immich: { ...answered, shownCount: 3, truncated: true, omittedCount: 0 } };
    renderPanel();

    // Off the flag and not off how many were held back: this library is asked for a clamped count,
    // so it truncates with nothing left to count, and a line keyed off the number would say
    // nothing at all for exactly the case it exists for.
    expect(screen.getByTestId('library-photos-truncated-immich')).toHaveTextContent(
      /only the first 3 photographs/i,
    );
  });

  it('says nothing about a limit for a viewport that simply ended', () => {
    loadStates = { immich: { ...answered, shownCount: 3 } };
    renderPanel();

    expect(screen.queryByTestId('library-photos-truncated-immich')).toBeNull();
  });

  it('still counts the photographs on the map while warning about the library', () => {
    // A library that answered one of the questions asked about itself and not another can be
    // answering viewports perfectly. The pins are drawn; withholding the count while the map shows
    // them argues with what the reader can already see.
    loadStates = { immich: { ...answered, shownCount: 4 } };
    renderPanel({
      photoLibraries: [library({ ...healthy, failureCode: 'photo_library.unavailable' })],
    });

    const block = screen.getByTestId('library-photos-status-immich');
    expect(block).toHaveTextContent('4 photographs shown');
    expect(block).toHaveTextContent(/did not come back/i);

    // And not the sentence for an answer that could not be read: nothing came back to read, and
    // sending an operator to look for a malformed answer is sending them nowhere.
    expect(block).not.toHaveTextContent(/not in a way this installation could read/i);
  });

  it('does not claim the library holds nothing here while it is only half answering', () => {
    renderPanel({
      photoLibraries: [library({ ...healthy, failureCode: 'photo_library.rejected' })],
    });

    const block = screen.getByTestId('library-photos-status-immich');
    expect(block).toHaveTextContent(/not in a way this installation could read/i);
    expect(block).not.toHaveTextContent(/no photographs in this area/i);
  });

  it('names a product nobody connected, which is the one thing an empty panel cannot say', () => {
    renderPanel({
      photoLibraries: [],
      visibleLibraryPhotoSources: [],
      unconfiguredPhotoLibraries: [
        {
          source: 'photoprism',
          name: 'PhotoPrism',
          search: 'text',
          configured: false,
          suspended: false,
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

  it('says a stopped library is stopped rather than letting it vanish from the panel', () => {
    // The three empty maps this panel exists to tell apart, and this is the third: a library that
    // is connected, running, and simply not being used. Left to itself it would be an overlay that
    // was there yesterday and is missing today, which reads as a library that broke.
    renderPanel({
      photoLibraries: [],
      visibleLibraryPhotoSources: [],
      suspendedPhotoLibraries: [{ ...library(healthy), suspended: true }],
    });

    const block = screen.getByTestId('library-photos-suspended-immich');
    expect(block).toHaveTextContent(/not using this photo library/i);
    // And no layer row: an overlay that can only ever draw nothing is not an overlay.
    expect(screen.queryByTestId('library-photos-status-immich')).toBeNull();
  });

  it('says nothing about unconnected products to an account the server sent none for', () => {
    // The server sends this list to a full administrator and to nobody else, so an empty list is
    // the whole of the rule reaching the panel — there is deliberately no second check here that
    // could disagree with it.
    renderPanel({ unconfiguredPhotoLibraries: [] });

    expect(screen.queryByTestId('library-photos-absent-photoprism')).toBeNull();
  });
});

describe('the basemap groups of the layer panel', () => {
  // An invented catalogue: one source outside any group, and two groups of two.
  const base = (id: number, name: string, groupName: string | null): MapLayerInfo => ({
    id,
    name,
    layerKind: 'xyz',
    urlTemplate: `https://tiles.example.invalid/${id}/{z}/{x}/{y}.png`,
    wms: null,
    wmts: null,
    attribution: null,
    groupName,
    minZoom: 0,
    maxZoom: 19,
    isBase: true,
    isDefault: id === 2,
    sortOrder: id,
    inDocuments: false,
  });
  const catalogue = [
    base(1, 'Loose Base', null),
    base(2, 'Street Base', 'Everyday'),
    base(3, 'Plain Base', 'Everyday'),
    base(4, 'Photo Base', 'Imagery'),
    base(5, 'Satellite Base', 'Imagery'),
  ];

  it('opens the group holding the drawn basemap even when the basemap is chosen after the catalogue arrives', () => {
    // The order the map page really goes through: the catalogue lands with nothing chosen yet, and
    // the default is picked out of it on the next render. A panel that only read its opening state
    // once would keep every group shut and hide the checked source inside one.
    const view = renderPanel({ layers: catalogue, activeBaseId: undefined });
    expect(screen.queryByRole('radio', { name: 'Street Base' })).toBeNull();

    view.rerender(panel({ layers: catalogue, activeBaseId: 2 }));

    expect(screen.getByRole('radio', { name: 'Street Base' })).toBeChecked();
    // Only that group: the others stay folded away, which is what the groups are for.
    expect(screen.queryByRole('radio', { name: 'Photo Base' })).toBeNull();
  });

  it('opens the group of a basemap chosen later, without closing the one already open', () => {
    // A restored view naming a source from a folded group has to show which source is drawn; and
    // a group somebody opened stays open rather than snapping shut behind them.
    const view = renderPanel({ layers: catalogue, activeBaseId: 2 });
    expect(screen.getByRole('radio', { name: 'Street Base' })).toBeChecked();

    view.rerender(panel({ layers: catalogue, activeBaseId: 5 }));

    expect(screen.getByRole('radio', { name: 'Satellite Base' })).toBeChecked();
    expect(screen.getByRole('radio', { name: 'Street Base' })).not.toBeChecked();
  });

  it('lets a group be folded by hand while its basemap stays drawn', () => {
    renderPanel({ layers: catalogue, activeBaseId: 2 });
    const header = screen.getByRole('button', { name: /Everyday \(2\)/ });
    expect(header).toHaveAttribute('aria-expanded', 'true');

    fireEvent.click(header);

    // Folding is about room on screen: the group stays shut although its source is still drawn,
    // because the panel opens a group when the drawn basemap moves into it and not on every render.
    expect(header).toHaveAttribute('aria-expanded', 'false');
    expect(screen.getByRole('radio', { name: 'Street Base', hidden: true })).toBeChecked();
  });
});

describe('the survey-line limits of the layer panel', () => {
  it('accepts exactly what the advanced settings accept for the same two numbers', () => {
    // The two places write the same stored values. A number one of them takes and the other marks
    // as out of range reads as one of the two being broken.
    renderPanel({ centerlinesVisible: true });

    const detailZoom = screen.getByRole('spinbutton', { name: 'Detail from zoom' });
    expect(detailZoom).toHaveAttribute('aria-valuemin', '1');
    expect(detailZoom).toHaveAttribute('aria-valuemax', '22');
    expect(screen.getByRole('spinbutton', { name: 'Line budget' })).toHaveAttribute('aria-valuemin', '100');
  });

  it('offers the map-corner notice a named block to take focus to, only while the lines are on', () => {
    // The notice on the map opens the panel and moves focus by this id; a block that is not
    // there, or not focusable, leaves a keyboard user wherever the notice was.
    const { unmount } = renderPanel({ centerlinesVisible: true });
    const block = screen.getByRole('group', { name: 'Cave centerlines' });
    expect(block).toHaveAttribute('id', CENTERLINE_STATUS_ID);
    expect(block).toHaveAttribute('tabindex', '-1');
    unmount();

    renderPanel({ centerlinesVisible: false });
    expect(document.getElementById(CENTERLINE_STATUS_ID)).toBeNull();
  });
});

describe('the photo overlay in the layer panel', () => {
  it('says not every photo is shown while the overlay is on and its answer was cut short', async () => {
    const map = {
      getSize: () => [800, 600],
      getView: () => ({ calculateExtent: () => [...fromLonLat([25, 45]), ...fromLonLat([26, 46])] }),
      on: () => undefined,
      un: () => undefined,
    } as unknown as Map;
    const detach = attachPhotoLoader(
      map,
      vi.fn(async () => ({ type: 'FeatureCollection', features: [], cutShort: true }) as PhotoFeatureCollection),
    );
    renderPanel();
    expect(screen.queryByTestId('map-photos-cut-short')).not.toBeInTheDocument();

    await act(async () => {
      setPhotosEnabled(true);
      await Promise.resolve();
    });
    expect(await screen.findByTestId('map-photos-cut-short')).toHaveTextContent('Not every photo is shown here');

    act(() => setPhotosEnabled(false));
    expect(screen.queryByTestId('map-photos-cut-short')).not.toBeInTheDocument();
    detach();
  });
});

describe('adding a layer from a saved filter', () => {
  const filterId = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;

  it('offers the saved filters by name and adds the one picked', () => {
    const onAdd = vi.fn();
    renderPanel({
      filterLayerCandidates: [{ id: filterId(1), name: 'Bihor over 300 m' }],
      onAddFilterLayer: onAdd,
    });
    const chooser = screen.getByRole('combobox', { name: /add a layer from a saved filter/i });
    fireEvent.mouseDown(chooser);
    fireEvent.click(screen.getByTitle('Bihor over 300 m'));
    expect(onAdd).toHaveBeenCalledWith(filterId(1));
  });

  it('is shut, and says why, once the map holds as many filter layers as it can', () => {
    let list = addFilterLayer([], filterId(1));
    for (let n = 2; n <= MAX_FILTER_LAYERS; n++) {
      list = addFilterLayer(list, filterId(n));
    }
    renderPanel({
      filterLayers: list.map((spec) => ({ spec, name: 'A filter', status: 'ready' })),
      filterLayerCandidates: [{ id: filterId(99), name: 'One more' }],
      onAddFilterLayer: vi.fn(),
    });
    expect(screen.getByRole('combobox', { name: /add a layer from a saved filter/i })).toBeDisabled();
    expect(screen.getByText(new RegExp(`${MAX_FILTER_LAYERS} filter layers at most`, 'i'))).toBeInTheDocument();
  });

  it('is not shown where the page offers no filter layers', () => {
    renderPanel();
    expect(screen.queryByRole('combobox', { name: /add a layer from a saved filter/i })).toBeNull();
  });
});
