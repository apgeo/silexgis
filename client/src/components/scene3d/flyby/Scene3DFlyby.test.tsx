// SPDX-License-Identifier: AGPL-3.0-or-later
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import i18n from '../../../i18n';
import en from '../../../i18n/locales/en.json';
import ro from '../../../i18n/locales/ro.json';
import { FLYBY_PRESETS, FLYBY_SECTION_KINDS, FLYBY_VERSION, type FlybyDocument } from '../../../flyby/flybyDocument.ts';
import { flybyFileText } from '../../../flyby/flybyLibrary.ts';
import { eyeLookingAt } from '../../../scene3d/camera3d.ts';
import type { CaveInView3D } from '../../../scene3d/centerlines3d.ts';
import type { Scene3DCameraState, Scene3DCore } from '../../../scene3d/scene3dEngine.ts';
import { useFlybyStore } from '../../../stores/flybyStore.ts';
import type { WorkspaceSelection } from '../../../stores/workspaceStore.ts';
import FlybySections from './FlybySections.tsx';
import Scene3DFlyby from './Scene3DFlyby.tsx';
import { flybySayWords, type FlybyScene } from './useFlyby.ts';
import type { FlybyLookTarget } from '../../../flyby/flybyLookApply.ts';

// The two reads the tool makes of the server: a cave's name for the panel, and where a cave is
// when nothing of it is drawn. Both answered from here, so no request leaves the test.
const { SAVED_FILTERS, asked, filterAnswer } = vi.hoisted(() => ({
  SAVED_FILTERS: [
    {
      id: 'f0000000-0000-4000-8000-000000000001',
      name: 'Deep caves',
      state: 'ready',
      document: { version: 1, scope: [{ world: 'feature', where: null }], sort: 'title', descending: false },
    },
    { id: 'f0000000-0000-4000-8000-000000000002', name: 'Broken', state: 'unreadable', document: null },
  ],
  /** What the tool asked the filter route. */
  asked: [] as unknown[],
  /** Whether the filter route answers with nothing, as for a reader who may see none of the caves. */
  filterAnswer: { empty: false },
}));
vi.mock('../../../api/hooks.ts', () => ({
  useCaveNames: (ids: readonly string[]) =>
    new Map(ids.filter((id) => id !== 'unnamed').map((id) => [id, `Cave ${id.toUpperCase()}`])),
  caveQuery: (id: string) => ({
    queryKey: ['cave', id],
    queryFn: async () => {
      if (id === 'forbidden') {
        throw new Error('403');
      }
      return { name: `Cave ${id.toUpperCase()}`, geom: { type: 'Point', coordinates: [25, 45.5] }, depth: 120, surveyedLength: 800 };
    },
  }),
  // The lists the look panel names things from: two backgrounds, nothing else.
  useMapLayers: () => ({
    data: [
      { id: 1, name: 'Street map', isBase: true, layerKind: 'xyz', urlTemplate: 'https://tiles.example/{z}/{x}/{y}.png' },
      { id: 2, name: 'Aerial', isBase: true, layerKind: 'xyz', urlTemplate: 'https://aerial.example/{z}/{x}/{y}.png' },
    ],
  }),
  useRasterMaps: () => ({ data: { items: [] } }),
  // Two kinds of feature from the catalogue, which a flyby's own layer can show.
  useFeatureTypes: () => ({ data: [{ id: 3, name: 'Spring' }, { id: 9, name: 'Sinkhole' }] }),
  // One imported file, which a flyby along a line can follow.
  useGeofiles: () => ({ data: { items: [{ id: 'walk', name: 'Ridge walk', importStatus: 'imported' }] } }),
  // One saved filter that can be run and one that no longer can; the first keeps caves b and a.
  useSavedFilters: () => ({ data: SAVED_FILTERS }),
  savedFiltersQuery: () => ({ queryKey: ['saved-filters'], queryFn: async () => SAVED_FILTERS }),
  filterQueryOptions: (body: { document: unknown; pageSize: number }) => ({
    queryKey: ['filter-query', body],
    queryFn: async () => {
      asked.push(body);
      const hits = filterAnswer.empty ? [] : ['b', 'a'].slice(0, body.pageSize);
      return { worlds: [{ world: 'feature', hits: hits.map((id) => ({ world: 'feature', id })) }] };
    },
  }),
  // A search that finds one cave by any name, and an entrance beside it that is not one.
  useSearch: (q: string) => ({
    data:
      q.trim().length >= 2
        ? { features: [{ id: 'far', kind: 'cave', name: 'Far cave', subtitle: null }, { id: 'e1', kind: 'caveEntrance', name: 'An entrance', subtitle: null }] }
        : undefined,
  }),
  geofileQuery: (id: string) => ({
    queryKey: ['geofile', id],
    queryFn: async () => ({ id, bbox: { type: 'Polygon', coordinates: [[[25, 45.5], [25.02, 45.5], [25.02, 45.51], [25, 45.51], [25, 45.5]]] } }),
  }),
  fetchGeofileFeatureCollection: async () => ({
    type: 'FeatureCollection',
    features: [
      {
        type: 'Feature',
        properties: {},
        geometry: { type: 'LineString', coordinates: [[25, 45.5], [25.01, 45.505], [25.02, 45.51]] },
      },
    ],
  }),
}));
vi.mock('../../../viewlinks/geoTargets.ts', () => ({
  APPROXIMATE_SPAN_DEGREES: 0.05,
  geometryFor: async () => null,
}));
const saveBlob = vi.fn();
vi.mock('../../../api/download.ts', () => ({ saveBlob: (...args: unknown[]) => saveBlob(...args) }));

const HOME: Scene3DCameraState = { longitude: 24.9, latitude: 45.4, height: 3000, heading: 0, pitch: -40, roll: 0 };

const drawnCave: CaveInView3D = { caveId: 'a', label: 'Line name', bounds: [25, 45.5, 25.004, 45.504], top: 990, bottom: 800 };

/** The view the tool is mounted in, reduced to what it asks of one. */
function fakeScene(caves: CaveInView3D[] = [drawnCave]) {
  let camera = HOME;
  const wrap = document.createElement('div');
  const surface = document.createElement('div');
  wrap.appendChild(surface);
  document.body.appendChild(wrap);
  const engine = {
    getCamera: () => camera,
    setCamera: (next: Scene3DCameraState) => {
      camera = next;
    },
    getCameraTarget: () => ({ longitude: 25, latitude: 45.5, height: 900 }),
    getProjection: () => 'perspective',
    getOrthoHalfWidth: () => undefined,
    groundHeight: () => 1000,
    requestRender: vi.fn(),
    isSettled: () => true,
    isDestroyed: () => false,
  } as unknown as Scene3DCore;
  const scene: FlybyScene & { camera(): Scene3DCameraState; refreshes: number; element: HTMLElement; surfaceElement: HTMLElement } = {
    engine: () => engine,
    cavesInView: () => caves,
    refreshData: () => {
      scene.refreshes += 1;
    },
    dataLoading: () => false,
    wrap: () => wrap,
    surface: () => surface,
    camera: () => camera,
    refreshes: 0,
    element: wrap,
    surfaceElement: surface,
  };
  return scene;
}

const caveSelected: WorkspaceSelection = { kind: 'cave', caveId: 'a' };

function renderFlyby(scene = fakeScene(), selection: WorkspaceSelection | null = caveSelected) {
  const onActiveChange = vi.fn();
  const onClose = vi.fn();
  const { unmount } = render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <Scene3DFlyby scene={scene} selection={selection} onActiveChange={onActiveChange} onClose={onClose} />
    </QueryClientProvider>,
  );
  return { scene, onActiveChange, onClose, unmount };
}

/** Lets time pass for the tool: its waits for the scene, its frames and its clock all move together. */
async function pass(milliseconds: number) {
  await act(async () => {
    await vi.advanceTimersByTimeAsync(milliseconds);
  });
}

const status = () => screen.getByTestId('flyby-time').dataset.status;

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'requestAnimationFrame', 'cancelAnimationFrame', 'performance'] });
  useFlybyStore.setState({ library: [], loadRoute: false });
  saveBlob.mockClear();
});

afterEach(() => {
  vi.useRealTimers();
  document.body.replaceChildren();
});

describe('the flyby tool over the 3D scene', () => {
  it('opens on what is selected, with the flyby that suits it', () => {
    renderFlyby();
    expect(screen.getByRole('radio', { name: 'What is selected' })).toBeChecked();
    expect(screen.getByTestId('flyby-preset')).toHaveTextContent('Around a cave');
    // Approach and circle, named for the cave by the name the server gives it.
    fireEvent.click(screen.getByText('2 sections'));
    expect(screen.getByTestId('flyby-sections')).toHaveTextContent('Approach · Cave A');
    expect(screen.getByTestId('flyby-sections')).toHaveTextContent('Circle · Cave A');
  });

  it('falls back to what the view shows when nothing is selected', () => {
    renderFlyby(fakeScene(), null);
    expect(screen.getByRole('radio', { name: 'What the view shows' })).toBeChecked();
    expect(screen.getByRole('radio', { name: 'What is selected' })).toBeDisabled();
    expect(screen.getByTestId('flyby-preset')).toHaveTextContent('Around a mountain');
  });

  it('plays: takes the camera, flies it, and gives it back when stopped', async () => {
    const { scene, onActiveChange } = renderFlyby();
    expect(screen.getByTestId('flyby-stop')).toBeDisabled();

    fireEvent.click(screen.getByTestId('flyby-play'));
    await pass(50);
    expect(status()).toBe('playing');
    expect(onActiveChange).toHaveBeenLastCalledWith(true);
    const first = scene.camera();
    expect(first).not.toEqual(HOME);
    await pass(2000);
    expect(scene.camera()).not.toEqual(first);
    expect(screen.getByTestId('flyby-time')).toHaveTextContent('0:02 / 0:30');

    fireEvent.click(screen.getByTestId('flyby-pause'));
    const held = scene.camera();
    await pass(1000);
    expect(status()).toBe('paused');
    expect(scene.camera()).toEqual(held);

    fireEvent.click(screen.getByTestId('flyby-stop'));
    expect(scene.camera()).toEqual(HOME);
    expect(status()).toBe('idle');
    expect(onActiveChange).toHaveBeenLastCalledWith(false);
  });

  it('moves to any moment from the position slider, and names both sliders for a reader', async () => {
    const { scene } = renderFlyby();
    // Named on their handles, which is where a screen reader and a keyboard meet a slider.
    expect(screen.getByRole('slider', { name: 'Speed of the whole flyby' })).toBeInTheDocument();
    const position = screen.getByRole('slider', { name: 'Position in the flyby' });
    // Nowhere to be until there is a path.
    expect(position).toHaveAttribute('aria-disabled', 'true');

    fireEvent.click(screen.getByTestId('flyby-play'));
    await pass(3000);
    fireEvent.click(screen.getByTestId('flyby-pause'));
    const paused = scene.camera();
    const handle = screen.getByRole('slider', { name: 'Position in the flyby' });
    expect(handle).toHaveAttribute('aria-disabled', 'false');
    // Back to the start, which a flyby that had clicked its way here could not do.
    // The slider reads the key by its number, which a bare key name does not carry here.
    fireEvent.keyDown(handle, { key: 'Home', keyCode: 36, which: 36 });
    expect(scene.camera()).not.toEqual(paused);
    expect(screen.getByTestId('flyby-time')).toHaveTextContent('0:00 / 0:30');
    expect(status()).toBe('paused');
  });

  it('holds what the flyby is built from while it has the camera', async () => {
    renderFlyby();
    fireEvent.click(screen.getByTestId('flyby-play'));
    await pass(50);
    expect(screen.getByTestId('flyby-turns')).toBeDisabled();
    expect(screen.getByRole('radio', { name: 'Caves in view' })).toBeDisabled();
    fireEvent.click(screen.getByTestId('flyby-stop'));
    expect(screen.getByTestId('flyby-turns')).not.toBeDisabled();
  });

  it('lets a press on the scene take the camera back', async () => {
    const { scene } = renderFlyby();
    fireEvent.click(screen.getByTestId('flyby-play'));
    await pass(500);
    fireEvent.pointerDown(scene.surfaceElement);
    expect(status()).toBe('paused');
    // And goes on from there when asked.
    fireEvent.click(screen.getByTestId('flyby-play'));
    await pass(100);
    expect(status()).toBe('playing');
  });

  it('goes to look at a cave nothing is drawn of, and loads the route when asked to', async () => {
    useFlybyStore.setState({ loadRoute: true });
    const { scene } = renderFlyby(fakeScene([]), { kind: 'cave', caveId: 'far' });
    fireEvent.click(screen.getByTestId('flyby-play'));
    await pass(100);
    // Still getting ready: the scene is being waited on, and the position cannot be moved yet.
    expect(screen.getByTestId('flyby-status')).toBeInTheDocument();
    expect(screen.queryByTestId('flyby-position')).not.toBeInTheDocument();
    await pass(20_000);
    expect(status()).toBe('playing');
    // One visit for the cave and one for each stop along the route, each asking the scene to load.
    expect(scene.refreshes).toBeGreaterThan(8);
  });

  it('says so when what it flies around cannot be read, and gives the camera back', async () => {
    const { scene, onActiveChange } = renderFlyby(fakeScene([]), { kind: 'cave', caveId: 'forbidden' });
    fireEvent.click(screen.getByTestId('flyby-play'));
    await pass(200);
    expect(screen.getByTestId('flyby-problem')).toHaveTextContent('could not be found');
    expect(scene.camera()).toEqual(HOME);
    expect(onActiveChange).toHaveBeenLastCalledWith(false);
  });

  it('builds a tour of the caves ticked, in the order they were ticked', () => {
    const caves: CaveInView3D[] = [
      drawnCave,
      { caveId: 'b', bounds: [25.1, 45.5, 25.104, 45.504], top: 900, bottom: 850 },
      { caveId: 'unnamed', bounds: [25.2, 45.5, 25.2, 45.5] },
    ];
    renderFlyby(fakeScene(caves), null);
    fireEvent.click(screen.getByRole('radio', { name: 'Caves in view' }));
    // All three start ticked; the one the server names no better keeps a plain word.
    expect(screen.getByRole('checkbox', { name: 'Cave A' })).toBeChecked();
    expect(screen.getByRole('checkbox', { name: 'Unnamed cave' })).toBeChecked();
    expect(screen.getByTestId('flyby-preset')).toHaveTextContent('Cave tour');
    fireEvent.click(screen.getByRole('checkbox', { name: 'Cave A' }));
    fireEvent.click(screen.getByRole('checkbox', { name: 'Cave A' }));
    fireEvent.click(screen.getByText('7 sections'));
    const names = Array.from(
      screen.getByTestId('flyby-sections').querySelectorAll('.scene3d-flyby-section-name'),
      (node) => node.textContent,
    );
    expect(names).toEqual([
      'Approach · Cave B',
      'Circle · Cave B',
      'Over to · Unnamed cave',
      'Circle · Unnamed cave',
      'Over to · Cave A',
      'Circle · Cave A',
      'Back up · all together',
    ]);
  });

  it('changes how long a section lasts, and says the sections are now its own', () => {
    renderFlyby();
    fireEvent.click(screen.getByText('2 sections'));
    fireEvent.change(screen.getByTestId('flyby-section-orbit-2-seconds'), { target: { value: '40' } });
    fireEvent.blur(screen.getByTestId('flyby-section-orbit-2-seconds'));
    expect(screen.getByTestId('flyby-edited')).toBeInTheDocument();
    // Asking for the underground visit builds the preset again, which puts the changed length back.
    fireEvent.click(screen.getByTestId('flyby-underground'));
    expect(screen.queryByTestId('flyby-edited')).not.toBeInTheDocument();
    expect(screen.getByText('5 sections')).toBeInTheDocument();
  });

  it('keeps a flyby under its name and opens it again', () => {
    renderFlyby();
    expect(screen.getByTestId('flyby-save')).toBeDisabled();
    fireEvent.change(screen.getByTestId('flyby-name'), { target: { value: 'Evening tour' } });
    fireEvent.click(screen.getByTestId('flyby-save'));
    const kept = useFlybyStore.getState().library;
    expect(kept.map((entry) => entry.name)).toEqual(['Evening tour']);
    expect(kept[0].subjects).toEqual([{ kind: 'cave', caveId: 'a' }]);
    // Its file is the same document, and names the cave by id alone.
    fireEvent.click(screen.getByTestId('flyby-file-save'));
    expect(saveBlob).toHaveBeenCalledWith(expect.any(Blob), 'evening-tour.flyby.json');
    fireEvent.click(screen.getByTestId('flyby-delete'));
    expect(useFlybyStore.getState().library).toEqual([]);
  });

  it('opens a flyby from a file, and refuses a file that is not one', async () => {
    vi.useRealTimers();
    renderFlyby(fakeScene(), null);
    const opened: FlybyDocument = {
      version: FLYBY_VERSION,
      name: 'From a file',
      speed: 2,
      loop: true,
      subjects: [{ kind: 'cave', caveId: 'b' }],
      start: { subject: 0, pose: { heading: 0, pitch: -30, range: 4, lift: 0, clearance: 60 } },
      sections: [{ id: 'orbit-1', kind: 'orbit', subject: 0, seconds: 12, to: { headingBy: 360 } }],
    };
    const input = screen.getByTestId('flyby-file-input');
    fireEvent.change(input, { target: { files: [new File([flybyFileText(opened)], 'tour.flyby.json')] } });
    expect(await screen.findByDisplayValue('From a file')).toBeInTheDocument();
    expect(screen.getByText('1 section')).toBeInTheDocument();
    expect(screen.getByTestId('flyby-loop')).toHaveAttribute('aria-pressed', 'true');

    fireEvent.change(input, { target: { files: [new File(['{"version": 9}'], 'other.json')] } });
    expect(await screen.findByTestId('flyby-problem')).toHaveTextContent('not a flyby');
  });

  it('plays on a clean screen after a countdown, and Escape brings the view back', async () => {
    const { scene } = renderFlyby();
    fireEvent.click(screen.getByTestId('flyby-clean'));
    await pass(50);
    expect(scene.element).toHaveClass('scene3d-wrap-clean');
    expect(screen.queryByTestId('flyby-panel')).not.toBeInTheDocument();
    expect(screen.getByTestId('flyby-countdown')).toHaveTextContent('3');
    const waiting = scene.camera();
    await pass(2000);
    expect(screen.getByTestId('flyby-countdown')).toHaveTextContent('1');
    expect(scene.camera()).toEqual(waiting);
    await pass(2000);
    expect(screen.queryByTestId('flyby-countdown')).not.toBeInTheDocument();
    expect(scene.camera()).not.toEqual(waiting);
    // A press on the scene does not end a flyby that is being recorded.
    fireEvent.pointerDown(scene.surfaceElement);
    const before = scene.camera();
    await pass(500);
    expect(scene.camera()).not.toEqual(before);

    fireEvent.keyDown(window, { key: 'Escape' });
    expect(scene.element).not.toHaveClass('scene3d-wrap-clean');
    expect(screen.getByTestId('flyby-panel')).toBeInTheDocument();
    expect(status()).toBe('paused');
  });

  it('gives the camera back when the tool is closed with a flyby in flight', async () => {
    const { scene, onClose, onActiveChange, unmount } = renderFlyby();
    fireEvent.click(screen.getByTestId('flyby-play'));
    await pass(1000);
    expect(scene.camera()).not.toEqual(HOME);
    fireEvent.click(screen.getByTestId('flyby-close'));
    expect(onClose).toHaveBeenCalled();
    // The view takes the tool away in answer.
    unmount();
    expect(scene.camera()).toEqual(HOME);
    expect(onActiveChange).toHaveBeenLastCalledWith(false);
  });
});

/** The fake view with a look of its own: what the flyby last put on it, member by member. */
function lookingScene() {
  const scene = fakeScene();
  const shown: Record<string, unknown> = {};
  const record = (member: string) => (value: unknown) => {
    if (value === undefined) {
      delete shown[member];
    } else {
      shown[member] = value;
    }
  };
  const look: FlybyLookTarget = {
    viewerLook: (layers) => ({
      background: 1,
      backgroundOpacity: 1,
      surface: 'overlay',
      layers: Object.fromEntries(layers.map((key) => [key, { visible: true, opacity: 1 }])),
    }),
    showBackground: record('background'),
    showLayer: (key, state) => record(`layer:${key}`)(state),
    showFeatureLayers: record('features'),
    showCave: (caveId, tint) => record(`cave:${caveId}`)(tint),
    showWalls: record('walls'),
    showLines: record('lines'),
    showSurface: record('surface'),
    showSky: record('sky'),
    requestRender: () => {},
  };
  return Object.assign(scene, { look, shown });
}

describe('the look of a flyby and the texts over it', () => {
  it('opens with a soft sky, puts the look on the scene while it plays, and gives it back when stopped', async () => {
    const scene = lookingScene();
    renderFlyby(scene);
    fireEvent.click(screen.getByText('Look at the start'));
    expect(screen.getByTestId('flyby-look-sky-on')).toBeChecked();
    fireEvent.click(screen.getByTestId('flyby-look-lines-on'));
    fireEvent.change(screen.getByLabelText('Colour of the survey lines'), { target: { value: '#00FF00' } });

    fireEvent.click(screen.getByTestId('flyby-play'));
    await pass(100);
    expect(scene.shown).toEqual({ sky: '#b4c8dc', lines: '#00ff00' });
    fireEvent.click(screen.getByTestId('flyby-stop'));
    expect(scene.shown).toEqual({});
  });

  it('writes ready-made changes, lists them in words, and lets each be corrected or removed', async () => {
    const scene = lookingScene();
    renderFlyby(scene);
    fireEvent.click(screen.getByText('Ready-made changes'));
    fireEvent.change(screen.getByLabelText('Colour of the caves'), { target: { value: '#ff0000' } });
    fireEvent.click(screen.getByTestId('flyby-recipe-colour-add'));
    fireEvent.click(screen.getByTestId('flyby-recipe-see-through-add'));
    fireEvent.click(screen.getByText('4 changes'));
    const list = screen.getByTestId('flyby-changes');
    expect(list).toHaveTextContent('Walls in #ff0000, 70%');
    expect(list).toHaveTextContent('Survey lines in #ff0000');
    expect(list).toHaveTextContent('Background at 40%');
    expect(list).toHaveTextContent('2. Circle');

    // Moved onto the moment of another change of the same thing: said beside it, and refused on Play.
    fireEvent.change(screen.getByTestId('flyby-change-backgroundOpacity-2-at'), { target: { value: '0' } });
    fireEvent.blur(screen.getByTestId('flyby-change-backgroundOpacity-2-at'));
    expect(screen.getByTestId('flyby-change-backgroundOpacity-2-trouble')).toHaveTextContent('same thing');
    fireEvent.click(screen.getByTestId('flyby-play'));
    await pass(50);
    expect(screen.getByTestId('flyby-problem')).toHaveTextContent('a change or a caption');

    fireEvent.click(screen.getByRole('button', { name: /Remove the change — Background at 100%/ }));
    expect(screen.getByText('3 changes')).toBeInTheDocument();
    expect(screen.queryByTestId('flyby-change-backgroundOpacity-2-trouble')).not.toBeInTheDocument();
    // A cut has no length to set.
    expect(screen.getByTestId('flyby-change-lines-2-seconds')).toBeDisabled();
  });

  it('shows the title over the scene while it is on, and nothing once the flyby stops', async () => {
    renderFlyby(lookingScene());
    fireEvent.click(screen.getByText('Texts over the picture'));
    fireEvent.change(screen.getByTestId('flyby-text-title'), { target: { value: 'Peștera A' } });
    fireEvent.change(screen.getByTestId('flyby-text-caption-orbit-2'), { target: { value: 'Around it' } });
    fireEvent.click(screen.getByTestId('flyby-text-fade-out'));
    const overlay = screen.getByTestId('flyby-texts');
    expect(overlay.dataset.shown).toBe('');

    fireEvent.click(screen.getByTestId('flyby-play'));
    await pass(100);
    expect(overlay.dataset.shown).toBe('title');
    // The circle starts at 6 s; its caption is on through it.
    await pass(7000);
    expect(overlay.dataset.shown).toBe('caption');
    fireEvent.click(screen.getByTestId('flyby-stop'));
    expect(overlay.dataset.shown).toBe('');
  });

  it('keeps the look, the changes and the texts with the flyby, and opens them again', () => {
    renderFlyby(lookingScene());
    fireEvent.click(screen.getByText('Texts over the picture'));
    fireEvent.change(screen.getByTestId('flyby-text-end'), { target: { value: 'Club' } });
    fireEvent.change(screen.getByTestId('flyby-name'), { target: { value: 'With a look' } });
    fireEvent.click(screen.getByTestId('flyby-save'));
    const [kept] = useFlybyStore.getState().library;
    expect(kept.look).toEqual({ sky: '#b4c8dc' });
    expect(kept.texts).toEqual({ end: { text: 'Club', seconds: 4 } });
  });
});

describe('editing the sections by hand', () => {
  const sectionNames = () =>
    Array.from(screen.getByTestId('flyby-sections').querySelectorAll('.scene3d-flyby-section-name'), (node) => node.textContent);

  it('adds, duplicates, moves and removes sections, each button named for the section', () => {
    renderFlyby();
    fireEvent.click(screen.getByText('2 sections'));
    fireEvent.click(screen.getByRole('button', { name: 'Duplicate 2. Circle' }));
    expect(sectionNames()).toEqual(['Approach · Cave A', 'Circle · Cave A', 'Circle · Cave A']);
    expect(screen.getByTestId('flyby-edited')).toBeInTheDocument();
    // The last can only go up; the first only down.
    expect(screen.getByRole('button', { name: 'Move 3. Circle down' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Move 1. Approach up' })).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: 'Move 1. Approach down' }));
    expect(sectionNames()[0]).toBe('Circle · Cave A');

    fireEvent.mouseDown(screen.getByRole('combobox', { name: 'Kind of section to add' }));
    fireEvent.click(screen.getByTitle('Tilt'));
    fireEvent.click(screen.getByTestId('flyby-section-add'));
    expect(screen.getByText('4 sections')).toBeInTheDocument();
    expect(sectionNames()[3]).toBe('Tilt · Cave A');

    fireEvent.click(screen.getByRole('button', { name: 'Remove 4. Tilt' }));
    fireEvent.click(screen.getByRole('button', { name: 'Remove 3. Circle' }));
    fireEvent.click(screen.getByRole('button', { name: 'Remove 2. Approach' }));
    // One section is a flyby; none is not, so the last cannot be taken out.
    expect(screen.getByRole('button', { name: 'Remove 1. Circle' })).toBeDisabled();
  });

  it('edits what a section does, every field named, and says inline what is wrong', async () => {
    const { scene } = renderFlyby();
    fireEvent.click(screen.getByText('2 sections'));
    fireEvent.click(screen.getByRole('button', { name: 'Edit what 2. Circle does' }));
    const editor = screen.getByTestId('flyby-section-orbit-2-editor');
    expect(editor).toHaveTextContent('What 2. Circle does');
    // A circle turns by a whole turn, shown in turns.
    expect(screen.getByRole('spinbutton', { name: 'Turn by, in turns — 2. Circle' })).toHaveValue('1.00');
    expect(screen.getByRole('spinbutton', { name: 'Angle below the horizon — 2. Circle' })).toHaveValue('');
    for (const name of [
      'Kind — 2. Circle',
      'About — 2. Circle',
      'Distance, in sizes of what it is about — 2. Circle',
      'Looks at — 2. Circle',
      'Raised by, in half-heights — 2. Circle',
      'Above the ground — 2. Circle',
    ]) {
      expect(screen.getByLabelText(name)).toBeInTheDocument();
    }

    const range = screen.getByRole('spinbutton', { name: 'Distance, in sizes of what it is about — 2. Circle' });
    fireEvent.change(range, { target: { value: '6' } });
    fireEvent.blur(range);
    fireEvent.click(screen.getByTestId('flyby-section-orbit-2-rest'));
    fireEvent.change(screen.getByTestId('flyby-name'), { target: { value: 'Edited' } });
    fireEvent.click(screen.getByTestId('flyby-save'));
    const [kept] = useFlybyStore.getState().library;
    expect(kept.sections[1]).toMatchObject({ id: 'orbit-2', rest: true, to: { headingBy: 360, range: 6 } });

    // The camera goes where the edit says: further out than the preset's circle.
    fireEvent.click(screen.getByTestId('flyby-play'));
    await pass(31_000);
    expect(status()).toBe('ended');
    const end = scene.camera();
    const distance = Math.hypot((end.longitude - 25.002) * 78_000, (end.latitude - 45.502) * 111_000);
    expect(distance).toBeGreaterThan(800);
    fireEvent.click(screen.getByTestId('flyby-stop'));

  });

  it('says beside a section what is wrong with it', () => {
    const document: FlybyDocument = {
      version: FLYBY_VERSION,
      name: '',
      speed: 1,
      loop: false,
      subjects: [{ kind: 'cave', caveId: 'a' }],
      start: { subject: 0, pose: { heading: 0, pitch: -30, range: 4, lift: 0, clearance: 60 } },
      sections: [
        { id: 'o', kind: 'orbit', subject: 3, seconds: 12, to: { headingBy: 360, lift: 40 } },
        { id: 'h', kind: 'hold', subject: 0, seconds: 2, to: {} },
      ],
    };
    render(<FlybySections document={document} busy={false} subjectLabel={() => 'Cave A'} onEdit={() => {}} />);
    expect(screen.getByTestId('flyby-section-o-trouble')).toHaveTextContent('What it is about is not in this flyby.');
    expect(screen.getByTestId('flyby-section-o-trouble')).toHaveTextContent('A turn, a lift or a height above the ground');
    expect(screen.queryByTestId('flyby-section-h-trouble')).not.toBeInTheDocument();
  });
});

describe('changes of the look by hand', () => {
  it('adds a change of any kind, and the flyby puts it on the scene at its moment', async () => {
    const scene = lookingScene();
    renderFlyby(scene);
    fireEvent.click(screen.getByText('0 changes'));
    fireEvent.mouseDown(screen.getByRole('combobox', { name: 'What changes' }));
    fireEvent.click(screen.getByTitle('A layer shown or hidden'));
    // The scene's own layers are offered by name.
    fireEvent.mouseDown(screen.getByRole('combobox', { name: 'Layer' }));
    fireEvent.click(screen.getByTitle('Cave walls'));
    fireEvent.mouseDown(screen.getByRole('combobox', { name: 'During the section' }));
    fireEvent.click(screen.getByTitle('2. Circle'));
    fireEvent.change(screen.getByRole('spinbutton', { name: 'Seconds into the section it starts' }), { target: { value: '1' } });
    fireEvent.blur(screen.getByRole('spinbutton', { name: 'Seconds into the section it starts' }));
    fireEvent.change(screen.getByRole('spinbutton', { name: 'Seconds it takes' }), { target: { value: '0' } });
    fireEvent.blur(screen.getByRole('spinbutton', { name: 'Seconds it takes' }));
    fireEvent.click(screen.getByTestId('flyby-add-change-add'));
    expect(screen.getByText('1 change')).toBeInTheDocument();
    expect(screen.getByTestId('flyby-changes')).toHaveTextContent('Hide the walls · 2. Circle');

    // A cut has no length, whatever was typed.
    fireEvent.mouseDown(screen.getByRole('combobox', { name: 'What changes' }));
    fireEvent.click(screen.getByTitle('Ground over the caves'));
    expect(screen.getByRole('spinbutton', { name: 'Seconds it takes' })).toBeDisabled();

    fireEvent.click(screen.getByTestId('flyby-play'));
    await pass(100);
    expect(scene.shown['layer:walls']).toEqual({ visible: true, opacity: 1 });
    // The circle starts at 6 s; one second into it the walls are put off.
    await pass(7500);
    expect(scene.shown['layer:walls']).toEqual({ visible: false, opacity: 0 });
    fireEvent.click(screen.getByTestId('flyby-stop'));
  });

  it('opens with each layer shown, hidden or as the viewer has it, at a strength of its own', () => {
    const scene = lookingScene();
    renderFlyby(scene);
    fireEvent.click(screen.getByText('Look at the start'));
    fireEvent.mouseDown(screen.getByRole('combobox', { name: 'Entrances: shown or hidden' }));
    fireEvent.click(screen.getAllByTitle('Hidden').at(-1)!);
    const strength = screen.getByRole('spinbutton', { name: 'Survey lines: strength' });
    fireEvent.change(strength, { target: { value: '40' } });
    fireEvent.blur(strength);
    fireEvent.change(screen.getByTestId('flyby-name'), { target: { value: 'Layers' } });
    fireEvent.click(screen.getByTestId('flyby-save'));
    const [kept] = useFlybyStore.getState().library;
    expect(kept.look?.layers).toEqual({ entrances: { visible: false }, centerlines: { opacity: 0.4 } });
  });
});

describe('a flyby through views captured from the scene', () => {
  it('captures views, edits their list, and flies through them in order', async () => {
    const scene = fakeScene();
    const engine = scene.engine() as unknown as {
      setCamera(camera: Scene3DCameraState): void;
      getCameraTarget(): { longitude: number; latitude: number; height: number } | undefined;
    };
    renderFlyby(scene);
    fireEvent.mouseDown(screen.getByRole('combobox', { name: 'Preset' }));
    fireEvent.click(screen.getByTitle('My own views'));
    expect(screen.getByTestId('flyby-views')).toHaveTextContent('Add this view');

    // Pointed at the sky, there is nothing to fly to.
    engine.getCameraTarget = () => undefined;
    fireEvent.click(screen.getByTestId('flyby-view-add'));
    expect(screen.getByTestId('flyby-view-no-ground')).toBeInTheDocument();

    engine.getCameraTarget = () => ({ longitude: 25, latitude: 45.5, height: 900 });
    fireEvent.click(screen.getByTestId('flyby-view-add'));
    expect(screen.queryByTestId('flyby-view-no-ground')).not.toBeInTheDocument();
    engine.setCamera({ longitude: 25.01, latitude: 45.49, height: 2500, heading: 30, pitch: -30, roll: 0 });
    engine.getCameraTarget = () => ({ longitude: 25.02, latitude: 45.5, height: 950 });
    fireEvent.click(screen.getByTestId('flyby-view-add'));
    // A camera that really looks at its target: two kilometres west of it and up.
    const third = eyeLookingAt({ lon: 25.05, lat: 45.49, height: 1000 }, 90, -20, 2000);
    engine.setCamera({ longitude: third.lon, latitude: third.lat, height: third.height, heading: 90, pitch: -20, roll: 0 });
    engine.getCameraTarget = () => ({ longitude: 25.05, latitude: 45.49, height: 1000 });
    fireEvent.click(screen.getByTestId('flyby-view-add'));
    expect(screen.getByTestId('flyby-view-list').querySelectorAll('li')).toHaveLength(3);
    expect(screen.getByText('2 sections')).toBeInTheDocument();
    expect(screen.getByTestId('flyby-holds-place')).toBeInTheDocument();

    // The third becomes the second, the leg into it shorter; the old second is taken out.
    fireEvent.click(screen.getByRole('button', { name: 'Move View 3 up' }));
    const seconds = screen.getByRole('spinbutton', { name: 'Seconds to fly to View 2' });
    fireEvent.change(seconds, { target: { value: '4' } });
    fireEvent.blur(seconds);
    fireEvent.click(screen.getByRole('button', { name: 'Remove View 3' }));
    expect(screen.getByText('1 section')).toBeInTheDocument();
    // Taken again from where the camera is now: the first view becomes this one.
    fireEvent.click(screen.getByRole('button', { name: 'Take View 1 again from the camera as it is now' }));

    fireEvent.click(screen.getByTestId('flyby-play'));
    await pass(5000);
    expect(status()).toBe('ended');
    // Where the third camera stood, which is now the second view.
    const end = scene.camera();
    expect(end.longitude).toBeCloseTo(third.lon, 6);
    expect(end.latitude).toBeCloseTo(third.lat, 6);
    expect(end.height).toBeCloseTo(third.height, 1);
    expect(end.heading).toBeCloseTo(90, 3);
  });
});

describe('a descent down a cave\'s survey', () => {
  const chooseDescent = () => {
    fireEvent.mouseDown(screen.getByRole('combobox', { name: 'Preset' }));
    fireEvent.click(screen.getByTitle('Descent'));
  };

  it('follows the survey the scene draws, found when the flyby is prepared', async () => {
    const scene = Object.assign(fakeScene(), {
      caveLines: () => [
        [
          { longitude: 25, latitude: 45.5, height: 990 },
          { longitude: 25.002, latitude: 45.502, height: 900 },
          { longitude: 25.004, latitude: 45.504, height: 800 },
        ],
      ],
    });
    renderFlyby(scene);
    chooseDescent();
    fireEvent.click(screen.getByText('4 sections'));
    expect(screen.getByTestId('flyby-sections')).toHaveTextContent('Down the survey · Cave A');
    fireEvent.click(screen.getByTestId('flyby-play'));
    await pass(24_000);
    expect(status()).toBe('playing');
    expect(screen.queryByTestId('flyby-line-fallback-descent')).not.toBeInTheDocument();
    // Half-way down the survey the camera is under the ground, between the entrance and the bottom.
    expect(scene.camera().height).toBeLessThan(1000);
    expect(scene.camera().longitude).toBeGreaterThan(24.999);
    fireEvent.click(screen.getByTestId('flyby-stop'));
  });

  it('says so, and dives instead, when the scene draws no survey to follow', async () => {
    renderFlyby(Object.assign(fakeScene(), { caveLines: () => [] }));
    chooseDescent();
    fireEvent.click(screen.getByTestId('flyby-play'));
    // The camera goes to look at the cave first, so the scene draws its survey, and waits on it.
    await pass(1000);
    expect(status()).toBe('playing');
    expect(screen.getByTestId('flyby-line-fallback-descent')).toHaveTextContent('dives straight down');
  });
});

describe('a flight along an imported file', () => {
  it('follows the file chosen, named by its own name', async () => {
    const { scene } = renderFlyby(fakeScene(), null);
    fireEvent.click(screen.getByRole('radio', { name: 'An imported file' }));
    fireEvent.mouseDown(screen.getByRole('combobox', { name: 'The file to follow' }));
    fireEvent.click(screen.getByTitle('Ridge walk'));
    expect(screen.getByTestId('flyby-preset')).toHaveTextContent('Along a line');
    fireEvent.click(screen.getByText('2 sections'));
    expect(screen.getByTestId('flyby-sections')).toHaveTextContent('Along the line · Ridge walk');
    fireEvent.click(screen.getByTestId('flyby-play'));
    await pass(40_000);
    expect(status()).toBe('playing');
    expect(screen.queryByTestId('flyby-line-fallback-follow')).not.toBeInTheDocument();
    // Part of the way along, over the line between its ends.
    const camera = scene.camera();
    expect(camera.longitude).toBeGreaterThan(24.995);
    expect(camera.longitude).toBeLessThan(25.02);
    fireEvent.click(screen.getByTestId('flyby-stop'));
  });
});

describe('a flight from far away', () => {
  it('opens far above the cave and comes down into the preset', async () => {
    const { scene } = renderFlyby();
    fireEvent.click(screen.getByTestId('flyby-from-far'));
    fireEvent.click(screen.getByText('3 sections'));
    expect(screen.getByTestId('flyby-sections').querySelector('.scene3d-flyby-section-name')).toHaveTextContent('From far away · Cave A');
    fireEvent.click(screen.getByTestId('flyby-play'));
    await pass(50);
    // At the scale of the country, looking straight down.
    expect(scene.camera().height).toBeGreaterThan(300_000);
    expect(scene.camera().pitch).toBeLessThan(-89);
    fireEvent.click(screen.getByTestId('flyby-stop'));
  });
});

describe('a tour that stops at each cave', () => {
  it('stops for the time given and writes the cave\'s name and size over the picture, only when asked', async () => {
    const caves: CaveInView3D[] = [drawnCave, { caveId: 'b', bounds: [25.1, 45.5, 25.104, 45.504], top: 900, bottom: 850 }];
    renderFlyby(fakeScene(caves), null);
    fireEvent.click(screen.getByRole('radio', { name: 'Caves in view' }));
    expect(screen.queryByTestId('flyby-pause-say')).not.toBeInTheDocument();
    const pause = screen.getByRole('spinbutton', { name: 'Stop at each cave for' });
    fireEvent.change(pause, { target: { value: '4' } });
    fireEvent.blur(pause);
    // Nothing is written until asked for.
    expect(screen.getByTestId('flyby-pause-say')).toHaveTextContent('Nothing');
    fireEvent.mouseDown(screen.getByRole('combobox', { name: 'Write over the picture' }));
    fireEvent.click(screen.getByTitle('Its name, length and depth'));
    fireEvent.click(screen.getByText('7 sections'));
    expect(screen.getByTestId('flyby-sections')).toHaveTextContent('Hold · Cave A');
    // The turns are shortened so the test reaches the first stop quickly.
    fireEvent.change(screen.getByTestId('flyby-turns'), { target: { value: '0.25' } });
    fireEvent.blur(screen.getByTestId('flyby-turns'));

    const overlay = screen.getByTestId('flyby-texts');
    fireEvent.click(screen.getByTestId('flyby-play'));
    // Approach 6 s, a quarter turn 6 s, then the stop.
    await pass(14_000);
    expect(status()).toBe('playing');
    expect(overlay.dataset.shown).toBe('caption');
    fireEvent.click(screen.getByTestId('flyby-stop'));
  });

  it('says a cave\'s name and size in the reader\'s words', () => {
    const english = flybySayWords(i18n.getFixedT('en'), 'en');
    expect(english({ name: 'Cave A', lengthM: 1234.4, depthM: 85 }, 'nameAndSize')).toBe('Cave A — 1,234 m long, 85 m deep');
    expect(english({ name: 'Cave A', lengthM: 1234.4 }, 'name')).toBe('Cave A');
    expect(english({ name: 'Cave A' }, 'nameAndSize')).toBe('Cave A');
    expect(english({}, 'name')).toBeUndefined();
    const romanian = flybySayWords(i18n.getFixedT('ro'), 'ro');
    expect(romanian({ name: 'Peștera A', lengthM: 1234, depthM: 85 }, 'nameAndSize')).toBe('Peștera A — 1.234 m lungime, 85 m adâncime');
  });
});

describe('the furniture of the frame', () => {
  it('draws a north arrow and a corner line over the picture when asked', async () => {
    renderFlyby(lookingScene());
    fireEvent.click(screen.getByText('Texts over the picture'));
    fireEvent.click(screen.getByTestId('flyby-text-north'));
    fireEvent.change(screen.getByRole('textbox', { name: 'A line of text in the corner' }), { target: { value: 'Club' } });
    const overlay = screen.getByTestId('flyby-texts');
    fireEvent.click(screen.getByTestId('flyby-play'));
    await pass(100);
    expect(overlay.dataset.shown).toBe('north corner');
    fireEvent.click(screen.getByTestId('flyby-stop'));
    expect(overlay.dataset.shown).toBe('');
  });
});

describe('what a flyby is about, found by a saved filter or by name', () => {
  afterEach(() => {
    asked.length = 0;
    filterAnswer.empty = false;
  });

  it('tours the caves a saved filter keeps, asked for only when it is played, and keeps only the filter', async () => {
    renderFlyby(fakeScene([drawnCave, { caveId: 'b', bounds: [25.1, 45.5, 25.104, 45.504], top: 900, bottom: 850 }]), null);
    fireEvent.click(screen.getByRole('radio', { name: 'Caves a saved filter selects' }));
    // Only a filter that can be run is offered.
    fireEvent.mouseDown(screen.getByRole('combobox', { name: 'The saved filter' }));
    expect(screen.queryByTitle('Broken')).not.toBeInTheDocument();
    fireEvent.click(screen.getByTitle('Deep caves'));
    expect(screen.getByRole('spinbutton', { name: 'At most' })).toHaveValue('12');
    expect(screen.getByTestId('flyby-preset')).toHaveTextContent('Cave tour');
    fireEvent.click(screen.getByText('3 sections'));
    const names = Array.from(
      screen.getByTestId('flyby-sections').querySelectorAll('.scene3d-flyby-section-name'),
      (node) => node.textContent,
    );
    expect(names).toEqual([
      'Over to · each of the caves of “Deep caves”',
      'Circle · each of the caves of “Deep caves”',
      'Back up · the caves of “Deep caves”',
    ]);
    // Nothing is asked until the flyby is played.
    expect(asked).toEqual([]);

    fireEvent.change(screen.getByTestId('flyby-turns'), { target: { value: '0.25' } });
    fireEvent.blur(screen.getByTestId('flyby-turns'));
    fireEvent.click(screen.getByTestId('flyby-play'));
    await pass(50);
    expect(status()).toBe('playing');
    // Its caves, asked for this reader, at most the number given, in the filter's order.
    expect(asked).toEqual([
      expect.objectContaining({
        pageSize: 12,
        document: expect.objectContaining({
          sort: 'title',
          scope: [{ world: 'feature', where: { node: 'condition', field: 'kind', op: 'equals', values: [{ type: 'id', value: 'cave' }] } }],
        }),
      }),
    ]);
    // Two caves of a hop and a quarter circle, then the rise: 2 × (8 + 6) + 8 seconds.
    expect(screen.getByTestId('flyby-time')).toHaveTextContent('/ 0:36');
    // The panel marks the section being flown as written: the hop over, for the second cave too.
    await pass(15_000);
    expect(screen.getByTestId('flyby-sections').querySelector('.scene3d-flyby-section-current')?.textContent).toContain('Over to');
    fireEvent.click(screen.getByTestId('flyby-stop'));

    // Kept, it names the filter and the number, and none of the caves.
    fireEvent.change(screen.getByTestId('flyby-name'), { target: { value: 'By filter' } });
    fireEvent.click(screen.getByTestId('flyby-save'));
    const kept = useFlybyStore.getState().library[0];
    expect(kept.subjects).toEqual([{ kind: 'filter', savedFilterId: 'f0000000-0000-4000-8000-000000000001', limit: 12 }]);
    expect(JSON.stringify(kept)).not.toMatch(/"caveId"/);
  });

  it('says so when the filter keeps no cave the reader may see, and gives the camera back', async () => {
    filterAnswer.empty = true;
    const { scene } = renderFlyby(fakeScene(), null);
    fireEvent.click(screen.getByRole('radio', { name: 'Caves a saved filter selects' }));
    fireEvent.mouseDown(screen.getByRole('combobox', { name: 'The saved filter' }));
    fireEvent.click(screen.getByTitle('Deep caves'));
    fireEvent.click(screen.getByTestId('flyby-play'));
    await pass(50);
    expect(screen.getByTestId('flyby-problem')).toHaveTextContent('selects no cave you may see');
    expect(scene.camera()).toEqual(HOME);
    expect(status()).toBe('idle');
  });

  it('adds a cave found by its name to the tour, though the view does not show it', async () => {
    renderFlyby(fakeScene(), null);
    fireEvent.click(screen.getByRole('radio', { name: 'Caves in view' }));
    fireEvent.change(screen.getByRole('combobox', { name: 'Add a cave by name' }), { target: { value: 'Far' } });
    await pass(400);
    // A cave is offered; an entrance found by the same words is not.
    expect(screen.queryByText('An entrance')).not.toBeInTheDocument();
    fireEvent.click(screen.getByText('Far cave'));
    expect(screen.getByRole('checkbox', { name: 'Cave FAR' })).toBeChecked();
    expect(screen.getByRole('checkbox', { name: 'Cave A' })).toBeChecked();
    // Two caves ticked: a tour of both, the one found by name last.
    fireEvent.click(screen.getByText('5 sections'));
    expect(screen.getByTestId('flyby-sections')).toHaveTextContent('Over to · Cave FAR');
  });
});

describe('a colour for the cave being visited', () => {
  it('lights each cave of a tour in turn and dims the others, and gives every cave back when stopped', async () => {
    const scene = Object.assign(lookingScene(), {
      cavesInView: () => [drawnCave, { caveId: 'b', bounds: [25.1, 45.5, 25.104, 45.504] as [number, number, number, number], top: 900, bottom: 850 }],
    });
    renderFlyby(scene, null);
    fireEvent.click(screen.getByRole('radio', { name: 'Caves in view' }));
    fireEvent.click(screen.getByText('Ready-made changes'));
    fireEvent.change(screen.getByLabelText('Colour of the cave being visited'), { target: { value: '#FF0000' } });
    fireEvent.click(screen.getByTestId('flyby-recipe-light-add'));
    fireEvent.click(screen.getByText('4 changes'));
    expect(screen.getByTestId('flyby-changes')).toHaveTextContent('The cave visited in #ff0000, 85%');
    expect(screen.getByTestId('flyby-changes')).toHaveTextContent('The cave visited back to the colour of the others');

    fireEvent.click(screen.getByTestId('flyby-play'));
    await pass(10_000);
    expect(scene.shown).toMatchObject({ walls: { colour: '#5a6470', strength: 0.75 }, lines: '#5a6470', 'cave:a': { colour: '#ff0000' } });
    expect(scene.shown['cave:b']).toBeUndefined();
    fireEvent.click(screen.getByTestId('flyby-stop'));
    expect(Object.keys(scene.shown).filter((member) => member.startsWith('cave:') || member === 'walls')).toEqual([]);
  });
});

describe('walls on the route that wait for a yes', () => {
  function walledScene(pending: boolean) {
    const agreed: string[][] = [];
    const scene = Object.assign(fakeScene(), {
      walls: {
        pending: (caveIds: readonly string[]) => (pending && caveIds.includes('a') ? [{ caveId: 'a', bytes: 60 * 1024 * 1024 }] : []),
        agree: (caveIds: readonly string[]) => {
          agreed.push([...caveIds]);
        },
      },
    });
    return { scene, agreed };
  }

  it('asks once before the flyby plays, and downloads them on a yes', async () => {
    const { scene, agreed } = walledScene(true);
    renderFlyby(scene);
    fireEvent.click(screen.getByTestId('flyby-play'));
    await pass(50);
    expect(screen.getByText('Walls on this flyby\'s route')).toBeInTheDocument();
    expect(screen.getByText(/The walls of 1 cave on the route are 60.0 MB to download/)).toBeInTheDocument();
    fireEvent.click(screen.getByTestId('flyby-walls-download'));
    await pass(50);
    expect(agreed).toEqual([['a']]);
    expect(status()).toBe('playing');
    fireEvent.click(screen.getByTestId('flyby-stop'));
  });

  it('plays without them on a no, and downloads nothing', async () => {
    const { scene, agreed } = walledScene(true);
    renderFlyby(scene);
    fireEvent.click(screen.getByTestId('flyby-play'));
    await pass(50);
    fireEvent.click(screen.getByTestId('flyby-walls-without'));
    await pass(50);
    expect(agreed).toEqual([]);
    expect(status()).toBe('playing');
    fireEvent.click(screen.getByTestId('flyby-stop'));
  });

  it('asks nothing when no walls wait for a yes', async () => {
    const { scene } = walledScene(false);
    renderFlyby(scene);
    fireEvent.click(screen.getByTestId('flyby-play'));
    await pass(50);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(status()).toBe('playing');
    fireEvent.click(screen.getByTestId('flyby-stop'));
  });
});

describe('layers of features the flyby draws itself', () => {
  it('adds one by kind and one by a saved filter, shows them in turn, and leaves none behind', async () => {
    const scene = lookingScene();
    renderFlyby(scene);
    fireEvent.click(screen.getByText('Features the flyby shows (0)'));
    fireEvent.mouseDown(screen.getByRole('combobox', { name: 'Kinds of feature' }));
    fireEvent.click(screen.getByTitle('Spring'));
    fireEvent.click(screen.getByTestId('flyby-features-add'));
    fireEvent.click(screen.getByText('Saved filter'));
    fireEvent.mouseDown(screen.getByRole('combobox', { name: 'The saved filter' }));
    // Only a filter that can be run is offered.
    expect(screen.queryByTitle('Broken')).not.toBeInTheDocument();
    fireEvent.click(screen.getByTitle('Deep caves'));
    fireEvent.click(screen.getByTestId('flyby-features-add'));
    expect(screen.getByTestId('flyby-features-f1')).toHaveTextContent('Features: Spring');
    expect(screen.getByTestId('flyby-features-f2')).toHaveTextContent('Deep caves');
    expect(screen.getByRole('combobox', { name: 'Colour of Features: Spring' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Remove the layer Deep caves' })).toBeInTheDocument();

    // In turn, by the ready-made set that shows layers of features one after another.
    fireEvent.click(screen.getByText('Ready-made changes'));
    fireEvent.mouseDown(screen.getByRole('combobox', { name: 'Layers of features, in order' }));
    // The last of each name is the option in the list just opened; a list closed before keeps its own.
    fireEvent.click(screen.getAllByTitle('Features: Spring').at(-1)!);
    fireEvent.click(screen.getAllByTitle('Deep caves').at(-1)!);
    fireEvent.click(screen.getByTestId('flyby-recipe-features-add'));
    expect(screen.getByText('3 changes')).toBeInTheDocument();

    fireEvent.click(screen.getByTestId('flyby-play'));
    await pass(3000);
    // Both are the scene's to draw; the first is in, the second not yet.
    expect((scene.shown.features as { id: string }[]).map((layer) => layer.id)).toEqual(['f1', 'f2']);
    expect(scene.shown['layer:features:f1']).toMatchObject({ visible: true });
    expect(scene.shown['layer:features:f2']).toMatchObject({ visible: false });
    fireEvent.click(screen.getByTestId('flyby-stop'));
    expect(Object.keys(scene.shown).filter((member) => member.includes('features'))).toEqual([]);

    // Removed, a layer takes its changes with it.
    fireEvent.click(screen.getByRole('button', { name: 'Remove the layer Features: Spring' }));
    expect(screen.queryByTestId('flyby-features-f1')).not.toBeInTheDocument();
    expect(screen.getByText('1 change')).toBeInTheDocument();
  });
});

describe('the words for every preset and every kind of section', () => {
  it('exist in both languages', () => {
    for (const locale of [en, ro]) {
      for (const preset of FLYBY_PRESETS) {
        expect(locale.flyby.preset[preset].name.length).toBeGreaterThan(0);
        expect(locale.flyby.preset[preset].hint.length).toBeGreaterThan(0);
      }
      for (const kind of FLYBY_SECTION_KINDS) {
        expect(locale.flyby.section[kind].length).toBeGreaterThan(0);
      }
      expect(Object.keys(locale.flyby.preset).sort()).toEqual([...FLYBY_PRESETS].sort());
      expect(Object.keys(locale.flyby.section).sort()).toEqual([...FLYBY_SECTION_KINDS].sort());
    }
  });
});
