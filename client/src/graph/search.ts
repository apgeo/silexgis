// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ViewNode } from './view.ts';
import type { NodeKey } from './types.ts';

/** Lower-case, accent-free text: "Peștera Ţiganului" and "pestera tiganului" are the same words to a searcher. */
export function normalise(text: string): string {
  return text.normalize('NFD').replace(/\p{M}+/gu, '').toLocaleLowerCase('en').replace(/\s+/g, ' ').trim();
}

/**
 * The nodes of a view whose title or subtitle contains the typed text, best first: a title that
 * starts with it, then a title that contains it, then a subtitle match; ties keep the view's order.
 * Placeholders and "+N more" stubs have no title and never match — searching must not be a way to
 * probe what a placeholder stands for.
 */
export function searchNodes(nodes: readonly ViewNode[], query: string): NodeKey[] {
  const needle = normalise(query);
  if (needle === '') {
    return [];
  }
  const scored: { key: NodeKey; rank: number; order: number }[] = [];
  nodes.forEach((vn, order) => {
    const node = vn.node;
    if (node === null || node.restricted) {
      return;
    }
    const title = normalise(node.title ?? '');
    const subtitle = normalise(node.subtitle ?? '');
    const rank = title.startsWith(needle) ? 0 : title.includes(needle) ? 1 : subtitle.includes(needle) ? 2 : -1;
    if (rank >= 0) {
      scored.push({ key: vn.key, rank, order });
    }
  });
  return scored.sort((a, b) => a.rank - b.rank || a.order - b.order).map((s) => s.key);
}

/** Enter moves to the next match and wraps; with no current match, the first. */
export function nextMatch(matches: readonly NodeKey[], current: NodeKey | null): NodeKey | null {
  if (matches.length === 0) {
    return null;
  }
  const at = current === null ? -1 : matches.indexOf(current);
  return matches[(at + 1) % matches.length];
}

export function previousMatch(matches: readonly NodeKey[], current: NodeKey | null): NodeKey | null {
  if (matches.length === 0) {
    return null;
  }
  const at = current === null ? 0 : matches.indexOf(current);
  return matches[(at <= 0 ? matches.length : at) - 1];
}
