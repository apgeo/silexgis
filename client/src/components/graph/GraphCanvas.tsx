// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useImperativeHandle, useRef, useState, type Ref } from 'react';
import { useTranslation } from 'react-i18next';
import type { GraphLabels, GraphRenderer, LayoutKind, Point } from '../../graph/renderer.ts';
import { loadRenderer, type RendererKind } from '../../graph/rendererRegistry.ts';
import type { NodeKey } from '../../graph/types.ts';
import type { GraphTheme } from '../../graph/visuals.ts';
import type { GraphView } from '../../graph/view.ts';

/** What the page can ask of the drawing without owning it. */
export interface GraphCanvasHandle {
  fitView: () => void;
  focus: (key: NodeKey) => void;
}

export interface GraphCanvasProps {
  view: GraphView;
  labels: GraphLabels;
  theme: GraphTheme;
  layout: LayoutKind;
  rendererKind: RendererKind;
  /** The node just expanded: nodes that join are placed around it. */
  anchorKey: NodeKey | null;
  onNodeClick: (key: NodeKey) => void;
  onNodeDoubleClick: (key: NodeKey) => void;
  onEdgeClick: (key: string) => void;
  onCanvasClick: () => void;
  ref?: Ref<GraphCanvasHandle>;
}

/** The hook automation reads node positions through; present only in a browser driven by a test. */
interface GraphTestHook {
  positions: () => Record<NodeKey, Point>;
}
declare global {
  interface Window {
    __silexgisGraph?: GraphTestHook;
  }
}

/**
 * Hosts a renderer in an element. The renderer is a mutable object owned by a drawing library, so
 * it lives in a ref and in the effect that creates and destroys it, never in React state — the
 * same arrangement the map and the charts use. Effects below push changes to it: a new view, a new
 * theme, a new arrangement; none of them recreates it, which is what keeps nodes where they are.
 *
 * The element carries data attributes (counts, selection, arrangement, how much has been drawn)
 * that a browser test reads instead of looking at pixels.
 */
export default function GraphCanvas(props: GraphCanvasProps) {
  const { view, labels, theme, layout, rendererKind, anchorKey } = props;
  const { t } = useTranslation();
  const container = useRef<HTMLDivElement | null>(null);
  const [renderer, setRenderer] = useState<GraphRenderer | null>(null);
  const [drawn, setDrawn] = useState({ count: 0, nodes: -1 });

  // The renderer reads these when an event fires, not when the effect ran.
  const latest = useRef(props);
  latest.current = props;
  // What the renderer was last given, so a re-render with the same theme or arrangement repaints nothing.
  const applied = useRef<{ theme: GraphTheme; layout: LayoutKind }>({ theme, layout });

  useImperativeHandle(
    props.ref,
    () => ({
      fitView: () => void renderer?.fitView(),
      focus: (key) => void renderer?.focus(key),
    }),
    [renderer],
  );

  // Create on mount, destroy on unmount. A strict-mode double mount or a quick navigation destroys
  // a renderer whose library was still loading, so every step checks it is still wanted.
  useEffect(() => {
    const element = container.current;
    if (element === null) {
      return undefined;
    }
    let wanted = true;
    let created: GraphRenderer | null = null;
    let observer: ResizeObserver | null = null;
    void loadRenderer(rendererKind).then(async (r) => {
      if (!wanted) {
        r.destroy();
        return;
      }
      created = r;
      const now = latest.current;
      await r.mount(element, { theme: now.theme, reduceMotion: now.theme.reduceMotion, layout: now.layout });
      if (!wanted) {
        return;
      }
      applied.current = { theme: now.theme, layout: now.layout };
      r.setHandlers({
        nodeClick: (key) => latest.current.onNodeClick(key),
        nodeDoubleClick: (key) => latest.current.onNodeDoubleClick(key),
        edgeClick: (key) => latest.current.onEdgeClick(key),
        canvasClick: () => latest.current.onCanvasClick(),
      });
      observer = new ResizeObserver(() => r.resize());
      observer.observe(element);
      if (navigator.webdriver) {
        window.__silexgisGraph = { positions: () => Object.fromEntries(r.positions()) };
      }
      setRenderer(r);
    });
    return () => {
      wanted = false;
      observer?.disconnect();
      if (window.__silexgisGraph !== undefined && navigator.webdriver) {
        delete window.__silexgisGraph;
      }
      created?.destroy();
      setRenderer(null);
      setDrawn({ count: 0, nodes: -1 });
    };
  }, [rendererKind]);

  useEffect(() => {
    if (renderer === null) {
      return;
    }
    let current = true;
    void renderer.setView(view, { anchorKey: anchorKey ?? undefined, labels }).then(() => {
      if (current) {
        setDrawn((d) => ({ count: d.count + 1, nodes: view.nodeCount }));
      }
    });
    return () => {
      current = false;
    };
  }, [renderer, view, labels, anchorKey]);

  useEffect(() => {
    if (renderer !== null && applied.current.theme !== theme) {
      applied.current = { ...applied.current, theme };
      renderer.setTheme(theme);
    }
  }, [renderer, theme]);

  useEffect(() => {
    if (renderer !== null && applied.current.layout !== layout) {
      applied.current = { ...applied.current, layout };
      const selected = view.selection?.kind === 'node' ? view.selection.key : undefined;
      void renderer.setLayout(layout, { focusKey: selected ?? view.seedKey ?? undefined });
    }
    // The arrangement is applied when the choice changes; a new view must not re-arrange.
    // oxlint-disable-next-line react-hooks/exhaustive-deps
  }, [renderer, layout]);

  const selectedKey = view.selection?.key ?? '';
  return (
    <div
      ref={container}
      role="img"
      aria-label={t('graph.canvasLabel')}
      data-testid="graph-canvas"
      data-node-count={view.nodeCount}
      data-edge-count={view.edgeCount}
      data-selected={selectedKey}
      data-layout={layout}
      data-renderer={rendererKind}
      data-ready={renderer !== null}
      data-drawn={drawn.count}
      data-drawn-nodes={drawn.nodes}
      style={{ width: '100%', height: '100%', minHeight: 360, position: 'relative', overflow: 'hidden' }}
    />
  );
}
