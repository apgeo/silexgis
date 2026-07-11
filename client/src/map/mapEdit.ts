// SPDX-License-Identifier: AGPL-3.0-or-later
import type Map from 'ol/Map';
import Feature, { type FeatureLike } from 'ol/Feature';
import GeoJSON from 'ol/format/GeoJSON';
import type Geometry from 'ol/geom/Geometry';
import type Point from 'ol/geom/Point';
import { Draw, Modify, Snap, Translate } from 'ol/interaction';
import VectorLayer from 'ol/layer/Vector';
import { toLonLat } from 'ol/proj';
import VectorSource from 'ol/source/Vector';
import { getArea, getLength } from 'ol/sphere';
import { Circle as CircleStyle, Fill, Stroke, Style, Text } from 'ol/style';
import { getSurfaceFeatureSource } from './featureLayer.ts';

// All OL edit interactions live here; React components only dispatch intents and
// subscribe to plain-data snapshots via the listener.

export type EditMode =
  | 'none'
  | 'draw'
  | 'modify'
  | 'translate'
  | 'measure-distance'
  | 'measure-area'
  | 'add-cave'
  | 'add-entrance';
export type DrawShape = 'Point' | 'LineString' | 'Polygon';
export type PlacementMode = 'add-cave' | 'add-entrance';

export interface EditState {
  mode: EditMode;
  snap: boolean;
  canUndo: boolean;
  canRedo: boolean;
  dirty: number; // created + geometry-modified features not yet saved
  measureResult: string | null;
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
  private readonly measureSource = new VectorSource();
  private readonly measureLayer: VectorLayer;

  private interactions: (Draw | Modify | Translate | Snap)[] = [];
  private undoStack: Command[] = [];
  private redoStack: Command[] = [];
  private state: EditState = {
    mode: 'none', snap: true, canUndo: false, canRedo: false, dirty: 0, measureResult: null,
  };
  private listener: ((s: EditState) => void) | undefined;

  /** Invoked after each completed draw so the UI can collect attributes. */
  onDrawEnd: ((feature: Feature) => void) | undefined;

  /** Invoked when a cave/entrance placement click lands (lon/lat, EPSG:4326). */
  onPointPlaced: ((mode: PlacementMode, lonLat: [number, number]) => void) | undefined;

  private createdFeatures: { feature: Feature; geometryType: DrawShape }[] = [];
  private modifiedGeometries = new globalThis.Map<string, object>();

  constructor(map: Map) {
    this.map = map;
    this.source = getSurfaceFeatureSource();
    this.measureLayer = new VectorLayer({
      source: this.measureSource,
      // Data overlays get sequential zIndex from their group position; keep
      // measurements far above however many of them exist.
      zIndex: 1000,
      style: measureStyle,
    });
    this.measureLayer.set('id', 'measure');
    map.addLayer(this.measureLayer);
  }

  subscribe(listener: (s: EditState) => void): void {
    this.listener = listener;
    this.emit();
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
    this.state = { ...this.state, mode, measureResult: null };

    switch (mode) {
      case 'draw':
        this.attachDraw(drawShape ?? 'Point', drawTypeId);
        break;
      case 'modify':
        this.attachModify();
        break;
      case 'translate':
        this.attachTranslate();
        break;
      case 'measure-distance':
        this.attachMeasure('LineString');
        break;
      case 'measure-area':
        this.attachMeasure('Polygon');
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

  /** Called by the toolbar after a successful save or discard. */
  reset(): void {
    this.createdFeatures = [];
    this.modifiedGeometries.clear();
    this.undoStack = [];
    this.redoStack = [];
    this.clearMeasurements();
    this.setMode('none');
  }

  clearMeasurements(): void {
    this.measureSource.clear();
    this.state = { ...this.state, measureResult: null };
    this.emit();
  }

  dispose(): void {
    this.detachInteractions();
    this.map.removeLayer(this.measureLayer);
    this.listener = undefined;
    this.onDrawEnd = undefined;
    this.onPointPlaced = undefined;
  }

  // ---- interactions ----

  private attachDraw(shape: DrawShape, typeId?: number): void {
    const draw = new Draw({ source: this.source, type: shape });
    draw.on('drawend', (event) => {
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
    const modify = new Modify({ source: this.source });
    this.trackGeometryChanges(modify);
    this.map.addInteraction(modify);
    this.interactions.push(modify);
  }

  private attachTranslate(): void {
    const translate = new Translate({ layers: (l) => l.getSource() === this.source });
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
    const draw = new Draw({ type: 'Point' });
    draw.on('drawend', (event) => {
      const point = event.feature.getGeometry() as Point;
      const [lon, lat] = toLonLat(point.getCoordinates());
      this.setMode('none');
      this.onPointPlaced?.(mode, [Number(lon.toFixed(6)), Number(lat.toFixed(6))]);
    });
    this.map.addInteraction(draw);
    this.interactions.push(draw);
  }

  private attachMeasure(shape: 'LineString' | 'Polygon'): void {
    const draw = new Draw({ source: this.measureSource, type: shape });
    draw.on('drawend', (event) => {
      const geometry = event.feature.getGeometry()!;
      const result = shape === 'LineString'
        ? formatLength(getLength(geometry))
        : formatArea(getArea(geometry));
      event.feature.set('measure', result);
      this.state = { ...this.state, measureResult: result };
      this.emit();
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

  private emit(): void {
    this.state = {
      ...this.state,
      canUndo: this.undoStack.length > 0,
      canRedo: this.redoStack.length > 0,
      dirty: this.createdFeatures.length + this.modifiedGeometries.size,
    };
    this.listener?.(this.state);
  }
}

function formatLength(meters: number): string {
  return meters >= 1000 ? `${(meters / 1000).toFixed(2)} km` : `${meters.toFixed(1)} m`;
}

function formatArea(squareMeters: number): string {
  return squareMeters >= 1_000_000
    ? `${(squareMeters / 1_000_000).toFixed(3)} km²`
    : `${squareMeters.toFixed(0)} m²`;
}

function measureStyle(feature: FeatureLike): Style {
  return new Style({
    stroke: new Stroke({ color: '#c41d7f', width: 2, lineDash: [6, 6] }),
    fill: new Fill({ color: 'rgba(196, 29, 127, 0.08)' }),
    image: new CircleStyle({ radius: 4, fill: new Fill({ color: '#c41d7f' }) }),
    text: new Text({
      text: (feature.get('measure') as string | undefined) ?? '',
      offsetY: -12,
      font: '12px sans-serif',
      fill: new Fill({ color: '#c41d7f' }),
      stroke: new Stroke({ color: '#ffffff', width: 3 }),
    }),
  });
}
