// SPDX-License-Identifier: AGPL-3.0-or-later
import { theme } from 'antd';
import { useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import type { EChartsOption } from 'echarts';

import { axisStyle, paletteFor, themedTooltip } from './chartTheme.ts';
import { useECharts } from './useECharts.ts';
import type { DistributionBar } from './registryDistribution.ts';

/**
 * The registry's own intervals, drawn as it worked them out.
 *
 * <p>
 * Nothing here counts anything. The intervals, their bounds and their counts arrive decided, and
 * the chart's whole job is not to quietly undo that. Two ways it could: by re-binning what it was
 * given, and by drawing an interval the registry joined as though it were an ordinary one. The
 * second is the subtle one — a joined interval is wider than its neighbours, so a reader who is
 * not told takes that width for a real feature of the distribution instead of for the registry
 * declining to publish a count too small to publish. So a joined interval says so in its label,
 * carries the other series colour, and says it again in the tooltip.
 * </p>
 * <p>
 * The axis is categorical on purpose. Once anything is joined the intervals are no longer of
 * equal width, and a bar chart cannot draw unequal widths without a chart shape this application
 * does not register; a row of labelled intervals claims nothing about width that the labels do
 * not already say.
 * </p>
 */
export function RegistryDistributionChart({
  bars,
  xLabel,
  logCount = false,
  height = 320,
  testId = 'chart-registry-distribution',
}: {
  bars: readonly DistributionBar[];
  xLabel: string;
  logCount?: boolean;
  height?: number;
  testId?: string;
}) {
  const { t } = useTranslation();
  const { token } = theme.useToken();

  const option = useMemo<EChartsOption | null>(() => {
    if (bars.length === 0) return null;
    const palette = paletteFor(token);
    const countLabel = t('registryStats.caves');

    return {
      grid: { left: 72, right: 24, top: 24, bottom: 84 },
      xAxis: {
        type: 'category',
        name: xLabel,
        nameLocation: 'middle',
        nameGap: 56,
        data: bars.map((bar) => bar.label),
        axisLabel: { rotate: 40, hideOverlap: true },
        ...axisStyle(token),
      },
      yAxis: {
        type: logCount ? 'log' : 'value',
        name: countLabel,
        // An empty interval is an observation, not missing data. On a logarithmic axis it cannot
        // be drawn at all, so it is left as a gap rather than moved to one, which would invent a
        // cave, or to zero, which would throw the axis away.
        min: logCount ? 1 : 0,
        minInterval: logCount ? undefined : 1,
        ...axisStyle(token),
      },
      series: [
        {
          type: 'bar',
          name: countLabel,
          data: bars.map((bar) => ({
            value: logCount && bar.count === 0 ? null : bar.count,
            itemStyle: { color: bar.merged ? palette.series[1] : palette.series[0] },
          })),
        },
      ],
      tooltip: {
        ...themedTooltip(palette),
        trigger: 'item',
        formatter: (params: unknown) => {
          const index = (params as { dataIndex: number }).dataIndex;
          const bar = bars[index];
          if (bar === undefined) return '';
          const counted = t('registryStats.binCount', { caves: bar.count });
          return bar.merged ? `${bar.label}<br/>${counted}<br/>${t('registryStats.joinedNote')}` : `${bar.label}<br/>${counted}`;
        },
      },
    };
  }, [bars, xLabel, logCount, token, t]);

  const container = useECharts(option);
  return <div ref={container} data-testid={testId} style={{ width: '100%', height }} />;
}
