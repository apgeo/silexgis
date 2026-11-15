// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo } from 'react';
import { ColorPicker, Flex, Select, Typography } from 'antd';
import Button from '../Button.tsx';
import { useTranslation } from 'react-i18next';
import {
  catalogueEntryOf,
  parseSymbol,
  shapeReference,
  symbolCatalogue,
  symbolGroups,
  symbolImage,
} from '../../map/symbolLibrary.ts';

/**
 * Chooses a map symbol from the library: drawn shapes, the club's speleological set, cartographic
 * and geological symbols, grouped and searchable, each shown as it will be drawn. A drawn shape can
 * be given a colour of its own; without one it is drawn in `layerColor`, the colour of the layer
 * that uses it, which is also the colour its preview is shown in.
 *
 * The value is the stored reference (`osm_cave.svg`, `shape:cave:d46b08`), so the control drops
 * into a form item as it is. With `allowClear` the reference can be removed altogether, and the
 * caller draws its own fallback.
 */
export default function SymbolPicker({
  value,
  onChange,
  layerColor,
  allowClear = false,
  approximate = false,
  testId,
}: {
  value?: string | null;
  onChange?: (next: string | null) => void;
  layerColor: string;
  allowClear?: boolean;
  /** Preview the symbol as it is drawn for an approximate position. */
  approximate?: boolean;
  testId?: string;
}) {
  const { t } = useTranslation();
  const parsed = parseSymbol(value);
  // A shape's own colour is set aside while choosing, so moving from one shape to another keeps it.
  const shapeColor = parsed?.kind === 'shape' ? parsed.color : undefined;
  const selected = catalogueEntryOf(value)?.reference ?? (value || undefined);

  const options = useMemo(
    () =>
      symbolGroups.map(({ group, labelKey }) => ({
        label: t(labelKey),
        options: symbolCatalogue
          .filter((entry) => entry.group === group)
          .map((entry) => ({
            value: entry.reference,
            name: t(entry.nameKey),
            label: (
              <Flex gap={8} align="center">
                <SymbolPreview reference={entry.reference} color={layerColor} approximate={approximate} size={18} />
                {t(entry.nameKey)}
              </Flex>
            ),
          })),
      })),
    [t, layerColor, approximate],
  );

  const choose = (reference: string | undefined) => {
    if (!reference) {
      onChange?.(null);
      return;
    }
    const next = parseSymbol(reference);
    onChange?.(next?.kind === 'shape' ? shapeReference(next.shape, shapeColor) : reference);
  };

  return (
    <Flex gap={8} align="center" wrap>
      <Select
        showSearch={{ optionFilterProp: 'name' }}
        allowClear={allowClear}
        value={selected}
        placeholder={t('mapSymbols.none')}
        style={{ width: 300 }}
        options={options}
        onChange={(next: string | undefined) => choose(next)}
        data-testid={testId}
      />
      {parsed?.kind === 'shape' && (
        <Flex gap={4} align="center">
          <ColorPicker
            format="hex"
            disabledAlpha
            allowClear
            value={shapeColor ?? layerColor}
            onChange={(_colour, hex) => onChange?.(shapeReference(parsed.shape, hex))}
            onClear={() => onChange?.(shapeReference(parsed.shape))}
            showText={() => (shapeColor ? t('mapSymbols.color') : t('mapSymbols.layerColour'))}
            data-testid={testId ? `${testId}-color` : undefined}
          />
          {shapeColor && (
            <Button size="small" type="link" onClick={() => onChange?.(shapeReference(parsed.shape))}>
              {t('mapSymbols.layerColour')}
            </Button>
          )}
        </Flex>
      )}
    </Flex>
  );
}

/** One symbol as it is drawn on the map, at `size` CSS pixels; a plain dot when there is none. */
export function SymbolPreview({
  reference,
  color,
  approximate = false,
  size = 20,
}: {
  reference: string | null | undefined;
  color: string;
  approximate?: boolean;
  size?: number;
}) {
  const image = symbolImage(reference, { color, approximate });
  if (!image) {
    return (
      <span
        aria-hidden
        style={{ display: 'inline-block', width: size * 0.5, height: size * 0.5, borderRadius: '50%', background: color }}
      />
    );
  }
  return <img src={image.src} alt="" width={size} height={size} style={{ objectFit: 'contain' }} />;
}

/** The sentence naming where the library's symbols come from and under which licences. */
export function SymbolSourcesNote() {
  const { t } = useTranslation();
  return (
    <Typography.Paragraph type="secondary" style={{ fontSize: 12 }}>
      {t('mapSymbols.sources')}
    </Typography.Paragraph>
  );
}
