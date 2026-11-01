// SPDX-License-Identifier: AGPL-3.0-or-later
import type { GraphRenderer } from './renderer.ts';

export type RendererKind = 'g6';
export const RENDERER_KINDS: readonly RendererKind[] = ['g6'];
export const DEFAULT_RENDERER: RendererKind = 'g6';

/** The toolbar offers a renderer switch only when there is a choice to make. */
export const hasRendererChoice = (): boolean => RENDERER_KINDS.length > 1;

/**
 * The only way a renderer is reached. Each loader is a dynamic import, so a renderer's library
 * lands in a chunk of its own that is fetched when the explorer opens and by nobody else; a static
 * import of a renderer anywhere would drag the library into that importer's chunk.
 */
const LOADERS: Record<RendererKind, () => Promise<GraphRenderer>> = {
  g6: async () => (await import('./g6/G6Renderer.ts')).createG6Renderer(),
};

export function loadRenderer(kind: RendererKind = DEFAULT_RENDERER): Promise<GraphRenderer> {
  return LOADERS[kind]();
}
