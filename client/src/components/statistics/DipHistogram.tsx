// SPDX-License-Identifier: AGPL-3.0-or-later
import { Segmented, Space, theme } from 'antd';
import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { EChartsOption } from 'echarts';

import { axisStyle, paletteFor } from './chartTheme.ts';
import { useECharts } from './useECharts.ts';

/**
 * How steeply a cave's passages run, banded from straight down to straight up.
 *
 * <p>
 * Unlike the rose beside it this is an ordinary bar chart and is drawn by the charting library:
 * nothing about a signed inclination needs arithmetic of its own, and a second hand-drawn figure
 * would be two places to fix one axis bug.
 * </p>
 * <p>
 * The bands run from −90° to +90° and are <em>not</em> the rose's bands, even though the server
 * describes both with the same record: the rose folds onto a half-circle of bearings and this runs
 * over signed inclinations. The edges are therefore taken from the bands handed in and never
 * assumed, because a chart that assumed the other range would label every bar wrongly and look
 * entirely convincing doing it.
 * </p>
 * <p>
 * A logarithmic count axis is offered because one band usually holds most of a cave: level
 * passage is common and vertical passage is not, and on a linear axis the steep bands of a mostly
 * horizontal cave are visually absent rather than small. On the logarithmic axis an empty band is
 * drawn as a gap rather than as a substituted one, because no passage at all and one passage are
 * different observations.
 * </p>
 */

/** One band of inclination and how much fell in it. Structural, so it can be built by hand in a test. */
export interface DipBin {
  fromDegrees: number;
  toDegrees: number;
  count: number;
  lengthM: number;
}

export interface DipHistogramProps {
  bins: DipBin[];
  /** Height in pixels. A chart has no intrinsic height; the container decides. */
  height?: number;
}

export default function DipHistogram({ bins, height = 260 }: DipHistogramProps) {
  const { t } = useTranslation();
  const { token } = theme.useToken();
  const [countAxis, setCountAxis] = useState<'linear' | 'log'>('linear');

  const option = useMemo<EChartsOption | null>(() => {
    // Nothing was measured. An axis with no bars reads as "this cave is level everywhere", which
    // is a claim nobody made; whoever mounted this says why there is nothing instead.
    if (bins.length === 0 || bins.every((b) => b.count === 0)) return null;

    const log = countAxis === 'log';
    const palette = paletteFor(token);

    return {
      xAxis: {
        type: 'category',
        name: t('statistics.orientation.dipAxis'),
        nameLocation: 'middle',
        nameGap: 28,
        data: bins.map((b) => t('statistics.orientation.degrees', { value: b.fromDegrees })),
        ...axisStyle(token),
      },
      yAxis: {
        type: log ? 'log' : 'value',
        name: t('statistics.orientation.legs'),
        min: log ? 1 : 0,
        ...axisStyle(token),
      },
      series: [
        {
          type: 'bar',
          name: t('statistics.orientation.legs'),
          itemStyle: { color: palette.series[0] },
          data: bins.map((b) => (log && b.count === 0 ? null : b.count)),
        },
      ],
      // Spread over the base tooltip rather than replacing it, so the themed background survives:
      // a top-level key set here replaces the shared one wholesale rather than merging into it.
      tooltip: { ...paletteTooltip(palette), trigger: 'axis' },
    };
  }, [bins, countAxis, token, t]);

  const container = useECharts(option);

  return (
    <Space orientation="vertical" size={8} style={{ width: '100%' }}>
      <Segmented
        size="small"
        aria-label={t('statistics.orientation.countAxis')}
        value={countAxis}
        onChange={(value) => setCountAxis(value as 'linear' | 'log')}
        options={[
          { value: 'linear', label: t('statistics.orientation.linearCount') },
          { value: 'log', label: t('statistics.orientation.logCount') },
        ]}
      />
      <div ref={container} data-testid="chart-dip" style={{ width: '100%', height }} />
    </Space>
  );
}

function paletteTooltip(palette: ReturnType<typeof paletteFor>) {
  return {
    backgroundColor: palette.tooltipBackground,
    borderColor: palette.axisLine,
    textStyle: { color: palette.tooltipText },
  };
}
