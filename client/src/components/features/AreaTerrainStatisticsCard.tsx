// SPDX-License-Identifier: AGPL-3.0-or-later
import { Card, Empty, Table, Tabs, Typography } from 'antd';
import { useTranslation } from 'react-i18next';

import { useAreaTerrainStatistics, type TerrainMeasure } from '../../api/hooks/karstProfiles.ts';
import { TerrainClassBars, TerrainFacingRose } from '../statistics/TerrainClassCharts.tsx';

type ClassRow = TerrainMeasure['classes'][number];

const percent = (share: number | null | undefined) =>
  share === null || share === undefined ? '—' : `${Math.round(share * 100)} %`;

const ratio = (value: number | null | undefined) =>
  value === null || value === undefined ? '—' : value.toFixed(2);

/**
 * Where in the ground an area's entrances and sinkholes sit: one figure and one table per measure
 * (slope classes as paired bars, facing as a rose, position on the relief as paired bars), each class set beside the same class's share of the area's own
 * ground, with the ratio between them. Above one the caves sit there more often than the terrain
 * alone would suggest — the figure that guides prospecting.
 *
 * <p>
 * A measure with no computed picture says so in a sentence and shows no table, so that a missing
 * picture cannot be read as "no caves on steep ground". The card renders nothing on a refusal: the
 * route needs the right to read elevation, and a card of blanks would announce what the reader may
 * not be shown.
 * </p>
 */
export default function AreaTerrainStatisticsCard({
  featureId,
  geometryType,
}: {
  featureId: string;
  /** The feature's GeoJSON geometry type, or null when it carries none. */
  geometryType: string | null;
}) {
  const { t } = useTranslation();
  const enabled = geometryType === 'Polygon' || geometryType === 'MultiPolygon';
  const { data, isLoading, isError } = useAreaTerrainStatistics(featureId, enabled);

  if (!enabled || isError) return null;

  const reasonText = (reason: string | null | undefined) => {
    switch (reason) {
      case 'needs_slope':
        return t('terrainStats.needsSlope');
      case 'slope_unit':
        return t('terrainStats.slopeUnit');
      default:
        return t('terrainStats.noLayer');
    }
  };

  const table = (measure: TerrainMeasure) => (
    <>
      {measure.measure === 'aspect' ? (
        <div style={{ display: 'flex', justifyContent: 'center' }}>
          <TerrainFacingRose measure={measure} />
        </div>
      ) : (
        <TerrainClassBars measure={measure} />
      )}
      <Table<ClassRow>
        size="small"
        pagination={false}
        rowKey="code"
        dataSource={measure.classes}
        scroll={{ x: true }}
        columns={[
          {
            title: t('terrainStats.class'),
            dataIndex: 'code',
            render: (code: string) => t(`terrainStats.classes.${code}`),
          },
          {
            title: t('terrainStats.entrances'),
            render: (_: unknown, row) => `${row.entrances} (${percent(row.entranceShare)})`,
          },
          { title: `${t('terrainStats.entrances')} · ${t('terrainStats.ratio')}`, render: (_: unknown, row) => ratio(row.entranceRatio) },
          {
            title: t('terrainStats.sinkholes'),
            render: (_: unknown, row) => `${row.sinkholes} (${percent(row.sinkholeShare)})`,
          },
          { title: `${t('terrainStats.sinkholes')} · ${t('terrainStats.ratio')}`, render: (_: unknown, row) => ratio(row.sinkholeRatio) },
          { title: t('terrainStats.background'), render: (_: unknown, row) => percent(row.backgroundShare) },
        ]}
      />
      {measure.measure === 'aspect' && (
        <Typography.Paragraph type="secondary" style={{ marginTop: 8, marginBottom: 0 }}>
          {t('terrainStats.flatNote')}
        </Typography.Paragraph>
      )}
    </>
  );

  return (
    <Card title={t('terrainStats.title')} style={{ marginBottom: 16 }} data-testid="area-terrain-statistics">
      {isLoading || !data ? (
        <Empty description={t('terrainStats.loading')} />
      ) : !data.hasTerrain ? (
        <Empty description={t('terrainStats.noTerrainBuilt')} />
      ) : (
        <>
          <Typography.Paragraph type="secondary">
            {t('terrainStats.counts', {
              entrances: data.entrances,
              sinkholes: data.sinkholes,
              background: data.backgroundPoints,
            })}
          </Typography.Paragraph>
          <Tabs
            items={data.measures.map((measure) => ({
              key: measure.measure,
              label: t(`terrainStats.measure.${measure.measure}`),
              children: measure.available ? (
                table(measure)
              ) : (
                <Empty description={reasonText(measure.reason)} />
              ),
            }))}
          />
          <Typography.Paragraph type="secondary" style={{ marginBottom: 0 }}>
            {t('terrainStats.explained')}
          </Typography.Paragraph>
        </>
      )}
    </Card>
  );
}
