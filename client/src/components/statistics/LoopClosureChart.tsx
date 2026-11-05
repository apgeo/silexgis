// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo } from 'react';
import { theme, Typography } from 'antd';
import type { EChartsOption } from 'echarts';
import { useTranslation } from 'react-i18next';
import type { SurveyLogLoop } from '../../api/hooks.ts';
import { formatFigure } from '../surveyUploads/viewer/formatFigure.ts';
import { axisStyle, paletteFor, themedTooltip } from './chartTheme.ts';
import { useECharts } from './useECharts.ts';

/** Each loop's misclosure as a share of its length, in the order the compiler listed the loops. */
export default function LoopClosureChart({ loops }: { loops: readonly SurveyLogLoop[] }) {
  const { t, i18n } = useTranslation();
  const { token } = theme.useToken();

  const option = useMemo<EChartsOption>(() => {
    const palette = paletteFor(token);
    return {
      grid: { left: 48, right: 16, top: 32, bottom: 40 },
      xAxis: {
        type: 'category',
        name: t('surveyViewer.figures.loopAxis'),
        nameLocation: 'middle',
        nameGap: 26,
        data: loops.map((_, i) => String(i + 1)),
        ...axisStyle(token),
      },
      yAxis: { type: 'value', name: t('surveyViewer.figures.loopErrorAxis'), min: 0, ...axisStyle(token) },
      series: [
        {
          type: 'bar',
          name: t('surveyViewer.figures.loopErrorAxis'),
          itemStyle: { color: palette.series[0] },
          data: loops.map((loop) => loop.relativeErrorPercent),
        },
      ],
      tooltip: {
        ...themedTooltip(palette),
        trigger: 'axis',
        formatter: (params: unknown) => {
          const index = (Array.isArray(params) ? (params[0] as { dataIndex: number }) : (params as { dataIndex: number })).dataIndex;
          const loop = loops[index]!;
          return t('surveyViewer.figures.loopTooltip', {
            n: index + 1,
            percent: formatFigure(loop.relativeErrorPercent, i18n.language, 2),
            error: formatFigure(loop.absoluteErrorM, i18n.language, 2),
            length: formatFigure(loop.lengthM, i18n.language),
            stations: loop.stationCount,
          });
        },
      },
    };
  }, [loops, token, t, i18n.language]);

  const container = useECharts(option);
  return (
    <div>
      <Typography.Text strong>{t('surveyViewer.figures.loopChart')}</Typography.Text>
      <div ref={container} data-testid="survey-viewer-loop-chart" style={{ width: '100%', height: 240 }} />
    </div>
  );
}
