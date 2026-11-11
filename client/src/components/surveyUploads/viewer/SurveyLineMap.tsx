// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useRef } from 'react';
import { theme } from 'antd';
import Map from 'ol/Map';
import View from 'ol/View';
import GeoJSON from 'ol/format/GeoJSON';
import TileLayer from 'ol/layer/Tile';
import VectorLayer from 'ol/layer/Vector';
import VectorSource from 'ol/source/Vector';
import XYZ from 'ol/source/XYZ';
import { Stroke, Style } from 'ol/style';
import { useTranslation } from 'react-i18next';
import type { MapLayerInfo, SurveyLineWork } from '../../../api/hooks.ts';
import { backgroundLayerFor, type BackgroundLayer } from '../../../map/baseLayers.ts';
import { defaultBasemap, OPEN_TOPO_MAP } from './basemap.ts';

/**
 * A survey's line work drawn over a map of where it lies, fitted to the line.
 *
 * The line is whatever the server handed this page — to whoever may open the upload's sent files,
 * or holds its public link; nothing here decides what may be shown. The map is built once per line
 * and background and torn down with the page, so it holds no drawing context while nobody looks.
 */
export default function SurveyLineMap({
  lineWork,
  layers,
  height = 360,
}: {
  lineWork: SurveyLineWork;
  /** The installation's catalogue; undefined while unknown, which draws the fallback background. */
  layers: readonly MapLayerInfo[] | undefined;
  height?: number;
}) {
  const { t, i18n } = useTranslation();
  const { token } = theme.useToken();
  const target = useRef<HTMLDivElement | null>(null);
  const basemap = defaultBasemap(layers);

  useEffect(() => {
    if (!target.current) {
      return undefined;
    }
    const features = new GeoJSON().readFeatures(lineWork, {
      dataProjection: 'EPSG:4326',
      featureProjection: 'EPSG:3857',
    });
    const background: BackgroundLayer = basemap
      ? backgroundLayerFor(basemap, 0, i18n.language)
      : new TileLayer({ source: new XYZ(OPEN_TOPO_MAP) });
    background.setVisible(true);
    const source = new VectorSource({ features });
    const map = new Map({
      target: target.current,
      layers: [
        background,
        new VectorLayer({
          source,
          zIndex: 10,
          // A light casing under the line keeps it readable over any background, dark or busy.
          style: [
            new Style({ stroke: new Stroke({ color: 'rgba(255, 255, 255, 0.9)', width: 5 }) }),
            new Style({ stroke: new Stroke({ color: token.colorError, width: 2.5 }) }),
          ],
        }),
      ],
      view: new View({ center: [0, 0], zoom: 2 }),
    });
    const extent = source.getExtent();
    if (features.length > 0 && extent && extent.every(Number.isFinite)) {
      map.getView().fit(extent, { padding: [32, 32, 32, 32], maxZoom: 17 });
    }
    return () => map.setTarget(undefined);
  }, [lineWork, basemap, i18n.language, token.colorError]);

  return (
    <div
      ref={target}
      role="img"
      aria-label={t('surveyViewer.map.label')}
      data-testid="survey-viewer-map"
      data-basemap={basemap ? String(basemap.id) : 'opentopomap'}
      style={{ width: '100%', height }}
    />
  );
}
