// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useRef, useState, type ReactNode } from 'react';
import { Empty, Typography } from 'antd';
import type { FeatureLike } from 'ol/Feature';
import type Feature from 'ol/Feature';
import Map from 'ol/Map';
import View from 'ol/View';
import GeoJSON from 'ol/format/GeoJSON';
import TileLayer from 'ol/layer/Tile';
import VectorLayer from 'ol/layer/Vector';
import { fromLonLat, transformExtent } from 'ol/proj';
import VectorSource from 'ol/source/Vector';
import XYZ from 'ol/source/XYZ';
import { Circle as CircleStyle, Fill, Stroke, Style } from 'ol/style';
import { useTranslation } from 'react-i18next';
import { useCampAreasMap, useTripLogMap, type CalendarEntry } from '../../api/hooks.ts';
import { surfaceFeaturePalette, tripPalette as palette } from '../../map/markerPalette.ts';

/** Braşov: the middle of the karst this application was written for. */
const DEFAULT_CENTER: [number, number] = [25.6, 45.65];

/** Where the trip worked: the shape it drew of itself. */
const whereStyle = new Style({
  image: new CircleStyle({
    radius: 6,
    fill: new Fill({ color: palette.sketch }),
    stroke: new Stroke({ color: palette.stroke, width: 2 }),
  }),
  stroke: new Stroke({ color: palette.sketch, width: 3 }),
  fill: new Fill({ color: palette.sketchFill }),
});

/** Where its party met: a different place, and hollow so it cannot be mistaken for the first. */
const meetingStyle = new Style({
  image: new CircleStyle({
    radius: 6,
    fill: new Fill({ color: palette.meetingCentre }),
    stroke: new Stroke({ color: palette.meeting, width: 3 }),
  }),
});

/**
 * A camp's working area: the shape somebody drew on the camp's plan, a region rather than a
 * place, drawn as a surface so it reads as ground the camp worked over and not as a position.
 */
const areaStyle = new Style({
  stroke: new Stroke({ color: surfaceFeaturePalette.line, width: 2, lineDash: [6, 4] }),
  fill: new Fill({ color: surfaceFeaturePalette.fill }),
});

/**
 * Which of the shapes this is, read from the shape's own word for itself.
 *
 * A trip states up to two and they mean different things — where the party worked against where
 * it gathered — and drawing them alike would put a car park where the reader read a cave; a camp
 * states one, its working area. The answer says which each one is for exactly that reason, so
 * nothing here guesses from the geometry.
 */
const styleFor = (feature: FeatureLike): Style => {
  switch (feature.get('kind')) {
    case 'meeting':
      return meetingStyle;
    case 'area':
      return areaStyle;
    default:
      return whereStyle;
  }
};

/** The rectangle the map is looking at, as the answer wants it: west,south,east,north in degrees. */
function viewBbox(map: Map): string | undefined {
  const size = map.getSize();
  if (!size || size[0] === 0 || size[1] === 0) {
    return undefined;
  }
  const extent = transformExtent(
    map.getView().calculateExtent(size),
    'EPSG:3857',
    'EPSG:4326',
  );
  // Whole degrees of precision would move the rectangle by tens of kilometres; six is more than
  // any view needs and keeps the request key from changing on sub-pixel drift.
  return extent.map((value) => value.toFixed(6)).join(',');
}

interface Props {
  /**
   * The rows the calendar is showing, whatever view is showing them. Only their identifiers are
   * used: this pane never reads a position out of a calendar row, because a calendar row does not
   * carry one and is not going to start.
   */
  entries: CalendarEntry[];
  /** The window the rows were read over, inclusive, `YYYY-MM-DD`. */
  from: string;
  to: string;
  /**
   * Whether the pane is really on screen. A map built against a container with no size measures
   * nothing and draws a blank tile grid that never repairs itself, so it is not built until the
   * page says the container is laid out — and it is built then, even though the container was
   * mounted earlier.
   */
  active: boolean;
  height?: number;
}

/**
 * Where the trips and camps in the days on screen actually are.
 *
 * **Three answers matched on the client, and none of them widened.** The rows come from the
 * calendar, which carries no position of any kind; the shapes come from the two map answers that
 * already serve exactly this question — the trips a reader may see whose shapes fall inside a
 * rectangle over a range of days, and the camps a reader may see whose working areas do — and
 * each walks the same rule about who may read its record over the same table its record lives
 * in. So a shape is drawn when the identifier it carries is one of the rows on screen, and every
 * narrowing the reader made to the record narrows the map with it, without a map answer having
 * to learn a single word of the calendar's vocabulary.
 *
 * **A camp is its working area and nothing more.** Its own page draws three things — the area,
 * its trips' sketches, and the entrances of the caves those trips name, the last withheld from a
 * reader who may not place them. Here only the first is asked for: the sketches belong to the
 * trips and arrive through the trip answer like any other trip's, and the entrances are not asked
 * for at all, so there is no second door for a withheld coordinate to leave by.
 *
 * **What it cannot draw, said here rather than discovered.** Club dates have no shape, so a
 * month whose records are all meetings draws nothing. And the rectangle asked for is the one on
 * screen and never the whole world — an answer over the world is capped and would be a scatter
 * of whichever rows sorted first, drawn as though it were everything — so a record outside the
 * opening view is drawn once the reader pans to it and not before.
 */
export default function CalendarMapPane({ entries, from, to, active, height = 320 }: Props) {
  const { t } = useTranslation();
  const target = useRef<HTMLDivElement | null>(null);
  const map = useRef<Map | null>(null);
  const source = useRef(new VectorSource());
  const fitted = useRef(false);
  const [bbox, setBbox] = useState<string | undefined>(undefined);
  // Counted as records and not as shapes, because one trip can state two of them and a reader
  // comparing this against the record is counting records. Held rather than read off the layer
  // at render time: the layer is filled from an effect, which is after the render that would
  // have read it.
  const [drawn, setDrawn] = useState(0);

  const trips = useTripLogMap(bbox, from, to, active);
  const camps = useCampAreasMap(bbox, from, to, active);
  const data = trips.data;
  const campData = camps.data;
  const isError = trips.isError || camps.isError;

  // The identifiers on screen, per family, because an identifier is only meaningful beside the
  // table it came from: a trip and a camp cannot share one, but a set that mixed them would be
  // asking the wrong answer about the right row. A trip states up to two shapes and both carry
  // the trip's own identifier, so these are sets of rows and not counts of anything drawable.
  const wantedTrips = new Set(entries.filter((e) => e.source === 'tripLog').map((e) => e.id));
  const wantedCamps = new Set(entries.filter((e) => e.source === 'expedition').map((e) => e.id));

  const draw = () => {
    source.current.clear();
    // The container is laid out only while the pane is shown, and a cached answer can arrive
    // before the first paint — so the map can have been built against a container measured 0x0.
    // Re-measuring here, after the render that laid it out, is what stops a later answer
    // un-hiding a blank tile grid. Cheap and idempotent otherwise.
    map.current?.updateSize();
    if (!data && !campData) {
      setDrawn(0);
      return;
    }
    const reader = new GeoJSON();
    const tripFeatures = data
      ? (reader.readFeatures(data, { featureProjection: 'EPSG:3857' }) as Feature[]).filter(
          (feature) => wantedTrips.has(String(feature.get('id'))),
        )
      : [];
    const campFeatures = campData
      ? (reader.readFeatures(campData, { featureProjection: 'EPSG:3857' }) as Feature[]).filter(
          (feature) => wantedCamps.has(String(feature.get('id'))),
        )
      : [];
    const features = [...campFeatures, ...tripFeatures];
    source.current.addFeatures(features);
    setDrawn(
      new Set(tripFeatures.map((feature) => String(feature.get('id')))).size +
        new Set(campFeatures.map((feature) => String(feature.get('id')))).size,
    );
    // Framed once, on the first answer that holds anything, and never again: the rectangle asked
    // for is the one on screen, so moving the view is what asks the next question — and a pane
    // that re-framed itself on every answer would chase its own request round the map and would
    // undo every pan the reader made.
    if (!fitted.current && features.length > 0) {
      const extent = source.current.getExtent();
      // An empty source reports an infinite extent, and fitting one throws.
      if (Number.isFinite(extent[0])) {
        fitted.current = true;
        map.current?.getView().fit(extent, { padding: [24, 24, 24, 24], maxZoom: 14 });
      }
    }
  };

  const buildMap = () => {
    if (map.current || !target.current || !active) {
      return;
    }
    const instance = new Map({
      target: target.current,
      layers: [
        new TileLayer({
          source: new XYZ({
            url: 'https://tile.openstreetmap.org/{z}/{x}/{y}.png',
            attributions: '© OpenStreetMap contributors',
          }),
        }),
        new VectorLayer({ source: source.current, style: styleFor }),
      ],
      view: new View({ center: fromLonLat(DEFAULT_CENTER), zoom: 9 }),
    });
    // Every rest after a move is a new question. Asking while the view is still moving would ask
    // several times for rectangles nobody looked at.
    instance.on('moveend', () => setBbox(viewBbox(instance)));
    // A map does not know how big it is at the moment it is built — it learns that from the
    // container once, asynchronously, and then again whenever the window is resized. Until it
    // knows, there is no rectangle to ask about and the pane asks nothing rather than guessing
    // one; this is what turns the first measurement into the first question.
    instance.on('change:size', () => setBbox(viewBbox(instance)));
    map.current = instance;
    setBbox(viewBbox(instance));
    draw();
  };

  const onTargetRef = (el: HTMLDivElement | null) => {
    target.current = el;
    buildMap();
  };

  // The map becomes buildable the moment the page says this pane is really on screen, which is
  // after its container was mounted.
  useEffect(() => {
    if (active) {
      buildMap();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- building is idempotent and guarded
  }, [active]);

  useEffect(() => {
    draw();
    // eslint-disable-next-line react-hooks/exhaustive-deps -- redraws when any answer changes
  }, [data, campData, entries]);

  // The only teardown site. Doing it from the target ref would run on every re-render, because a
  // callback ref with a fresh identity is detached and re-attached each time, and the map would
  // be destroyed and rebuilt — losing wherever the reader had panned to.
  useEffect(
    () => () => {
      map.current?.setTarget(undefined);
      // Every layer, source and interaction the map owns stays subscribed until the map is
      // disposed; dropping the reference alone leaks the whole graph for the life of the page.
      map.current?.dispose();
      map.current = null;
    },
    [],
  );

  /**
   * What the pane says under the map, which is three different sentences because only one of the
   * three states is a claim about anything.
   *
   * "No trip in these days has a position in view" is something the pane has to have been told. A
   * request that failed and a request not yet answered have told it nothing, and an empty map
   * captioned with that sentence would report an outage as a fact about the club's month — the
   * reader would pan away from a region satisfied there was nothing in it. So a failure says it
   * failed, and a first answer still on its way says nothing at all.
   */
  // "Answered" means both families have answered: a caption that counted the trips while the
  // camps were still on their way would report a number that is about to change.
  const answered = data !== undefined && campData !== undefined;
  let caption: ReactNode = null;
  if (isError && !answered) {
    caption = (
      <Empty
        image={Empty.PRESENTED_IMAGE_SIMPLE}
        description={t('calendar.mapFailed')}
        data-testid="calendar-map-failed"
      />
    );
  } else if (answered && drawn === 0) {
    caption = (
      <Empty
        image={Empty.PRESENTED_IMAGE_SIMPLE}
        description={t('calendar.mapEmpty')}
        data-testid="calendar-map-empty"
      />
    );
  } else if (answered) {
    caption = (
      <Typography.Paragraph
        type="secondary"
        style={{ marginTop: 8, marginBottom: 0 }}
        data-testid="calendar-map-drawn"
      >
        {t('calendar.mapDrawn', { count: drawn })}
      </Typography.Paragraph>
    );
  }

  return (
    <div data-testid="calendar-map-pane" style={{ marginTop: 12 }}>
      <div
        ref={onTargetRef}
        data-testid="calendar-map"
        style={{ width: '100%', height, borderRadius: 4, overflow: 'hidden' }}
      />
      {caption}
      <Typography.Paragraph type="secondary" style={{ marginTop: 8, marginBottom: 0 }}>
        {t('calendar.mapCaveat')}
      </Typography.Paragraph>
    </div>
  );
}
