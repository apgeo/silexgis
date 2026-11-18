// SPDX-License-Identifier: AGPL-3.0-or-later
import type { GlobalToken } from 'antd';
import type { EChartsOption } from 'echarts';
import type { TFunction } from 'i18next';
import type { CavePotentialRateCurve, CavePotentialTrainingInfo } from '../../api/hooks/cavePotential.ts';
import { axisStyle, paletteFor, themedTooltip } from '../../components/statistics/chartTheme.ts';

/** An area under a rate curve as it is written: three decimals, which is what the figure is good for. */
export function formatAuc(value: number): string {
  return value.toFixed(3);
}

/** A curve's points as percentages of the ground and of the occupied cells. */
function percentPoints(curve: CavePotentialRateCurve): [number, number][] {
  return curve.points.map((point) => [point.areaShare * 100, point.occurrenceShare * 100]);
}

/**
 * The two rate curves of a run on one pair of axes: along the bottom the ground taken from the
 * most favourable cell down, up the side the share of occupied cells found on it.
 *
 * Both are aggregates over every cell of the map — at most a hundred and one points each — and no
 * point of either stands for a cell or an entrance. The diagonal is what a map that knew nothing
 * would draw, and is there because a curve is read by how far above it it runs.
 */
export function rateCurvesOption(t: TFunction, token: GlobalToken, training: CavePotentialTrainingInfo): EChartsOption {
  const palette = paletteFor(token);
  const success = t('cavePotential.training.successRate');
  const prediction = t('cavePotential.training.predictionRate');
  const chance = t('cavePotential.training.chance');
  return {
    grid: { left: 56, right: 24, top: 40, bottom: 48 },
    legend: { top: 0, textStyle: { color: palette.axisLabel }, data: [success, prediction, chance] },
    xAxis: {
      type: 'value',
      name: t('cavePotential.training.areaAxis'),
      nameLocation: 'middle',
      nameGap: 28,
      min: 0,
      max: 100,
      ...axisStyle(token),
    },
    yAxis: {
      type: 'value',
      name: t('cavePotential.training.occurrenceAxis'),
      min: 0,
      max: 100,
      ...axisStyle(token),
    },
    tooltip: { ...themedTooltip(palette), trigger: 'axis' },
    series: [
      {
        type: 'line',
        name: success,
        showSymbol: false,
        lineStyle: { color: palette.series[0] },
        itemStyle: { color: palette.series[0] },
        data: percentPoints(training.successRate),
      },
      {
        type: 'line',
        name: prediction,
        showSymbol: false,
        lineStyle: { color: palette.series[1] },
        itemStyle: { color: palette.series[1] },
        data: percentPoints(training.predictionRate),
      },
      {
        type: 'line',
        name: chance,
        showSymbol: false,
        silent: true,
        lineStyle: { color: palette.unclassified, type: 'dashed' },
        itemStyle: { color: palette.unclassified },
        data: [
          [0, 0],
          [100, 100],
        ],
      },
    ],
  };
}
