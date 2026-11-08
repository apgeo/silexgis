// SPDX-License-Identifier: AGPL-3.0-or-later
import { DeleteOutlined } from '@ant-design/icons';
import { Input, Table, Tag, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { formatMetres, type PointPair } from '../../../geodata/placement/placement.ts';
import Button from '../../Button.tsx';
import CoordinateInput from './CoordinateInput.tsx';

interface Props {
  points: readonly PointPair[];
  onChange: (index: number, next: PointPair) => void;
  onRemove: (index: number) => void;
  /** Whether the ground halves are typed in degrees rather than in metres of a grid. */
  degrees?: boolean;
  /** Points shown as they are, for a system that cannot be typed in here. */
  readOnly?: boolean;
  /** A label column, for the mode where points are many and worth naming. */
  showLabels?: boolean;
  /** Each row's residual in metres from the last preview, when the table shows them. */
  residuals?: ReadonlyMap<number, number>;
  /** Rows whose residual is far above the rest. */
  outliers?: ReadonlySet<number>;
}

/**
 * Control points as a table: where each is on the sheet, where on the ground — typed or corrected
 * by hand — and, once a preview has answered, how far the fitted sheet misses it. It holds no rule
 * about how many points there must be; that belongs to the transformation the caller chose.
 */
export default function ControlPointTable({
  points,
  onChange,
  onRemove,
  degrees = true,
  readOnly = false,
  showLabels = false,
  residuals,
  outliers,
}: Props) {
  const { t } = useTranslation();
  const xTitle = degrees ? t('geodata.placement.longitude') : t('geodata.placement.easting');
  const yTitle = degrees ? t('geodata.placement.latitude') : t('geodata.placement.northing');
  const bounds = degrees
    ? { x: [-180, 180], y: [-85, 85], precision: 6 }
    : { x: [-1e8, 1e8], y: [-1e8, 1e8], precision: 2 };
  const rows = points.map((point, index) => ({ point, index }));
  return (
    <Table<{ point: PointPair; index: number }>
      size="small"
      pagination={false}
      rowKey="index"
      dataSource={rows}
      data-testid="placement-point-table"
      locale={{ emptyText: t('geodata.placement.noPoints') }}
      columns={[
        { title: '#', key: 'n', width: 40, render: (_, { index }) => index + 1 },
        ...(showLabels
          ? [
              {
                title: t('geodata.placement.label'),
                key: 'label',
                render: (_: unknown, { point, index }: { point: PointPair; index: number }) => (
                  <Input
                    key={`${point.u},${point.v}`}
                    size="small"
                    style={{ width: 110 }}
                    maxLength={40}
                    disabled={readOnly}
                    defaultValue={point.label ?? ''}
                    placeholder={String(index + 1)}
                    onBlur={(e) => {
                      if ((e.target.value || null) !== (point.label ?? null)) {
                        onChange(index, { ...point, label: e.target.value || null });
                      }
                    }}
                    aria-label={`${index + 1} — ${t('geodata.placement.label')}`}
                    data-testid={`placement-point-${index}-label`}
                  />
                ),
              },
            ]
          : []),
        {
          title: t('geodata.placement.onSheet'),
          key: 'sheet',
          render: (_, { point }) => (
            <Typography.Text type="secondary">
              {`${(point.u * 100).toFixed(1)} %, ${(point.v * 100).toFixed(1)} %`}
            </Typography.Text>
          ),
        },
        {
          title: xTitle,
          key: 'x',
          render: (_, { point, index }) => (
            <CoordinateInput
              value={point.x}
              min={bounds.x[0]}
              max={bounds.x[1]}
              precision={bounds.precision}
              disabled={readOnly}
              onCommit={(x) => onChange(index, { ...point, x })}
              aria-label={`${index + 1} — ${xTitle}`}
              data-testid={`placement-point-${index}-lon`}
            />
          ),
        },
        {
          title: yTitle,
          key: 'y',
          render: (_, { point, index }) => (
            <CoordinateInput
              value={point.y}
              min={bounds.y[0]}
              max={bounds.y[1]}
              precision={bounds.precision}
              disabled={readOnly}
              onCommit={(y) => onChange(index, { ...point, y })}
              aria-label={`${index + 1} — ${yTitle}`}
              data-testid={`placement-point-${index}-lat`}
            />
          ),
        },
        ...(residuals
          ? [
              {
                title: t('geodata.placement.residualColumn'),
                key: 'residual',
                render: (_: unknown, { index }: { index: number }) => {
                  const metres = residuals.get(index);
                  if (metres === undefined) {
                    return null;
                  }
                  return outliers?.has(index) ? (
                    <Tag color="error" data-testid={`placement-point-${index}-residual`}>
                      {t('geodata.placement.metres', { value: formatMetres(metres) })}
                    </Tag>
                  ) : (
                    <span data-testid={`placement-point-${index}-residual`}>
                      {t('geodata.placement.metres', { value: formatMetres(metres) })}
                    </span>
                  );
                },
              },
            ]
          : []),
        {
          title: '',
          key: 'remove',
          width: 48,
          render: (_, { index }) => (
            <Button
              size="small"
              icon={<DeleteOutlined />}
              disabled={readOnly}
              aria-label={t('geodata.placement.removePoint')}
              onClick={() => onRemove(index)}
            />
          ),
        },
      ]}
    />
  );
}
