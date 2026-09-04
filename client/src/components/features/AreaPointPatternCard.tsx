// SPDX-License-Identifier: AGPL-3.0-or-later
import { Alert, Card, Descriptions, Empty, Segmented, Space, Statistic, Typography } from 'antd';
import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';

import { useMapDensity, useMapPointPattern } from '../../api/hooks.ts';
import { EnvelopeChart } from '../statistics/DistributionCharts.tsx';
import RoseDiagram from '../statistics/RoseDiagram.tsx';

/**
 * How thickly the caves of one karst area sit, whether that arrangement differs from chance, and
 * which way it runs.
 *
 * <p>
 * Three readings that only mean anything together, and each of which is misleading alone. A density
 * says how many caves to the square kilometre and nothing about how they are placed; a
 * nearest-neighbour index says they are closer together than chance and nothing about how thick
 * that is; and neither can see a direction at all, so a row of entrances along a fault and a round
 * huddle of the same entrances are the same answer to both. The rose is the third, and it is the
 * one that names a cause.
 * </p>
 * <p>
 * <b>The cell size is the server's decision, not this card's.</b> The first request names no cell
 * at all, and the answer states the finest one this installation will publish — its
 * location-protection grid, below which a count per cell would start describing where an individual
 * cave is rather than how thick the karst is. The control offers multiples of that number, so it
 * cannot ask for something that will be refused, and the floor is shown rather than left to be
 * discovered by a failed request.
 * </p>
 * <p>
 * The point-pattern half is computed only over the caves this reader may place exactly. That is a
 * smaller set than the one the density counts, deliberately: a spacing measured between coordinates
 * rounded onto a protection lattice measures the lattice. The counts differ for that reason and the
 * card says so rather than letting a reader take them for the same n.
 * </p>
 */
export default function AreaPointPatternCard({
  featureId,
  featureTypeCode,
  geometry,
}: {
  featureId: string;
  featureTypeCode: string | null;
  geometry: { type?: string; coordinates?: unknown } | null;
}) {
  const { t } = useTranslation();
  const [cellMultiple, setCellMultiple] = useState(1);

  // Only the kinds that stand for a tract of karst, matching the statistics card beside this one.
  // Caves per square kilometre of a building is not a reading anybody wants offered.
  const enabled = featureTypeCode === 'karst_area' || featureTypeCode === 'massif';
  const bbox = useMemo(() => (enabled ? bboxOf(geometry) : null), [enabled, geometry]);

  // The floor is not known until an answer arrives, so the first request names no cell. Afterwards
  // the multiple is applied to the floor the server published.
  const probe = useMapDensity(bbox ? { bbox, areaId: featureId } : undefined);
  const floor = probe.data?.minimumCellMetres ?? null;

  const density = useMapDensity(
    bbox && floor && cellMultiple > 1
      ? { bbox, areaId: featureId, cellMetres: floor * cellMultiple }
      : undefined,
  );
  const grid = cellMultiple > 1 ? density.data : probe.data;

  const pattern = useMapPointPattern(
    bbox ? { bbox, areaId: featureId, simulations: 99, seed: 1 } : undefined,
  );

  // A refusal and an area nobody may place answer identically, so there is nothing honest to show.
  if (!enabled || !bbox || probe.isError) return null;

  if (probe.isLoading || !grid) {
    return (
      <Card title={t('karstDensity.title')} style={{ marginBottom: 16 }}>
        <Empty description={t('karstDensity.loading')} />
      </Card>
    );
  }

  const clark = pattern.data?.clarkEvans ?? null;
  const ripley = pattern.data?.ripley ?? null;
  const alignment = pattern.data?.alignment ?? null;

  return (
    <Card title={t('karstDensity.title')} style={{ marginBottom: 16 }} data-testid="area-point-pattern">
      <Alert
        type="info"
        showIcon
        style={{ marginBottom: 16 }}
        message={t('karstDensity.protectionNote', { metres: round(grid.protectionGridMetres) })}
        description={t('karstDensity.protectionNoteDetail', {
          counted: grid.featureCount,
          placed: pattern.data?.featureCount ?? 0,
        })}
      />

      <Space orientation="vertical" size="middle" style={{ width: '100%' }}>
        <div>
          <Typography.Text type="secondary">
            {t('karstDensity.cellSize', { floor: round(grid.minimumCellMetres) })}
          </Typography.Text>
          <br />
          <Segmented
            value={cellMultiple}
            onChange={(value) => setCellMultiple(Number(value))}
            data-testid="karst-density-cell"
            options={[1, 2, 4, 8].map((multiple) => ({
              label: `${round(grid.minimumCellMetres * multiple)} m`,
              value: multiple,
            }))}
          />
        </div>

        <Descriptions size="small" column={{ xs: 1, sm: 2, md: 3 }} bordered>
          <Descriptions.Item label={t('karstDensity.cellMetres')}>
            <span data-testid="karst-density-cell-value">{round(grid.cellMetres)}</span>
          </Descriptions.Item>
          <Descriptions.Item label={t('karstDensity.bandwidthMetres')}>
            {round(grid.bandwidthMetres)}
          </Descriptions.Item>
          <Descriptions.Item label={t('karstDensity.cellCount')}>{grid.cellCount}</Descriptions.Item>
          <Descriptions.Item label={t('karstDensity.featureCount')}>
            {grid.featureCount}
          </Descriptions.Item>
          <Descriptions.Item label={t('karstDensity.studyAreaKm2')}>
            {format(grid.studyAreaKm2, 1, t('karstDensity.notMeasured'))}
          </Descriptions.Item>
          <Descriptions.Item label={t('karstDensity.peakDensity')}>
            <span data-testid="karst-density-peak">
              {format(peakDensity(grid.cells), 2, t('karstDensity.notMeasured'))}
            </span>
          </Descriptions.Item>
        </Descriptions>

        {clark && (
          <Space size="large" wrap data-testid="karst-clark-evans">
            <Statistic
              title={t('karstDensity.clarkIndex')}
              value={clark.index}
              precision={3}
            />
            <Statistic title={t('karstDensity.clarkZ')} value={clark.zScore} precision={2} />
            <Statistic title={t('karstDensity.clarkP')} value={clark.pValue} precision={4} />
            <Typography.Text type="secondary" style={{ maxWidth: 320, display: 'inline-block' }}>
              {/* The index alone is unreadable. One is chance, below one is clustered, above one is
                  more evenly spaced than chance — and the reading is stated in words rather than
                  left to a number a reader has to remember the convention for. */}
              {clark.index < 1
                ? t('karstDensity.clustered')
                : clark.index > 1
                  ? t('karstDensity.spaced')
                  : t('karstDensity.random')}
            </Typography.Text>
          </Space>
        )}

        {ripley && ripley.steps.length > 0 && (
          <div data-testid="karst-ripley">
            <Typography.Text strong>
              {t('karstDensity.ripley', { simulations: ripley.simulations, seed: ripley.seed })}
            </Typography.Text>
            {/* A step whose band came back absent is drawn on the curve itself rather than at nought,
                so the shaded region collapses where nothing was simulated instead of sweeping the
                whole chart down to the axis and implying a band that reaches it. */}
            <EnvelopeChart
              x={ripley.steps.map((s) => s.radiusM)}
              curve={ripley.steps.map((s) => s.observedL)}
              lower={ripley.steps.map((s) => s.lowerL ?? s.observedL)}
              upper={ripley.steps.map((s) => s.upperL ?? s.observedL)}
              xLabel={t('karstDensity.radiusM')}
              yLabel={t('karstDensity.observedL')}
              height={260}
            />
            {ripley.simulations === 0 && (
              <Typography.Text type="secondary">{t('karstDensity.noEnvelope')}</Typography.Text>
            )}
          </div>
        )}

        {alignment && (
          <div data-testid="karst-alignment">
            <Typography.Text strong>
              {t('karstDensity.alignment', {
                pairs: alignment.pairCount,
                min: round(alignment.minSeparationM),
                max: round(alignment.maxSeparationM),
              })}
            </Typography.Text>
            {/* The same rose a cave's passage trends are drawn on, not a second one built for this:
                the bins are the same sectors and the diagram is already axial, which is what a
                joining line between two entrances needs — A to B and B to A are one alignment. */}
            <RoseDiagram
              bins={alignment.rose.bins}
              meanAxis={{
                length: alignment.rose.byLength.meanAxisDegrees,
                count: alignment.rose.byCount.meanAxisDegrees,
              }}
            />
          </div>
        )}

        {!clark && !alignment && (
          <Empty description={t('karstDensity.tooFewPlaceable', {
            minimum: pattern.data?.minimumFeatureCount ?? 0,
          })} />
        )}
      </Space>
    </Card>
  );
}

/** The outline's window, as the API's `west,south,east,north`. */
function bboxOf(geometry: { type?: string; coordinates?: unknown } | null): string | null {
  if (!geometry) return null;

  let west = Infinity;
  let south = Infinity;
  let east = -Infinity;
  let north = -Infinity;

  const walk = (node: unknown): void => {
    if (!Array.isArray(node)) return;
    if (typeof node[0] === 'number' && typeof node[1] === 'number') {
      west = Math.min(west, node[0]);
      east = Math.max(east, node[0]);
      south = Math.min(south, node[1]);
      north = Math.max(north, node[1]);
      return;
    }
    for (const child of node) walk(child);
  };
  walk(geometry.coordinates);

  if (!Number.isFinite(west) || !Number.isFinite(south) || west >= east || south >= north) {
    return null;
  }
  return [west, south, east, north].map((v) => v.toFixed(6)).join(',');
}

function peakDensity(cells: Array<{ kernelDensityPerKm2: number }>): number | null {
  return cells.length === 0 ? null : Math.max(...cells.map((c) => c.kernelDensityPerKm2));
}

function round(value: number): number {
  return Math.round(value);
}

function format(value: number | null | undefined, digits: number, fallback: string): string {
  return value === null || value === undefined ? fallback : value.toFixed(digits);
}
