// SPDX-License-Identifier: AGPL-3.0-or-later
import { theme } from 'antd';
import { useMemo } from 'react';
import type { EChartsOption } from 'echarts';

import { axisStyle, paletteFor } from './chartTheme.ts';
import { useECharts } from './useECharts.ts';
import type { CorrelationLine } from './registryCorrelation.ts';

/**
 * The fitted relationship, drawn as a line and as nothing else.
 *
 * <p>
 * There are no observations here because the registry does not publish them: it answers this
 * question with the relationship, worked out over the caves the reader may read, and the caves
 * themselves stay where they are. So the picture is one line and the axes it runs between, and
 * the figures that say how much weight it carries — the goodness and the number of pairs — belong
 * beside it rather than in it. A cloud of points invented to sit under the line would be a
 * picture of caves nobody was shown.
 * </p>
 * <p>
 * The axes are logarithmic exactly when the fit was, because a relationship fitted through the
 * logarithms of two measurements is a straight line only in that form; drawn on linear axes it is
 * a curve, and drawing it straight there would be a different claim about the caves.
 * </p>
 * <p>
 * A line is drawn only when the caller has one to pass. Where the answer supported no fit this
 * component is handed nothing and draws nothing, and the reason is said in words by the page —
 * an empty pair of axes on its own reads as a relationship that is flat.
 * </p>
 */
export function RegistryCorrelationChart({
  line,
  logarithmic,
  xLabel,
  yLabel,
  height = 340,
  testId = 'chart-registry-correlation',
}: {
  line: CorrelationLine | null;
  logarithmic: boolean;
  xLabel: string;
  yLabel: string;
  height?: number;
  testId?: string;
}) {
  const { token } = theme.useToken();

  const option = useMemo<EChartsOption | null>(() => {
    if (line === null) return null;
    const palette = paletteFor(token);
    const axis = logarithmic ? ('log' as const) : ('value' as const);

    return {
      grid: { left: 80, right: 32, top: 24, bottom: 72 },
      xAxis: {
        type: axis,
        name: xLabel,
        nameLocation: 'middle',
        nameGap: 44,
        ...axisStyle(token),
      },
      yAxis: {
        type: axis,
        name: yLabel,
        nameLocation: 'middle',
        nameGap: 60,
        ...axisStyle(token),
      },
      series: [
        {
          type: 'line',
          name: yLabel,
          symbol: 'none',
          data: line.map(([x, y]) => [x, y]),
          lineStyle: { color: palette.series[0] },
          itemStyle: { color: palette.series[0] },
        },
      ],
    };
  }, [line, logarithmic, xLabel, yLabel, token]);

  const container = useECharts(option);
  return <div ref={container} data-testid={testId} style={{ width: '100%', height }} />;
}
