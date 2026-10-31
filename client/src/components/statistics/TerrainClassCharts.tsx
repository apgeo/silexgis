// SPDX-License-Identifier: AGPL-3.0-or-later
import { theme } from 'antd';
import { useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import type { EChartsOption } from 'echarts';

import type { TerrainMeasure } from '../../api/hooks/karstProfiles.ts';
import { axisStyle, paletteFor, themedTooltip } from './chartTheme.ts';
import { useECharts } from './useECharts.ts';

type ClassRow = TerrainMeasure['classes'][number];

/** The share of a class as a percentage, or null when nothing of that kind was read. */
const pct = (share: number | null | undefined) =>
  share === null || share === undefined ? null : Math.round(share * 1000) / 10;

/**
 * Slope classes (or any ordered class set) as paired bars: the share of the entrances and of the
 * sinkholes that fall in each class, beside the share of the area's own ground in the same class.
 * A bar taller than the ground's is a class where caves sit more often than chance would put them.
 * A kind with nothing read is left out, not drawn as zero.
 */
export function TerrainClassBars({ measure, height = 220 }: { measure: TerrainMeasure; height?: number }) {
  const { t } = useTranslation();
  const { token } = theme.useToken();

  const option = useMemo<EChartsOption | null>(() => {
    if (measure.classes.length === 0) return null;
    const palette = paletteFor(token);
    const bar = (name: string, color: string, pick: (r: ClassRow) => number | null) => ({
      type: 'bar' as const,
      name,
      itemStyle: { color },
      data: measure.classes.map(pick),
    });
    return {
      xAxis: {
        type: 'category',
        data: measure.classes.map((c) => t(`terrainStats.classes.${c.code}`)),
        ...axisStyle(token),
      },
      yAxis: { type: 'value', name: '%', min: 0, ...axisStyle(token) },
      series: [
        bar(t('terrainStats.entrances'), palette.series[0], (r) => pct(r.entranceShare)),
        bar(t('terrainStats.sinkholes'), palette.series[1], (r) => pct(r.sinkholeShare)),
        bar(t('terrainStats.background'), palette.unclassified, (r) => pct(r.backgroundShare)),
      ],
      legend: { bottom: 0, textStyle: { color: palette.axisLabel } },
      tooltip: { trigger: 'axis', ...themedTooltip(palette) },
    };
  }, [measure, token, t]);

  const container = useECharts(option);
  return (
    <div
      ref={container}
      role="img"
      aria-label={t('terrainStats.chartLabel', { measure: t(`terrainStats.measure.${measure.measure}`) })}
      data-testid={`chart-terrain-${measure.measure}`}
      style={{ width: '100%', height }}
    />
  );
}

const VIEW = 240;
const CENTRE = VIEW / 2;
const RADIUS = 84;
/** Compass order, clockwise from north: the order the facing classes arrive in. */
const SECTORS = ['n', 'ne', 'e', 'se', 's', 'sw', 'w', 'nw'] as const;

/** Where a share lies on the spoke of sector `i`, with the outermost ring standing for `scale`. */
function spokePoint(i: number, share: number, scale: number): [number, number] {
  const angle = (i * 45 * Math.PI) / 180;
  const r = scale === 0 ? 0 : (share / scale) * RADIUS;
  return [CENTRE + r * Math.sin(angle), CENTRE - r * Math.cos(angle)];
}

/**
 * The eight facings as a rose: the ground of the area as a filled shape, the entrances and the
 * sinkholes as outlines over it, north at the top. All three share one scale, the largest share in
 * view, so a lobe that sticks out past the ground's is a facing caves favour. Drawn by hand as
 * plain SVG: eight spokes need no charting library, and the shapes can be asserted in a test.
 */
export function TerrainFacingRose({ measure, size = 260 }: { measure: TerrainMeasure; size?: number }) {
  const { t } = useTranslation();
  const { token } = theme.useToken();
  const palette = paletteFor(token);

  const byCode = new Map(measure.classes.map((c) => [c.code, c]));
  const rows = SECTORS.map((code) => byCode.get(code));
  if (rows.every((r) => r === undefined)) return null;

  const series = [
    { key: 'background', label: t('terrainStats.background'), color: palette.unclassified, fill: true, pick: (r?: ClassRow) => r?.backgroundShare },
    { key: 'entrances', label: t('terrainStats.entrances'), color: palette.series[0], fill: false, pick: (r?: ClassRow) => r?.entranceShare },
    { key: 'sinkholes', label: t('terrainStats.sinkholes'), color: palette.series[1], fill: false, pick: (r?: ClassRow) => r?.sinkholeShare },
  ] as const;

  const all = series.flatMap((s) => rows.map((r) => s.pick(r) ?? 0));
  const scale = Math.max(0.05, ...all);

  return (
    <svg
      viewBox={`0 0 ${VIEW} ${VIEW}`}
      width={size}
      height={size}
      role="img"
      aria-label={t('terrainStats.chartLabel', { measure: t('terrainStats.measure.aspect') })}
      data-testid="chart-terrain-aspect"
    >
      {[0.5, 1].map((f) => (
        <circle key={f} cx={CENTRE} cy={CENTRE} r={RADIUS * f} fill="none" stroke={palette.splitLine} />
      ))}
      {SECTORS.map((code, i) => {
        const [x, y] = spokePoint(i, scale, scale);
        const [lx, ly] = spokePoint(i, scale * 1.22, scale);
        return (
          <g key={code}>
            <line x1={CENTRE} y1={CENTRE} x2={x} y2={y} stroke={palette.splitLine} />
            <text x={lx} y={ly} fontSize={10} textAnchor="middle" dominantBaseline="middle" fill={palette.axisLabel}>
              {t(`terrainStats.classes.${code}`)}
            </text>
          </g>
        );
      })}
      {series.map((s) => {
        // A kind with no share anywhere was not read at all; an outline of zeros would say the
        // caves sit nowhere.
        if (rows.every((r) => s.pick(r) == null)) return null;
        const points = rows.map((r, i) => spokePoint(i, s.pick(r) ?? 0, scale).join(',')).join(' ');
        return (
          <polygon
            key={s.key}
            points={points}
            data-series={s.key}
            fill={s.fill ? palette.unclassified : 'none'}
            fillOpacity={s.fill ? 0.25 : 0}
            stroke={s.color}
            strokeWidth={2}
          />
        );
      })}
      <g fontSize={10} fill={palette.axisLabel}>
        {series.map((s, i) => (
          <g key={s.key} transform={`translate(4 ${VIEW - 38 + i * 12})`}>
            <rect width={8} height={8} fill={s.color} />
            <text x={12} y={8}>{s.label}</text>
          </g>
        ))}
      </g>
    </svg>
  );
}
