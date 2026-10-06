// SPDX-License-Identifier: AGPL-3.0-or-later
import { act, cleanup, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import i18n from '../../../i18n';
import { CAVEVIEW_HOME, type Cv2Namespace } from '../../../caveview/loadCaveView.ts';
import { DEFAULT_MOVIE_SETTINGS } from '../../../caveview/movie/movieSettings.ts';
import MoviePreviewHost, { type MoviePreviewHandle } from './MoviePreviewHost.tsx';
import { previewBox, previewSurface } from './previewBox.ts';

// What the survey-model read answers, as the test sets it. Hoisted because the mock's factory runs
// before anything declared in this file.
const model = vi.hoisted(() => ({
  current: {
    data: undefined as { modelUrl: string; name: string; format: string } | undefined,
    isStale: false,
    isFetchedAfterMount: false,
    error: null as unknown,
  },
}));
vi.mock('../../../api/hooks.ts', () => ({ useSurveyModel: () => model.current }));
// The bundle loader caches the namespace it first resolved; each test here brings its own.
vi.mock('../../../caveview/loadCaveView.ts', async (original) => ({
  ...(await original<typeof import('../../../caveview/loadCaveView.ts')>()),
  loadCaveView: () => Promise.resolve(window.CV2!),
}));

type Listener = (event: unknown) => void;

class FakeViewer {
  static all: FakeViewer[] = [];
  containerId: string;
  config: Record<string, unknown>;
  listeners = new Map<string, Listener[]>();
  writes: string[] = [];
  resize = vi.fn();
  endCapture = vi.fn();
  // The layers, as a model with every one of them would have them.
  legs = true;
  grid = true;
  HUD = true;
  hasLegs = true;
  hasGrid = true;
  liveMarkerLabels = true;
  liveMarkerLabelBacking = true;
  liveMarkerLabelSize: number | null = 12;
  cameraType = 1;
  linewidth = 0;
  zScale = 0.5;
  shadingMode = 0;
  hasRealTerrain = null;
  // Every view the camera was turned to. The viewer reads its view back as the plan view whatever
  // it shows, which is why a turn cannot be compared before it is made.
  viewWrites: number[] = [];
  get view() {
    return 1;
  }
  set view(mode: number) {
    this.viewWrites.push(mode);
  }
  // Settling a turn under way, which a new turn is always preceded by.
  settles = 0;
  setCameraAngles(angles: { azimuth?: number; polar?: number }) {
    if (angles.azimuth === undefined && angles.polar === undefined) this.settles += 1;
  }
  constructor(containerId: string, config: Record<string, unknown>) {
    this.containerId = containerId;
    this.config = config;
    FakeViewer.all.push(this);
  }
  addEventListener(type: string, listener: Listener) {
    this.listeners.set(type, [...(this.listeners.get(type) ?? []), listener]);
  }
  // A model that has loaded is announced carrying its survey; the viewer's other `newCave`, when a
  // key switches how its controls behave, carries none.
  emit(type: string, event: Record<string, unknown> = type === 'newCave' ? { survey: {} } : {}) {
    for (const listener of this.listeners.get(type) ?? []) listener(event);
  }
}

const loadCave = vi.fn();
const dispose = vi.fn();
class FakeUi {
  loadCave = loadCave;
  dispose = dispose;
}

const FRAME = { width: 640, height: 360 };

function host(overrides: Partial<Parameters<typeof MoviePreviewHost>[0]> = {}) {
  return (
    <MoviePreviewHost
      surveyModelId="model-1"
      frame={FRAME}
      view={DEFAULT_MOVIE_SETTINGS.view}
      cavers={DEFAULT_MOVIE_SETTINGS.cavers}
      maxHeight={400}
      {...overrides}
    />
  );
}

beforeEach(() => {
  FakeViewer.all = [];
  vi.clearAllMocks();
  model.current = {
    data: { modelUrl: 'http://files.local/model?sig=1', name: 'Pestera 1', format: 'survex3d' },
    isStale: false,
    isFetchedAfterMount: false,
    error: null,
  };
  window.CV2 = {
    CaveViewer: FakeViewer,
    CaveViewUI: FakeUi,
    CAMERA_PERSPECTIVE: 1,
    CAMERA_ORTHOGRAPHIC: 2,
    SHADING_DEPTH: 9,
    SHADING_DEPTH_CURSOR: 11,
    VIEW_PLAN: 1,
    VIEW_ELEVATION_N: 2,
    VIEW_ELEVATION_S: 3,
    VIEW_ELEVATION_E: 4,
    VIEW_ELEVATION_W: 5,
  } as unknown as Cv2Namespace;
  vi.stubGlobal('fetch', vi.fn(async () => new Response(new Blob(['survey']))));
  // jsdom lays nothing out; the room the preview is given is 800 by 400.
  vi.spyOn(Element.prototype, 'clientWidth', 'get').mockReturnValue(800);
  vi.spyOn(Element.prototype, 'clientHeight', 'get').mockReturnValue(400);
});

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
  window.CV2 = undefined;
});

describe('previewBox', () => {
  it('is the largest box of the frame’s shape that fits, to within half a pixel', () => {
    // Height is what limits a 16:9 frame in an 800×400 room.
    expect(previewBox({ width: 800, height: 400 }, { width: 1280, height: 720 })).toEqual({ width: 711, height: 400 });
    // Width is what limits it in a tall room.
    expect(previewBox({ width: 500, height: 900 }, { width: 640, height: 480 })).toEqual({ width: 500, height: 375 });
    const box = previewBox({ width: 733.4, height: 999 }, { width: 640, height: 360 });
    expect(Math.abs(box.height - (box.width * 360) / 640)).toBeLessThanOrEqual(0.5);
    expect(previewBox({ width: 0, height: 400 }, FRAME)).toEqual({ width: 0, height: 0 });
  });
});

describe('MoviePreviewHost', () => {
  it('builds its own viewer, reads the model once, and applies the movie’s view after the model loads', async () => {
    const ready = vi.fn<(handle: MoviePreviewHandle | null) => void>();
    const { rerender } = render(host({ onReady: ready }));
    await waitFor(() => expect(loadCave).toHaveBeenCalledTimes(1));
    const viewer = FakeViewer.all[0];
    expect(fetch).toHaveBeenCalledWith(
      'http://files.local/model?sig=1',
      expect.objectContaining({ signal: expect.any(AbortSignal) }),
    );
    expect((loadCave.mock.calls[0][0] as File).name).toBe('Pestera 1.3d');
    expect(document.getElementById(viewer.containerId)).not.toBeNull();
    // Nothing is set before the model is there to set it on.
    expect(viewer.grid).toBe(true);

    act(() => viewer.emit('newCave'));
    await waitFor(() => expect(screen.getByTestId('movie-preview')).toHaveAttribute('data-status', 'ready'));
    // The movie's private defaults: no grid, no heads-up display.
    expect(viewer.grid).toBe(false);
    expect(viewer.HUD).toBe(false);
    // A 640×360 frame fills an 800×400 room as a 711×400 box, so at one device pixel per page
    // pixel a label of 16 frame pixels is drawn at 16·711/640.
    expect(viewer.liveMarkerLabelSize).toBeCloseTo((DEFAULT_MOVIE_SETTINGS.cavers.labelSize * 711) / 640, 1);
    expect(viewer.resize).toHaveBeenCalled();
    expect(ready).toHaveBeenLastCalledWith(expect.objectContaining({ viewer }));
    // Once, for the viewer; not again for every box that changes size.
    expect(ready).toHaveBeenCalledTimes(1);
    // The movie's labels are written white on a dark plate, whichever the reader chooses.
    expect(viewer.config.theme).toEqual({
      liveMarkers: { labelBackground: '#141414', labelText: '#ffffff', labelBackgroundOpacity: 0.8 },
    });

    // A refreshed signed address does not read the model again.
    model.current = { ...model.current, data: { ...model.current.data!, modelUrl: 'http://files.local/model?sig=2' } };
    rerender(host({ onReady: ready, view: { ...DEFAULT_MOVIE_SETTINGS.view, grid: true } }));
    expect(fetch).toHaveBeenCalledTimes(1);
    expect(FakeViewer.all).toHaveLength(1);
    // A changed setting reaches the viewer at once.
    await waitFor(() => expect(viewer.grid).toBe(true));
  });

  it('builds its viewer as the one on the page behind it was built: same home, same language, a lookup of its own', async () => {
    // The viewer's catalogue is one object shared by every viewer on the page, and a viewer given
    // no language sets the browser's for all of them. The preview opens over a panel that is
    // showing a model, so a preview built without the interface's language rewrote that panel's
    // settings and scale caption in the browser's — and asked for a catalogue nobody ships when
    // the browser was in a third language. These tests read English, so Romanian is the language
    // that shows the option is passed rather than happening to match a default.
    await i18n.changeLanguage('ro');
    try {
      render(host());
      await waitFor(() => expect(FakeViewer.all).toHaveLength(1));
      expect(FakeViewer.all[0].config.language).toBe('ro');
      // The versioned home is what brings an upgraded viewer to the browser, and the lookup is
      // what keeps the bundle from asking a third party which coordinate system a survey is in.
      expect(FakeViewer.all[0].config.home).toBe(CAVEVIEW_HOME);
      expect(typeof FakeViewer.all[0].config.crsLookup).toBe('function');

      // Switched with the dialog open: not a reason to read the model again. A turn of the event
      // loop is waited out, since a rebuild would only then have asked for the file.
      await act(async () => {
        await i18n.changeLanguage('en');
        await new Promise((resolve) => setTimeout(resolve, 0));
      });
      expect(fetch).toHaveBeenCalledTimes(1);
      expect(FakeViewer.all).toHaveLength(1);

    } finally {
      await i18n.changeLanguage('en');
    }
  });

  it('reads the language when the viewer is built, not when the dialog opened', async () => {
    // The viewer is built once the model's address has arrived, which can be a while after the
    // preview was mounted. A language switched in between is the one the interface is in.
    model.current = { ...model.current, data: undefined };
    await i18n.changeLanguage('ro');
    try {
      const { rerender } = render(host());
      await act(async () => {
        await i18n.changeLanguage('en');
      });
      expect(FakeViewer.all).toHaveLength(0);

      model.current = {
        ...model.current,
        data: { modelUrl: 'http://files.local/model?sig=1', name: 'Pestera 1', format: 'survex3d' },
      };
      rerender(host());
      await waitFor(() => expect(FakeViewer.all).toHaveLength(1));
      expect(FakeViewer.all[0].config.language).toBe('en');
    } finally {
      await i18n.changeLanguage('en');
    }
  });

  it('draws labels at the preview’s share of the frame', async () => {
    render(host({ frame: { width: 1280, height: 720 }, cavers: { ...DEFAULT_MOVIE_SETTINGS.cavers, labelSize: 32 } }));
    await waitFor(() => expect(loadCave).toHaveBeenCalled());
    act(() => FakeViewer.all[0].emit('newCave'));
    // A 711-pixel-wide preview of a 1280-pixel frame draws a 32-pixel label at 32·711/1280.
    await waitFor(() => expect(FakeViewer.all[0].liveMarkerLabelSize).toBeCloseTo((32 * 711) / 1280, 1));
  });

  it('lays the surface out smaller and scales it up where the box has more pixels than the labels can follow', async () => {
    vi.stubGlobal('devicePixelRatio', 2);
    render(host({ frame: { width: 320, height: 180 } }));
    await waitFor(() => expect(loadCave).toHaveBeenCalled());
    const viewer = FakeViewer.all[0];
    act(() => viewer.emit('newCave'));
    const surface = document.getElementById(viewer.containerId)!;
    // The 711-pixel box is 1422 device pixels; a 16-pixel label of a 320-pixel frame would want
    // 71 of them, past the 45 the atlas draws. So the surface is laid out at 900 device pixels —
    // 450 page pixels — and scaled up to the box.
    await waitFor(() => expect(surface.style.getPropertyValue('--movie-surface-width')).toBe('450px'));
    expect(surface.style.getPropertyValue('--movie-surface-height')).toBe('253px');
    expect(Number(surface.style.getPropertyValue('--movie-surface-scale'))).toBeCloseTo(711 / 450, 6);
    // The label keeps its share of the frame: 16 of 320 is 45 of 900.
    await waitFor(() => expect(viewer.liveMarkerLabelSize).toBe(45));
  });

  it('lays the surface out at the box where the labels fit, or are not drawn', () => {
    const box = { width: 711, height: 400 };
    expect(previewSurface(box, { width: 1280, height: 720 }, 16, 2, 45)).toBe(box);
    expect(previewSurface(box, { width: 320, height: 180 }, null, 2, 45)).toBe(box);
    const reduced = previewSurface(box, { width: 320, height: 180 }, 16, 2, 45);
    expect(reduced.width * 2 * (16 / 320)).toBeLessThanOrEqual(45);
    expect(Math.abs(reduced.height - (reduced.width * 180) / 320)).toBeLessThanOrEqual(0.5);
  });

  it('puts the viewer’s own shading back when the movie’s is left to the viewer again', async () => {
    const { rerender } = render(host());
    await waitFor(() => expect(loadCave).toHaveBeenCalled());
    const viewer = FakeViewer.all[0];
    viewer.shadingMode = 3;
    act(() => viewer.emit('newCave'));
    await waitFor(() => expect(screen.getByTestId('movie-preview')).toHaveAttribute('data-status', 'ready'));

    rerender(host({ view: { ...DEFAULT_MOVIE_SETTINGS.view, shadingMode: 1 } }));
    await waitFor(() => expect(viewer.shadingMode).toBe(1));
    rerender(host({ view: { ...DEFAULT_MOVIE_SETTINGS.view, shadingMode: null } }));
    await waitFor(() => expect(viewer.shadingMode).toBe(3));
  });

  it('turns to the starting view when the model loads, when the choice changes and when asked again — and on nothing else', async () => {
    const { rerender } = render(host());
    await waitFor(() => expect(loadCave).toHaveBeenCalled());
    const viewer = FakeViewer.all[0];
    expect(viewer.viewWrites).toEqual([]);
    act(() => viewer.emit('newCave'));
    // A movie starts from the north elevation unless the reader chooses otherwise.
    await waitFor(() => expect(viewer.viewWrites).toEqual([2]));

    // Another setting changes: the reader's own turning and zooming of the preview is kept.
    const withGrid = { ...DEFAULT_MOVIE_SETTINGS.view, grid: true };
    rerender(host({ view: withGrid }));
    await waitFor(() => expect(viewer.grid).toBe(true));
    expect(viewer.viewWrites).toEqual([2]);

    // The choice changes.
    const east = { ...withGrid, direction: 'east' as const };
    rerender(host({ view: east }));
    await waitFor(() => expect(viewer.viewWrites).toEqual([2, 4]));
    // The reader asks for it again after turning the preview away from it.
    rerender(host({ view: east, viewRequest: 1 }));
    await waitFor(() => expect(viewer.viewWrites).toEqual([2, 4, 4]));

    // A recording takes the viewer and lets it go: the movie started from what the preview
    // showed, and the preview is not turned back afterwards.
    rerender(host({ view: east, viewRequest: 1, recording: true }));
    rerender(host({ view: east, viewRequest: 1, recording: false }));
    await act(async () => {});
    expect(viewer.viewWrites).toEqual([2, 4, 4]);
  });

  it('settles a turn under way before turning again, which the viewer would otherwise ignore', async () => {
    const { rerender } = render(host());
    await waitFor(() => expect(loadCave).toHaveBeenCalled());
    const viewer = FakeViewer.all[0];
    act(() => viewer.emit('newCave'));
    await waitFor(() => expect(viewer.viewWrites).toEqual([2]));
    expect(viewer.settles).toBe(1);
    rerender(host({ view: { ...DEFAULT_MOVIE_SETTINGS.view, direction: 'west' } }));
    await waitFor(() => expect(viewer.viewWrites).toEqual([2, 5]));
    expect(viewer.settles).toBe(2);
  });

  it('holds a turn asked for during a recording until it lets go, then makes it once', async () => {
    const { rerender } = render(host());
    await waitFor(() => expect(loadCave).toHaveBeenCalled());
    const viewer = FakeViewer.all[0];
    act(() => viewer.emit('newCave'));
    await waitFor(() => expect(viewer.viewWrites).toEqual([2]));
    rerender(host({ recording: true }));
    rerender(host({ recording: true, viewRequest: 1 }));
    await act(async () => {});
    // Nothing turns under a recording: the frame it draws next would show the turn.
    expect(viewer.viewWrites).toEqual([2]);
    rerender(host({ recording: false, viewRequest: 1 }));
    await waitFor(() => expect(viewer.viewWrites).toEqual([2, 2]));
    rerender(host({ recording: false, viewRequest: 1 }));
    await act(async () => {});
    expect(viewer.viewWrites).toEqual([2, 2]);
  });

  it('does not take the viewer\'s other newCave, for a change of controls, as a model loading', async () => {
    render(host());
    await waitFor(() => expect(loadCave).toHaveBeenCalled());
    const viewer = FakeViewer.all[0];
    act(() => viewer.emit('newCave'));
    await waitFor(() => expect(viewer.viewWrites).toEqual([2]));
    viewer.shadingMode = 6;
    act(() => viewer.emit('newCave', {}));
    await act(async () => {});
    // The reader's framing stays, and the viewer's own shading is not re-read from the movie's.
    expect(viewer.viewWrites).toEqual([2]);
  });

  it('turns the labels off rather than drawing them empty', async () => {
    render(host({ cavers: { ...DEFAULT_MOVIE_SETTINGS.cavers, labels: 'off' } }));
    await waitFor(() => expect(loadCave).toHaveBeenCalled());
    act(() => FakeViewer.all[0].emit('newCave'));
    await waitFor(() => expect(FakeViewer.all[0].liveMarkerLabels).toBe(false));
  });

  it('waits for a fresh address when all it has is an old one', async () => {
    model.current = { ...model.current, isStale: true, isFetchedAfterMount: false };
    const { rerender } = render(host());
    await act(async () => {});
    expect(fetch).not.toHaveBeenCalled();

    model.current = { ...model.current, isFetchedAfterMount: true };
    rerender(host());
    await waitFor(() => expect(fetch).toHaveBeenCalledTimes(1));
  });

  it('says so when the model cannot be read', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => new Response('', { status: 403 })));
    render(host());
    await waitFor(() => expect(screen.getByTestId('movie-preview')).toHaveAttribute('data-status', 'error'));
    expect(screen.getByText('The model could not be loaded for the preview.')).toBeInTheDocument();
  });

  it('lets go of the viewer when it goes: the session ended, the viewer disposed, the caller told', async () => {
    const ready = vi.fn<(handle: MoviePreviewHandle | null) => void>();
    const { unmount } = render(host({ onReady: ready }));
    await waitFor(() => expect(loadCave).toHaveBeenCalled());
    const viewer = FakeViewer.all[0];
    act(() => viewer.emit('newCave'));
    await waitFor(() => expect(ready).toHaveBeenCalledWith(expect.objectContaining({ viewer })));
    // The viewer's canvas, as the real one puts it in its container.
    const loseContext = vi.fn();
    const canvas = document.createElement('canvas');
    vi.spyOn(canvas, 'getContext').mockImplementation(((kind: string) =>
      kind === 'webgl2' ? { getExtension: (name: string) => (name === 'WEBGL_lose_context' ? { loseContext } : null) } : null) as never);
    document.getElementById(viewer.containerId)!.appendChild(canvas);
    unmount();
    expect(viewer.endCapture).toHaveBeenCalled();
    expect(dispose).toHaveBeenCalledTimes(1);
    expect(ready).toHaveBeenLastCalledWith(null);
    // Its WebGL context is given back at once, not left to the collector.
    expect(loseContext).toHaveBeenCalledTimes(1);
  });
});

describe('a model still on its way', () => {
  it('is called off when the preview goes', async () => {
    let signal: AbortSignal | undefined;
    vi.stubGlobal(
      'fetch',
      vi.fn((_url: string, init?: RequestInit) => new Promise<Response>(() => {
        signal = init?.signal ?? undefined;
      })),
    );
    const { unmount } = render(host());
    await waitFor(() => expect(signal).toBeDefined());
    expect(signal!.aborted).toBe(false);
    unmount();
    expect(signal!.aborted).toBe(true);
  });
});
