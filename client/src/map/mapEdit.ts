// SPDX-License-Identifier: AGPL-3.0-or-later
import type Map from 'ol/Map';
import Feature, { type FeatureLike } from 'ol/Feature';
import GeoJSON from 'ol/format/GeoJSON';
import type Geometry from 'ol/geom/Geometry';
import LineString from 'ol/geom/LineString';
import Point from 'ol/geom/Point';
import Polygon from 'ol/geom/Polygon';
import { Draw, Modify, Snap, Translate } from 'ol/interaction';
import { fromLonLat, toLonLat } from 'ol/proj';
import type VectorSource from 'ol/source/Vector';
import { fetchFeatureVersion } from '../api/hooks.ts';
import { getSurfaceFeatureSource, PENDING_RESHAPE, SAVE_REFUSED } from './featureLayer.ts';
import { coarsePointer } from './pointer.ts';

// All OL edit interactions live here; React components only dispatch intents and
// subscribe to plain-data snapshots via the listener. (Measuring is not an edit
// concern anymore — the toolbar hosts react-geo's measure buttons directly.)

export type EditMode = 'none' | 'draw' | 'modify' | 'translate' | 'add-cave' | 'add-entrance';
export type DrawShape = 'Point' | 'LineString' | 'Polygon';
export type PlacementMode = 'add-cave' | 'add-entrance';

export interface EditState {
  mode: EditMode;
  snap: boolean;
  canUndo: boolean;
  canRedo: boolean;
  dirty: number; // created + geometry-modified features not yet saved
  /** Features drawn here and not saved yet — always this person's to reshape. */
  created: number;
  /** A multi-vertex sketch is in progress (between the first click and the finish). */
  sketchActive: boolean;
  /** Shape/type the draw tool is armed with (kept while mode is 'draw'). */
  drawShape?: DrawShape;
  drawTypeId?: number;
}

/**
 * Which saved features the reshaping tools (modify, move) may take hold of: every one, but for one
 * whose own rights withhold it; one alone; or none. Features drawn here and not saved yet are
 * always included.
 */
export type ReshapeScope = { kind: 'every'; except?: string } | { kind: 'only'; id: string } | { kind: 'none' };

export interface PendingEdits {
  /** Locally drawn features awaiting attributes + save (GeoJSON geometry, 4326). */
  created: { feature: Feature; geometryType: DrawShape }[];
  /** Existing features whose geometry changed (id → new GeoJSON geometry). */
  modified: globalThis.Map<string, object>;
}

type Command = {
  undo: () => void;
  redo: () => void;
  /**
   * The saved feature a reshape step changes. Its redo is only good while that very object is
   * still on the map: once every reshape of it is undone the layer no longer holds it, and a reload
   * swaps it for the server's copy.
   */
  reshapes?: Feature;
};

// Fewest vertices a shape can be finished with — the same thresholds OL enforces on the
// finishing *gesture*, restated here because the programmatic finish enforces nothing.
const MIN_LINE_VERTICES = 2;
const MIN_RING_VERTICES = 3;

const format = new GeoJSON();

export class MapEditController {
  private readonly map: Map;
  private readonly source: VectorSource;

  private interactions: (Draw | Modify | Translate | Snap)[] = [];
  private undoStack: Command[] = [];
  private redoStack: Command[] = [];
  private state: EditState = {
    mode: 'none', snap: true, canUndo: false, canRedo: false, dirty: 0, created: 0, sketchActive: false,
  };
  private readonly listeners = new Set<(s: EditState) => void>();

  /** Invoked after each completed draw so the UI can collect attributes. */
  onDrawEnd: ((feature: Feature) => void) | undefined;

  /** Invoked when a cave/entrance placement click lands (lon/lat, EPSG:4326). */
  onPointPlaced: ((mode: PlacementMode, lonLat: [number, number]) => void) | undefined;

  /** The sketch our own Draw is building, while it is being built. */
  private sketchFeature: Feature | undefined;

  private createdFeatures: { feature: Feature; geometryType: DrawShape }[] = [];
  private modifiedGeometries = new globalThis.Map<string, object>();
  /**
   * How many recorded reshapes of each saved feature are in effect — gestures made, less those
   * undone. At zero the feature is back in the shape the server holds and is no longer pending,
   * so undoing every edit leaves nothing to save rather than an update that changes nothing.
   */
  private reshapeCounts = new globalThis.Map<string, number>();

  /**
   * The version of each reshaped saved feature that its reshaping began from, read as the first
   * gesture on it starts, and kept with the very object the gesture took hold of. The save names
   * this version, so a change somebody else saved since is refused rather than overwritten by a
   * shape drawn over the older one. A reload that replaces the object puts a newer shape on the
   * map, and the next gesture on it reads the version afresh.
   */
  private openedVersions = new globalThis.Map<string, { feature: Feature; version: Promise<string | undefined> }>();

  /** Which saved features the reshaping tools may take hold of. */
  private reshapeScope: ReshapeScope = { kind: 'every' };

  private readonly readVersion: (id: string) => Promise<string | undefined>;

  constructor(map: Map, readVersion: (id: string) => Promise<string | undefined> = fetchFeatureVersion) {
    this.map = map;
    this.source = getSurfaceFeatureSource();
    this.readVersion = readVersion;
  }

  /** Registers a state listener; it is called immediately with the current snapshot. */
  subscribe(listener: (s: EditState) => void): () => void {
    this.listeners.add(listener);
    this.refreshState();
    listener(this.state);
    return () => {
      this.listeners.delete(listener);
    };
  }

  /**
   * The version a reshaped saved feature's pending shape was drawn against, for the save to name.
   * Undefined when it could not be read when the reshaping began.
   */
  openedVersion(id: string): Promise<string | undefined> {
    return this.openedVersions.get(id)?.version ?? Promise.resolve(undefined);
  }

  getPendingEdits(): PendingEdits {
    return { created: [...this.createdFeatures], modified: new globalThis.Map(this.modifiedGeometries) };
  }

  /** Serializes a drawn/edited geometry to GeoJSON (EPSG:4326). */
  static toGeoJsonGeometry(geometry: Geometry): object {
    return JSON.parse(
      format.writeGeometry(geometry, { featureProjection: 'EPSG:3857', dataProjection: 'EPSG:4326' }),
    ) as object;
  }

  setMode(mode: EditMode, drawShape?: DrawShape, drawTypeId?: number): void {
    this.detachInteractions();
    // Draw arming is sticky: re-entering draw without explicit params (e.g. the
    // snap toggle re-attaching interactions) keeps the current shape and type.
    const shape = mode === 'draw' ? (drawShape ?? this.state.drawShape ?? 'Point') : undefined;
    const typeId = mode === 'draw' ? (drawTypeId ?? this.state.drawTypeId) : undefined;
    // Detaching killed any sketch that was in progress along with its interaction.
    this.sketchFeature = undefined;
    this.state = { ...this.state, mode, drawShape: shape, drawTypeId: typeId, sketchActive: false };

    switch (mode) {
      case 'draw':
        this.attachDraw(shape ?? 'Point', typeId);
        break;
      case 'modify':
        this.attachModify();
        break;
      case 'translate':
        this.attachTranslate();
        break;
      case 'add-cave':
      case 'add-entrance':
        this.attachPlacePoint(mode);
        break;
      case 'none':
        break;
    }

    if (this.state.snap && (mode === 'draw' || mode === 'modify' || mode === 'translate')) {
      this.attachSnap();
    }

    this.emit();
  }

  /**
   * Limits modify and move to the saved features this person may change. Without a limit the tools
   * take hold of every feature on the map, including ones the person may not change: the save
   * would be refused, but only after the shape had been dragged about. A feature drawn here and not
   * saved yet is always theirs to reshape.
   *
   * An armed tool is re-attached when the scope changes, so the limit holds at once — and only
   * then: re-attaching drops what the tool was holding, such as the vertex a finger last touched.
   */
  setReshapeScope(scope: ReshapeScope): void {
    if (JSON.stringify(scope) === JSON.stringify(this.reshapeScope)) {
      return;
    }
    this.reshapeScope = scope;
    if (this.state.mode === 'modify' || this.state.mode === 'translate') {
      this.setMode(this.state.mode);
    }
  }

  private readonly mayReshape = (feature: FeatureLike): boolean => {
    if (feature.get('pendingNew') === true) {
      return true;
    }
    const id = feature.get('id') as string | undefined;
    const scope = this.reshapeScope;
    switch (scope.kind) {
      case 'every':
        return id === undefined || id !== scope.except;
      case 'only':
        return id === scope.id;
      case 'none':
        return false;
    }
  };

  toggleSnap(): void {
    this.state = { ...this.state, snap: !this.state.snap };
    this.setMode(this.state.mode); // reattach with/without snap
  }

  undo(): void {
    const command = this.undoStack.pop();
    if (command) {
      command.undo();
      this.redoStack.push(command);
      this.emit();
    }
  }

  redo(): void {
    this.dropStaleRedo();
    const command = this.redoStack.pop();
    if (command) {
      command.redo();
      this.undoStack.push(command);
    }
    // Emitted even with nothing redone: dropping stale steps can have emptied the stack.
    this.emit();
  }

  /**
   * Terminates the sketch in progress as if its last vertex had been double-clicked.
   *
   * This is the only termination path a finger has: finishing by gesture means hitting the
   * last vertex within ~12px, and a double-tap is claimed by the map's zoom. It also covers
   * measuring, whose Draw belongs to the measure buttons rather than to this controller —
   * hence "every active Draw on the map" rather than just our own. OL's finish/abort/
   * removeLastPoint are all no-ops on a Draw that has no sketch, so the ones not currently
   * drawing ignore the call.
   */
  finishDrawing(): boolean {
    if (!this.sketchCanFinish()) {
      return false;
    }
    let finished = false;
    this.forEachActiveDraw((draw) => {
      if (draw.finishDrawing()) {
        finished = true;
      }
    });
    return finished;
  }

  /** Discards the sketch in progress; the tool stays armed for another attempt. */
  abortDrawing(): void {
    this.forEachActiveDraw((draw) => draw.abortDrawing());
  }

  /** Retracts the last placed vertex — a mis-tapped point without restarting the shape. */
  removeLastPoint(): void {
    this.forEachActiveDraw((draw) => draw.removeLastPoint());
  }

  /**
   * Deletes the vertex the pointer last touched, the touch stand-in for desktop's
   * alt-click (which OL's default deleteCondition keeps). Returns false when no vertex
   * was under the last touch, which the toolbar turns into a hint rather than a silent
   * no-op. The removal runs through Modify's own modifystart/modifyend events, so it
   * lands in the undo stack like any other vertex edit.
   */
  removeVertex(): boolean {
    const modify = this.interactions.find((i): i is Modify => i instanceof Modify);
    return modify?.removePoint() ?? false;
  }

  /**
   * Whether finishing the sketch in progress would produce a usable shape.
   *
   * OL only lets the *gesture* finish once enough vertices are down; `finishDrawing()`
   * applies no such rule and will happily end a one-tap sketch as a single-coordinate
   * line or a zero-area ring — which then fails server-side validation on save, long
   * after the tap that caused it. The sketch geometry carries the placed vertices plus
   * the one trailing the pointer, and a ring additionally repeats its first point.
   */
  private sketchCanFinish(): boolean {
    const geometry = this.sketchFeature?.getGeometry();
    // Nothing of ours in progress: a sketch owned by another tool (measuring) may still
    // have something to finish, and its own rules apply to it.
    if (!geometry) {
      return true;
    }
    if (geometry instanceof LineString) {
      return geometry.getCoordinates().length - 1 >= MIN_LINE_VERTICES;
    }
    if (geometry instanceof Polygon) {
      return geometry.getCoordinates()[0].length - 2 >= MIN_RING_VERTICES;
    }
    return true;
  }

  private forEachActiveDraw(action: (draw: Draw) => void): void {
    for (const interaction of this.map.getInteractions().getArray()) {
      if (interaction instanceof Draw && interaction.getActive()) {
        action(interaction);
      }
    }
  }

  /**
   * Places a point feature of the given type at an exact coordinate (EPSG:4326) —
   * the context menu's "add here" path. Runs through the same pending/undo pipeline
   * as an interactive draw, so attribute collection and the batched save behave
   * identically to a canvas click.
   */
  placePointAt(lonLat: [number, number], typeId: number): void {
    const feature = new Feature(new Point(fromLonLat(lonLat)));
    feature.set('pendingNew', true);
    feature.set('featureTypeId', typeId);
    this.source.addFeature(feature);
    const entry = { feature, geometryType: 'Point' as DrawShape };
    this.createdFeatures.push(entry);
    this.pushCommand({
      undo: () => {
        this.source.removeFeature(feature);
        this.createdFeatures = this.createdFeatures.filter((c) => c.feature !== feature);
        this.syncDirty();
      },
      redo: () => {
        this.source.addFeature(feature);
        this.createdFeatures.push(entry);
        this.syncDirty();
      },
    });
    this.syncDirty();
    this.onDrawEnd?.(feature);
  }

  /**
   * Opens the cave/entrance create flow for an exact coordinate (context-menu path):
   * same callback the one-shot placement tools use, without needing a second click.
   */
  requestPlacement(mode: PlacementMode, lonLat: [number, number]): void {
    this.setMode('none');
    this.onPointPlaced?.(mode, lonLat);
  }

  /**
   * Takes a drawn feature off the pending list once the server has created it, so that a save
   * which fails part way leaves exactly what was not saved, and a retry cannot create this one a
   * second time. The feature stays on the map as the saved feature it now is — carrying its
   * server id, reshapable as any saved feature — until the next load replaces it with the
   * server's copy.
   */
  markCreatedSaved(feature: Feature, id: string): void {
    this.createdFeatures = this.createdFeatures.filter((c) => c.feature !== feature);
    feature.set('id', id);
    feature.unset('pendingNew');
    feature.unset('pendingAttrs');
    feature.unset(SAVE_REFUSED);
    this.forgetHistory();
  }

  /** Marks a drawing the server refused to create, so the map shows which one it was. */
  markCreatedRefused(feature: Feature): void {
    feature.set(SAVE_REFUSED, true);
  }

  /** Marks a reshaped saved feature whose new shape the server refused, so the map shows which. */
  markModifiedRefused(id: string): void {
    for (const feature of this.source.getFeatures()) {
      if (feature.get('id') === id && feature.get(PENDING_RESHAPE) === true) {
        feature.set(SAVE_REFUSED, true);
      }
    }
  }

  /** Takes the refusal marks of the last Save off, as the next one goes out. */
  clearRefused(): void {
    for (const feature of this.source.getFeatures()) {
      if (feature.get(SAVE_REFUSED) === true) {
        feature.unset(SAVE_REFUSED);
      }
    }
  }

  /**
   * Takes a saved feature's reshaped geometry off the pending list once the server has stored it —
   * unless the feature was reshaped again while that request was out, in which case the newer
   * shape is still pending and stays on the list.
   */
  markModifiedSaved(id: string, sentGeometry: object): void {
    if (this.modifiedGeometries.get(id) === sentGeometry) {
      this.modifiedGeometries.delete(id);
      this.reshapeCounts.delete(id);
      // The stored shape is now this editor's own, at a version its next gesture reads afresh.
      this.openedVersions.delete(id);
      this.releaseReshaped((feature) => feature.get('id') === id);
    }
    this.forgetHistory();
  }

  /** Called by the toolbar after a successful save or discard. */
  reset(): void {
    this.dropPending();
    this.setMode('none');
  }

  /**
   * Lets go of the map. Whatever was not saved goes with this controller, exactly as a discard
   * would take it: the feature source outlives the page, so a drawing or a reshape left in it would
   * come back with the next visit to the map as a feature no controller tracks — still drawn, never
   * saved, and a reshaped one in a shape its editor threw away, which the next reshape of it would
   * then write to the server.
   */
  dispose(): void {
    this.dropPending();
    this.detachInteractions();
    this.listeners.clear();
    this.onDrawEnd = undefined;
    this.onPointPlaced = undefined;
  }

  // ---- interactions ----

  private attachDraw(shape: DrawShape, typeId?: number): void {
    // stopClick keeps sketch clicks out of the map's singleclick: without it every vertex
    // placed also runs the selection handler, which churns the details panel mid-draw on
    // desktop and, on touch, makes the finishing double-tap both finish and zoom.
    const draw = new Draw({ source: this.source, type: shape, stopClick: true });
    draw.on('drawstart', (event) => {
      this.sketchFeature = event.feature;
      this.setSketchActive(true);
    });
    draw.on('drawabort', () => {
      this.sketchFeature = undefined;
      this.setSketchActive(false);
    });
    draw.on('drawend', (event) => {
      this.sketchFeature = undefined;
      this.setSketchActive(false);
      const feature = event.feature;
      feature.set('pendingNew', true);
      feature.set('featureTypeId', typeId);
      const entry = { feature, geometryType: shape };
      this.createdFeatures.push(entry);
      this.pushCommand({
        undo: () => {
          this.source.removeFeature(feature);
          this.createdFeatures = this.createdFeatures.filter((c) => c.feature !== feature);
          this.syncDirty();
        },
        redo: () => {
          this.source.addFeature(feature);
          this.createdFeatures.push(entry);
          this.syncDirty();
        },
      });
      this.syncDirty();
      this.onDrawEnd?.(feature);
    });
    this.map.addInteraction(draw);
    this.interactions.push(draw);
  }

  private attachModify(): void {
    // A fingertip covers far more than the 10px OL grabs a vertex within by default.
    const modify = new Modify({
      source: this.source,
      pixelTolerance: coarsePointer() ? 16 : 10,
      filter: this.mayReshape,
    });
    this.trackGeometryChanges(modify);
    this.map.addInteraction(modify);
    this.interactions.push(modify);
  }

  private attachTranslate(): void {
    // Translate hit-tests exactly under the pointer by default, which a finger cannot aim.
    const translate = new Translate({
      layers: (l) => l.getSource() === this.source,
      filter: this.mayReshape,
      hitTolerance: coarsePointer() ? 10 : 4,
    });
    this.trackGeometryChanges(translate);
    this.map.addInteraction(translate);
    this.interactions.push(translate);
  }

  /** Records before/after geometry snapshots around a modify/translate gesture. */
  private trackGeometryChanges(interaction: Modify | Translate): void {
    let before = new globalThis.Map<Feature, Geometry>();

    const capture = (features: Feature[]) => {
      before = new globalThis.Map(features.map((f) => [f, f.getGeometry()!.clone()]));
      for (const feature of features) {
        this.noteOpenedVersion(feature);
      }
    };
    const commit = (features: Feature[]) => {
      // The end event only fires after an actual change; snapshot both sides for undo.
      for (const feature of features) {
        const previous = before.get(feature);
        if (!previous) {
          continue;
        }
        const next = feature.getGeometry()!.clone();
        const saved = feature.get('id') !== undefined && feature.get('pendingNew') !== true;
        this.registerGeometryChange(feature, 1);
        this.pushCommand({
          reshapes: saved ? feature : undefined,
          undo: () => {
            feature.setGeometry(previous.clone());
            this.registerGeometryChange(feature, -1);
          },
          redo: () => {
            feature.setGeometry(next.clone());
            this.registerGeometryChange(feature, 1);
          },
        });
      }
      this.syncDirty();
    };

    if (interaction instanceof Modify) {
      interaction.on('modifystart', (e) => capture(e.features.getArray()));
      interaction.on('modifyend', (e) => commit(e.features.getArray()));
    } else {
      interaction.on('translatestart', (e) => capture(e.features.getArray()));
      interaction.on('translateend', (e) => commit(e.features.getArray()));
    }
  }

  /** Reads the version a saved feature is at as the first gesture on this object of it starts. */
  private noteOpenedVersion(feature: Feature): void {
    const id = feature.get('id') as string | undefined;
    if (!id || feature.get('pendingNew') === true || this.openedVersions.get(id)?.feature === feature) {
      return;
    }
    const entry = {
      feature,
      version: this.readVersion(id).catch(() => {
        // Not read: the next gesture tries again, and until then the save names none of its own.
        if (this.openedVersions.get(id) === entry) {
          this.openedVersions.delete(id);
        }
        return undefined;
      }),
    };
    this.openedVersions.set(id, entry);
  }

  private registerGeometryChange(feature: Feature, step: 1 | -1): void {
    const id = feature.get('id') as string | undefined;
    if (id && feature.get('pendingNew') !== true) {
      const count = (this.reshapeCounts.get(id) ?? 0) + step;
      if (count <= 0) {
        this.reshapeCounts.delete(id);
        this.modifiedGeometries.delete(id);
        feature.unset(PENDING_RESHAPE);
        return;
      }
      this.reshapeCounts.set(id, count);
      this.modifiedGeometries.set(id, MapEditController.toGeoJsonGeometry(feature.getGeometry()!));
      // Marks the feature as the editor's, so a reload of the layer keeps this object in its
      // edited shape instead of replacing it with the server's stored one.
      feature.set(PENDING_RESHAPE, true);
    }
  }

  private attachSnap(): void {
    const snap = new Snap({ source: this.source });
    this.map.addInteraction(snap);
    this.interactions.push(snap);
  }

  /**
   * One-shot point placement for the cave/entrance tools: the click is reported
   * as lon/lat and the tool disarms — no feature is kept (the create dialog and
   * the subsequent server reload own what appears on the map).
   */
  private attachPlacePoint(mode: PlacementMode): void {
    const draw = new Draw({ type: 'Point', stopClick: true });
    draw.on('drawend', (event) => {
      const point = event.feature.getGeometry() as Point;
      const [lon, lat] = toLonLat(point.getCoordinates());
      this.setMode('none');
      this.onPointPlaced?.(mode, [Number(lon.toFixed(6)), Number(lat.toFixed(6))]);
    });
    this.map.addInteraction(draw);
    this.interactions.push(draw);
  }

  private detachInteractions(): void {
    for (const interaction of this.interactions) {
      this.map.removeInteraction(interaction);
    }
    this.interactions = [];
  }

  private pushCommand(command: Command): void {
    this.undoStack.push(command);
    this.redoStack = [];
    this.emit();
  }

  /**
   * Throws away every pending edit: the drawings come off the map, and the reshaped features are
   * handed back to the layer, whose next load replaces them with the stored shapes. After a whole
   * save both lists are already empty here.
   */
  private dropPending(): void {
    this.clearRefused();
    for (const { feature } of this.createdFeatures) {
      if (this.source.hasFeature(feature)) {
        this.source.removeFeature(feature);
      }
    }
    this.releaseReshaped(() => true);
    this.createdFeatures = [];
    this.modifiedGeometries.clear();
    this.reshapeCounts.clear();
    this.openedVersions.clear();
    this.undoStack = [];
    this.redoStack = [];
  }

  /** Stops the layer holding these features in their edited shape across its reloads. */
  private releaseReshaped(which: (feature: Feature) => boolean): void {
    for (const feature of this.source.getFeatures()) {
      if (feature.get(PENDING_RESHAPE) === true && which(feature)) {
        feature.unset(PENDING_RESHAPE);
      }
    }
  }

  /**
   * Once anything has reached the server the edit history no longer describes what is pending:
   * undoing a draw that was saved would only hide the feature, and redoing it would put it back
   * on the pending list to be created again. A save that partly succeeded therefore ends the
   * history just as a whole save does.
   */
  private isStale(command: Command): boolean {
    return command.reshapes !== undefined && !this.source.hasFeature(command.reshapes);
  }

  /**
   * Drops the redo steps of saved features that a reload has since replaced. Redoing one would set
   * the edited shape on an object no longer on the map and put that shape on the pending list, so
   * Save would write a shape the editor cannot see. The steps of each feature stand on their own,
   * so the rest of the stack keeps its order and meaning.
   */
  private dropStaleRedo(): void {
    this.redoStack = this.redoStack.filter((command) => !this.isStale(command));
  }

  private forgetHistory(): void {
    this.undoStack = [];
    this.redoStack = [];
    this.emit();
  }

  private syncDirty(): void {
    this.emit();
  }

  private setSketchActive(active: boolean): void {
    this.state = { ...this.state, sketchActive: active };
    this.emit();
  }

  private refreshState(): void {
    this.state = {
      ...this.state,
      canUndo: this.undoStack.length > 0,
      canRedo: this.redoStack.some((command) => !this.isStale(command)),
      dirty: this.createdFeatures.length + this.modifiedGeometries.size,
      created: this.createdFeatures.length,
    };
  }

  private emit(): void {
    this.refreshState();
    for (const listener of this.listeners) {
      listener(this.state);
    }
  }
}

