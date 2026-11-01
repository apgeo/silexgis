// SPDX-License-Identifier: AGPL-3.0-or-later
import { Flex, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { legendOf } from '../../graph/legend.ts';
import { categoryVisual, MORE_NODE_VISUAL, nodeTypeVisual, type GraphTheme, type NodeShape, type PresetName } from '../../graph/visuals.ts';
import type { GraphView } from '../../graph/view.ts';
import type { GraphText } from './useGraphPresentation.ts';

const BOX = 20;

function polygon(points: [number, number][]): string {
  return points.map(([x, y]) => `${x},${y}`).join(' ');
}

function hexagonPoints(): string {
  return polygon(
    [0, 1, 2, 3, 4, 5].map((i) => [10 + 8 * Math.cos((Math.PI / 3) * i), 10 + 8 * Math.sin((Math.PI / 3) * i)] as [number, number]),
  );
}

function starPoints(): string {
  return polygon(
    Array.from({ length: 10 }, (_, i) => {
      const r = i % 2 === 0 ? 9 : 4;
      const a = -Math.PI / 2 + (Math.PI / 5) * i;
      return [10 + r * Math.cos(a), 10 + r * Math.sin(a)] as [number, number];
    }),
  );
}

function Shape({ shape, colour }: { shape: NodeShape; colour: string }) {
  const common = { fill: colour, stroke: colour };
  return (
    <svg width={BOX} height={BOX} viewBox="0 0 20 20" aria-hidden="true" focusable="false">
      {shape === 'circle' && <circle cx="10" cy="10" r="8" {...common} />}
      {shape === 'ellipse' && <ellipse cx="10" cy="10" rx="9" ry="6" {...common} />}
      {shape === 'rect' && <rect x="2" y="3" width="16" height="14" rx="2" {...common} />}
      {shape === 'triangle' && <polygon points="10,2 18,17 2,17" {...common} />}
      {shape === 'diamond' && <polygon points="10,1 19,10 10,19 1,10" {...common} />}
      {shape === 'hexagon' && <polygon points={hexagonPoints()} {...common} />}
      {shape === 'star' && <polygon points={starPoints()} {...common} />}
    </svg>
  );
}

function Line({ colour, dashed }: { colour: string; dashed: boolean }) {
  return (
    <svg width={28} height={BOX} viewBox="0 0 28 20" aria-hidden="true" focusable="false">
      <line x1="2" y1="10" x2="26" y2="10" stroke={colour} strokeWidth="2.5" strokeDasharray={dashed ? '5 3' : undefined} />
    </svg>
  );
}

export interface GraphLegendProps {
  view: GraphView;
  text: GraphText;
  theme: GraphTheme;
}

/**
 * The key to the picture, as ordinary page content so it can be read, translated and tested like
 * any other. It lists only the kinds of item and relation currently on screen, with the same shape
 * and colour tables the canvas paints from, so the two cannot drift apart.
 */
export default function GraphLegend({ view, text, theme }: GraphLegendProps) {
  const { t } = useTranslation();
  const content = legendOf(view);
  const colour = (preset: PresetName) => theme.preset[preset];
  if (content.types.length === 0 && content.categories.length === 0) {
    return null;
  }
  return (
    <Flex vertical gap={6} data-testid="graph-legend" role="group" aria-label={t('graph.legend.title')}>
      <Typography.Text strong>{t('graph.legend.title')}</Typography.Text>
      <Flex wrap gap={12} component="ul" style={{ listStyle: 'none', margin: 0, padding: 0 }}>
        {content.types.map((type) => {
          const visual = nodeTypeVisual(type);
          return (
            <li key={`type:${type}`} data-testid="graph-legend-type" data-legend-type={type} style={{ display: 'flex', alignItems: 'center', gap: 4 }}>
              <Shape shape={visual.shape} colour={colour(visual.preset)} />
              <span>{text.typeName(type)}</span>
            </li>
          );
        })}
        {content.hasRestricted && (
          <li data-testid="graph-legend-restricted" style={{ display: 'flex', alignItems: 'center', gap: 4 }}>
            <svg width={BOX} height={BOX} viewBox="0 0 20 20" aria-hidden="true" focusable="false">
              <circle cx="10" cy="10" r="8" fill="none" stroke={theme.textSecondary} strokeWidth="2" strokeDasharray="4 3" />
              <rect x="14" y="2" width="5" height="5" fill={theme.textSecondary} />
            </svg>
            <span>{t('graph.legend.restricted')}</span>
          </li>
        )}
        {content.hasMore && (
          <li data-testid="graph-legend-more" style={{ display: 'flex', alignItems: 'center', gap: 4 }}>
            <Shape shape={MORE_NODE_VISUAL.shape} colour={colour(MORE_NODE_VISUAL.preset)} />
            <span>{t('graph.legend.more')}</span>
          </li>
        )}
      </Flex>
      <Flex wrap gap={12} component="ul" style={{ listStyle: 'none', margin: 0, padding: 0 }}>
        {content.categories.map((category) => {
          const visual = categoryVisual(category);
          return (
            <li key={`category:${category}`} data-testid="graph-legend-category" data-legend-category={category} style={{ display: 'flex', alignItems: 'center', gap: 4 }}>
              <Line colour={colour(visual.preset)} dashed={visual.dashed} />
              <span>{text.categoryName(category)}</span>
            </li>
          );
        })}
      </Flex>
    </Flex>
  );
}
