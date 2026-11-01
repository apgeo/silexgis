// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo } from 'react';
import { theme as antTheme } from 'antd';
import { useTranslation } from 'react-i18next';
import type { ResLinkRelationType } from '../../api/hooks.ts';
import type { GraphLabels } from '../../graph/renderer.ts';
import {
  buildGraphTheme,
  categoryLabel,
  nodeLabel,
  nodeTypeLabel,
  relationLabel,
  type GraphTheme,
  type LabelDeps,
  type Reading,
  type RelationRef,
  type Translate,
} from '../../graph/visuals.ts';
import type { ViewEdge, ViewNode } from '../../graph/view.ts';
import { useUiPrefsStore } from '../../stores/uiPrefsStore.ts';
import { resolveDark } from '../../theme.ts';
import { relationPhrase } from '../reslinks/relations.ts';

/**
 * The canvas cannot read CSS variables, so the look is rebuilt from the antd tokens whenever they
 * change (a dark-mode switch, a high-contrast switch) and handed to the renderer as a new object.
 */
export function useGraphTheme(): GraphTheme {
  const { token } = antTheme.useToken();
  const appearance = useUiPrefsStore((s) => s.appearance);
  const dark = resolveDark(appearance.theme);
  return useMemo(
    () => buildGraphTheme(token as unknown as Record<string, unknown>, dark, appearance.reduceMotion),
    [token, dark, appearance.reduceMotion],
  );
}

export interface GraphText {
  /** What the renderer draws; a new object whenever the language changes, which makes it repaint. */
  labels: GraphLabels;
  node: (vn: ViewNode) => string;
  relation: (edge: Parameters<typeof relationLabel>[0], reading: Reading) => string;
  categoryName: (category: string) => string;
  typeName: (type: string) => string;
}

/** The words of the graph in the current language, for the canvas and for the list view alike. */
export function useGraphText(): GraphText {
  const { t } = useTranslation();
  return useMemo(() => {
    const translate = t as unknown as Translate;
    const deps: LabelDeps = {
      t: translate,
      reslinkPhrase: (relation: RelationRef | null, reading: Reading) =>
        relationPhrase(
          relation === null
            ? null
            : // Only these four fields are read; the rest of the stored row is not on a graph edge.
              ({ code: relation.code, directed: relation.directed, name: relation.name ?? relation.code, inverseName: relation.inverseName } as ResLinkRelationType),
          reading,
          t,
        ),
    };
    const node = (vn: ViewNode) => nodeLabel(vn, translate);
    const relation = (edge: Parameters<typeof relationLabel>[0], reading: Reading) => relationLabel(edge, reading, deps);
    return {
      node,
      relation,
      categoryName: (category: string) => categoryLabel(category, translate),
      typeName: (type: string) => nodeTypeLabel(type, translate),
      labels: { node, edge: (edge: ViewEdge) => relation(edge, 'forward') },
    };
  }, [t]);
}
