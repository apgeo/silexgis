// SPDX-License-Identifier: AGPL-3.0-or-later
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { SurveyModelInfo } from '../api/hooks.ts';
import type { Scene3DModelOptions } from './scene3dEngine.ts';

// The listing is the only thing this loader fetches, and every decision it makes is about what
// came back — so it is stubbed, and every other module involved is the real one.
const listed: SurveyModelInfo[][] = [];
const askedFor: string[] = [];
let refuseListing = false;

vi.mock('../api/hooks.ts', () => ({
  fetchSurveyModels: (caveId: string) => {
    askedFor.push(caveId);
    if (refuseListing) {
      return Promise.reject(new Error('no'));
    }
    return Promise.resolve(listed.shift() ?? []);
  },
}));

const { ANCHORED_TO_SURFACE } = await import('./altitude3d.ts');
const { attachSurveyMesh3d, drawableSurveyMesh, SURVEY_MESH_LAYER_ID } = await import(
  './surveyMesh3d.ts'
);

/** One `loadModel` the loader made, held open until the test lands or refuses it. */
interface ModelRead {
  id: string;
  options: Scene3DModelOptions;
  land: () => void;
  refuse: () => void;
}

/**
 * A scene that records what it was asked to do with models and holds every read open.
 *
 * A wall mesh takes long enough to arrive for the selection to move under it, so a read that
 * lands by itself would hide exactly the races worth testing.
 */
class FakeScene {
  readonly reads: ModelRead[] = [];
  readonly removals: string[] = [];
  placements: unknown[] = [];

  loadModel(id: string, options: Scene3DModelOptions): Promise<void> {
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
  }

  setModelPlacement(placement: unknown): void {
    this.placements.push(placement);
  }

  /** The read still open for a URL, which is how a test lands one out of several. */
  readOf(url: string): ModelRead {
    const read = this.reads.find((candidate) => candidate.options.url === url);
    if (!read) {
      throw new Error(`no read is open for ${url}`);
    }
    return read;
  }
}

function model(overrides: Partial<SurveyModelInfo> = {}): SurveyModelInfo {
  return {
    id: 'model-1',
    caveId: 'cave-1',
    name: 'Coiba Mare',
    format: 'stl',
    fileId: 'file-1',
    description: null,
    surveyedAt: null,
    modelUrl: '/files/source',
    status: 'ready',
    processingError: null,
    meshUrl: '/files/mesh-1',
    anchorLongitude: 22.8,
    anchorLatitude: 46.5,
    anchorHeightM: 1100,
    triangleCount: 4200,
    sourcePrecisionLost: false,
    createdAt: '2026-08-01T00:00:00Z',
    updatedAt: '2026-08-01T00:00:00Z',
    ...overrides,
  } as SurveyModelInfo;
}

/** Lets every promise already resolved settle, which is how a fetch lands in these tests. */
const settle = () => new Promise<void>((resolve) => setTimeout(resolve, 0));

beforeEach(() => {
  listed.length = 0;
  askedFor.length = 0;
  refuseListing = false;
});

describe('drawableSurveyMesh', () => {
  it('takes only a model that has both a mesh and somewhere to put it', () => {
    // A file declared in local coordinates is given its anchor at upload, before anything has been
    // converted, so an anchor on its own is not evidence that there is anything to draw.
    const anchoredButUnconverted = model({ id: 'a', meshUrl: null, status: 'pending' });
    const drawable = model({ id: 'b' });

    expect(drawableSurveyMesh([anchoredButUnconverted, drawable])?.id).toBe('b');
    expect(drawableSurveyMesh([anchoredButUnconverted])).toBeUndefined();
    expect(drawableSurveyMesh([model({ anchorHeightM: null })])).toBeUndefined();
    expect(drawableSurveyMesh([])).toBeUndefined();
  });

  it('takes the most recently uploaded mesh when a cave holds several', () => {
    const older = model({ id: 'older', createdAt: '2026-01-01T00:00:00Z' });
    const newer = model({ id: 'newer', createdAt: '2026-07-01T00:00:00Z' });

    expect(drawableSurveyMesh([newer, older])?.id).toBe('newer');
    expect(drawableSurveyMesh([older, newer])?.id).toBe('newer');
  });

  it('ignores the line-plot models a cave also holds', () => {
    // A .lox or .3d upload is ready the moment it lands and has no mesh at all; the embedded
    // survey viewer reads those, and the scene must not try to.
    const linePlot = model({ id: 'plot', format: 'lox', meshUrl: null, anchorLongitude: null });

    expect(drawableSurveyMesh([linePlot])).toBeUndefined();
  });
});

describe('attachSurveyMesh3d', () => {
  it('loads nothing at all until a cave is selected', async () => {
    const scene = new FakeScene();
    const mesh = attachSurveyMesh3d(scene);

    await settle();

    // Every other overlay in this scene fetches what the camera can see. This one costs tens of
    // megabytes of graphics memory for one cave, so it waits to be asked.
    expect(askedFor).toEqual([]);
    expect(scene.reads).toEqual([]);
    expect(mesh.getState().status).toBe('off');
  });

  it('loads the selected cave and says how big the mesh is before it arrives', async () => {
    const scene = new FakeScene();
    listed.push([model({ triangleCount: 999_999 })]);
    const mesh = attachSurveyMesh3d(scene);

    mesh.setCave('cave-1', 1150);
    await settle();

    expect(askedFor).toEqual(['cave-1']);
    expect(mesh.getState()).toMatchObject({
      status: 'loading',
      name: 'Coiba Mare',
      triangleCount: 999_999,
    });
    const read = scene.readOf('/files/mesh-1');
    expect(read.id).toBe(SURVEY_MESH_LAYER_ID);
    // The cave's own top, not the mesh's zero plane: the walls and the survey lines of one cave
    // hang from one number or they separate vertically by the difference between two.
    expect(read.options.anchor).toEqual({
      longitude: 22.8,
      latitude: 46.5,
      altitudeM: 1100,
      surveyTopAltitudeM: 1150,
    });
    read.land();
    await settle();
    expect(mesh.getState().status).toBe('drawn');
  });

  it('releases the mesh when the selection moves to a cave that has none', async () => {
    const scene = new FakeScene();
    listed.push([model()], []);
    const mesh = attachSurveyMesh3d(scene);

    mesh.setCave('cave-1');
    await settle();
    scene.readOf('/files/mesh-1').land();
    await settle();
    expect(mesh.getState().status).toBe('drawn');

    mesh.setCave('cave-2');
    await settle();

    // The release is a real unload — the same call that frees the graphics memory — and the second
    // cave is reported as having nothing rather than leaving the first cave's walls on screen.
    expect(scene.removals).toContain(SURVEY_MESH_LAYER_ID);
    expect(mesh.getState().status).toBe('unavailable');
    expect(mesh.getState().caveId).toBe('cave-2');
  });

  it('draws the cave that is selected now, not the one whose listing came back last', async () => {
    const scene = new FakeScene();
    listed.push([model({ id: 'first', meshUrl: '/files/mesh-first' })]);
    listed.push([model({ id: 'second', meshUrl: '/files/mesh-second' })]);
    const mesh = attachSurveyMesh3d(scene);

    // Both selections happen before either listing lands, which is the ordinary case when a
    // viewer clicks through a list of caves.
    mesh.setCave('cave-1');
    mesh.setCave('cave-2');
    await settle();

    expect(askedFor).toEqual(['cave-1', 'cave-2']);
    expect(scene.reads.map((read) => read.options.url)).toEqual(['/files/mesh-second']);
  });

  it('moves a drawn mesh when the ground gains relief, and never reads it again', async () => {
    const scene = new FakeScene();
    listed.push([model()]);
    const mesh = attachSurveyMesh3d(scene);

    mesh.setCave('cave-1', 1150);
    await settle();
    scene.readOf('/files/mesh-1').land();
    await settle();

    mesh.setAltitudePlacement({ absolute: true, offsetM: 43 });

    // A survey mesh is up to a hundred megabytes; the height it is drawn at is arithmetic on its
    // anchor, so the answer to the ground changing is a matrix and not a second download.
    expect(scene.placements).toEqual([{ absolute: true, offsetM: 43 }]);
    expect(askedFor).toEqual(['cave-1']);
    expect(scene.reads).toHaveLength(1);
  });

  it('moves a drawn mesh when the cave top arrives late, without fetching the cave again', async () => {
    const scene = new FakeScene();
    listed.push([model()]);
    const mesh = attachSurveyMesh3d(scene);

    // The top is read from the camera-driven survey loader, which can answer after the walls are
    // already on screen.
    mesh.setCave('cave-1', undefined);
    await settle();
    scene.readOf('/files/mesh-1').land();
    await settle();

    mesh.setCave('cave-1', 1150);
    await settle();

    expect(askedFor).toEqual(['cave-1']);
    expect(scene.reads).toHaveLength(2);
    expect(scene.reads[1].options.url).toBe('/files/mesh-1');
    expect(scene.reads[1].options.anchor.surveyTopAltitudeM).toBe(1150);
  });

  it('keeps a drawn mesh at the top it was given when a later answer omits that cave', async () => {
    const scene = new FakeScene();
    listed.push([model()]);
    const mesh = attachSurveyMesh3d(scene);

    mesh.setCave('cave-1', 1150);
    await settle();
    scene.readOf('/files/mesh-1').land();
    await settle();

    // The survey loader answers for what is inside the view, so it stops reporting this cave the
    // moment it is panned off the screen, the zoom drops to flat outlines carrying no altitudes,
    // or the lines are switched off. None of that says the cave's top changed, and a mesh that
    // took it as an answer would jump by the difference between the cave's top and its own zero
    // plane — and stop agreeing with the survey lines drawn inside it.
    mesh.setCave('cave-1', undefined);
    await settle();

    expect(scene.reads).toHaveLength(1);
    expect(scene.reads[0].options.anchor.surveyTopAltitudeM).toBe(1150);
    expect(mesh.getState().status).toBe('drawn');
  });

  it('forgets the top of the cave it was holding when the selection moves on', async () => {
    const scene = new FakeScene();
    listed.push([model()], [model({ id: 'model-2', meshUrl: '/files/mesh-2' })]);
    const mesh = attachSurveyMesh3d(scene);

    mesh.setCave('cave-1', 1150);
    await settle();
    scene.readOf('/files/mesh-1').land();
    await settle();

    // Stickiness belongs to one cave. Carrying the first cave's top onto the second would hang
    // the new walls from a height that has nothing to do with them.
    mesh.setCave('cave-2', undefined);
    await settle();

    expect(scene.readOf('/files/mesh-2').options.anchor.surveyTopAltitudeM).toBeUndefined();
  });

  it('unloads when the layer is turned off and loads again when it is turned back on', async () => {
    const scene = new FakeScene();
    listed.push([model()], [model()]);
    const mesh = attachSurveyMesh3d(scene);

    mesh.setCave('cave-1');
    await settle();
    scene.readOf('/files/mesh-1').land();
    await settle();
    expect(mesh.getState().status).toBe('drawn');

    mesh.setVisible(false);
    await settle();

    // Hiding it would keep every byte of the geometry on the graphics card, which is the whole
    // thing a viewer turns this off to get back.
    expect(scene.removals.filter((id) => id === SURVEY_MESH_LAYER_ID)).not.toHaveLength(0);
    expect(mesh.getState().status).toBe('off');

    mesh.setVisible(true);
    await settle();
    expect(scene.readOf('/files/mesh-1')).toBeDefined();
  });

  it('says a conversion has not finished rather than that the cave has no walls', async () => {
    const scene = new FakeScene();
    listed.push([model({ meshUrl: null, status: 'processing', anchorLongitude: null })]);
    const mesh = attachSurveyMesh3d(scene);

    mesh.setCave('cave-1');
    await settle();

    expect(mesh.getState()).toMatchObject({ status: 'converting', name: 'Coiba Mare' });
    expect(scene.reads).toEqual([]);
  });

  it("carries the conversion's own reason when it failed", async () => {
    const scene = new FakeScene();
    listed.push([
      model({ meshUrl: null, status: 'failed', processingError: 'This is not a binary STL.' }),
    ]);
    const mesh = attachSurveyMesh3d(scene);

    mesh.setCave('cave-1');
    await settle();

    expect(mesh.getState()).toMatchObject({
      status: 'failed',
      message: 'This is not a binary STL.',
    });
  });

  it('says so when the mesh itself could not be read', async () => {
    const scene = new FakeScene();
    listed.push([model()]);
    const mesh = attachSurveyMesh3d(scene);

    mesh.setCave('cave-1');
    await settle();
    scene.readOf('/files/mesh-1').refuse();
    await settle();

    // A scene that quietly shows nothing is indistinguishable from a cave with no walls.
    expect(mesh.getState().status).toBe('failed');
  });

  it('does not claim a cave has no walls when the listing could not be fetched', async () => {
    const scene = new FakeScene();
    refuseListing = true;
    const mesh = attachSurveyMesh3d(scene);

    mesh.setCave('cave-1');
    await settle();

    expect(mesh.getState().status).toBe('failed');
    expect(mesh.getState().status).not.toBe('unavailable');
  });

  it('passes the placement in force at the moment of loading, so nothing jumps on arrival', async () => {
    const scene = new FakeScene();
    listed.push([model()]);
    const mesh = attachSurveyMesh3d(scene);

    mesh.setAltitudePlacement({ absolute: true, offsetM: 43 });
    mesh.setCave('cave-1', 1150);
    await settle();

    expect(scene.readOf('/files/mesh-1').options.placement).toEqual({
      absolute: true,
      offsetM: 43,
    });
  });

  it('releases the mesh when it is taken down, and drops what is still in the air', async () => {
    const scene = new FakeScene();
    listed.push([model()]);
    const mesh = attachSurveyMesh3d(scene);

    mesh.setCave('cave-1');
    await settle();
    const read = scene.readOf('/files/mesh-1');

    mesh.detach();
    read.land();
    await settle();

    // The scene outlives this loader — it is shared and reference counted — so what it was asked
    // to hold has to be handed back.
    expect(scene.removals).toContain(SURVEY_MESH_LAYER_ID);
    expect(mesh.getState().status).not.toBe('drawn');
  });

  it('starts from the bare-ellipsoid rule until it is told otherwise', async () => {
    const scene = new FakeScene();
    listed.push([model()]);
    const mesh = attachSurveyMesh3d(scene);

    mesh.setCave('cave-1');
    await settle();

    expect(scene.readOf('/files/mesh-1').options.placement).toEqual(ANCHORED_TO_SURFACE);
  });
});
