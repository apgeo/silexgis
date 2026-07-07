// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useRef } from 'react';
import { App, Checkbox, Flex, Form, Input, InputNumber, Modal, Select } from 'antd';
import Map from 'ol/Map';
import View from 'ol/View';
import Feature from 'ol/Feature';
import Point from 'ol/geom/Point';
import TileLayer from 'ol/layer/Tile';
import VectorLayer from 'ol/layer/Vector';
import { fromLonLat, toLonLat } from 'ol/proj';
import VectorSource from 'ol/source/Vector';
import XYZ from 'ol/source/XYZ';
import { Circle as CircleStyle, Fill, Stroke, Style } from 'ol/style';
import { useTranslation } from 'react-i18next';
import {
  useCreateEntrance,
  useEntranceTypes,
  useUpdateEntrance,
  type Entrance,
  type EntranceWrite,
} from '../../api/hooks.ts';
import { clampLonLat } from '../../geo/coords.ts';

interface EntranceEditorModalProps {
  caveId: string;
  entrance: Entrance | null; // null → create
  defaultCenter: [number, number]; // lon, lat
  open: boolean;
  onClose: () => void;
}

interface EntranceFormValues {
  name?: string;
  entranceTypeId: number;
  isMain: boolean;
  lon: number;
  lat: number;
  altitude?: number;
  positionQuality: string;
  description?: string;
}

/**
 * Entrance editor: click-to-place on a mini map, kept in two-way
 * sync with exact manual coordinate inputs — v1 parity for "manual cave coordinates".
 */
export default function EntranceEditorModal({
  caveId,
  entrance,
  defaultCenter,
  open,
  onClose,
}: EntranceEditorModalProps) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const [form] = Form.useForm<EntranceFormValues>();
  const mapTarget = useRef<HTMLDivElement>(null);
  const miniMap = useRef<Map>(null);
  const marker = useRef(new Feature<Point>());

  const { data: entranceTypes } = useEntranceTypes();
  const createEntrance = useCreateEntrance(caveId);
  const updateEntrance = useUpdateEntrance(caveId);

  const placeMarker = (lon: number, lat: number, recenter: boolean) => {
    marker.current.setGeometry(new Point(fromLonLat([lon, lat])));
    if (recenter) {
      miniMap.current?.getView().setCenter(fromLonLat([lon, lat]));
    }
  };

  useEffect(() => {
    if (!open) {
      return;
    }

    const initial: EntranceFormValues = entrance
      ? {
          name: entrance.name ?? undefined,
          entranceTypeId: Number(entrance.entranceTypeId),
          isMain: entrance.isMain,
          lon: Number(entrance.geom.coordinates[0]),
          lat: Number(entrance.geom.coordinates[1]),
          altitude: entrance.altitude ?? undefined,
          positionQuality: entrance.positionQuality,
          description: entrance.description ?? undefined,
        }
      : {
          entranceTypeId: Number(entranceTypes?.[0]?.id ?? 0),
          isMain: false,
          lon: defaultCenter[0],
          lat: defaultCenter[1],
          positionQuality: 'gps',
        };
    form.setFieldsValue(initial);

    const map = new Map({
      target: mapTarget.current ?? undefined,
      layers: [
        new TileLayer({ source: new XYZ({ url: 'https://tile.openstreetmap.org/{z}/{x}/{y}.png' }) }),
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
      view: new View({ center: fromLonLat([initial.lon, initial.lat]), zoom: 14 }),
    });
    map.on('singleclick', (event) => {
      const [lon, lat] = toLonLat(event.coordinate);
      form.setFieldsValue({ lon: Number(lon.toFixed(6)), lat: Number(lat.toFixed(6)) });
      placeMarker(lon, lat, false);
    });
    miniMap.current = map;
    placeMarker(initial.lon, initial.lat, true);
    window.setTimeout(() => map.updateSize(), 100); // modal animation settles first

    return () => {
      map.setTarget(undefined);
      miniMap.current = null;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps -- rebuild only when the modal opens
  }, [open]);

  const onOk = async () => {
    const values = await form.validateFields();
    const [lon, lat] = clampLonLat(values.lon, values.lat);
    const body: EntranceWrite = {
      name: values.name ?? null,
      entranceTypeId: values.entranceTypeId,
      isMain: values.isMain,
      geom: { type: 'Point', coordinates: [lon, lat] },
      altitude: values.altitude ?? null,
      description: values.description ?? null,
      positionQuality: values.positionQuality as EntranceWrite['positionQuality'],
      surveyedAt: null,
    };
    try {
      if (entrance) {
        await updateEntrance.mutateAsync({ id: entrance.id, body });
      } else {
        await createEntrance.mutateAsync(body);
      }
      message.success(t('common.saved'));
      onClose();
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  return (
    <Modal
      title={entrance ? t('entrances.edit') : t('entrances.add')}
      open={open}
      onCancel={onClose}
      onOk={() => void onOk()}
      confirmLoading={createEntrance.isPending || updateEntrance.isPending}
      width={720}
      destroyOnHidden
    >
      <div ref={mapTarget} style={{ height: 280, marginBottom: 16, background: '#e8ecef' }} />
      <Form<EntranceFormValues> form={form} layout="vertical">
        <Flex gap={12} wrap>
          <Form.Item name="name" label={t('caves.name')} style={{ width: 220 }}>
            <Input />
          </Form.Item>
          <Form.Item name="entranceTypeId" label={t('caves.type')} rules={[{ required: true }]} style={{ width: 200 }}>
            <Select options={entranceTypes?.map((x) => ({ value: Number(x.id), label: x.name }))} />
          </Form.Item>
          <Form.Item name="isMain" label={t('entrances.main')} valuePropName="checked">
            <Checkbox />
          </Form.Item>
        </Flex>
        <Flex gap={12} wrap>
          <Form.Item name="lon" label={t('entrances.longitude')} rules={[{ required: true }]} style={{ width: 180 }}>
            <InputNumber
              min={-180}
              max={180}
              step={0.00001}
              style={{ width: '100%' }}
              onChange={(lon) => {
                const lat = form.getFieldValue('lat') as number | undefined;
                if (typeof lon === 'number' && typeof lat === 'number') {
                  placeMarker(lon, lat, true);
                }
              }}
            />
          </Form.Item>
          <Form.Item name="lat" label={t('entrances.latitude')} rules={[{ required: true }]} style={{ width: 180 }}>
            <InputNumber
              min={-90}
              max={90}
              step={0.00001}
              style={{ width: '100%' }}
              onChange={(lat) => {
                const lon = form.getFieldValue('lon') as number | undefined;
                if (typeof lon === 'number' && typeof lat === 'number') {
                  placeMarker(lon, lat, true);
                }
              }}
            />
          </Form.Item>
          <Form.Item name="altitude" label={t('caves.fields.altitude')} style={{ width: 140 }}>
            <InputNumber style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item name="positionQuality" label={t('entrances.positionQuality')} style={{ width: 180 }}>
            <Select
              options={['unknown', 'gps', 'map', 'estimated'].map((v) => ({
                value: v,
                label: t(`entrances.qualityValues.${v}`),
              }))}
            />
          </Form.Item>
        </Flex>
        <Form.Item name="description" label={t('caves.fields.description')}>
          <Input.TextArea rows={2} />
        </Form.Item>
      </Form>
    </Modal>
  );
}
