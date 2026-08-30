// SPDX-License-Identifier: AGPL-3.0-or-later
import { Segmented, Space, Typography, theme } from 'antd';
import { useId, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';

import { paletteFor } from './chartTheme.ts';
import {
  bearingPoint,
  foldAxisDegrees,
  rosePetals,
  roseScale,
  roseSummary,
  wedgePath,
  type RoseBin,
  type RoseWeighting,
} from './roseGeometry.ts';

/**
 * Which way a cave's passages run, drawn by hand.
 *
 * <p>
 * This is the one figure here that is not drawn by the charting library. What makes a rose right
 * or wrong is not how the wedges are painted but how the angles are treated, and that reasoning is
 * worth owning: the diagram is <em>axial</em>, so a passage surveyed from one end and the same
 * passage surveyed from the other fall in one sector, which is then drawn twice, opposed. The
 * mirrored half is not extra data — it is the same measurement shown on both sides, and the
 * diagram says so in words for a reader who cannot see the shape.
 * </p>
 * <p>
 * Length weighting and count weighting produce visibly different roses from the same cave: a
 * hundred short legs in a chamber outvote one long straight gallery when legs are counted, and
 * lose to it when metres are. Both are offered and the diagram always names the one in force,
 * because a rose that quietly picked one would be answering a question the reader did not ask.
 * </p>
 */

export interface RoseDiagramProps {
  /** The folded 0–180° sectors, as measured. */
  bins: RoseBin[];
  /** The mean trend for each weighting, where the sample supports one. */
  meanAxis?: Partial<Record<RoseWeighting, number | null>>;
  /** Which weighting the diagram opens on. */
  defaultWeighting?: RoseWeighting;
  /** Edge length of the square drawing, in pixels. */
  size?: number;
}

const VIEW = 240;
const CENTRE = { x: VIEW / 2, y: VIEW / 2 };
const OUTER_RADIUS = 86;
const LABEL_RADIUS = 104;
/** The bearings that get a full diameter drawn through the diagram. */
const SPOKE_BEARINGS = [0, 45, 90, 135];

export default function RoseDiagram({ bins, meanAxis, defaultWeighting = 'length', size = 260 }: RoseDiagramProps) {
  const { t } = useTranslation();
  const { token } = theme.useToken();
  const id = useId();
  const [weighting, setWeighting] = useState<RoseWeighting>(defaultWeighting);

  const petals = useMemo(() => rosePetals(bins, weighting), [bins, weighting]);
  const scale = useMemo(() => roseScale(bins, weighting), [bins, weighting]);
  const summary = useMemo(() => roseSummary(bins, weighting), [bins, weighting]);

  // Nothing was measured in any direction. A set of empty rings would read as "the passages of
  // this cave run nowhere", which is a claim nobody made — so the diagram is absent instead, and
  // whoever mounted it says why.
  if (!summary || petals.length === 0 || scale.max <= 0) return null;

  const palette = paletteFor(token);
  const percent = (fraction: number) => Math.round(fraction * 1000) / 10;
  const meanDegrees = meanAxis?.[weighting];
  const meanTrend = typeof meanDegrees === 'number' ? foldAxisDegrees(meanDegrees) : null;

  const values = {
    sectors: summary.sectorCount,
    width: summary.sectorWidthDegrees,
    from: summary.dominantFromDegrees,
    to: summary.dominantToDegrees,
    percent: percent(summary.dominantFraction),
  };
  // Two whole sentences rather than one sentence with the weighting interpolated into it: the word
  // for the weighting does not sit in the same place in every language this interface is written
  // in, and a sentence assembled from fragments reads as one in none of them.
  const description =
    weighting === 'length'
      ? t('statistics.orientation.roseDescriptionLength', values)
      : t('statistics.orientation.roseDescriptionCount', values);

  const cardinals: { bearing: number; label: string }[] = [
    { bearing: 0, label: t('statistics.orientation.north') },
    { bearing: 90, label: t('statistics.orientation.east') },
    { bearing: 180, label: t('statistics.orientation.south') },
    { bearing: 270, label: t('statistics.orientation.west') },
  ];

  return (
    <div data-testid="chart-rose">
      <Space orientation="vertical" size={8} style={{ width: '100%' }}>
        <Segmented
          size="small"
          aria-label={t('statistics.orientation.weighting')}
          value={weighting}
          onChange={(value) => setWeighting(value as RoseWeighting)}
          options={[
            { value: 'length', label: t('statistics.orientation.byLength') },
            { value: 'count', label: t('statistics.orientation.byCount') },
          ]}
        />
        <svg
          role="img"
          aria-labelledby={`${id}-title ${id}-desc`}
          viewBox={`0 0 ${VIEW} ${VIEW}`}
          preserveAspectRatio="xMidYMid meet"
          // A fixed view box and no measurement of the container: the diagram scales itself, so it
          // draws correctly inside a panel that has not been opened yet and needs no observer.
          style={{ width: '100%', maxWidth: size, height: 'auto', display: 'block' }}
        >
          <title id={`${id}-title`}>{t('statistics.orientation.rose')}</title>
          <desc id={`${id}-desc`}>{description}</desc>

          {scale.rings.map((ring) => (
            <circle
              key={`ring-${ring}`}
              cx={CENTRE.x}
              cy={CENTRE.y}
              r={(ring / scale.max) * OUTER_RADIUS}
              fill="none"
              stroke={palette.splitLine}
            />
          ))}

          {SPOKE_BEARINGS.map((bearing) => {
            const a = bearingPoint(bearing, OUTER_RADIUS, CENTRE);
            const b = bearingPoint(bearing + 180, OUTER_RADIUS, CENTRE);
            return <line key={`spoke-${bearing}`} x1={a.x} y1={a.y} x2={b.x} y2={b.y} stroke={palette.axisLine} />;
          })}

          {petals.map((petal) => (
            <path
              key={`${petal.fromDegrees}-${petal.mirrored}`}
              data-petal={petal.mirrored ? 'mirrored' : 'measured'}
              data-from={petal.fromDegrees}
              d={wedgePath(
                petal.fromDegrees,
                petal.toDegrees,
                (petal.fraction / scale.max) * OUTER_RADIUS,
                CENTRE,
              )}
              fill={palette.series[0]}
              fillOpacity={0.7}
              stroke={palette.series[0]}
            />
          ))}

          {meanTrend !== null && (
            <line
              data-testid="rose-mean-axis"
              x1={bearingPoint(meanTrend, OUTER_RADIUS, CENTRE).x}
              y1={bearingPoint(meanTrend, OUTER_RADIUS, CENTRE).y}
              x2={bearingPoint(meanTrend + 180, OUTER_RADIUS, CENTRE).x}
              y2={bearingPoint(meanTrend + 180, OUTER_RADIUS, CENTRE).y}
              stroke={palette.series[1]}
              strokeWidth={2}
              strokeDasharray="6 4"
            />
          )}

          {scale.rings.map((ring) => (
            <text
              key={`ring-label-${ring}`}
              x={CENTRE.x + 4}
              y={CENTRE.y - (ring / scale.max) * OUTER_RADIUS + 1}
              fill={palette.axisLabel}
              fontSize={9}
              textAnchor="start"
            >
              {t('statistics.orientation.ringLabel', { value: percent(ring) })}
            </text>
          ))}

          {cardinals.map(({ bearing, label }) => {
            const at = bearingPoint(bearing, LABEL_RADIUS, CENTRE);
            return (
              <text
                key={`cardinal-${bearing}`}
                x={at.x}
                y={at.y}
                fill={palette.axisLabel}
                fontSize={12}
                textAnchor="middle"
                dominantBaseline="middle"
              >
                {label}
              </text>
            );
          })}
        </svg>

        {meanTrend !== null && (
          <Typography.Text type="secondary">
            {t('statistics.orientation.meanAxis', { value: Math.round(meanTrend) })}
          </Typography.Text>
        )}
      </Space>
    </div>
  );
}
