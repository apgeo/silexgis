// SPDX-License-Identifier: AGPL-3.0-or-later
import type { GlobalToken } from 'antd';

/**
 * The one module in the chart code allowed to name a colour.
 *
 * <p>
 * Charts are drawn by a library that knows nothing about this application's theme, so every colour
 * it uses has to be handed to it. Doing that at each call site is how a chart ends up unreadable in
 * one of the two themes — the axis stays the colour it was written as while the ground underneath
 * it flips. So the mapping from theme token to chart colour lives here, once, and the chart
 * modules read tokens rather than colours. A grep for a hex literal anywhere else under this
 * directory should find nothing, and there is a test that says so.
 * </p>
 * <p>
 * The categorical series colours are derived from the theme's own accent rather than invented: a
 * palette picked independently drifts away from the product every time the theme is touched.
 * </p>
 */
export interface ChartPalette {
  /** Series colours, in the order series are added. */
  series: string[];
  axisLine: string;
  axisLabel: string;
  splitLine: string;
  title: string;
  tooltipBackground: string;
  tooltipText: string;
  /** The band drawn behind a curve to show a range. */
  envelope: string;
}

/**
 * Chart colours for the theme currently in force.
 *
 * Pass the token set from antd's own hook, so this follows a theme change without being told about
 * it — including the compact algorithm and any future token override, which a hard-coded palette
 * would silently ignore.
 */
export function paletteFor(token: GlobalToken): ChartPalette {
  return {
    series: [
      token.colorPrimary,
      token.colorWarning,
      token.colorSuccess,
      token.colorError,
      token.colorInfo,
      token.colorTextTertiary,
    ],
    axisLine: token.colorBorder,
    axisLabel: token.colorTextSecondary,
    splitLine: token.colorBorderSecondary,
    title: token.colorText,
    tooltipBackground: token.colorBgElevated,
    tooltipText: token.colorText,
    envelope: token.colorFillSecondary,
  };
}

/**
 * The chart options every chart in this application starts from: fonts, grid, tooltip and axis
 * styling taken from the theme, and animation switched off when the reader has asked for less
 * motion.
 *
 * <p>
 * `animation` is not cosmetic here. The application already offers a reduced-motion preference and
 * honours it through the antd token set; a chart library that animates regardless would be the one
 * place that ignores it.
 * </p>
 */
export function baseOption(token: GlobalToken, reduceMotion: boolean) {
  const palette = paletteFor(token);
  return {
    animation: !reduceMotion,
    color: palette.series,
    textStyle: {
      fontFamily: token.fontFamily,
      fontSize: token.fontSize,
      color: palette.title,
    },
    // Explicit margins rather than asking the library to fit the labels for us: version 6
    // deprecated that option and warns on the console for every chart that uses it, which the
    // browser-error sweep would (correctly) record as a defect. The margins below are sized for
    // the axis labels these charts actually draw.
    grid: { left: 64, right: 24, top: 24, bottom: 56 },
    tooltip: {
      backgroundColor: palette.tooltipBackground,
      borderColor: palette.axisLine,
      textStyle: { color: palette.tooltipText },
    },
  };
}

/** Axis styling shared by every chart, so two charts never disagree about what an axis looks like. */
export function axisStyle(token: GlobalToken) {
  const palette = paletteFor(token);
  return {
    axisLine: { lineStyle: { color: palette.axisLine } },
    axisLabel: { color: palette.axisLabel },
    splitLine: { lineStyle: { color: palette.splitLine } },
    nameTextStyle: { color: palette.axisLabel },
  };
}
