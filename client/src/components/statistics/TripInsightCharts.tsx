// SPDX-License-Identifier: AGPL-3.0-or-later
import { theme } from 'antd';
import { useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import type { EChartsOption } from 'echarts';

import { axisStyle, paletteFor, themedTooltip } from './chartTheme.ts';
import { axisRoomFor, chartHeightForCategories, truncateLabel } from './tripInsights.ts';
import { useECharts } from './useECharts.ts';

/**
 * The two chart shapes a trip archive is read through: what happened when, and what it was made
 * of.
 *
 * <p>
 * Both are bars and lines, which the shared chart registry already carries — no shape is
 * registered for this page, because every extra one is carried by everybody who loads a chart
 * anywhere and neither of these needs one.
 * </p>
 * <p>
 * Nothing here names a colour and nothing here counts anything. Colours come from the theme
 * bridge and numbers arrive already worked out, so a chart is only ever wrong about how something
 * is drawn and never about what it says.
 * </p>
 */

/** One year of the counted trips, and how much ground had been covered by the end of it. */
export interface TripYearPoint {
  year: number;
  trips: number;
  areasSoFar: number;
}

/** One labelled value of a breakdown. Labels are resolved before they get here. */
export interface TripBreakdownPoint {
  label: string;
  count: number;
}

function Frame({ option, height, testId }: { option: EChartsOption | null; height: number; testId: string }) {
  const container = useECharts(option);
  return <div ref={container} data-testid={testId} style={{ width: '100%', height }} />;
}

/**
 * Trips per year as bars, with the distinct areas reached by the end of each year as a line over
 * them.
 *
 * The two are drawn together on purpose. Apart, they are volume and novelty; together they answer
 * the question a trip log cannot otherwise be asked — whether a club going out as much as ever is
 * still finding new ground, which is a rising line, or going back to the ground it knows, which
 * is a line flattening under bars that are not.
 */
export function TripYearChart({ years, height = 320 }: { years: readonly TripYearPoint[]; height?: number }) {
  const { t } = useTranslation();
  const { token } = theme.useToken();

  const option = useMemo<EChartsOption | null>(() => {
    if (years.length === 0) return null;
    const palette = paletteFor(token);

    return {
      // Explicit, because the shared margins are replaced wholesale by whatever a chart sets and
      // the second axis on the right needs room the shared ones do not leave.
      grid: { left: 64, right: 64, top: 40, bottom: 56 },
      xAxis: {
        type: 'category',
        name: t('tripStats.year'),
        nameLocation: 'middle',
        nameGap: 28,
        data: years.map((point) => String(point.year)),
        ...axisStyle(token),
      },
      yAxis: [
        { type: 'value', name: t('tripStats.trips'), minInterval: 1, ...axisStyle(token) },
        {
          type: 'value',
          name: t('tripStats.areasSoFar'),
          minInterval: 1,
          ...axisStyle(token),
          // The running curve has its own scale: distinct areas and trips are different
          // quantities, and forcing them onto one axis would flatten whichever is smaller into
          // the floor and say nothing. Only one of the two axes draws the horizontal lines,
          // though — after the shared axis styling, which carries a split line of its own and
          // would put this one back — or the grid is two sets of lines at two sets of ticks.
          splitLine: { show: false },
        },
      ],
      series: [
        {
          type: 'bar',
          name: t('tripStats.trips'),
          data: years.map((point) => point.trips),
          itemStyle: { color: palette.series[0] },
        },
        {
          type: 'line',
          name: t('tripStats.areasSoFar'),
          yAxisIndex: 1,
          symbol: 'circle',
          symbolSize: 6,
          data: years.map((point) => point.areasSoFar),
          lineStyle: { color: palette.series[1] },
          itemStyle: { color: palette.series[1] },
        },
      ],
      legend: { top: 0, textStyle: { color: palette.axisLabel } },
      tooltip: { ...themedTooltip(palette), trigger: 'axis' },
    };
  }, [years, token, t]);

  return <Frame option={option} height={height} testId="chart-trip-years" />;
}

/**
 * One breakdown of the counted trips, largest first, drawn sideways.
 *
 * Sideways because the categories are names — of people, of places, of purposes — and a name
 * turned on its side is a name nobody reads. The height follows the number of categories so that
 * four bars are not lost in a tall empty frame and forty are not squeezed into an unreadable one.
 *
 * One colour for every bar, not one per category: the palette carries six series colours and a
 * breakdown carries up to forty values, so colouring by category would repeat itself and invent a
 * kinship between the values that happened to share a colour. The label is what tells them apart.
 */
export function TripBreakdownChart({
  values,
  testId,
  countLabel,
}: {
  values: readonly TripBreakdownPoint[];
  testId: string;
  countLabel?: string;
}) {
  const { t } = useTranslation();
  const { token } = theme.useToken();

  const height = chartHeightForCategories(values.length);

  const option = useMemo<EChartsOption | null>(() => {
    if (values.length === 0) return null;
    const palette = paletteFor(token);

    // A category axis is drawn from the bottom up, so the order that puts the largest at the top
    // — where a reader looks first — is the reverse of the order it arrives in.
    const ordered = [...values].reverse();

    return {
      grid: { left: axisRoomFor(ordered.map((point) => point.label)), right: 32, top: 16, bottom: 48 },
      xAxis: {
        type: 'value',
        name: countLabel ?? t('tripStats.trips'),
        nameLocation: 'middle',
        nameGap: 28,
        minInterval: 1,
        ...axisStyle(token),
      },
      yAxis: {
        type: 'category',
        data: ordered.map((point) => truncateLabel(point.label)),
        ...axisStyle(token),
      },
      series: [
        {
          type: 'bar',
          name: countLabel ?? t('tripStats.trips'),
          data: ordered.map((point) => point.count),
          itemStyle: { color: palette.series[0] },
        },
      ],
      tooltip: { ...themedTooltip(palette), trigger: 'axis' },
    };
  }, [values, countLabel, token, t]);

  return <Frame option={option} height={height} testId={testId} />;
}
