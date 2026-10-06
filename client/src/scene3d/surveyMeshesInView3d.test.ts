// SPDX-License-Identifier: AGPL-3.0-or-later
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { CaveMeshesInView, CaveMeshInView } from '../api/hooks.ts';
import type { Scene3DBounds, Scene3DModelOptions } from './scene3dEngine.ts';

// The listing is the only thing this loader fetches, and every decision it makes is about what
// came back — so it is stubbed, and every other module involved is the real one.
let asked: string[] = [];
/** How many caves each of those requests asked to be answered with. */
let askedCaves: (number | undefined)[] = [];
let respond: (bbox: string) => Promise<CaveMeshesInView> = () => Promise.resolve(answer([]));

vi.mock('../api/hooks.ts', () => ({
  fetchCaveMeshesInView: (bbox: string, maxCaves?: number) => {
    asked.push(bbox);
    askedCaves.push(maxCaves);
    return respond(bbox);
  },
}));

const {
  attachSurveyMeshesInView3d,
  chooseMeshesInView,
  meshesInViewLimitsInForce,
  surveyMeshInViewModelId,
} = await import('./surveyMeshesInView3d.ts');
type SurveyMeshesInView3DEngine = import('./surveyMeshesInView3d.ts').SurveyMeshesInView3DEngine;
type SurveyMeshesInView3DLimits = import('./surveyMeshesInView3d.ts').SurveyMeshesInView3DLimits;

const MB = 1024 * 1024;
const limits: SurveyMeshesInView3DLimits = { minZoom: 14, maxCaves: 12, maxBytes: 64 * MB };

/** One cave's mesh as the server describes it; the address is signed, so it differs per answer. */
function mesh(caveId: string, overrides: Partial<CaveMeshInView> = {}): CaveMeshInView {
  return {
    caveId,
    caveName: `Cave ${caveId}`,
    surveyModelId: `model-${caveId}`,
    modelName: 'Walls',
    meshUrl: `/files/${caveId}.glb?token=first`,
    anchorLongitude: 25.44,
    anchorLatitude: 45.53,
    anchorHeightM: 500,
    triangleCount: 1200,
    sizeBytes: 300_000,
    ...overrides,
  };
}

function answer(items: CaveMeshInView[], total = items.length): CaveMeshesInView {
  return { items, total };
}

/** One `loadModel` the loader made, held open until the test lands or refuses it. */
interface ModelRead {
  id: string;
  options: Scene3DModelOptions;
  land: () => void;
  refuse: () => void;
}

/**
 * A scene that records what it was asked to do with models, holds every read open, and answers
 * for a camera the test moves by hand. It behaves as the real scene does in the one way this
 * loader leans on: the same address under an id that already holds it is a move, not a read.
 */
class FakeScene implements SurveyMeshesInView3DEngine {
  readonly reads: ModelRead[] = [];
  readonly moves: { id: string; options: Scene3DModelOptions }[] = [];
  readonly removals: string[] = [];
  readonly placements: unknown[] = [];
  readonly held = new Map<string, Scene3DModelOptions>();
  readonly viewListeners = new Set<() => void>();
  bounds: Scene3DBounds | undefined = [25.4, 45.5, 25.5, 45.6];
  zoom = 15;

  loadModel(id: string, options: Scene3DModelOptions): Promise<void> {
    if (this.held.get(id)?.url === options.url) {
      this.held.set(id, options);
      this.moves.push({ id, options });
      return Promise.resolve();
    }
    this.held.set(id, options);
    return new Promise<void>((resolve, reject) => {
      this.reads.push({
        id,
        options,
        land: () => resolve(),
        refuse: () => reject(new Error('unreadable')),
      });
    });
  }

  removeModel(id: string): void {
    this.removals.push(id);
    this.held.delete(id);
  }

  setModelPlacement(placement: unknown): void {
    this.placements.push(placement);
  }

  getVisibleBounds() {
    return this.bounds;
  }

  getPseudoZoom() {
    return this.zoom;
  }

  onViewChanged(listener: () => void) {
    this.viewListeners.add(listener);
    return () => this.viewListeners.delete(listener);
  }

  /** Stands in for the viewer moving the camera. */
  move() {
    for (const listener of [...this.viewListeners]) listener();
  }

  /** The caves whose walls are in the scene or on their way, in the order they were asked for. */
  heldCaves(): string[] {
    return [...this.held.keys()].map((id) => id.replace('survey-mesh-in-view:', ''));
  }

  /** The caves a read was started for, in order — which is the order the budget was spent in. */
  readCaves(): string[] {
    return this.reads.map((read) => read.id.replace('survey-mesh-in-view:', ''));
  }
}

/** Lets every promise already resolved settle, which is how a listing lands in these tests. */
const settle = () => new Promise<void>((resolve) => setTimeout(resolve, 0));

/** A loader that is on, has its limits, and has had its first answer. */
async function started(
  scene: FakeScene,
  first: CaveMeshesInView,
  options: { limits?: SurveyMeshesInView3DLimits; tops?: Record<string, number>; selected?: string } = {},
) {
  respond = () => Promise.resolve(first);
  const tops: Record<string, number> = { ...options.tops };
  const loader = attachSurveyMeshesInView3d(scene, (caveId) => tops[caveId]);
  loader.setLimits(options.limits ?? limits);
  loader.setSelectedCave(options.selected);
  loader.setActive(true);
  await settle();
  return { loader, tops };
}

beforeEach(() => {
  asked = [];
  askedCaves = [];
  respond = () => Promise.resolve(answer([]));
});

afterEach(() => {
  vi.useRealTimers();
});

describe('chooseMeshesInView', () => {
  it('takes the nearest meshes until the count limit is reached', () => {
    const chosen = chooseMeshesInView(
      answer([mesh('a'), mesh('b'), mesh('c'), mesh('d')]),
      { ...limits, maxCaves: 2 },
    );

    expect(chosen.taken.map((item) => item.caveId)).toEqual(['a', 'b']);
    expect(chosen.limitedBy).toBe('count');
  });

  it('stops at the first mesh the byte budget cannot hold, rather than skipping to a smaller one', () => {
    // Thirty and forty do not fit sixty-four together. The one megabyte behind them would, and
    // taking it would draw the walls at the edge of the view around a hole in the middle of it.
    const chosen = chooseMeshesInView(
      answer([
        mesh('a', { sizeBytes: 30 * MB }),
        mesh('b', { sizeBytes: 40 * MB }),
        mesh('c', { sizeBytes: 1 * MB }),
      ]),
      limits,
    );

    expect(chosen.taken.map((item) => item.caveId)).toEqual(['a']);
    expect(chosen.limitedBy).toBe('bytes');
  });

  it('fills the byte budget exactly and no further', () => {
    const chosen = chooseMeshesInView(
      answer([
        mesh('a', { sizeBytes: 32 * MB }),
        mesh('b', { sizeBytes: 32 * MB }),
        mesh('c', { sizeBytes: 1 }),
      ]),
      limits,
    );

    expect(chosen.taken.map((item) => item.caveId)).toEqual(['a', 'b']);
    expect(chosen.limitedBy).toBe('bytes');
  });

  it('takes the selected cave first, however far from the middle of the view it is', () => {
    const chosen = chooseMeshesInView(
      answer([mesh('a'), mesh('b'), mesh('c')]),
      { ...limits, maxCaves: 2 },
      'c',
    );

    expect(chosen.taken.map((item) => item.caveId)).toEqual(['c', 'a']);
  });

  it('takes the selected cave even when it is bigger than the whole byte budget, and then nothing else', () => {
    // The other mode draws the selected cave's walls whatever their size. This one must not be
    // the way to lose them.
    const chosen = chooseMeshesInView(
      answer([mesh('a'), mesh('huge', { sizeBytes: 100 * MB })]),
      limits,
      'huge',
    );

    expect(chosen.taken.map((item) => item.caveId)).toEqual(['huge']);
    expect(chosen.limitedBy).toBe('bytes');
  });

  it('says the count was the limit when the server counted caves it did not describe', () => {
    const chosen = chooseMeshesInView(answer([mesh('a'), mesh('b')], 5), limits);

    expect(chosen.taken).toHaveLength(2);
    expect(chosen.limitedBy).toBe('count');
  });

  it('names no limit when every cave in view fits', () => {
    expect(chooseMeshesInView(answer([mesh('a'), mesh('b')]), limits).limitedBy).toBeUndefined();
  });
});

describe('attachSurveyMeshesInView3d', () => {
  it('asks for nothing and holds nothing until it is switched on and told the limits', async () => {
    const scene = new FakeScene();
    const loader = attachSurveyMeshesInView3d(scene, () => undefined);
    scene.move();
    await settle();

    expect(asked).toEqual([]);
    expect(loader.getState().status).toBe('off');

    // On, but with no budget to spend: guessing one would be a limit compiled into the client.
    loader.setActive(true);
    await settle();
    expect(asked).toEqual([]);
    expect(loader.getState().status).toBe('looking');

    respond = () => Promise.resolve(answer([mesh('a')]));
    loader.setLimits(limits);
    await settle();
    expect(asked).toEqual(['25.40000,45.50000,25.50000,45.60000']);
    expect(scene.heldCaves()).toEqual(['a']);
  });

  it('holds nothing below the zoom floor, and says that zooming in is what is missing', async () => {
    const scene = new FakeScene();
    scene.zoom = 13.4;
    const { loader } = await started(scene, answer([mesh('a')]));

    expect(asked).toEqual([]);
    expect(scene.reads).toEqual([]);
    expect(loader.getState()).toMatchObject({ status: 'zoomIn', shown: 0, inView: 0 });

    // The floor is the zoom the map endpoints would be asked at, which rounds: 13.6 is 14.
    vi.useFakeTimers();
    scene.zoom = 13.6;
    scene.move();
    await vi.advanceTimersByTimeAsync(250);
    expect(asked).toHaveLength(1);
    expect(scene.heldCaves()).toEqual(['a']);
    expect(loader.getState().status).toBe('ready');
  });

  it('releases everything when the camera pulls back out past the floor', async () => {
    const scene = new FakeScene();
    const { loader } = await started(scene, answer([mesh('a'), mesh('b')]));
    scene.reads.forEach((read) => read.land());
    await settle();
    expect(loader.getState().shown).toBe(2);

    vi.useFakeTimers();
    scene.zoom = 11;
    scene.move();
    await vi.advanceTimersByTimeAsync(250);

    expect(scene.heldCaves()).toEqual([]);
    expect(scene.removals).toEqual([surveyMeshInViewModelId('a'), surveyMeshInViewModelId('b')]);
    expect(asked).toHaveLength(1);
    expect(loader.getState()).toMatchObject({ status: 'zoomIn', shown: 0, heldBytes: 0 });
  });

  it('reads the nearest caves first and stops at the count limit, saying how many it left out', async () => {
    const scene = new FakeScene();
    const { loader } = await started(
      scene,
      answer([mesh('a'), mesh('b'), mesh('c'), mesh('d')]),
      { limits: { ...limits, maxCaves: 2 } },
    );

    expect(scene.readCaves()).toEqual(['a', 'b']);
    expect(loader.getState()).toMatchObject({
      status: 'ready',
      shown: 2,
      loading: 2,
      inView: 4,
      leftOut: 2,
      limitedBy: 'count',
      heldBytes: 600_000,
    });
  });

  it('stops at the byte budget, and counts what it holds in the sizes the server stated', async () => {
    const scene = new FakeScene();
    const { loader } = await started(
      scene,
      answer([
        mesh('a', { sizeBytes: 30 * MB }),
        mesh('b', { sizeBytes: 30 * MB }),
        mesh('c', { sizeBytes: 10 * MB }),
      ]),
    );

    expect(scene.readCaves()).toEqual(['a', 'b']);
    expect(loader.getState()).toMatchObject({
      shown: 2,
      inView: 3,
      leftOut: 1,
      limitedBy: 'bytes',
      heldBytes: 60 * MB,
      limits,
    });
  });

  it('counts the caves the server did not describe among the ones left out', async () => {
    const scene = new FakeScene();
    const { loader } = await started(scene, answer([mesh('a'), mesh('b')], 7));

    expect(loader.getState()).toMatchObject({ shown: 2, inView: 7, leftOut: 5, limitedBy: 'count' });
  });

  it('reads the selected cave before any other', async () => {
    const scene = new FakeScene();
    await started(scene, answer([mesh('a'), mesh('b'), mesh('c')]), {
      limits: { ...limits, maxCaves: 2 },
      selected: 'c',
    });

    expect(scene.readCaves()).toEqual(['c', 'a']);
  });

  it('settles a change of selection from the answer it has, without asking or re-reading', async () => {
    const scene = new FakeScene();
    const { loader } = await started(scene, answer([mesh('a'), mesh('b'), mesh('c')]), {
      limits: { ...limits, maxCaves: 2 },
    });
    expect(scene.heldCaves()).toEqual(['a', 'b']);

    loader.setSelectedCave('c');

    // The cave pushed out of the budget is released; the one that stays is neither removed nor
    // read a second time; and the view did not move, so nothing was asked of the server.
    expect(scene.removals).toEqual([surveyMeshInViewModelId('b')]);
    expect(scene.readCaves()).toEqual(['a', 'b', 'c']);
    expect(scene.heldCaves()).toEqual(['a', 'c']);
    expect(asked).toHaveLength(1);
    expect(loader.getState()).toMatchObject({ shown: 2, inView: 3, leftOut: 1 });
  });

  it('removes a mesh that left the view and does not read again one that stayed', async () => {
    vi.useFakeTimers();
    const scene = new FakeScene();
    const { loader } = await startedWithFakeTimers(scene, answer([mesh('a'), mesh('b')]));
    scene.reads.forEach((read) => read.land());
    await vi.advanceTimersByTimeAsync(0);

    // The same cave comes back under a freshly signed address, as it does in every answer. That
    // is still the same mesh, and reading it again on every pan would be the whole budget spent
    // on files already on the graphics card.
    respond = () =>
      Promise.resolve(answer([mesh('b', { meshUrl: '/files/b.glb?token=second' }), mesh('c')]));
    scene.move();
    await vi.advanceTimersByTimeAsync(250);

    expect(scene.removals).toEqual([surveyMeshInViewModelId('a')]);
    expect(scene.readCaves()).toEqual(['a', 'b', 'c']);
    expect(scene.moves).toEqual([]);
    expect(scene.heldCaves()).toEqual(['b', 'c']);
    expect(loader.getState()).toMatchObject({ shown: 2, loading: 1, inView: 2, leftOut: 0 });
  });

  it('replaces the mesh held for a cave when the cave is now drawn by a different model', async () => {
    vi.useFakeTimers();
    const scene = new FakeScene();
    await startedWithFakeTimers(scene, answer([mesh('a')]));

    respond = () =>
      Promise.resolve(
        answer([mesh('a', { surveyModelId: 'model-a-2', meshUrl: '/files/a2.glb?token=x' })]),
      );
    scene.move();
    await vi.advanceTimersByTimeAsync(250);

    expect(scene.removals).toEqual([surveyMeshInViewModelId('a')]);
    expect(scene.reads.map((read) => read.options.url)).toEqual([
      '/files/a.glb?token=first',
      '/files/a2.glb?token=x',
    ]);
  });

  it('asks once for a camera that moved several times before coming to rest', async () => {
    vi.useFakeTimers();
    const scene = new FakeScene();
    await startedWithFakeTimers(scene, answer([mesh('a')]));

    scene.move();
    await vi.advanceTimersByTimeAsync(100);
    scene.move();
    await vi.advanceTimersByTimeAsync(100);
    scene.move();
    await vi.advanceTimersByTimeAsync(249);
    expect(asked).toHaveLength(1);

    await vi.advanceTimersByTimeAsync(1);
    expect(asked).toHaveLength(2);
  });

  it('releases everything it holds when it is switched off, and says so', async () => {
    const scene = new FakeScene();
    const { loader } = await started(scene, answer([mesh('a'), mesh('b')]));
    scene.reads[0].land();
    await settle();

    loader.setActive(false);

    // Both, including the one still being read: a read left to land after the walls were turned
    // off would put a mesh into a scene that had just been told to hold none.
    expect(scene.removals).toEqual([surveyMeshInViewModelId('a'), surveyMeshInViewModelId('b')]);
    expect(scene.heldCaves()).toEqual([]);
    expect(loader.getState()).toMatchObject({
      status: 'off',
      shown: 0,
      loading: 0,
      inView: 0,
      leftOut: 0,
      heldBytes: 0,
    });

    // And a camera that moves while it is off asks for nothing.
    vi.useFakeTimers();
    scene.move();
    await vi.advanceTimersByTimeAsync(500);
    expect(asked).toHaveLength(1);
  });

  it('drops an answer that arrives after it was switched off', async () => {
    const scene = new FakeScene();
    let deliver: (value: CaveMeshesInView) => void = () => {};
    respond = () =>
      new Promise<CaveMeshesInView>((resolve) => {
        deliver = resolve;
      });
    const loader = attachSurveyMeshesInView3d(scene, () => undefined);
    loader.setLimits(limits);
    loader.setActive(true);
    await settle();

    loader.setActive(false);
    deliver(answer([mesh('a')]));
    await settle();

    expect(scene.reads).toEqual([]);
    expect(loader.getState().status).toBe('off');
  });

  it('draws the answer to the newest question when an older one arrives late', async () => {
    vi.useFakeTimers();
    const scene = new FakeScene();
    const pending: ((value: CaveMeshesInView) => void)[] = [];
    respond = () =>
      new Promise<CaveMeshesInView>((resolve) => {
        pending.push(resolve);
      });
    const loader = attachSurveyMeshesInView3d(scene, () => undefined);
    loader.setLimits(limits);
    loader.setActive(true);
    await vi.advanceTimersByTimeAsync(0);
    scene.move();
    await vi.advanceTimersByTimeAsync(250);
    expect(pending).toHaveLength(2);

    pending[1](answer([mesh('new')]));
    await vi.advanceTimersByTimeAsync(0);
    pending[0](answer([mesh('old')]));
    await vi.advanceTimersByTimeAsync(0);

    expect(scene.heldCaves()).toEqual(['new']);
  });

  it('counts walls as they land, and counts apart the ones that could not be read', async () => {
    const scene = new FakeScene();
    const { loader } = await started(
      scene,
      answer([mesh('a', { sizeBytes: 100 }), mesh('b', { sizeBytes: 200 }), mesh('c', { sizeBytes: 400 })]),
    );
    const seen: number[] = [];
    loader.subscribe((state) => seen.push(state.loading));
    expect(loader.getState()).toMatchObject({ shown: 3, loading: 3, failed: 0, heldBytes: 700 });

    scene.reads[0].land();
    await settle();
    expect(loader.getState()).toMatchObject({ shown: 3, loading: 2, failed: 0 });

    scene.reads[1].refuse();
    await settle();
    // An unreadable mesh is not shown, holds nothing, and was not left out for the budget either:
    // it has a number of its own, so the sentence built from these can say what happened to it.
    expect(loader.getState()).toMatchObject({
      shown: 2,
      loading: 1,
      failed: 1,
      inView: 3,
      leftOut: 0,
      heldBytes: 500,
    });
    expect(seen).toEqual([2, 1]);
  });

  it('does not ask again for a mesh that could not be read while the cave stays in view', async () => {
    vi.useFakeTimers();
    const scene = new FakeScene();
    await startedWithFakeTimers(scene, answer([mesh('a')]));
    scene.reads[0].refuse();
    await vi.advanceTimersByTimeAsync(0);

    scene.move();
    await vi.advanceTimersByTimeAsync(250);

    expect(asked).toHaveLength(2);
    expect(scene.reads).toHaveLength(1);
  });

  it('keeps what is drawn when a later listing fails, and says the first one failed when it does', async () => {
    vi.useFakeTimers();
    const scene = new FakeScene();
    const { loader } = await startedWithFakeTimers(scene, answer([mesh('a')]));
    scene.reads[0].land();
    await vi.advanceTimersByTimeAsync(0);

    respond = () => Promise.reject(new Error('offline'));
    scene.move();
    await vi.advanceTimersByTimeAsync(250);

    expect(scene.removals).toEqual([]);
    expect(loader.getState()).toMatchObject({ status: 'ready', listing: false, shown: 1, inView: 1 });

    // With nothing drawn to keep, the failure is the whole of what there is to say.
    const empty = new FakeScene();
    const second = attachSurveyMeshesInView3d(empty, () => undefined);
    second.setLimits(limits);
    second.setActive(true);
    await vi.advanceTimersByTimeAsync(0);
    expect(second.getState()).toMatchObject({ status: 'failed', shown: 0 });
  });

  it('says a listing is in the air, so chrome can wait for the view to finish filling in', async () => {
    const scene = new FakeScene();
    let deliver: (value: CaveMeshesInView) => void = () => {};
    respond = () =>
      new Promise<CaveMeshesInView>((resolve) => {
        deliver = resolve;
      });
    const loader = attachSurveyMeshesInView3d(scene, () => undefined);
    loader.setLimits(limits);
    loader.setActive(true);
    await settle();
    expect(loader.getState()).toMatchObject({ status: 'looking', listing: true });

    deliver(answer([]));
    await settle();
    expect(loader.getState()).toMatchObject({ status: 'ready', listing: false, inView: 0 });
  });

  it('hangs each cave from its own survey top, not from one top for the whole view', async () => {
    const scene = new FakeScene();
    await started(scene, answer([mesh('a'), mesh('b'), mesh('c')]), {
      tops: { a: 700, b: 1320 },
    });

    expect(scene.reads.map((read) => read.options.anchor)).toEqual([
      { longitude: 25.44, latitude: 45.53, altitudeM: 500, surveyTopAltitudeM: 700 },
      { longitude: 25.44, latitude: 45.53, altitudeM: 500, surveyTopAltitudeM: 1320 },
      // Nothing has said where this cave's top is, so its own origin is what hangs.
      { longitude: 25.44, latitude: 45.53, altitudeM: 500, surveyTopAltitudeM: undefined },
    ]);
  });

  it('moves a mesh when its cave’s top turns up later, and keeps a top that stops being reported', async () => {
    const scene = new FakeScene();
    const { loader, tops } = await started(scene, answer([mesh('a'), mesh('b')]), {
      tops: { a: 700 },
    });
    scene.reads.forEach((read) => read.land());
    await settle();

    // The survey lines of cave b arrive after its walls did.
    tops.b = 900;
    loader.refreshSurveyTops();

    expect(scene.moves).toHaveLength(1);
    expect(scene.moves[0].id).toBe(surveyMeshInViewModelId('b'));
    expect(scene.moves[0].options.url).toBe('/files/b.glb?token=first');
    expect(scene.moves[0].options.anchor.surveyTopAltitudeM).toBe(900);
    expect(scene.reads).toHaveLength(2);

    // Cave a is panned to the edge and stops being reported. That is not news about where it is.
    delete tops.a;
    loader.refreshSurveyTops();
    expect(scene.moves).toHaveLength(1);
    expect(scene.held.get(surveyMeshInViewModelId('a'))?.anchor.surveyTopAltitudeM).toBe(700);
  });

  it('moves what is loaded when the ground gains relief, and loads later meshes against it', async () => {
    vi.useFakeTimers();
    const scene = new FakeScene();
    const { loader } = await startedWithFakeTimers(scene, answer([mesh('a')]));
    const withRelief = { absolute: true, offsetM: 43 };

    loader.setAltitudePlacement(withRelief);
    loader.setAltitudePlacement(withRelief);
    expect(scene.placements).toEqual([withRelief]);
    expect(asked).toHaveLength(1);

    respond = () => Promise.resolve(answer([mesh('a'), mesh('b')]));
    scene.move();
    await vi.advanceTimersByTimeAsync(250);
    expect(scene.reads.at(-1)?.options.placement).toEqual(withRelief);
  });

  it('releases everything when it is taken down', async () => {
    const scene = new FakeScene();
    const { loader } = await started(scene, answer([mesh('a'), mesh('b')]));

    loader.detach();

    expect(scene.heldCaves()).toEqual([]);
    expect(scene.viewListeners.size).toBe(0);
  });
});

describe('limits that change while the walls are on', () => {
  /** A listing that never answers, so what a test sees next happened without one. */
  const neverAnswers = () => new Promise<CaveMeshesInView>(() => {});

  it('asks for as many caves as the limit in force allows, because the answer stops at that count', async () => {
    const scene = new FakeScene();
    await started(scene, answer([mesh('a')]), { limits: { ...limits, maxCaves: 30 } });

    expect(askedCaves).toEqual([30]);
  });

  it('releases what a lowered byte budget no longer covers at once, before the view is asked about again', async () => {
    const scene = new FakeScene();
    const { loader } = await started(
      scene,
      answer([mesh('a', { sizeBytes: 30 * MB }), mesh('b', { sizeBytes: 30 * MB })]),
    );
    scene.reads.forEach((read) => read.land());
    await settle();
    expect(loader.getState()).toMatchObject({ shown: 2, heldBytes: 60 * MB });

    respond = neverAnswers;
    const lowered = { ...limits, maxBytes: 40 * MB };
    loader.setLimits(lowered);

    // Somebody lowers a budget to get memory back. It is back before this line, not after a
    // round trip — and the mesh that still fits was neither removed nor read again.
    expect(scene.removals).toEqual([surveyMeshInViewModelId('b')]);
    expect(scene.heldCaves()).toEqual(['a']);
    expect(scene.reads).toHaveLength(2);
    expect(loader.getState()).toMatchObject({
      shown: 1,
      inView: 2,
      leftOut: 1,
      limitedBy: 'bytes',
      heldBytes: 30 * MB,
      limits: lowered,
    });
    // The view is asked about again all the same, without the camera having moved.
    expect(asked).toHaveLength(2);
  });

  it('releases the farthest caves at once when the count is lowered, and asks for the smaller count', async () => {
    const scene = new FakeScene();
    const { loader } = await started(scene, answer([mesh('a'), mesh('b'), mesh('c')]));
    expect(scene.heldCaves()).toEqual(['a', 'b', 'c']);

    respond = neverAnswers;
    loader.setLimits({ ...limits, maxCaves: 1 });

    expect(scene.removals).toEqual([surveyMeshInViewModelId('b'), surveyMeshInViewModelId('c')]);
    expect(scene.heldCaves()).toEqual(['a']);
    expect(loader.getState()).toMatchObject({ shown: 1, inView: 3, leftOut: 2, limitedBy: 'count' });
    expect(askedCaves).toEqual([12, 1]);
  });

  it('takes up a raised budget without waiting for the camera: from the answer it holds, then from a fresh one', async () => {
    const scene = new FakeScene();
    const described = [
      mesh('a', { sizeBytes: 30 * MB }),
      mesh('b', { sizeBytes: 30 * MB }),
      mesh('c', { sizeBytes: 30 * MB }),
    ];
    // Five caves in view, three described: the server stopped at the count it was asked for.
    const { loader } = await started(scene, answer(described, 5), {
      limits: { ...limits, maxCaves: 3 },
    });
    expect(scene.readCaves()).toEqual(['a', 'b']);
    expect(loader.getState()).toMatchObject({ shown: 2, limitedBy: 'bytes' });

    let deliver: (value: CaveMeshesInView) => void = () => {};
    respond = () =>
      new Promise<CaveMeshesInView>((resolve) => {
        deliver = resolve;
      });
    loader.setLimits({ minZoom: 14, maxCaves: 5, maxBytes: 256 * MB });

    // The third cave had been described and only the old budget kept it out, so it starts now.
    expect(scene.readCaves()).toEqual(['a', 'b', 'c']);
    // The other two were only ever counted; describing them takes asking for five.
    expect(askedCaves).toEqual([3, 5]);
    expect(loader.getState()).toMatchObject({ shown: 3, inView: 5, leftOut: 2, limitedBy: 'count' });

    deliver(answer([...described, mesh('d'), mesh('e')], 5));
    await settle();

    expect(scene.readCaves()).toEqual(['a', 'b', 'c', 'd', 'e']);
    expect(scene.removals).toEqual([]);
    expect(loader.getState()).toMatchObject({ shown: 5, inView: 5, leftOut: 0 });
    expect(loader.getState().limitedBy).toBeUndefined();
  });

  it('lets everything go when the floor is raised above the view, and asks again when it is lowered back', async () => {
    const scene = new FakeScene();
    const first = answer([
      mesh('a', { sizeBytes: 30 * MB }),
      mesh('b', { sizeBytes: 30 * MB }),
      mesh('c', { sizeBytes: 30 * MB }),
    ]);
    const { loader } = await started(scene, first);
    expect(scene.heldCaves()).toEqual(['a', 'b']);

    // The camera is at 15. The byte budget is raised in the same breath, and the cave it would
    // now cover is not started: a read begun here would be abandoned by the very next line.
    loader.setLimits({ minZoom: 17, maxCaves: 12, maxBytes: 256 * MB });

    expect(scene.readCaves()).toEqual(['a', 'b']);
    expect(scene.heldCaves()).toEqual([]);
    expect(asked).toHaveLength(1);
    expect(loader.getState()).toMatchObject({ status: 'zoomIn', shown: 0, inView: 0, heldBytes: 0 });

    loader.setLimits({ minZoom: 12, maxCaves: 12, maxBytes: 256 * MB });
    await settle();

    expect(asked).toHaveLength(2);
    expect(scene.heldCaves()).toEqual(['a', 'b', 'c']);
    expect(loader.getState().status).toBe('ready');
  });

  it('asks nothing when it is told the limits it already has', async () => {
    const scene = new FakeScene();
    const { loader } = await started(scene, answer([mesh('a')]));

    loader.setLimits({ ...limits });

    expect(asked).toHaveLength(1);
    expect(scene.removals).toEqual([]);
  });

  it('holds new limits without asking while the walls are off, and works within them once on', async () => {
    const scene = new FakeScene();
    const loader = attachSurveyMeshesInView3d(scene, () => undefined);
    loader.setLimits(limits);
    loader.setLimits({ ...limits, maxCaves: 1 });
    await settle();
    expect(asked).toEqual([]);

    respond = () => Promise.resolve(answer([mesh('a')], 2));
    loader.setActive(true);
    await settle();

    expect(askedCaves).toEqual([1]);
    expect(scene.heldCaves()).toEqual(['a']);
  });
});

describe('meshesInViewLimitsInForce', () => {
  const published = {
    meshesInViewMinZoom: 14,
    meshesInViewMaxCaves: 12,
    meshesInViewMaxBytes: 64 * MB,
    meshesInViewMaxCavesLimit: 60,
    meshesInViewMaxBytesLimit: 512 * MB,
  };

  it('is the installation’s three defaults for a person who has set nothing', () => {
    const defaults = { minZoom: 14, maxCaves: 12, maxBytes: 64 * MB };

    expect(meshesInViewLimitsInForce(published)).toEqual(defaults);
    expect(meshesInViewLimitsInForce(published, {})).toEqual(defaults);
    expect(
      meshesInViewLimitsInForce(published, { minZoom: undefined, maxCaves: undefined, maxBytes: undefined }),
    ).toEqual(defaults);
  });

  it('takes a personal value over the default, each of the three on its own', () => {
    expect(meshesInViewLimitsInForce(published, { maxCaves: 30 })).toEqual({
      minZoom: 14,
      maxCaves: 30,
      maxBytes: 64 * MB,
    });
    expect(meshesInViewLimitsInForce(published, { minZoom: 12, maxBytes: 256 * MB })).toEqual({
      minZoom: 12,
      maxCaves: 12,
      maxBytes: 256 * MB,
    });
    // Less than the installation offers is a person's to choose as well: a phone wants it.
    expect(
      meshesInViewLimitsInForce(published, { minZoom: 17, maxCaves: 3, maxBytes: 8 * MB }),
    ).toEqual({ minZoom: 17, maxCaves: 3, maxBytes: 8 * MB });
  });

  it('holds a personal value to the installation’s ceilings', () => {
    expect(
      meshesInViewLimitsInForce(published, { maxCaves: 500, maxBytes: 4096 * MB }),
    ).toMatchObject({ maxCaves: 60, maxBytes: 512 * MB });

    // A ceiling lowered after the value was stored is the one in force.
    expect(
      meshesInViewLimitsInForce(
        { ...published, meshesInViewMaxCavesLimit: 20, meshesInViewMaxBytesLimit: 100 * MB },
        { maxCaves: 30, maxBytes: 256 * MB },
      ),
    ).toMatchObject({ maxCaves: 20, maxBytes: 100 * MB });
  });

  it('holds a personal value above the floors: one cave, one megabyte, a zoom a map can be at', () => {
    expect(meshesInViewLimitsInForce(published, { minZoom: 0, maxCaves: 0, maxBytes: 1 })).toEqual({
      minZoom: 1,
      maxCaves: 1,
      maxBytes: MB,
    });
    expect(meshesInViewLimitsInForce(published, { minZoom: -3, maxCaves: -1 })).toMatchObject({
      minZoom: 1,
      maxCaves: 1,
    });
    expect(meshesInViewLimitsInForce(published, { minZoom: 40 }).minZoom).toBe(22);
  });

  it('lets a ceiling set below a floor win, because the operator keeps the last word', () => {
    expect(
      meshesInViewLimitsInForce(
        { ...published, meshesInViewMaxBytesLimit: 4096 },
        { maxBytes: 8 * MB },
      ).maxBytes,
    ).toBe(4096);
  });

  it('takes the installation’s own numbers as published, even outside what a person may type', () => {
    // The floors and the zoom range bound what somebody types into a settings page. An
    // operator's default is not that: a floor of zero or a budget under a megabyte is theirs to set.
    expect(
      meshesInViewLimitsInForce({
        ...published,
        meshesInViewMinZoom: 0,
        meshesInViewMaxBytes: 1000,
      }),
    ).toEqual({ minZoom: 0, maxCaves: 12, maxBytes: 1000 });
    expect(meshesInViewLimitsInForce({ ...published, meshesInViewMinZoom: 25 }).minZoom).toBe(25);
  });

  it('reads a stored value that is not a number as not set, and a fraction as the nearest whole number', () => {
    expect(
      meshesInViewLimitsInForce(published, { minZoom: Number.NaN, maxCaves: Number.POSITIVE_INFINITY }),
    ).toMatchObject({ minZoom: 14, maxCaves: 12 });
    // The count travels to the server as a whole number, which is the only kind it reads.
    expect(meshesInViewLimitsInForce(published, { maxCaves: 12.6 }).maxCaves).toBe(13);
  });

  it('keeps to the defaults when the installation published no ceilings to hold a personal value to', () => {
    // What a server that predates the ceilings answers with. A limit computed from a missing
    // ceiling would be NaN, and every comparison against NaN is false: the budget would be off.
    const withoutCeilings = {
      meshesInViewMinZoom: 14,
      meshesInViewMaxCaves: 12,
      meshesInViewMaxBytes: 64 * MB,
    } as typeof published;

    expect(
      meshesInViewLimitsInForce(withoutCeilings, { minZoom: 12, maxCaves: 500, maxBytes: 4096 * MB }),
    ).toEqual({ minZoom: 12, maxCaves: 12, maxBytes: 64 * MB });
  });
});

/** The same start as `started`, for a test that has already frozen the clock. */
async function startedWithFakeTimers(scene: FakeScene, first: CaveMeshesInView) {
  respond = () => Promise.resolve(first);
  const loader = attachSurveyMeshesInView3d(scene, () => undefined);
  loader.setLimits(limits);
  loader.setActive(true);
  await vi.advanceTimersByTimeAsync(0);
  return { loader };
}
