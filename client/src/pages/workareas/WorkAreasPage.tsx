// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useMemo, useRef, useState } from 'react';
import { Alert, Breadcrumb, Button, Card, Empty, Flex, List, Spin, Typography } from 'antd';
import { EnvironmentOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import Map from 'ol/Map';
import View from 'ol/View';
import GeoJSON from 'ol/format/GeoJSON';
import TileLayer from 'ol/layer/Tile';
import VectorLayer from 'ol/layer/Vector';
import VectorSource from 'ol/source/Vector';
import XYZ from 'ol/source/XYZ';
import type Feature from 'ol/Feature';
import { Fill, Stroke, Style, Text } from 'ol/style';
import { transformExtent } from 'ol/proj';
import { ScaleLine, defaults as defaultControls } from 'ol/control';
import { useMapLayers, useWorkAreas } from '../../api/hooks.ts';
import { childrenOf, colourFor, extentOfAll, pathTo, topLevel } from '../../workareas/tree.ts';

/**
 * The overview of the ground this club works: every area at one level, told apart by colour and
 * named on the map, with the level beneath each one a click away.
 *
 * A map of its own rather than the workspace map. That one is a module-level singleton — one
 * instance for the whole application, holding the reader's layers, filters and camera — so
 * showing this view through it would take the map away from the map page and hand it back
 * scrolled somewhere else with different layers on. This canvas draws one thing and owns nothing.
 */
export default function WorkAreasPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { data, isLoading, isError } = useWorkAreas();
  const { data: layers } = useMapLayers();

  /** The area whose inside is being shown, or null at the top level. */
  const [openId, setOpenId] = useState<string | null>(null);

  const areas = useMemo(() => data?.items ?? [], [data]);
  const trail = useMemo(() => (openId ? pathTo(areas, openId) : []), [areas, openId]);
  const level = useMemo(
    () => (openId ? childrenOf(areas, openId) : topLevel(areas)),
    [areas, openId],
  );

  const mapDiv = useRef<HTMLDivElement>(null);
  const mapRef = useRef<Map | null>(null);
  const sourceRef = useRef<VectorSource | null>(null);

  // Built once. The level's shapes are swapped on the source below rather than by rebuilding the
  // map, so drilling in does not tear down and re-create a canvas the reader is looking at.
  useEffect(() => {
    if (!mapDiv.current || mapRef.current) {
      return;
    }
    const source = new VectorSource();
    const map = new Map({
      target: mapDiv.current,
      controls: defaultControls().extend([new ScaleLine()]),
      layers: [
        new VectorLayer({
          source,
          style: (feature) =>
            new Style({
              fill: new Fill({ color: `${feature.get('colour') as string}33` }),
              stroke: new Stroke({ color: feature.get('colour') as string, width: 2 }),
              text: new Text({
                text: (feature.get('name') as string | null) ?? '',
                font: '600 13px sans-serif',
                fill: new Fill({ color: '#1f1f1f' }),
                // The names sit over aerial imagery as often as over a plain background, so the
                // halo is what keeps them readable rather than the fill colour.
                stroke: new Stroke({ color: 'rgba(255,255,255,0.85)', width: 3 }),
                overflow: true,
              }),
            }),
        }),
      ],
      view: new View({ center: [0, 0], zoom: 2 }),
    });
    mapRef.current = map;
    sourceRef.current = source;
    return () => {
      map.setTarget(undefined);
      mapRef.current = null;
      sourceRef.current = null;
    };
  }, []);

  // The background, once the catalog arrives. Kept under the shapes by an explicit index, since
  // the vector layer was added first and would otherwise be painted beneath it.
  useEffect(() => {
    const map = mapRef.current;
    const base = layers?.find((l) => l.layerKind === 'xyz' && l.isBase);
    if (!map || !base || map.getLayers().getArray().some((l) => l.get('silexgis:overviewBase'))) {
      return;
    }
    const tile = new TileLayer({
      source: new XYZ({ url: base.urlTemplate, attributions: base.attribution ?? undefined }),
      zIndex: 0,
    });
    tile.set('silexgis:overviewBase', true);
    map.addLayer(tile);
  }, [layers]);

  // The shapes of the level being shown, and the camera framed on them.
  useEffect(() => {
    const map = mapRef.current;
    const source = sourceRef.current;
    if (!map || !source) {
      return;
    }
    const format = new GeoJSON();
    const features: Feature[] = [];
    level.forEach((area, index) => {
      if (!area.geometry) {
        // Listed beside the map but not drawn on it: an area nobody has outlined yet has no
        // boundary, and inventing one would put a shape on the map that nobody agreed to.
        return;
      }
      const feature = format.readFeature(
        { type: 'Feature', geometry: area.geometry, properties: {} },
        { dataProjection: 'EPSG:4326', featureProjection: 'EPSG:3857' },
      ) as Feature;
      feature.set('areaId', area.id);
      feature.set('name', area.name);
      feature.set('colour', colourFor(index));
      features.push(feature);
    });
    source.clear();
    source.addFeatures(features);

    const extent = extentOfAll(level);
    if (extent) {
      map.getView().fit(transformExtent(extent, 'EPSG:4326', 'EPSG:3857'), {
        padding: [40, 40, 40, 40],
        duration: 250,
        maxZoom: 14,
      });
    }
  }, [level]);

  // Clicking a shape does what clicking its row in the list does. Re-bound on every render rather
  // than memoised: the handler closes over the current level, and a stale one would drill into
  // whichever area used to be under that pixel.
  useEffect(() => {
    const map = mapRef.current;
    if (!map) {
      return undefined;
    }
    const onClick = (event: { pixel: number[] }) => {
      const hit = map.forEachFeatureAtPixel(
        event.pixel,
        (feature) => feature.get('areaId') as string | undefined,
      );
      const area = hit ? areas.find((a) => a.id === hit) : undefined;
      if (!area) {
        return;
      }
      // An area with nothing inside it is where drilling stops and the real map takes over.
      if (area.childCount > 0) {
        setOpenId(area.id);
      } else {
        navigate(`/map?area=${encodeURIComponent(area.id)}`);
      }
    };
    map.on('click', onClick as never);
    return () => map.un('click', onClick as never);
  });

  if (isLoading) {
    return <Flex justify="center" style={{ padding: 48 }}><Spin /></Flex>;
  }
  if (isError) {
    return <Alert type="error" message={t('workAreas.loadFailed')} style={{ margin: 24 }} />;
  }

  return (
    <Flex vertical gap={16} style={{ padding: 24, height: '100%' }}>
      <Flex justify="space-between" align="center" wrap gap={12}>
        <Typography.Title level={4} style={{ margin: 0 }}>{t('workAreas.title')}</Typography.Title>
        <Breadcrumb
          items={[
            { title: <a onClick={() => setOpenId(null)}>{t('workAreas.allAreas')}</a> },
            ...trail.map((area) => ({
              title: <a onClick={() => setOpenId(area.id)}>{area.name ?? t('workAreas.untitled')}</a>,
            })),
          ]}
        />
      </Flex>

      {data?.truncated ? <Alert type="warning" showIcon message={t('workAreas.truncated')} /> : null}

      {areas.length === 0 ? (
        <Empty description={t('workAreas.none')} />
      ) : (
        <Flex gap={16} align="stretch" style={{ flex: 1, minHeight: 360 }} wrap>
          <Card
            size="small"
            title={t('workAreas.thisLevel')}
            style={{ width: 280, flexShrink: 0, overflow: 'auto' }}
            styles={{ body: { padding: 0 } }}
          >
            <List
              size="small"
              dataSource={level}
              locale={{ emptyText: t('workAreas.noSubAreas') }}
              renderItem={(area, index) => (
                <List.Item
                  style={{ cursor: 'pointer', paddingInline: 12 }}
                  onClick={() => (area.childCount > 0
                    ? setOpenId(area.id)
                    : navigate(`/map?area=${encodeURIComponent(area.id)}`))}
                  actions={[
                    <Button
                      key="map"
                      type="link"
                      size="small"
                      icon={<EnvironmentOutlined />}
                      onClick={(event) => {
                        event.stopPropagation();
                        navigate(`/map?area=${encodeURIComponent(area.id)}`);
                      }}
                    >
                      {t('workAreas.openOnMap')}
                    </Button>,
                  ]}
                >
                  <List.Item.Meta
                    avatar={(
                      <span
                        aria-hidden
                        style={{
                          display: 'inline-block', width: 12, height: 12, borderRadius: 2,
                          background: colourFor(index),
                        }}
                      />
                    )}
                    title={area.name ?? t('workAreas.untitled')}
                    description={
                      area.childCount > 0
                        ? t('workAreas.subAreaCount', { count: area.childCount })
                        : area.description
                    }
                  />
                </List.Item>
              )}
            />
          </Card>
          <div
            ref={mapDiv}
            data-testid="work-areas-map"
            style={{ flex: 1, minWidth: 320, minHeight: 360, borderRadius: 4, overflow: 'hidden' }}
          />
        </Flex>
      )}
    </Flex>
  );
}
