// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useRef } from 'react';
import { Alert, App, Checkbox, Flex, Form, Input, InputNumber, Select, theme } from 'antd';
import Feature from 'ol/Feature';
import Point from 'ol/geom/Point';
import VectorLayer from 'ol/layer/Vector';
import { fromLonLat, toLonLat } from 'ol/proj';
import VectorSource from 'ol/source/Vector';
import { Circle as CircleStyle, Fill, Stroke, Style } from 'ol/style';
import { useTranslation } from 'react-i18next';
import {
  fetchFeatureVersion,
  useCreateEntrance,
  useEntranceTypes,
  useUpdateEntrance,
  type Entrance,
  type EntranceWrite,
} from '../../api/hooks.ts';
import { isConcurrencyConflict } from '../../api/client.ts';
import { clampLonLat } from '../../geo/coords.ts';
import DialogHost from '../DialogHost.tsx';
import PositionTextInput from '../coordinates/PositionTextInput.tsx';
import { useSmallMap } from '../map/useSmallMap.ts';
import { entranceWriteProblem } from './entranceWriteProblems.ts';

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
  const marker = useRef(new Feature<Point>());
  // OpenLayers draws outside React, so the marker reads the theme's colour through a ref.
  const { token } = theme.useToken();
  const markerColour = useRef(token.colorPrimary);
  markerColour.current = token.colorPrimary;

  const { data: entranceTypes } = useEntranceTypes();
  const createEntrance = useCreateEntrance(caveId);
  const updateEntrance = useUpdateEntrance(caveId);

  /**
   * The version of the entrance this form was opened at, read once when it opens and sent with the
   * save, so a change somebody else saved meanwhile is refused and offered back rather than
   * overwritten. Read here because the entrance list carries no version; pinned at opening because
   * the page goes on re-reading the record while the form is open, and a version read then would
   * present their change as already seen. A read that fails leaves the save unguarded, as it was.
   */
  const openedVersion = useRef<Promise<string | undefined> | null>(null);

  /**
   * Whether what is on screen is the snapped position rather than the surveyed one.
   *
   * The server already refuses to take a position back from an editor who may not see the exact
   * one — its update handler writes geometry, altitude and position quality only when the caller
   * has exact view, precisely so a save cannot echo blurred coordinates over precise stored ones.
   * This does not duplicate that guard. It stops the editor from *inviting* an edit the server
   * will then discard, which otherwise reports "saved" over a move that did not happen and leaves
   * somebody believing they have corrected an entrance. Only an existing entrance can be in this
   * state; a new one has no stored position to blur.
   */
  const positionIsBlurred = entrance?.approximateLocation === true;
  const blurred = useRef(positionIsBlurred);
  blurred.current = positionIsBlurred;

  // Built once the dialog's content is mounted and kept through every re-render; when the dialog
  // host moves the content between a modal and a side drawer, the same map moves with it.
  const { targetRef, map: miniMap, ready } = useSmallMap({
    center: defaultCenter,
    zoom: 14,
    active: open,
    layers: () => [
      new VectorLayer({
        source: new VectorSource({ features: [marker.current] }),
        style: () => markerStyle(markerColour.current),
      }),
    ],
    onCreate: (map) => {
      map.on('singleclick', (event) => {
        if (blurred.current) {
          return;
        }
        const [clickLon, clickLat] = toLonLat(event.coordinate);
        form.setFieldsValue({ lon: Number(clickLon.toFixed(6)), lat: Number(clickLat.toFixed(6)) });
        placeMarker(clickLon, clickLat, false);
      });
    },
  });

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
    openedVersion.current = entrance ? fetchFeatureVersion(entrance.id).catch(() => undefined) : null;
    // eslint-disable-next-line react-hooks/exhaustive-deps -- reinitialize only when the modal opens
  }, [open]);

  // A new map shows the entrance the form opened with.
  useEffect(() => {
    if (!ready) {
      return;
    }
    const lon = form.getFieldValue('lon') as number | undefined;
    const lat = form.getFieldValue('lat') as number | undefined;
    if (typeof lon === 'number' && typeof lat === 'number') {
      placeMarker(lon, lat, true);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- once per map built
  }, [ready]);

  const onOk = async () => {
    let values: EntranceFormValues;
    try {
      values = await form.validateFields();
    } catch {
      // The form already marks the field that is missing or wrong; there is nothing to send.
      return;
    }
    const [lon, lat] = clampLonLat(values.lon, values.lat);
    const body: EntranceWrite = {
      name: values.name ?? null,
      entranceTypeId: values.entranceTypeId,
      isMain: values.isMain,
      geom: { type: 'Point', coordinates: [lon, lat] },
      altitude: values.altitude ?? null,
      description: values.description ?? null,
      positionQuality: values.positionQuality as EntranceWrite['positionQuality'],
      // The form has no field for the survey date, yet the update replaces it with whatever is
      // sent; sending the stored one keeps a date a survey upload or an import recorded.
      surveyedAt: entrance?.surveyedAt ?? null,
    };
    try {
      if (entrance) {
        const ifMatch = (await openedVersion.current) ?? undefined;
        await updateEntrance.mutateAsync({ id: entrance.id, body, ifMatch });
      } else {
        await createEntrance.mutateAsync(body);
      }
      message.success(t('common.saved'));
      onClose();
    } catch (error) {
      // Somebody else's newer change is answered by the one reload-and-reapply prompt every
      // mutation shares; the dialog stays open so nothing typed is lost.
      if (!isConcurrencyConflict(error)) {
        message.error(entranceWriteProblem(error, t));
      }
    }
  };

  return (
    <DialogHost
      kind="entrance-edit"
      title={entrance ? t('entrances.edit') : t('entrances.add')}
      open={open}
      onCancel={onClose}
      onOk={() => void onOk()}
      okLoading={createEntrance.isPending || updateEntrance.isPending}
      width={720}
    >
      {positionIsBlurred && (
        <Alert
          type="info"
          showIcon
          title={t('entrances.positionBlurred')}
          style={{ marginBottom: 12 }}
        />
      )}
      <div ref={targetRef} style={{ height: 280, marginBottom: 16, background: token.colorFillTertiary }} />
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
        {/* Typed or pasted in any format; it fills the two fields below, which are what is saved —
            and is disabled with them, never showing a blurred position back. */}
        <div style={{ marginBottom: 12 }}>
          <PositionTextInput
            disabled={positionIsBlurred}
            onPosition={([lon, lat]) => {
              const [clampedLon, clampedLat] = clampLonLat(Number(lon.toFixed(7)), Number(lat.toFixed(7)));
              form.setFieldsValue({ lon: clampedLon, lat: clampedLat });
              placeMarker(clampedLon, clampedLat, true);
            }}
          />
        </div>
        <Flex gap={12} wrap>
          {/* Disabled together, and exactly the four the update handler declines to take from a
              caller without exact view. Leaving them editable makes a save look like it moved an
              entrance that the server left where it was. */}
          <Form.Item name="lon" label={t('entrances.longitude')} rules={[{ required: true }]} style={{ width: 180 }}>
            <InputNumber
              min={-180}
              max={180}
              step={0.00001}
              disabled={positionIsBlurred}
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
              disabled={positionIsBlurred}
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
            <InputNumber disabled={positionIsBlurred} style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item name="positionQuality" label={t('entrances.positionQuality')} style={{ width: 180 }}>
            <Select
              disabled={positionIsBlurred}
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
    </DialogHost>
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
