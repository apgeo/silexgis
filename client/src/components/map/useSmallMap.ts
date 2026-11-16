// SPDX-License-Identifier: AGPL-3.0-or-later
import { useCallback, useEffect, useRef, useState, type RefObject } from 'react';
import { useTranslation } from 'react-i18next';
import Map from 'ol/Map';
import View, { type FitOptions } from 'ol/View';
import { isEmpty, type Extent } from 'ol/extent';
import type BaseLayer from 'ol/layer/Base';
import { fromLonLat } from 'ol/proj';
import { useMapLayers, type MapLayerInfo } from '../../api/hooks.ts';
import type { BackgroundLayer } from '../../map/baseLayers.ts';
import { chooseBackground, editorBackgroundLayer, workspaceBackgroundId } from '../../map/editorBackground.ts';

export interface SmallMapOptions {
  /** Where the camera starts, as [longitude, latitude], until something is framed. */
  center: readonly [number, number];
  zoom: number;
  /**
   * The map's own layers, drawn above the background. Called each time a map is built, and the
   * layers it returns are disposed with that map — so a source shared across builds is released
   * by every layer that drew it.
   */
  layers: () => BaseLayer[];
  /**
   * Whether the map may exist. A map built into a container that is not laid out yet (a dialog
   * still opening, a tab not shown) measures nothing; false also disposes a map already built.
   */
  active?: boolean;
  /** Called once for every map built, to attach its listeners; they go with the map. */
  onCreate?: (map: Map) => void;
  /** Which catalogue backgrounds the map may draw; any the flat map can draw when left out. */
  backgrounds?: (entry: MapLayerInfo) => boolean;
}

export interface SmallMap {
  /** The container's ref. Stable: re-rendering the owner never detaches or rebuilds the map. */
  targetRef: (element: HTMLDivElement | null) => void;
  /** The map while it exists. Never copy it into React state. */
  map: RefObject<Map | null>;
  /** True while a map exists — the dependency an effect that draws on it re-runs on. */
  ready: boolean;
  /**
   * Frames the camera on `extent` the first time it is called with `key`, and never again for the
   * same key: the key names what is shown (the set of candidates, not which one is selected), so
   * the reader's own panning survives every redraw that shows the same things. An empty extent
   * frames nothing and is not remembered; a map not measured yet is framed once it is.
   */
  frame: (key: string, extent: Extent | null | undefined, options?: FitOptions) => void;
}

const DEFAULT_FIT: FitOptions = { padding: [32, 32, 32, 32], maxZoom: 16 };

function measured(map: Map): boolean {
  const size = map.getSize();
  return Boolean(size && size[0] > 0 && size[1] > 0);
}

/**
 * One small OpenLayers map of a component's own — a picker in a dialog, a preview beside a list —
 * drawn over the background the reader has on the workspace map (else the installation's default,
 * else OpenTopoMap). The map is built when its container is mounted and `active` is true, moved
 * (not rebuilt) when the container is replaced, and disposed with every layer it was given when
 * the container goes, `active` turns false, or the owner unmounts.
 */
export function useSmallMap(options: SmallMapOptions): SmallMap {
  const { i18n } = useTranslation();
  const active = options.active ?? true;
  const latest = useRef(options);
  latest.current = options;

  const [target, setTarget] = useState<HTMLDivElement | null>(null);
  const [ready, setReady] = useState(false);
  const map = useRef<Map | null>(null);
  const background = useRef<{ key: string; layer: BackgroundLayer } | null>(null);
  const framedKey = useRef<string | null>(null);
  const pendingFit = useRef<{ extent: Extent; options: FitOptions } | null>(null);

  const targetRef = useCallback((element: HTMLDivElement | null) => setTarget(element), []);

  const dispose = useCallback(() => {
    const instance = map.current;
    if (!instance) {
      return;
    }
    map.current = null;
    // A map's own dispose releases its controls, interactions and listeners but not its layers,
    // and a layer left alive keeps listening to its source — which the owner may keep and draw
    // into a later map.
    const layers = instance.getAllLayers();
    instance.getLayers().clear();
    instance.setTarget(undefined);
    instance.dispose();
    layers.forEach((layer) => layer.dispose());
    background.current = null;
    framedKey.current = null;
    pendingFit.current = null;
    setReady(false);
  }, []);

  useEffect(() => {
    if (!target || !active) {
      dispose();
      return;
    }
    if (map.current) {
      map.current.setTarget(target);
      return;
    }
    const { center, zoom, layers, onCreate } = latest.current;
    const instance = new Map({
      target,
      layers: layers(),
      view: new View({ center: fromLonLat([center[0], center[1]]), zoom }),
    });
    instance.on('change:size', () => {
      const pending = pendingFit.current;
      if (pending && measured(instance)) {
        pendingFit.current = null;
        instance.getView().fit(pending.extent, pending.options);
      }
    });
    map.current = instance;
    onCreate?.(instance);
    setReady(true);
  }, [target, active, dispose]);

  useEffect(() => dispose, [dispose]);

  // The background is chosen once the catalogue has answered: before then nothing is drawn under
  // the map's own layers, rather than a fallback fetched only to be replaced a moment later. A
  // catalogue that failed to answer is drawn over the fallback.
  const catalogue = useMapLayers();
  const [workspaceId] = useState(() => workspaceBackgroundId());
  const entry = catalogue.isPending ? undefined : chooseBackground(catalogue.data, workspaceId, options.backgrounds);
  const backgroundKey = entry === undefined ? null : `${entry?.id ?? 'fallback'}|${i18n.language}`;
  useEffect(() => {
    const instance = map.current;
    if (!instance || backgroundKey === null || background.current?.key === backgroundKey) {
      return;
    }
    const layer = editorBackgroundLayer(entry ?? null, i18n.language);
    const layers = instance.getLayers();
    if (background.current) {
      layers.remove(background.current.layer);
      background.current.layer.dispose();
    }
    layers.insertAt(0, layer);
    background.current = { key: backgroundKey, layer };
    // eslint-disable-next-line react-hooks/exhaustive-deps -- the entry and language are named by the key
  }, [ready, backgroundKey]);

  const frame = useCallback((key: string, extent: Extent | null | undefined, fitOptions?: FitOptions) => {
    const instance = map.current;
    if (!instance || framedKey.current === key || !extent || isEmpty(extent)) {
      return;
    }
    framedKey.current = key;
    const resolved = { ...DEFAULT_FIT, ...fitOptions };
    // Fitting a map that has not been measured frames the extent into a guessed size, and the
    // camera lands at the wrong zoom; it waits for the first measurement instead.
    if (measured(instance)) {
      pendingFit.current = null;
      instance.getView().fit(extent, resolved);
    } else {
      pendingFit.current = { extent, options: resolved };
    }
  }, []);

  return { targetRef, map, ready, frame };
}
