// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useRef, useState } from 'react';
import { Flex, InputNumber, Modal, Typography, theme } from 'antd';
import Feature from 'ol/Feature';
import Point from 'ol/geom/Point';
import VectorLayer from 'ol/layer/Vector';
import { fromLonLat, toLonLat } from 'ol/proj';
import VectorSource from 'ol/source/Vector';
import { Circle as CircleStyle, Fill, Stroke, Style } from 'ol/style';
import { useTranslation } from 'react-i18next';
import { clampLonLat } from '../../geo/coords.ts';
import PositionTextInput from '../coordinates/PositionTextInput.tsx';
import { useSmallMap } from '../map/useSmallMap.ts';

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
  const marker = useRef(new Feature<Point>());
  // OpenLayers draws outside React, so the marker reads the theme's colour through a ref.
  const { token } = theme.useToken();
  const markerColour = useRef(token.colorPrimary);
  markerColour.current = token.colorPrimary;
  const [lon, setLon] = useState(value?.[0] ?? DEFAULT_CENTER[0]);
  const [lat, setLat] = useState(value?.[1] ?? DEFAULT_CENTER[1]);

  useEffect(() => {
    if (open) {
      setLon(value?.[0] ?? DEFAULT_CENTER[0]);
      setLat(value?.[1] ?? DEFAULT_CENTER[1]);
    }
  }, [open, value]);

  const placeMarker = (nextLon: number, nextLat: number, recentre: boolean) => {
    marker.current.setGeometry(new Point(fromLonLat([nextLon, nextLat])));
    if (recentre) {
      pickerMap.current?.getView().setCenter(fromLonLat([nextLon, nextLat]));
    }
  };

  // Built once the dialog's content is mounted, kept through re-renders, and disposed with its
  // layers when the dialog closes.
  const { targetRef, map: pickerMap, ready } = useSmallMap({
    center: value ?? DEFAULT_CENTER,
    zoom: value ? 14 : 9,
    active: open,
    layers: () => [
      new VectorLayer({
        source: new VectorSource({ features: [marker.current] }),
        style: () => markerStyle(markerColour.current),
      }),
    ],
    onCreate: (map) => {
      map.on('singleclick', (event) => {
        const [clickLon, clickLat] = toLonLat(event.coordinate);
        setLon(Number(clickLon.toFixed(6)));
        setLat(Number(clickLat.toFixed(6)));
        placeMarker(clickLon, clickLat, false);
      });
    },
  });

  // A new map shows the point the dialog opened with.
  useEffect(() => {
    if (ready) {
      placeMarker(lon, lat, true);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- once per map built
  }, [ready]);

  return (
    <Modal
      open={open}
      title={t('settings.pointPicker.title')}
      width={720}
      destroyOnHidden
      onCancel={onCancel}
      onOk={() => onPick(clampLonLat(lon, lat))}
      okText={t('settings.pointPicker.use')}
    >
      <Flex vertical gap={12}>
        <Typography.Text type="secondary">{t('settings.pointPicker.hint')}</Typography.Text>
        <div ref={targetRef} style={{ height: 360, width: '100%' }} data-testid="point-picker-map" />
        <PositionTextInput
          onPosition={([typedLon, typedLat]) => {
            const [nextLon, nextLat] = clampLonLat(Number(typedLon.toFixed(7)), Number(typedLat.toFixed(7)));
            setLon(nextLon);
            setLat(nextLat);
            placeMarker(nextLon, nextLat, true);
          }}
        />
        <Flex gap={12} wrap>
          <label>
            {t('entrances.longitude')}
            <InputNumber
              value={lon}
              min={-180}
              max={180}
              onChange={(next) => {
                // A cleared field is a reader mid-edit, not a request for the prime meridian.
                if (typeof next !== 'number') {
                  return;
                }
                setLon(next);
                placeMarker(next, lat, true);
              }}
              step={0.0001}
              style={{ width: 180, marginInlineStart: 8 }}
            />
          </label>
          <label>
            {t('entrances.latitude')}
            <InputNumber
              value={lat}
              min={-90}
              max={90}
              onChange={(next) => {
                if (typeof next !== 'number') {
                  return;
                }
                setLat(next);
                placeMarker(lon, next, true);
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

function markerStyle(colour: string) {
  return new Style({
    image: new CircleStyle({
      radius: 8,
      fill: new Fill({ color: colour }),
      stroke: new Stroke({ color: '#fff', width: 2 }),
    }),
  });
}
