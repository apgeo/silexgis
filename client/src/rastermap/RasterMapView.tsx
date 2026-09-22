// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useRef, useState } from 'react';
import Feature from 'ol/Feature';
import Map from 'ol/Map';
import View from 'ol/View';
import Point from 'ol/geom/Point';
import ImageLayer from 'ol/layer/Image';
import VectorLayer from 'ol/layer/Vector';
import Projection from 'ol/proj/Projection';
import ImageStatic from 'ol/source/ImageStatic';
import VectorSource from 'ol/source/Vector';
import { Circle as CircleStyle, Fill, Stroke, Style, Text } from 'ol/style';
import { useTranslation } from 'react-i18next';
import { shortNameOf } from '../caveview/modelParts.ts';
import { rasterMapPalette as palette } from '../map/markerPalette.ts';
import { coarsePointer } from '../map/pointer.ts';
import { markerHit } from './authoring.ts';
import { sheetCaverHit, type SheetCaverMarker } from './caverPlacement.ts';
import { fromMapCoordinate, imageExtent, toMapCoordinate, type ImageSize } from './coordinates.ts';
import type { MapStationMarker } from './mapPoints.ts';

interface Props {
  /** The rendering the reader is entitled to, as the derivative machinery minted it. */
  imageUrl: string;
  /** What the picture is, for assistive readers — the map document's title. */
  alt: string;
  /**
   * The station points to draw, in stored fractions. The caller has already applied every
   * fold rule — point shape, file pin, newest-wins — this component draws what it is
   * handed and decides nothing about what deserves drawing.
   */
  markers: readonly MapStationMarker[];
  /**
   * Whether the pane the map lives in is really on screen. A tab keeps its inactive panes
   * mounted but hidden, and an OL map built inside `display:none` measures 0×0 — so the
   * map is built on first activation and told to re-measure on every later one.
   */
  active: boolean;
  height?: number | string;
  testId?: string;
  /**
   * Answers a click on the sheet itself with the clicked point in stored fractions —
   * the authoring surfaces' placement click. A click outside the picture answers
   * nothing (the conversion refuses it), and a click that lands on a marker goes to
   * `onMarkerClick` instead. Absent on every read-only mount, where a click means
   * nothing and the map stays a picture.
   */
  onMapClick?: (at: { x: number; y: number }) => void;
  /**
   * Answers a click that lands on a drawn marker, within the pointer's own tolerance —
   * bigger for a finger, exactly as the marker itself draws bigger for one. The second
   * argument is where the click itself landed, in stored fractions, so a caller for whom
   * the marker is merely *near* the intended spot can still honor the click; null when
   * the click sat inside the tolerance halo but outside the picture.
   */
  onMarkerClick?: (marker: MapStationMarker, at: { x: number; y: number } | null) => void;
  /**
   * The party to draw over the pins — each dot already placed (through a station pin and
   * no other path; the fold owns that rule), already worded (the label is the same line
   * the 3D scene prints, or null while the labels switch is off) and already coloured
   * (underground against out is the palette's distinction, not this component's).
   * Absent everywhere except the tracking surfaces.
   */
  cavers?: readonly SheetCaverDrawnMarker[];
  /**
   * Answers a click that lands on a caver's dot, before the pin under it is considered:
   * the dot is drawn over the pin, so it wins the press for the same reason anything
   * painted on top does.
   */
  onCaverClick?: (marker: SheetCaverMarker) => void;
  /**
   * A point of the picture to bring into view — the overlay's press-a-row answer, in
   * stored fractions. The view glides there when it changes; null asks for nothing.
   */
  focus?: { x: number; y: number } | null;
}

/** One caver dot as the caller composed it: the placement, the words, the colour. */
export interface SheetCaverDrawnMarker {
  marker: SheetCaverMarker;
  /** The line the label prints, or null while labels are off. */
  label: string | null;
  color: string;
}

/**
 * A scanned cave map with station pins over it.
 *
 * <b>A throwaway OpenLayers map over a pixel projection</b> — the trip-sketch pattern,
 * never the workspace singleton: the image is drawn as an `ImageStatic` over an extent
 * that is simply the picture's natural size, which buys pan, zoom, pinch and hit-testing
 * for free on a Canvas-2D layer that costs no WebGL context. That last point is the whole
 * choice: this component lives in a tab strip next to a 3D viewer that owns one of the
 * browser's few WebGL contexts, and the geo map may hold another.
 *
 * Coordinates arrive as stored fractions and are converted at the OL boundary only
 * (`coordinates.ts` owns the y-flip); OL objects live in refs, never in React state.
 */
export default function RasterMapView({
  imageUrl,
  alt,
  markers,
  active,
  height = '70vh',
  testId = 'rastermap',
  onMapClick,
  onMarkerClick,
  cavers,
  onCaverClick,
  focus,
}: Props) {
  const { t } = useTranslation();
  const target = useRef<HTMLDivElement | null>(null);
  const map = useRef<Map | null>(null);
  const markerSource = useRef(new VectorSource<Feature<Point>>());
  const caverSource = useRef(new VectorSource<Feature<Point>>());
  const [size, setSize] = useState<ImageSize | null>(null);
  const [failed, setFailed] = useState(false);

  // The click listener is attached once, when the map is built, and what a click means
  // changes with every render — arming, disarming, fresh markers — so everything it
  // reads rides refs. Naming any of it in a rebuild would tear down the map (and the
  // reader's pan) to change a callback.
  const clickState = useRef({ onMapClick, onMarkerClick, markers, size, cavers, onCaverClick });
  clickState.current = { onMapClick, onMarkerClick, markers, size, cavers, onCaverClick };

  const teardown = () => {
    map.current?.setTarget(undefined);
    // Layers, sources and listeners stay subscribed until the map is disposed; dropping
    // the reference alone would leak the whole graph for the life of the page.
    map.current?.dispose();
    map.current = null;
  };

  // The picture's natural size is the whole coordinate space, and nothing but the picture
  // itself can say it — the file metadata a reader is given carries no dimensions. So the
  // image is loaded once, off-screen, before any map exists; the browser's cache makes the
  // ImageStatic load that follows a second read of the same bytes, not a second download.
  //
  // A changed URL means a different picture — the document's current file was replaced
  // while the pane sat mounted — and the built map still holds the old bytes in the old
  // extent. It is torn down here, before the new probe, so the markers (already filtered
  // to the NEW file by the pane) are never drawn over the OLD scan at coordinates scaled
  // by the new picture's size. The rebuild waits on the new probe exactly like the first
  // build did, and framing the whole new picture again is right: whatever pan the reader
  // had belongs to a picture that no longer exists.
  useEffect(() => {
    teardown();
    setSize(null);
    setFailed(false);
    let abandoned = false;
    const probe = new Image();
    probe.onload = () => {
      if (!abandoned) {
        setSize({ width: probe.naturalWidth, height: probe.naturalHeight });
      }
    };
    probe.onerror = () => {
      if (!abandoned) {
        setFailed(true);
      }
    };
    probe.src = imageUrl;
    return () => {
      abandoned = true;
    };
  }, [imageUrl]);

  // Teardown on unmount — mere hiding never tears down: a hidden pane keeps its map,
  // wherever the reader panned it to, and pays nothing while nothing is drawn. Only a
  // replaced picture (above) or the end of the component's life gets here.
  useEffect(() => () => teardown(), []);

  const buildMap = () => {
    if (map.current || !target.current || !active || size === null) {
      return;
    }
    const extent = imageExtent(size);
    // A private projection instance, never registered by code lookup: two map tabs must
    // not share or overwrite each other's coordinate space.
    const projection = new Projection({ code: 'rastermap-pixels', units: 'pixels', extent });
    const instance = new Map({
      target: target.current,
      layers: [
        new ImageLayer({
          source: new ImageStatic({ url: imageUrl, imageExtent: extent, projection }),
        }),
        new VectorLayer({ source: markerSource.current }),
        // The party above the pins: a caver's dot must never disappear under the very
        // point that placed it, and a press on the shared spot has to mean the person.
        new VectorLayer({ source: caverSource.current }),
      ],
      view: new View({ projection, center: [size.width / 2, size.height / 2], zoom: 1 }),
    });
    instance.getView().fit(extent, { padding: [16, 16, 16, 16] });
    // `singleclick` rather than `click`, so ending a pan or a pinch places nothing.
    instance.on('singleclick', (event) => {
      const now = clickState.current;
      // A mount with no listener of any kind is a picture and stays one — but any one
      // of the three makes a press meaningful, and a caver's dot answers even where
      // nothing else does (a reader without edit rights, a replay of a disarmed watch).
      if (
        now.size === null ||
        (now.onMapClick === undefined &&
          now.onMarkerClick === undefined &&
          now.onCaverClick === undefined)
      ) {
        return;
      }
      const coordinate = (event as { coordinate: number[] }).coordinate;
      // The same hit generosity the OL interactions give a finger: the pointer is the
      // size it is, whatever the stored point is.
      const tolerance = coarsePointer() ? 12 : 6;
      const resolution = instance.getView().getResolution() ?? 1;
      // The party is painted over the pins, so it answers the press first — a dot and
      // the pin that placed it share a spot by construction, and a press there means
      // the person, not the point.
      if (now.onCaverClick !== undefined && now.cavers !== undefined) {
        const person = sheetCaverHit(
          now.cavers.map((drawn) => drawn.marker),
          now.size,
          coordinate,
          resolution,
          tolerance,
        );
        if (person !== null) {
          now.onCaverClick(person);
          return;
        }
      }
      const hit = markerHit(now.markers, now.size, coordinate, resolution, tolerance);
      const at = fromMapCoordinate(coordinate, now.size);
      if (hit !== null && now.onMarkerClick !== undefined) {
        now.onMarkerClick(hit, at);
        return;
      }
      if (at !== null) {
        now.onMapClick?.(at);
      }
    });
    map.current = instance;
    drawMarkers();
    drawCavers();
  };

  const drawMarkers = () => {
    if (size === null) {
      return;
    }
    markerSource.current.clear();
    // Finger-driven maps get bigger pins for the same reason OL interactions get wider
    // hit tolerances: the pointer is the size it is, whatever the stored point is.
    const radius = coarsePointer() ? 9 : 7;
    for (const marker of markers) {
      const feature = new Feature({
        geometry: new Point(toMapCoordinate(marker, size)),
      });
      feature.setId(marker.memberId);
      feature.setStyle(
        new Style({
          image: new CircleStyle({
            radius,
            fill: new Fill({ color: palette.station }),
            stroke: new Stroke({ color: palette.stroke, width: 2 }),
          }),
          text: new Text({
            text: shortNameOf(marker.station),
            font: '12px sans-serif',
            offsetY: -(radius + 8),
            fill: new Fill({ color: palette.station }),
            stroke: new Stroke({ color: palette.labelHalo, width: 3 }),
          }),
        }),
      );
      markerSource.current.addFeature(feature);
    }
  };

  const drawCavers = () => {
    if (size === null) {
      return;
    }
    caverSource.current.clear();
    const radius = coarsePointer() ? 9 : 7;
    for (const drawn of cavers ?? []) {
      const { marker } = drawn;
      const feature = new Feature({
        geometry: new Point(toMapCoordinate(marker, size)),
      });
      feature.setId(`caver-${marker.caver.caverId}`);
      // The fan rides the style, never the geometry: the dot's place on the picture is
      // the pin's, and only its pixels move aside so a party sharing a pin stays
      // separately visible and separately pressable. Displacement is y-up, the same
      // frame the fold computes offsets in; the label follows its dot by the same
      // offset, drawn in text-offset coordinates (y down).
      const [dx, dy] = marker.offsetPx;
      feature.setStyle(
        new Style({
          image: new CircleStyle({
            radius,
            displacement: [dx, dy],
            fill: new Fill({ color: drawn.color }),
            stroke: new Stroke({ color: palette.stroke, width: 2 }),
          }),
          text:
            drawn.label === null
              ? undefined
              : new Text({
                  text: drawn.label,
                  font: '12px sans-serif',
                  offsetX: dx,
                  offsetY: -dy - (radius + 8),
                  fill: new Fill({ color: drawn.color }),
                  stroke: new Stroke({ color: palette.labelHalo, width: 3 }),
                }),
        }),
      );
      caverSource.current.addFeature(feature);
    }
  };

  const onTargetRef = (el: HTMLDivElement | null) => {
    target.current = el;
    buildMap();
  };

  // Activation after mount: the first time builds, the later times re-measure — the pane
  // was display:none in between, so whatever size OL last measured is stale.
  useEffect(() => {
    if (!active) {
      return;
    }
    if (map.current === null) {
      buildMap();
    } else {
      map.current.updateSize();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- building is idempotent and guarded
  }, [active, size]);

  // The pins the fold hands over, redrawn whenever they change.
  useEffect(() => {
    drawMarkers();
    // eslint-disable-next-line react-hooks/exhaustive-deps -- drawing reads only these two
  }, [markers, size]);

  // The party, redrawn as the watch is re-read — every half minute while anybody is
  // underground, which is exactly why this clears and refills a source instead of
  // rebuilding the map around it.
  useEffect(() => {
    drawCavers();
    // eslint-disable-next-line react-hooks/exhaustive-deps -- drawing reads only these two
  }, [cavers, size]);

  // The overlay's press-a-row answer: glide the view to the named point. The zoom is the
  // reader's own — a row press says "show me where", not "throw away how closely I was
  // looking".
  useEffect(() => {
    if (focus == null || size === null || map.current === null) {
      return;
    }
    map.current.getView().animate({ center: toMapCoordinate(focus, size), duration: 300 });
    // eslint-disable-next-line react-hooks/exhaustive-deps -- a glide is asked for by the point
  }, [focus?.x, focus?.y, size]);

  if (failed) {
    // A missing image is a real state — the document's file may have been replaced or the
    // rendering may have failed — and a silent blank would read as an empty map.
    return <div data-testid={`${testId}-missing`}>{t('rastermap.imageMissing')}</div>;
  }

  return (
    <div
      ref={onTargetRef}
      role="img"
      aria-label={alt}
      data-testid={`${testId}-map`}
      style={{
        height,
        width: '100%',
        touchAction: 'none',
        // The one visual admission that a click now writes: the placement cursor.
        cursor: onMapClick === undefined ? undefined : 'crosshair',
      }}
    />
  );
}
