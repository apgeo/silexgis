// SPDX-License-Identifier: AGPL-3.0-or-later
import type Map from 'ol/Map';
import Feature from 'ol/Feature';
import GeoJSON from 'ol/format/GeoJSON';
import type Geometry from 'ol/geom/Geometry';
import Point from 'ol/geom/Point';
import { Draw, Modify, Snap, Translate } from 'ol/interaction';
import { fromLonLat, toLonLat } from 'ol/proj';
import type VectorSource from 'ol/source/Vector';
import { getSurfaceFeatureSource } from './featureLayer.ts';
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
  /** A multi-vertex sketch is in progress (between the first click and the finish). */
  sketchActive: boolean;
  /** Shape/type the draw tool is armed with (kept while mode is 'draw'). */
  drawShape?: DrawShape;
  drawTypeId?: number;
}

export interface PendingEdits {
  /** Locally drawn features awaiting attributes + save (GeoJSON geometry, 4326). */
  created: { feature: Feature; geometryType: DrawShape }[];
  /** Existing features whose geometry changed (id → new GeoJSON geometry). */
  modified: globalThis.Map<string, object>;
}

type Command = { undo: () => void; redo: () => void };

const format = new GeoJSON();

export class MapEditController {
  private readonly map: Map;
  private readonly source: VectorSource;

  private interactions: (Draw | Modify | Translate | Snap)[] = [];
  private undoStack: Command[] = [];
  private redoStack: Command[] = [];
  private state: EditState = {
    mode: 'none', snap: true, canUndo: false, canRedo: false, dirty: 0, sketchActive: false,
  };
  private readonly listeners = new Set<(s: EditState) => void>();

  /** Invoked after each completed draw so the UI can collect attributes. */
  onDrawEnd: ((feature: Feature) => void) | undefined;

  /** Invoked when a cave/entrance placement click lands (lon/lat, EPSG:4326). */
  onPointPlaced: ((mode: PlacementMode, lonLat: [number, number]) => void) | undefined;

  private createdFeatures: { feature: Feature; geometryType: DrawShape }[] = [];
  private modifiedGeometries = new globalThis.Map<string, object>();

  constructor(map: Map) {
    this.map = map;
    this.source = getSurfaceFeatureSource();
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
    const command = this.redoStack.pop();
    if (command) {
      command.redo();
      this.undoStack.push(command);
      this.emit();
    }
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
  finishDrawing(): void {
    this.forEachActiveDraw((draw) => draw.finishDrawing());
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

  /** Called by the toolbar after a successful save or discard. */
  reset(): void {
    this.createdFeatures = [];
    this.modifiedGeometries.clear();
    this.undoStack = [];
    this.redoStack = [];
    this.setMode('none');
  }

  dispose(): void {
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
    draw.on('drawstart', () => this.setSketchActive(true));
    draw.on('drawabort', () => this.setSketchActive(false));
    draw.on('drawend', (event) => {
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
    const modify = new Modify({ source: this.source, pixelTolerance: coarsePointer() ? 16 : 10 });
    this.trackGeometryChanges(modify);
    this.map.addInteraction(modify);
    this.interactions.push(modify);
  }

  private attachTranslate(): void {
    // Translate hit-tests exactly under the pointer by default, which a finger cannot aim.
    const translate = new Translate({
      layers: (l) => l.getSource() === this.source,
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
    };
    const commit = (features: Feature[]) => {
      // The end event only fires after an actual change; snapshot both sides for undo.
      for (const feature of features) {
        const previous = before.get(feature);
        if (!previous) {
          continue;
        }
        const next = feature.getGeometry()!.clone();
        this.registerGeometryChange(feature);
        this.pushCommand({
          undo: () => {
            feature.setGeometry(previous.clone());
            this.registerGeometryChange(feature);
          },
          redo: () => {
            feature.setGeometry(next.clone());
            this.registerGeometryChange(feature);
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

  private registerGeometryChange(feature: Feature): void {
    const id = feature.get('id') as string | undefined;
    if (id && feature.get('pendingNew') !== true) {
      this.modifiedGeometries.set(id, MapEditController.toGeoJsonGeometry(feature.getGeometry()!));
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
      canRedo: this.redoStack.length > 0,
      dirty: this.createdFeatures.length + this.modifiedGeometries.size,
    };
  }

  private emit(): void {
    this.refreshState();
    for (const listener of this.listeners) {
      listener(this.state);
    }
  }
}

