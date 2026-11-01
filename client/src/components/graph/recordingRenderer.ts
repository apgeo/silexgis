// SPDX-License-Identifier: AGPL-3.0-or-later
import type { GraphRenderer, LayoutKind, Point, RendererHandlers } from '../../graph/renderer.ts';
import type { NodeKey } from '../../graph/types.ts';
import type { GraphTheme } from '../../graph/visuals.ts';
import type { GraphView } from '../../graph/view.ts';

/**
 * A renderer with no drawing library that records what the page told it. The component tests stand
 * it in through the renderer registry, so everything above the seam is exercised without a canvas.
 */
export class RecordingRenderer implements GraphRenderer {
  mounts = 0;
  views: GraphView[] = [];
  anchors: (NodeKey | undefined)[] = [];
  themes: GraphTheme[] = [];
  layouts: LayoutKind[] = [];
  focused: NodeKey[] = [];
  fits = 0;
  resizes = 0;
  destroyed = false;
  handlers: RendererHandlers = {};

  mount(): Promise<void> {
    this.mounts++;
    return Promise.resolve();
  }
  setView(view: GraphView, opts?: { anchorKey?: NodeKey }): Promise<void> {
    this.views.push(view);
    this.anchors.push(opts?.anchorKey);
    return Promise.resolve();
  }
  setLayout(kind: LayoutKind): Promise<void> {
    this.layouts.push(kind);
    return Promise.resolve();
  }
  focus(key: NodeKey): Promise<void> {
    this.focused.push(key);
    return Promise.resolve();
  }
  fitView(): Promise<void> {
    this.fits++;
    return Promise.resolve();
  }
  resize(): void {
    this.resizes++;
  }
  setTheme(theme: GraphTheme): void {
    this.themes.push(theme);
  }
  setHandlers(handlers: RendererHandlers): void {
    this.handlers = handlers;
  }
  positions(): ReadonlyMap<NodeKey, Point> {
    return new Map(this.views.at(-1)?.nodes.map((n, i) => [n.key, { x: i * 10, y: 0 }]) ?? []);
  }
  destroy(): void {
    this.destroyed = true;
  }
}
