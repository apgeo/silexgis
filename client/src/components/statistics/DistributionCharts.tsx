// SPDX-License-Identifier: AGPL-3.0-or-later
import { theme } from 'antd';
import { useCallback, useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import type { EChartsOption } from 'echarts';

import { axisStyle, paletteFor, themedTooltip } from './chartTheme.ts';
import { ccdf, fiveNumberSummary, histogram, logLogFit, powerLawExponent, type FiveNumber } from './distributions.ts';
import { useECharts, type EChartsHandlers } from './useECharts.ts';

/**
 * The four chart shapes this application draws, chosen as the corners of the difficulty space: if
 * these four work, the rest are variations. Each takes plain numbers and renders — no fetching, no
 * formatting decisions of its own beyond the axis labels it is given.
 *
 * <p>
 * Nothing here names a colour. Everything comes from the theme bridge, which is what keeps the
 * charts legible in both themes and what makes a stray hex literal a reviewable mistake rather
 * than an invisible one.
 * </p>
 */

interface SizedProps {
  /** Height in pixels. Charts have no intrinsic height; the container decides. */
  height?: number;
}

function Frame({
  option,
  height = 260,
  testId,
  handlers,
}: {
  option: EChartsOption | null;
  height?: number;
  testId: string;
  handlers?: EChartsHandlers;
}) {
  const container = useECharts(option, handlers);
  return <div ref={container} data-testid={testId} style={{ width: '100%', height }} />;
}

/**
 * Counts per interval, with an optional fitted power-law tail drawn over them.
 *
 * A logarithmic count axis is offered because cave lengths span orders of magnitude, and on a
 * linear axis every bin past the first is visually zero.
 */
export function HistogramChart({
  values,
  logCount = false,
  xLabel,
  height,
}: { values: number[]; logCount?: boolean; xLabel: string } & SizedProps) {
  const { t } = useTranslation();
  const { token } = theme.useToken();

  const option = useMemo<EChartsOption | null>(() => {
    const bins = histogram(values, 12);
    if (bins.length === 0) return null;

    const palette = paletteFor(token);
    const exponent = powerLawExponent(values, bins[0].to);

    // A logarithmic axis cannot show a count of zero, and an empty bin is a real observation
    // rather than missing data. Drawing it as null leaves a visible gap in the bar row, which is
    // honest; substituting 1 would invent a cave and substituting 0 would throw the axis away.
    const counts = bins.map((b) => (logCount && b.count === 0 ? null : b.count));

    return {
      xAxis: {
        type: 'category',
        name: xLabel,
        nameLocation: 'middle',
        nameGap: 28,
        data: bins.map((b) => Math.round(b.from).toLocaleString()),
        ...axisStyle(token),
      },
      yAxis: {
        type: logCount ? 'log' : 'value',
        name: t('karstStats.count'),
        min: logCount ? 1 : 0,
        ...axisStyle(token),
      },
      series: [
        { type: 'bar', name: t('karstStats.count'), data: counts },
        ...(exponent
          ? [{
              type: 'line' as const,
              name: t('karstStats.powerLawFit', { exponent: exponent.toFixed(2) }),
              symbol: 'none',
              lineStyle: { color: palette.series[1] },
              data: bins.map((b, i) => {
                const expected = bins[0].count * Math.pow(b.from / bins[0].from || 1, -(exponent - 1));
                return i === 0 || !Number.isFinite(expected) ? null : Math.max(expected, logCount ? 1 : 0);
              }),
            }]
          : []),
      ],
      legend: { bottom: 0, textStyle: { color: paletteFor(token).axisLabel } },
      tooltip: { trigger: 'axis' },
    };
  }, [values, logCount, xLabel, token, t]);

  return <Frame option={option} height={height} testId="chart-histogram" />;
}

/** Rank against value on two logarithmic axes — the plot the cave-length literature reads. */
export function CcdfChart({ values, xLabel, height }: { values: number[]; xLabel: string } & SizedProps) {
  const { t } = useTranslation();
  const { token } = theme.useToken();

  const option = useMemo<EChartsOption | null>(() => {
    const { points } = ccdf(values);
    if (points.length === 0) return null;

    return {
      xAxis: { type: 'log', name: xLabel, nameLocation: 'middle', nameGap: 28, ...axisStyle(token) },
      yAxis: { type: 'log', name: t('karstStats.rank'), ...axisStyle(token) },
      series: [{ type: 'scatter', symbolSize: 6, data: points.map((p) => [p.x, p.count]) }],
      tooltip: { trigger: 'item' },
    };
  }, [values, xLabel, token, t]);

  return <Frame option={option} height={height} testId="chart-ccdf" />;
}

/** Two measurements against each other on logarithmic axes, with the fitted line drawn over them. */
export function CorrelationChart({
  pairs,
  xLabel,
  yLabel,
  height,
}: { pairs: Array<[number, number]>; xLabel: string; yLabel: string } & SizedProps) {
  const { t } = useTranslation();
  const { token } = theme.useToken();

  const option = useMemo<EChartsOption | null>(() => {
    const usable = pairs.filter(([x, y]) => x > 0 && y > 0);
    if (usable.length === 0) return null;

    const fit = logLogFit(usable);
    const palette = paletteFor(token);
    const xs = usable.map(([x]) => x);
    const line = fit
      ? [Math.min(...xs), Math.max(...xs)].map((x) => [x, Math.exp(fit.intercept + fit.slope * Math.log(x))])
      : [];

    return {
      xAxis: { type: 'log', name: xLabel, nameLocation: 'middle', nameGap: 28, ...axisStyle(token) },
      yAxis: { type: 'log', name: yLabel, ...axisStyle(token) },
      series: [
        { type: 'scatter', name: yLabel, symbolSize: 7, data: usable },
        ...(fit
          ? [{
              type: 'line' as const,
              name: t('karstStats.regression', { slope: fit.slope.toFixed(2), r2: fit.r2.toFixed(2) }),
              symbol: 'none',
              lineStyle: { color: palette.series[1] },
              data: line,
            }]
          : []),
      ],
      legend: { bottom: 0, textStyle: { color: palette.axisLabel } },
      tooltip: { trigger: 'item' },
    };
  }, [pairs, xLabel, yLabel, token, t]);

  return <Frame option={option} height={height} testId="chart-correlation" />;
}

/** One box per category — the comparison figure, and the shape no lighter library provides. */
export function CategoryBoxChart({
  groups,
  yLabel,
  logScale = false,
  height,
}: { groups: Array<{ label: string; values: number[] }>; yLabel: string; logScale?: boolean } & SizedProps) {
  const { token } = theme.useToken();

  const option = useMemo<EChartsOption | null>(() => {
    const summarised = groups
      .map((g) => ({ label: g.label, summary: fiveNumberSummary(logScale ? g.values.filter((v) => v > 0) : g.values) }))
      .filter((g): g is { label: string; summary: FiveNumber } => g.summary !== null);
    if (summarised.length === 0) return null;

    return {
      xAxis: { type: 'category', data: summarised.map((g) => g.label), ...axisStyle(token) },
      yAxis: { type: logScale ? 'log' : 'value', name: yLabel, ...axisStyle(token) },
      series: [{
        type: 'boxplot',
        data: summarised.map((g) => [g.summary.min, g.summary.q1, g.summary.median, g.summary.q3, g.summary.max]),
      }],
      tooltip: { trigger: 'item' },
    };
  }, [groups, yLabel, logScale, token]);

  return <Frame option={option} height={height} testId="chart-box" />;
}

/**
 * One box per category, drawn from quartiles that were worked out elsewhere.
 *
 * The sibling above takes raw measurements and summarises them here. This one exists for the
 * figures a server has already reduced: it would be sent a five-number summary and re-deriving it
 * from a resent copy of every measurement is both wasteful and a second definition of a quartile,
 * which is how a number quoted in words comes to disagree with the box beside it.
 *
 * A category with no summary for a series draws a gap rather than a flat box on the axis floor,
 * because a slice of cave nobody measured is not a slice of passage with no size.
 */
export function SummaryBoxChart({
  categories,
  series,
  yLabel,
  height,
  testId = 'chart-summary-box',
}: {
  categories: string[];
  series: Array<{ name: string; summaries: Array<FiveNumber | null> }>;
  yLabel: string;
  testId?: string;
} & SizedProps) {
  const { token } = theme.useToken();

  const option = useMemo<EChartsOption | null>(() => {
    if (categories.length === 0 || series.every((s) => s.summaries.every((v) => v === null))) return null;

    const palette = paletteFor(token);

    return {
      xAxis: { type: 'category', data: categories, ...axisStyle(token) },
      yAxis: { type: 'value', name: yLabel, ...axisStyle(token) },
      series: series.map((s, i) => ({
        type: 'boxplot' as const,
        name: s.name,
        itemStyle: { borderColor: palette.series[i % palette.series.length] },
        // A category with no summary is given the renderer's own empty datum rather than a
        // fabricated box. Nothing is drawn there, which is the honest picture of a slice of cave
        // nobody measured.
        data: s.summaries.map((v) =>
          v === null ? ['-', '-', '-', '-', '-'] : [v.min, v.q1, v.median, v.q3, v.max]),
      })),
      legend: { bottom: 0, textStyle: { color: palette.axisLabel } },
      tooltip: { trigger: 'item' },
    } as EChartsOption;
  }, [categories, series, yLabel, token]);

  return <Frame option={option} height={height} testId={testId} />;
}

/**
 * A curve with a band behind it — what a simulation envelope and a depth profile both need.
 *
 * <p>
 * Every series accepts a null in place of a number, and a null is drawn as a hole rather than
 * joined across. That is not a convenience: a curve which is only known over part of its range —
 * a depth to a surface no elevation model covers, a band nobody simulated — asserts a value at
 * every distance the line crosses, so joining the two sides of a gap invents the middle. The
 * caller decides what is missing; this decides only that missing is drawn as missing.
 * </p>
 */
export function EnvelopeChart({
  x,
  curve,
  lower,
  upper,
  xLabel,
  yLabel,
  height,
  testId = 'chart-envelope',
  tooltipFormatter,
  onPointClick,
  onEmptyClick,
}: {
  x: number[];
  curve: readonly (number | null)[];
  lower: readonly (number | null)[];
  upper: readonly (number | null)[];
  xLabel: string;
  yLabel: string;
  testId?: string;
  /**
   * What to say about one reading when the pointer rests on it, composed by the caller because
   * only the caller has a translator and knows what the numbers are. Without one the chart falls
   * back to the library's own rendering of the raw values, which names no units and no reading.
   */
  tooltipFormatter?: (index: number) => string;
  /** A reading was pressed. The index is into the arrays above. */
  onPointClick?: (index: number) => void;
  /** The chart was pressed away from the curve. */
  onEmptyClick?: () => void;
} & SizedProps) {
  const { token } = theme.useToken();

  const option = useMemo<EChartsOption | null>(() => {
    if (x.length === 0) return null;
    const palette = paletteFor(token);

    // The band's thickness is unknown wherever either of its edges is, and stacking a thickness
    // onto an absent floor would draw the band from the axis instead of from the curve.
    const thickness = (i: number) => {
      const floor = lower[i];
      const ceiling = upper[i];
      return floor == null || ceiling == null ? null : ceiling - floor;
    };

    return {
      xAxis: { type: 'value', name: xLabel, nameLocation: 'middle', nameGap: 28, ...axisStyle(token) },
      yAxis: { type: 'value', name: yLabel, ...axisStyle(token) },
      series: [
        // The band is drawn as its floor stacked with its thickness, both transparent apart from
        // the upper one's fill — the standard way to get a filled range out of two line series.
        {
          type: 'line',
          stack: 'band',
          symbol: 'none',
          connectNulls: false,
          lineStyle: { opacity: 0 },
          data: x.map((v, i) => [v, lower[i] ?? null]),
        },
        {
          type: 'line',
          stack: 'band',
          symbol: 'none',
          connectNulls: false,
          lineStyle: { opacity: 0 },
          areaStyle: { color: palette.envelope },
          data: x.map((v, i) => [v, thickness(i)]),
        },
        {
          type: 'line',
          symbol: 'none',
          connectNulls: false,
          // The line itself is what answers a press. Drawn symbols would be the other way to make
          // readings pressable, and a profile of several hundred of them drawn as dots is a band
          // of ink rather than a curve. The press arrives as a position along the axis and not as
          // a reading — a point on a line is usually between two of them — so which reading it
          // names is worked out below rather than read off the event.
          triggerEvent: 'line',
          lineStyle: { color: palette.series[0], width: 2 },
          data: x.map((v, i) => [v, curve[i] ?? null]),
        },
      ],
      // Spread before the trigger: a top-level tooltip replaces the shared themed one rather than
      // merging into it, and a chart that sets only a trigger loses its colours with it.
      tooltip: {
        ...themedTooltip(palette),
        trigger: 'axis',
        ...(tooltipFormatter
          ? {
              formatter: (params: unknown) => {
                const first = Array.isArray(params) ? params[0] : params;
                const index = (first as { dataIndex?: number } | undefined)?.dataIndex;
                return typeof index === 'number' ? tooltipFormatter(index) : '';
              },
            }
          : {}),
      },
    };
  }, [x, curve, lower, upper, xLabel, yLabel, token, tooltipFormatter]);

  // Which reading a press on the curve names: the drawn one nearest along the axis. Readings the
  // caller left out are skipped rather than chosen — the curve has a hole where they are, so a
  // press cannot have landed on one, and answering with a reading that is not drawn would put a
  // mark somewhere the reader can see nothing.
  const pickNearest = useCallback(
    (along: number) => {
      if (!onPointClick) return;
      let nearest = -1;
      let smallest = Number.POSITIVE_INFINITY;
      for (let i = 0; i < x.length; i++) {
        if (curve[i] == null) continue;
        const gap = Math.abs(x[i] - along);
        if (gap < smallest) {
          smallest = gap;
          nearest = i;
        }
      }
      if (nearest >= 0) onPointClick(nearest);
    },
    [x, curve, onPointClick],
  );

  return (
    <Frame
      option={option}
      height={height}
      testId={testId}
      handlers={{ onPointClick, onPlotClick: pickNearest, onEmptyClick }}
    />
  );
}
