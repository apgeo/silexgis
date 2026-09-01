// SPDX-License-Identifier: AGPL-3.0-or-later
import { theme } from 'antd';
import { useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import type { EChartsOption } from 'echarts';

import { axisStyle, paletteFor } from './chartTheme.ts';
import { useECharts } from './useECharts.ts';

/**
 * How much of something sits at each height, drawn with height up the side rather than along the
 * bottom.
 *
 * <p>
 * Turned on its side on purpose. Every other histogram in this application runs left to right
 * because its axis is a quantity, but this one's axis is altitude, and a reader looking for the
 * storeys of a cave is matching what they see against a cross-section — which has height up the
 * page. Drawn the usual way round the same numbers are correct and unreadable.
 * </p>
 * <p>
 * The proposed levels are drawn as bands behind the bars and the reference heights as lines across
 * them. Both are shown as marks over the measurements rather than as bars of their own, because
 * neither is a measurement: one is an arithmetic reading of the histogram and the other is a height
 * that came from somewhere else entirely.
 * </p>
 */

/** One interval of height and how much fell in it. Structural, so a test can build one by hand. */
export interface ElevationHistogramBin {
  fromM: number;
  toM: number;
  count: number;
  weightM: number;
}

/** One proposed level. */
export interface ElevationHistogramBand {
  fromM: number;
  toM: number;
}

export interface ElevationHistogramProps {
  bins: ElevationHistogramBin[];
  /** Proposed levels drawn as bands behind the bars, if any. */
  bands?: ElevationHistogramBand[];
  /** Heights drawn as lines across the chart — spring altitudes, a base level. */
  referenceHeightsM?: number[];
  /** What the bars measure: metres of passage, or a count of entrances. */
  measure: 'weight' | 'count';
  height?: number;
  testId?: string;
}

export default function ElevationHistogram({
  bins,
  bands = [],
  referenceHeightsM = [],
  measure,
  height = 320,
  testId = 'chart-hypsometry',
}: ElevationHistogramProps) {
  const { t } = useTranslation();
  const { token } = theme.useToken();

  const option = useMemo<EChartsOption | null>(() => {
    if (bins.length === 0) return null;

    const palette = paletteFor(token);
    const values = bins.map((b) => (measure === 'weight' ? b.weightM : b.count));

    // Bands and reference lines hang off the first series as marks. A band is an interval of the
    // category axis and is named by the index of the bin its edge falls in, because a category
    // axis has no coordinate between its ticks.
    const indexOf = (metres: number) => {
      const i = bins.findIndex((b) => metres >= b.fromM && metres < b.toM);
      if (i >= 0) return i;
      return metres < bins[0].fromM ? 0 : bins.length - 1;
    };

    return {
      xAxis: {
        type: 'value',
        name:
          measure === 'weight'
            ? t('hypsometry.axisPassageMetres')
            : t('hypsometry.axisEntrances'),
        nameLocation: 'middle',
        nameGap: 28,
        ...axisStyle(token),
      },
      yAxis: {
        type: 'category',
        name: t('hypsometry.axisAltitude'),
        data: bins.map((b) => t('hypsometry.metres', { value: Math.round(b.fromM) })),
        ...axisStyle(token),
      },
      series: [
        {
          type: 'bar',
          name:
            measure === 'weight'
              ? t('hypsometry.axisPassageMetres')
              : t('hypsometry.axisEntrances'),
          itemStyle: { color: palette.series[0] },
          data: values,
          markArea:
            bands.length > 0
              ? {
                  silent: true,
                  itemStyle: { color: palette.envelope },
                  data: bands.map((band) => [
                    { yAxis: indexOf(band.fromM) },
                    { yAxis: indexOf(band.toM) },
                  ]),
                }
              : undefined,
          markLine:
            referenceHeightsM.length > 0
              ? {
                  silent: true,
                  symbol: 'none',
                  lineStyle: { color: palette.series[1], type: 'dashed' },
                  label: {
                    formatter: () => t('hypsometry.springLine'),
                    color: palette.axisLabel,
                  },
                  data: referenceHeightsM.map((metres) => ({ yAxis: indexOf(metres) })),
                }
              : undefined,
        },
      ],
      tooltip: {
        trigger: 'axis',
        axisPointer: { type: 'shadow' },
        backgroundColor: palette.tooltipBackground,
        borderColor: palette.axisLine,
        textStyle: { color: palette.tooltipText },
      },
    };
  }, [bins, bands, referenceHeightsM, measure, token, t]);

  const container = useECharts(option);

  return <div ref={container} data-testid={testId} style={{ width: '100%', height }} />;
}
