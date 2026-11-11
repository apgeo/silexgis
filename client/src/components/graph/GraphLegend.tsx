// SPDX-License-Identifier: AGPL-3.0-or-later
import { Flex, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { legendOf } from '../../graph/legend.ts';
import { iconDataUri } from '../../graph/icons.ts';
import { categoryVisual, MORE_NODE_VISUAL, nodeTypeVisual, type GraphTheme, type NodeVisual, type PresetName } from '../../graph/visuals.ts';
import type { GraphView } from '../../graph/view.ts';
import type { GraphText } from './useGraphPresentation.ts';

const BOX = 20;

/** A legend swatch drawn like the canvas draws the node: a filled badge with a white icon, or a hollow dashed ring. */
function Badge({ visual, colour, hollow, ringColour }: { visual: NodeVisual; colour: string; hollow?: boolean; ringColour: string }) {
  const iconColour = hollow ? ringColour : '#ffffff';
  return (
    <svg width={BOX} height={BOX} viewBox="0 0 20 20" aria-hidden="true" focusable="false">
      {visual.shape === 'diamond' ? (
        <polygon points="10,3 17,10 10,17 3,10" fill={colour} />
      ) : hollow ? (
        <circle cx="10" cy="10" r="8.5" fill="none" stroke={ringColour} strokeWidth="1.5" strokeDasharray="3 2" />
      ) : (
        <circle cx="10" cy="10" r="9.5" fill={colour} />
      )}
      {visual.icon !== null && <image href={iconDataUri(visual.icon, iconColour)} x="4" y="4" width="12" height="12" />}
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
 * any other. It lists only the kinds of item and relation currently on screen, with the same icon
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
              <Badge visual={visual} colour={colour(visual.preset)} ringColour={theme.textSecondary} />
              <span>{text.typeName(type)}</span>
            </li>
          );
        })}
        {content.hasRestricted && (
          <li data-testid="graph-legend-restricted" style={{ display: 'flex', alignItems: 'center', gap: 4 }}>
            <Badge visual={{ ...MORE_NODE_VISUAL, icon: 'lock' }} colour={theme.background} hollow ringColour={theme.textSecondary} />
            <span>{t('graph.legend.restricted')}</span>
          </li>
        )}
        {content.hasMore && (
          <li data-testid="graph-legend-more" style={{ display: 'flex', alignItems: 'center', gap: 4 }}>
            <Badge visual={MORE_NODE_VISUAL} colour={theme.background} hollow ringColour={theme.textSecondary} />
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
