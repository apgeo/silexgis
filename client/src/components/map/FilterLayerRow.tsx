// SPDX-License-Identifier: AGPL-3.0-or-later
import { CloseOutlined } from '@ant-design/icons';
import { Tag, Tooltip } from 'antd';
import { useTranslation } from 'react-i18next';
import Button from '../Button.tsx';
import {
  nextFilterLayerColor,
  nextFilterLayerSymbol,
  type FilterLayerSpec,
  type FilterLayerStatus,
  type FilterLayerSymbol,
} from '../../map/filterLayerList.ts';
import { filterLayerPalette, type FilterLayerColor } from '../../map/markerPalette.ts';

/** A filter layer as the layer panel shows it: what is stored, and what its filter reads as now. */
export interface FilterLayerRowInfo {
  spec: FilterLayerSpec;
  /** The saved filter's name, or null when this person can no longer see it. */
  name: string | null;
  status: FilterLayerStatus;
  /**
   * What the colour and symbol buttons step to — the next style no other layer draws with, worked
   * out over the whole list. Without them the buttons step plainly round.
   */
  nextColor?: FilterLayerColor;
  nextSymbol?: FilterLayerSymbol;
}

/** The marker a filter layer draws, small, for its row and its buttons. */
export function FilterLayerSwatch({ color, symbol }: { color: FilterLayerColor; symbol: FilterLayerSymbol }) {
  const fill = filterLayerPalette[color];
  const common = { fill, stroke: '#ffffff', strokeWidth: 1.5 };
  return (
    <svg width="14" height="14" viewBox="0 0 14 14" aria-hidden="true" focusable="false" className="filter-layer-swatch">
      {symbol === 'square' && <rect x="2" y="2" width="10" height="10" {...common} />}
      {symbol === 'triangle' && <polygon points="7,1.5 12.5,12 1.5,12" {...common} />}
      {symbol === 'diamond' && <polygon points="7,1 13,7 7,13 1,7" {...common} />}
      {symbol === 'circle' && <circle cx="7" cy="7" r="5.5" {...common} />}
    </svg>
  );
}

/**
 * The name line of a filter layer's row in the layer composer: its marker, its filter's name, the
 * unreadable mark when its filter cannot be run, and the controls that are only this kind of
 * layer's — change colour, change symbol, remove. Visibility, stacking and transparency are the
 * composer's, as for every other overlay.
 */
export function FilterLayerRowTitle({
  row,
  onStyleChange,
  onRemove,
}: {
  row: FilterLayerRowInfo;
  onStyleChange: (patch: { color?: FilterLayerColor; symbol?: FilterLayerSymbol }) => void;
  onRemove: () => void;
}) {
  const { t } = useTranslation();
  const name = row.name ?? t('filterLayers.unavailableName');
  const { color, symbol } = row.spec;
  return (
    <span className="filter-layer-title" data-testid={`filter-layer-${row.spec.savedFilterId}`}>
      <Button
        size="small"
        type="text"
        className="filter-layer-style-button"
        aria-label={t('filterLayers.changeColor', { name, color: t(`filterLayers.colors.${color}`) })}
        title={t('filterLayers.changeColor', { name, color: t(`filterLayers.colors.${color}`) })}
        icon={<FilterLayerSwatch color={color} symbol="circle" />}
        onClick={(event) => {
          event.stopPropagation();
          onStyleChange({ color: row.nextColor ?? nextFilterLayerColor(color) });
        }}
      />
      <Button
        size="small"
        type="text"
        className="filter-layer-style-button"
        aria-label={t('filterLayers.changeSymbol', { name, symbol: t(`filterLayers.symbols.${symbol}`) })}
        title={t('filterLayers.changeSymbol', { name, symbol: t(`filterLayers.symbols.${symbol}`) })}
        icon={<FilterLayerSwatch color={color} symbol={symbol} />}
        onClick={(event) => {
          event.stopPropagation();
          onStyleChange({ symbol: row.nextSymbol ?? nextFilterLayerSymbol(symbol) });
        }}
      />
      <span className="layer-node-name filter-layer-name">{name}</span>
      {row.status === 'unreadable' && (
        <Tooltip title={t('filterLayers.unreadableHint')}>
          <Tag color="red" className="filter-layer-unreadable" tabIndex={0}>
            {t('filterLayers.unreadable')}
          </Tag>
        </Tooltip>
      )}
      <Button
        size="small"
        type="text"
        aria-label={t('filterLayers.remove', { name })}
        title={t('filterLayers.remove', { name })}
        icon={<CloseOutlined />}
        onClick={(event) => {
          event.stopPropagation();
          onRemove();
        }}
      />
    </span>
  );
}
