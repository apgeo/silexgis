// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useRef, useState } from 'react';
import { Flex, InputNumber, Modal, Typography } from 'antd';
import Feature from 'ol/Feature';
import Map from 'ol/Map';
import View from 'ol/View';
import Point from 'ol/geom/Point';
import TileLayer from 'ol/layer/Tile';
import VectorLayer from 'ol/layer/Vector';
import { fromLonLat, toLonLat } from 'ol/proj';
import VectorSource from 'ol/source/Vector';
import XYZ from 'ol/source/XYZ';
import { Circle as CircleStyle, Fill, Stroke, Style } from 'ol/style';
import { useTranslation } from 'react-i18next';
import { clampLonLat } from '../../geo/coords.ts';

/** Braşov: the middle of the karst this application was written for. */
const DEFAULT_CENTER: [number, number] = [25.6, 45.65];

interface Props {
  open: boolean;
  value: [number, number] | null;
  onCancel: () => void;
  onPick: (lonLat: [number, number]) => void;
}

/**
 * Picks one point on a map.
 *
 * Builds its own throwaway map instance rather than borrowing the workspace one: the workspace
 * map is a module-level singleton whose layers own shared sources and bbox loaders, so attaching
 * a second view to it would have this dialog fighting the main map for the same data.
 */
export default function PointPickerModal({ open, value, onCancel, onPick }: Props) {
  const { t } = useTranslation();
  const mapTarget = useRef<HTMLDivElement | null>(null);
  const pickerMap = useRef<Map | null>(null);
  const marker = useRef(new Feature<Point>());
  const [lon, setLon] = useState(value?.[0] ?? DEFAULT_CENTER[0]);
  const [lat, setLat] = useState(value?.[1] ?? DEFAULT_CENTER[1]);

  useEffect(() => {
    if (open) {
      setLon(value?.[0] ?? DEFAULT_CENTER[0]);
      setLat(value?.[1] ?? DEFAULT_CENTER[1]);
    }
  }, [open, value]);

  const teardown = () => {
    pickerMap.current?.setTarget(undefined);
    pickerMap.current = null;
  };

  // Detach if the page navigates away while the dialog is open.
  useEffect(() => () => teardown(), []);

  const onMapTargetRef = (el: HTMLDivElement | null) => {
    mapTarget.current = el;
    if (!el) {
      teardown();
    }
  };

  const placeMarker = (nextLon: number, nextLat: number, recentre: boolean) => {
    marker.current.setGeometry(new Point(fromLonLat([nextLon, nextLat])));
    if (recentre) {
      pickerMap.current?.getView().setCenter(fromLonLat([nextLon, nextLat]));
    }
  };

  // The dialog mounts its children lazily, so the target div does not exist when `open` flips —
  // the map can only be built once the open transition has put the content in the document.
  const onAfterOpenChange = (visible: boolean) => {
    if (!visible) {
      teardown();
      return;
    }
    if (pickerMap.current || !mapTarget.current) {
      return;
    }

    const map = new Map({
      target: mapTarget.current,
      layers: [
        new TileLayer({
          source: new XYZ({
            url: 'https://tile.openstreetmap.org/{z}/{x}/{y}.png',
            attributions: '© OpenStreetMap contributors',
          }),
        }),
        new VectorLayer({
          source: new VectorSource({ features: [marker.current] }),
          style: new Style({
            image: new CircleStyle({
              radius: 8,
              fill: new Fill({ color: '#146262' }),
              stroke: new Stroke({ color: '#fff', width: 2 }),
            }),
          }),
        }),
      ],
      view: new View({ center: fromLonLat([lon, lat]), zoom: value ? 14 : 9 }),
    });
    map.on('singleclick', (event) => {
      const [clickLon, clickLat] = toLonLat(event.coordinate);
      setLon(Number(clickLon.toFixed(6)));
      setLat(Number(clickLat.toFixed(6)));
      placeMarker(clickLon, clickLat, false);
    });
    pickerMap.current = map;
    placeMarker(lon, lat, true);
  };

  return (
    <Modal
      open={open}
      title={t('settings.pointPicker.title')}
      width={720}
      destroyOnHidden
      afterOpenChange={onAfterOpenChange}
      onCancel={onCancel}
      onOk={() => onPick(clampLonLat(lon, lat))}
      okText={t('settings.pointPicker.use')}
    >
      <Flex vertical gap={12}>
        <Typography.Text type="secondary">{t('settings.pointPicker.hint')}</Typography.Text>
        <div ref={onMapTargetRef} style={{ height: 360, width: '100%' }} data-testid="point-picker-map" />
        <Flex gap={12} wrap>
          <label>
            {t('entrances.longitude')}
            <InputNumber
              value={lon}
              onChange={(next) => {
                const nextLon = next ?? 0;
                setLon(nextLon);
                placeMarker(nextLon, lat, true);
              }}
              step={0.0001}
              style={{ width: 180, marginInlineStart: 8 }}
            />
          </label>
          <label>
            {t('entrances.latitude')}
            <InputNumber
              value={lat}
              onChange={(next) => {
                const nextLat = next ?? 0;
                setLat(nextLat);
                placeMarker(lon, nextLat, true);
              }}
              step={0.0001}
              style={{ width: 180, marginInlineStart: 8 }}
            />
          </label>
        </Flex>
      </Flex>
    </Modal>
  );
}
