// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useMemo, useState, type Key } from 'react';
import FeatureGrid from '@terrestris/react-geo/dist/Grid/FeatureGrid/FeatureGrid';
import { AimOutlined } from '@ant-design/icons';
import { Alert, Button, Flex, Typography } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import type Feature from 'ol/Feature';
import type Geometry from 'ol/geom/Geometry';
import { createEmpty, extend, isEmpty } from 'ol/extent';
import { unByKey } from 'ol/Observable';
import { Circle as CircleStyle, Fill, Stroke, Style } from 'ol/style';
import { useTranslation } from 'react-i18next';
import { getEntranceSource } from '../../map/entranceLayer.ts';
import { getFeatureTypeNameByCode, getSurfaceFeatureSource } from '../../map/featureLayer.ts';
import { fitExtent } from '../../map/mapContext.ts';
import { useWorkspaceStore } from '../../stores/workspaceStore.ts';

// The grids receive the same OL feature instances the map layers render, so
// feature-level styles double as on-map row highlighting. The grid's own layer
// (it always creates one) stays invisible via this empty style.
const INVISIBLE_STYLE = new Style({});

const HOVER_STYLE = new Style({
  image: new CircleStyle({
    radius: 12,
    fill: new Fill({ color: 'rgba(105, 177, 255, 0.25)' }),
    stroke: new Stroke({ color: '#69b1ff', width: 2 }),
  }),
  stroke: new Stroke({ color: '#69b1ff', width: 4 }),
  fill: new Fill({ color: 'rgba(105, 177, 255, 0.2)' }),
});

const SELECT_STYLE = new Style({
  image: new CircleStyle({
    radius: 12,
    fill: new Fill({ color: 'rgba(22, 119, 255, 0.25)' }),
    stroke: new Stroke({ color: '#1677ff', width: 2 }),
  }),
  stroke: new Stroke({ color: '#1677ff', width: 4 }),
  fill: new Fill({ color: 'rgba(22, 119, 255, 0.2)' }),
});

const keyOf = (feature: Feature<Geometry>) => String(feature.get('id'));

interface InViewGridProps {
  features: Feature<Geometry>[];
  attributeFilter: string[];
  columns: ColumnsType<Record<string, unknown>>;
  onPick: (feature: Feature<Geometry>) => void;
}

/** One "in view" table: row click selects, checkboxes multi-select with zoom-to. */
function InViewGrid({ features, attributeFilter, columns, onPick }: InViewGridProps) {
  const { t } = useTranslation();
  const [selectedKeys, setSelectedKeys] = useState<Key[]>([]);

  const byKey = useMemo(
    () => new globalThis.Map(features.map((f) => [keyOf(f), f])),
    [features],
  );

  // Bbox reloads replace the OL instances; re-apply the checked-row marker to
  // the fresh instances (and clear any stale hover overrides).
  useEffect(() => {
    for (const [key, feature] of byKey) {
      feature.setStyle(selectedKeys.includes(key) ? SELECT_STYLE : undefined);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- deliberate: only on instance turnover
  }, [byKey]);

  const setChecked = (keys: Key[]) => {
    for (const [key, feature] of byKey) {
      feature.setStyle(keys.includes(key) ? SELECT_STYLE : undefined);
    }
    setSelectedKeys(keys);
  };

  const zoomToSelected = () => {
    const extent = createEmpty();
    for (const key of selectedKeys) {
      const geometry = byKey.get(String(key))?.getGeometry();
      if (geometry) {
        extend(extent, geometry.getExtent());
      }
    }
    if (!isEmpty(extent)) {
      fitExtent(extent as [number, number, number, number]);
    }
  };

  return (
    <>
      <FeatureGrid<Record<string, unknown>>
        features={features}
        keyFunction={keyOf}
        attributeFilter={attributeFilter}
        columnDefs={columns}
        featureStyle={INVISIBLE_STYLE}
        size="small"
        pagination={false}
        rowSelection={{ selectedRowKeys: selectedKeys, onChange: setChecked }}
        // Overrides the grid's built-in row handlers: its click zoom uses a
        // bare fit() that over-zooms point extents; we select + fit sanely.
        onRow={(record: Record<string, unknown>) => {
          const feature = byKey.get(String(record.key));
          return {
            onClick: () => feature && onPick(feature),
            onMouseOver: () => feature?.setStyle(HOVER_STYLE),
            onMouseOut: () =>
              feature?.setStyle(
                selectedKeys.includes(String(record.key)) ? SELECT_STYLE : undefined,
              ),
          };
        }}
      />
      {selectedKeys.length > 0 && (
        <Flex align="center" justify="space-between" style={{ padding: '4px 8px' }}>
          <Typography.Text type="secondary" style={{ fontSize: 12 }}>
            {t('map.selectedCount', { count: selectedKeys.length })}
          </Typography.Text>
          <Button size="small" icon={<AimOutlined />} onClick={zoomToSelected}>
            {t('map.zoomToSelected')}
          </Button>
        </Flex>
      )}
    </>
  );
}

/** Live index of the viewport: entrances and features currently loaded. */
export default function FeatureListPanel() {
  const { t } = useTranslation();
  const setSelection = useWorkspaceStore((s) => s.setSelection);

  // The bbox loaders clear+refill the sources on moveend; a debounced nonce
  // re-derives the lists once a reload settles.
  const [nonce, setNonce] = useState(0);
  useEffect(() => {
    let timer: number | undefined;
    const bump = () => {
      window.clearTimeout(timer);
      timer = window.setTimeout(() => setNonce((n) => n + 1), 200);
    };
    const keys = [getEntranceSource(), getSurfaceFeatureSource()].map((s) => s.on('change', bump));
    return () => {
      unByKey(keys);
      window.clearTimeout(timer);
    };
  }, []);

  const byName = (a: Feature<Geometry>, b: Feature<Geometry>) =>
    String(a.get('name') ?? '').localeCompare(String(b.get('name') ?? ''));

  const entrances = useMemo(
    () => getEntranceSource().getFeatures().filter((f) => f.get('cluster') !== true).sort(byName),
    // eslint-disable-next-line react-hooks/exhaustive-deps -- nonce mirrors source content
    [nonce],
  );
  const clustered = useMemo(
    () => getEntranceSource().getFeatures().some((f) => f.get('cluster') === true),
    // eslint-disable-next-line react-hooks/exhaustive-deps -- nonce mirrors source content
    [nonce],
  );
  const mapFeatures = useMemo(
    () =>
      getSurfaceFeatureSource()
        .getFeatures()
        // Freshly drawn (unsaved) features have no server id yet — not listable.
        .filter((f) => f.get('id') !== undefined && f.get('pendingNew') !== true)
        .sort(byName),
    // eslint-disable-next-line react-hooks/exhaustive-deps -- nonce mirrors source content
    [nonce],
  );

  const nameColumn = (title: string): ColumnsType<Record<string, unknown>>[number] => ({
    title,
    dataIndex: 'name',
    key: 'name',
    ellipsis: true,
    render: (value: unknown) =>
      typeof value === 'string' && value ? value : <Typography.Text type="secondary">{t('features.unnamed')}</Typography.Text>,
  });

  const typeColumn: ColumnsType<Record<string, unknown>>[number] = {
    title: t('features.type'),
    dataIndex: 'typeCode',
    key: 'typeCode',
    width: 110,
    ellipsis: true,
    render: (value: unknown) => getFeatureTypeNameByCode(value) ?? '',
  };

  return (
    <div style={{ padding: 8, overflow: 'auto', height: '100%' }}>
      <Typography.Text strong>
        {t('map.entrances')} ({entrances.length})
      </Typography.Text>
      {clustered && (
        <Alert
          type="info"
          showIcon
          title={t('map.zoomInForEntrances')}
          style={{ margin: '8px 0' }}
        />
      )}
      {entrances.length > 0 && (
        <InViewGrid
          features={entrances}
          attributeFilter={['name']}
          columns={[nameColumn(t('caves.name'))]}
          onPick={(feature) => {
            const props = feature.getProperties();
            if (typeof props.id === 'string' && typeof props.caveId === 'string') {
              setSelection({ kind: 'entrance', entranceId: props.id, caveId: props.caveId });
            }
            const geometry = feature.getGeometry();
            if (geometry) {
              fitExtent(geometry.getExtent() as [number, number, number, number]);
            }
          }}
        />
      )}
      <Typography.Text strong style={{ display: 'block', marginTop: 12 }}>
        {t('map.features')} ({mapFeatures.length})
      </Typography.Text>
      {mapFeatures.length > 0 && (
        <InViewGrid
          features={mapFeatures}
          attributeFilter={['name', 'typeCode']}
          columns={[nameColumn(t('caves.name')), typeColumn]}
          onPick={(feature) => {
            const id = feature.get('id') as string | undefined;
            if (id) {
              setSelection({ kind: 'feature', featureId: id });
            }
            const geometry = feature.getGeometry();
            if (geometry) {
              fitExtent(geometry.getExtent() as [number, number, number, number]);
            }
          }}
        />
      )}
      {entrances.length === 0 && mapFeatures.length === 0 && !clustered && (
        <Typography.Paragraph type="secondary" style={{ marginTop: 8 }}>
          {t('map.nothingInView')}
        </Typography.Paragraph>
      )}
    </div>
  );
}
