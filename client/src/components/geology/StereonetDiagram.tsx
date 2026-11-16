// SPDX-License-Identifier: AGPL-3.0-or-later
import { Space, Typography, theme } from 'antd';
import { useId, useMemo } from 'react';
import { useTranslation } from 'react-i18next';

import type { Stereonet } from '../../api/hooks/structural.ts';
import { paletteFor } from '../statistics/chartTheme.ts';
import { densityCells, NET_CENTRE, NET_RADIUS, NET_VIEW, netPoint } from './stereonetGeometry.ts';
import { MEASUREMENT_TYPES, measurementTypeLabel } from './structural.ts';

/**
 * The poles of a set of structural readings on an equal-area, lower-hemisphere net, drawn by hand.
 *
 * <p>
 * Every number on it is the server's: where each pole lands and how dense the poles are at each
 * node of the grid. This component scales those to the drawing and paints the density in bands, one
 * per contour interval, under a clip to the net's circle — so what reads as a contour is the edge
 * of a band, and the reader never sees a level the server did not count. No charting library is
 * involved: none of the ones in the project draws a stereonet, and the drawing is a circle, some
 * squares and some dots.
 * </p>
 */
export default function StereonetDiagram({ data, size = 280 }: { data: Stereonet; size?: number }) {
  const { t } = useTranslation();
  const { token } = theme.useToken();
  const id = useId();
  const palette = paletteFor(token);
  const cells = useMemo(() => densityCells(data.density), [data.density]);

  const colourOf = (type: string | null | undefined) => {
    const index = type ? MEASUREMENT_TYPES.indexOf(type as (typeof MEASUREMENT_TYPES)[number]) : -1;
    return index < 0 ? palette.unclassified : palette.series[index % palette.series.length];
  };
  const maxBand = cells.reduce((max, cell) => Math.max(max, cell.band), 0);
  const present = MEASUREMENT_TYPES.filter((type) => data.poles.some((p) => p.measurementType === type));
  const round = (value: number) => Math.round(value * 10) / 10;

  return (
    <div data-testid="stereonet">
      <Space orientation="vertical" size={8} style={{ width: '100%' }}>
        <svg
          role="img"
          aria-labelledby={`${id}-title ${id}-desc`}
          viewBox={`0 0 ${NET_VIEW} ${NET_VIEW}`}
          preserveAspectRatio="xMidYMid meet"
          style={{ width: '100%', maxWidth: size, height: 'auto', display: 'block' }}
        >
          <title id={`${id}-title`}>{t('structural.stereonet.title')}</title>
          <desc id={`${id}-desc`}>{t('structural.stereonet.description')}</desc>
          <defs>
            <clipPath id={`${id}-net`}>
              <circle cx={NET_CENTRE} cy={NET_CENTRE} r={NET_RADIUS} />
            </clipPath>
          </defs>

          <g clipPath={`url(#${id}-net)`} data-testid="stereonet-density">
            {cells.map((cell) => (
              <rect
                key={`${cell.x}-${cell.y}`}
                x={cell.x}
                y={cell.y}
                width={cell.size}
                height={cell.size}
                fill={token.colorPrimary}
                fillOpacity={maxBand > 0 ? 0.12 + (0.5 * cell.band) / maxBand : 0}
                stroke="none"
              />
            ))}
          </g>

          <circle cx={NET_CENTRE} cy={NET_CENTRE} r={NET_RADIUS} fill="none" stroke={palette.axisLine} />
          <line
            x1={NET_CENTRE - 4}
            y1={NET_CENTRE}
            x2={NET_CENTRE + 4}
            y2={NET_CENTRE}
            stroke={palette.axisLine}
          />
          <line
            x1={NET_CENTRE}
            y1={NET_CENTRE - 4}
            x2={NET_CENTRE}
            y2={NET_CENTRE + 4}
            stroke={palette.axisLine}
          />
          <line
            x1={NET_CENTRE}
            y1={NET_CENTRE - NET_RADIUS}
            x2={NET_CENTRE}
            y2={NET_CENTRE - NET_RADIUS - 6}
            stroke={palette.axisLine}
          />
          <text
            x={NET_CENTRE}
            y={NET_CENTRE - NET_RADIUS - 9}
            textAnchor="middle"
            fontSize={11}
            fill={palette.axisLabel}
          >
            {t('structural.stereonet.north')}
          </text>

          {data.poles.map((pole) => {
            const at = netPoint(pole.x, pole.y);
            return (
              <circle
                key={pole.featureId}
                data-testid="stereonet-pole"
                cx={at.x}
                cy={at.y}
                r={3}
                fill={colourOf(pole.measurementType)}
                stroke={token.colorBgContainer}
                strokeWidth={0.75}
              >
                <title>
                  {t('structural.stereonet.poleLabel', {
                    type: pole.measurementType ? measurementTypeLabel(t, pole.measurementType) : '—',
                    dip: round(pole.dipDegrees),
                    dipDirection: round(pole.dipDirectionDegrees),
                  })}
                </title>
              </circle>
            );
          })}
        </svg>

        {present.length > 0 && (
          <Space wrap size={12}>
            {present.map((type) => (
              <Space key={type} size={4}>
                <svg width={10} height={10} aria-hidden="true">
                  <circle cx={5} cy={5} r={4} fill={colourOf(type)} />
                </svg>
                <Typography.Text type="secondary">{measurementTypeLabel(t, type)}</Typography.Text>
              </Space>
            ))}
          </Space>
        )}

        {data.density && (
          <Typography.Text type="secondary" data-testid="stereonet-density-legend">
            {t('structural.stereonet.densityLegend', {
              interval: data.density.contourIntervalSigma,
              expected: data.density.expectedSigma,
              max: round(data.density.maxSigma),
            })}
          </Typography.Text>
        )}
      </Space>
    </div>
  );
}
