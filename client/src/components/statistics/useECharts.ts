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

/** What a chart can report back about being pressed. All optional; most charts want none of them. */
export interface EChartsHandlers {
  /** A mark was pressed: the index of the datum under it, within its series' data. */
  onPointClick?: (dataIndex: number) => void;
  /**
   * Drawn line work was pressed somewhere that names no single datum, reported as a position on
   * the horizontal axis in the chart's own units.
   *
   * <p>
   * Why this is not the same thing as the handler above. A press on a drawn symbol carries the
   * index of the datum under it. A press on a <i>line</i> does not and cannot: the point pressed
   * generally lies between two readings rather than on either, so the library attaches the series
   * identity to the event and no index at all. A chart whose readings are joined into a curve and
   * drawn without symbols — which is every profile here, several hundred dots being a band of ink
   * rather than a line — is therefore only pressable through this. Reading the index straight off
   * the event is the obvious implementation, and it does not fail loudly; it never fires.
   * </p>
   * <p>
   * A position rather than an index, because only the caller holds the readings and can say which
   * of them a place between two of them should count as.
   * </p>
   */
  onPlotClick?: (value: number) => void;
  /** The chart was pressed somewhere with nothing on it. */
  onEmptyClick?: () => void;
}

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
export function useECharts(option: EChartsOption | null, handlers?: EChartsHandlers) {
  const container = useRef<HTMLDivElement | null>(null);
  const chart = useRef<echarts.ECharts | null>(null);
  const { token } = theme.useToken();
  const reduceMotion = useUiPrefsStore((s) => s.appearance.reduceMotion);

  // The handlers are read through a ref rather than captured by the effect below, so that a
  // caller passing a fresh arrow function on every render — which is every caller — does not tear
  // the chart down and build it again once per render.
  const latest = useRef<EChartsHandlers | undefined>(handlers);
  latest.current = handlers;

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

    // Pressing a point on a curve is how a chart on a page with no map on it says "this reading,
    // and show me where it is". The index is all that is passed on: what the point means is the
    // caller's business, and a chart that knew would be a chart that had to be told.
    instance.on(
      'click',
      (event: {
        dataIndex?: number;
        seriesIndex?: number;
        event?: { offsetX?: number; offsetY?: number };
      }) => {
        if (typeof event.dataIndex === 'number') {
          latest.current?.onPointClick?.(event.dataIndex);
          return;
        }

        // A press on a line rather than on a mark. The event names the series and carries no
        // reading, so the pressed pixel is turned back into a position on the series' own axes
        // and handed over as that; deciding which reading it is belongs to whoever has them.
        const at = event.event;
        if (typeof event.seriesIndex !== 'number' || typeof at?.offsetX !== 'number' || typeof at.offsetY !== 'number') {
          return;
        }
        const converted = instance.convertFromPixel({ seriesIndex: event.seriesIndex }, [at.offsetX, at.offsetY]);
        const along = Array.isArray(converted) ? converted[0] : undefined;
        if (typeof along === 'number' && Number.isFinite(along)) {
          latest.current?.onPlotClick?.(along);
        }
      },
    );
    // A press that lands on no mark at all. Taken from the renderer rather than the chart because
    // the chart is only told about presses that hit something, and "nothing was hit" is exactly
    // the event a caller needs to put a mark it drew elsewhere away again.
    instance.getZr().on('click', (event: { target?: unknown }) => {
      if (!event.target) {
        latest.current?.onEmptyClick?.();
      }
    });

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
