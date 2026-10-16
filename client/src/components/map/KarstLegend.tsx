// SPDX-License-Identifier: AGPL-3.0-or-later
import { Alert, Typography, theme } from 'antd';
import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import type { KarstDensityLoadState } from '../../map/karstDensityLayer.ts';
import type { KarstIndexLoadState } from '../../map/karstIndexLayer.ts';
import { karstDensityPalette, karstIndexPalette } from '../../map/markerPalette.ts';

// The keys for the two karst overlays. Both draw their swatches from the same colour constants
// the map styles read, so a legend can never describe a colour the map is not using.
//
// The classes are listed here rather than imported from the index layer so that this component
// stays free of the drawing library; the order is lowest first, the unmeasurable class last.
const INDEX_CLASSES = ['veryLow', 'low', 'moderate', 'high', 'veryHigh', 'unknown'] as const;

// The map's fills are fixed colours, not theme tokens: the basemap under them is the same in the
// light and the dark theme, so a ramp that changed with the theme would change what a colour means
// on an unchanged map. The fills are translucent, though, and a translucent swatch laid straight on
// the panel takes the panel's colour — on the dark panel the pale classes would all read as one
// grey. So each swatch is painted over a light underlay, which shows the colour as it looks over a
// map, and ringed with the theme's own border colour so it stands off either panel.
const SWATCH_UNDERLAY = '#ffffff';

function Swatch({ fill, stroke, dashed }: { fill: string; stroke: string; dashed?: boolean }) {
  const { token } = theme.useToken();
  return (
    <span
      aria-hidden
      data-testid="map-karst-legend-swatch"
      style={{
        display: 'inline-block',
        width: 14,
        height: 14,
        marginInlineEnd: 6,
        verticalAlign: 'middle',
        background: `linear-gradient(${fill}, ${fill}), ${SWATCH_UNDERLAY}`,
        border: `1px ${dashed ? 'dashed' : 'solid'} ${stroke}`,
        boxShadow: `0 0 0 1px ${token.colorBorder}`,
      }}
    />
  );
}

function Rows({ testId, rows }: { testId: string; rows: { key: string; swatch: ReactNode; label: string }[] }) {
  return (
    <ul data-testid={testId} style={{ listStyle: 'none', margin: '4px 0 0', padding: 0, fontSize: 12 }}>
      {rows.map((row) => (
        <li key={row.key} data-testid={`${testId}-row`}>
          {row.swatch}
          {row.label}
        </li>
      ))}
    </ul>
  );
}

function formatValue(value: number, language: string): string {
  return value.toLocaleString(language, { maximumFractionDigits: value < 1 ? 2 : 1 });
}

/** The density grid's key: the value each band stands for, or what a hot and a cold spot are. */
export function KarstDensityLegend({ state }: { state: KarstDensityLoadState }) {
  const { t, i18n } = useTranslation();
  if (state.status !== 'ok') {
    return null;
  }
  if (state.mode === 'hotSpot') {
    const z =
      state.significanceZ === null
        ? ''
        : state.significanceZ.toLocaleString(i18n.language, { maximumFractionDigits: 2 });
    return (
      <Rows
        testId="map-karst-density-legend"
        rows={[
          {
            key: 'hot',
            swatch: <Swatch fill={karstDensityPalette.hot} stroke={karstDensityPalette.cellStroke} />,
            label: t('map.karstLegend.hot', { z }),
          },
          {
            key: 'cold',
            swatch: <Swatch fill={karstDensityPalette.cold} stroke={karstDensityPalette.cellStroke} />,
            label: t('map.karstLegend.cold', { z }),
          },
        ]}
      />
    );
  }
  if (state.bandMaxima.length === 0) {
    return (
      <Typography.Text type="secondary" style={{ fontSize: 12 }} data-testid="map-karst-density-legend-empty">
        {t('map.karstLegend.noCaves')}
      </Typography.Text>
    );
  }
  return (
    <>
      <Typography.Text type="secondary" style={{ fontSize: 12 }}>
        {t(`map.karstLegend.unit.${state.mode}`)}
      </Typography.Text>
      <Rows
        testId="map-karst-density-legend"
        rows={state.bandMaxima.map((upper, index) => ({
          key: String(index),
          swatch: <Swatch fill={karstDensityPalette.ramp[index]} stroke={karstDensityPalette.cellStroke} />,
          label: t('map.karstLegend.band', {
            from: formatValue(index === 0 ? 0 : state.bandMaxima[index - 1], i18n.language),
            to: formatValue(upper, i18n.language),
          }),
        }))}
      />
    </>
  );
}

/** The karstification index's key: one swatch per class, and a note when areas were left out. */
export function KarstIndexLegend({ state }: { state: KarstIndexLoadState }) {
  const { t } = useTranslation();
  if (state.status !== 'ok') {
    return null;
  }
  return (
    <>
      <Rows
        testId="map-karst-index-legend"
        rows={INDEX_CLASSES.map((cls) => {
          const colours = karstIndexPalette.classes[cls];
          return {
            key: cls,
            swatch: <Swatch fill={colours.fill} stroke={colours.stroke} dashed={cls === 'unknown'} />,
            label: t(`karstArea.class.${cls}`),
          };
        })}
      />
      {state.areaCount === 0 && (
        <Typography.Text type="secondary" style={{ fontSize: 12 }} data-testid="map-karst-index-empty">
          {t('map.karstLegend.noAreas')}
        </Typography.Text>
      )}
      {state.truncated && (
        <Alert
          type="info"
          showIcon
          style={{ marginTop: 4 }}
          data-testid="map-karst-index-truncated"
          title={t('map.karstLegend.truncated', { max: state.maxAreas ?? state.areaCount })}
        />
      )}
    </>
  );
}
