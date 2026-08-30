// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useRef } from 'react';
import { theme } from 'antd';

import * as echarts from 'echarts/core';
import { BarChart, BoxplotChart, LineChart, ScatterChart } from 'echarts/charts';
import { GridComponent, LegendComponent, TitleComponent, TooltipComponent } from 'echarts/components';
import { SVGRenderer } from 'echarts/renderers';
import type { EChartsOption } from 'echarts';

import { useUiPrefsStore } from '../../stores/uiPrefsStore.ts';
import { baseOption } from './chartTheme.ts';

// Registered once for the whole application rather than per chart: the registry is global, and
// registering the same piece repeatedly is wasted work on every mount. Only the pieces the charts
// in this application actually draw are pulled in — the whole library is roughly three times the
// size, and the unused two thirds would be carried by everyone.
echarts.use([
  BarChart,
  BoxplotChart,
  LineChart,
  ScatterChart,
  GridComponent,
  LegendComponent,
  TitleComponent,
  TooltipComponent,
  SVGRenderer,
]);

/**
 * Draws a chart into an element and keeps it in step with the theme, the container size and the
 * options handed to it.
 *
 * <p>
 * The chart instance is a mutable object owned by a library outside React, held in a ref rather
 * than in state — the same arrangement the map layers already use, and for the same reason: it is
 * not render output, and putting it in state would redraw the world every time it moved.
 * </p>
 * <p>
 * The SVG renderer is chosen deliberately over canvas. It draws real elements, so a test can
 * assert that an axis label exists and a box plot has one box per category instead of asserting
 * against a snapshot of a bitmap; and it stays sharp when a page is printed or zoomed.
 * </p>
 * <p>
 * Theme changes arrive as new tokens on an ordinary render — the root rebuilds its token set when
 * the preference flips — so the effect below re-applies options against the same instance instead
 * of tearing the chart down. A remount would lose the chart's own state and flash.
 * </p>
 */
export function useECharts(option: EChartsOption | null) {
  const container = useRef<HTMLDivElement | null>(null);
  const chart = useRef<echarts.ECharts | null>(null);
  const { token } = theme.useToken();
  const reduceMotion = useUiPrefsStore((s) => s.appearance.reduceMotion);

  useEffect(() => {
    const element = container.current;
    if (!element) return undefined;

    // A container with no measurable size is a real case, not only a test one: a chart inside a
    // collapsed panel or a tab that has not been opened measures zero, and the library then
    // renders nothing and says so on the console. Give it a size to lay out against and let the
    // observer below correct it the moment the container has one of its own.
    const hasSize = element.clientWidth > 0 && element.clientHeight > 0;
    const instance = echarts.init(element, undefined, {
      renderer: 'svg',
      ...(hasSize ? {} : { width: 640, height: 320 }),
    });
    chart.current = instance;

    // The container is sized by the layout around it, which React does not tell us about. This is
    // the observer the test setup already stubs, so it is safe to rely on here. `auto` matters:
    // without it a chart initialised at the fallback size above would keep that size for ever.
    const observer = new ResizeObserver(() => {
      if (element.clientWidth > 0 && element.clientHeight > 0) {
        instance.resize({ width: 'auto', height: 'auto' });
      }
    });
    observer.observe(element);

    return () => {
      observer.disconnect();
      instance.dispose();
      chart.current = null;
    };
  }, []);

  useEffect(() => {
    const instance = chart.current;
    if (!instance || !option) return;

    // `notMerge` because a series list that shrinks must actually shrink: merging would leave the
    // previous run's extra series drawn underneath the new ones.
    instance.setOption({ ...baseOption(token, reduceMotion), ...option }, { notMerge: true });
  }, [option, token, reduceMotion]);

  return container;
}
