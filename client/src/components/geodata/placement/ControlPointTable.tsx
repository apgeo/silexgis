// SPDX-License-Identifier: AGPL-3.0-or-later
import { DeleteOutlined } from '@ant-design/icons';
import { Table, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import type { PointPair } from '../../../geodata/placement/placement.ts';
import Button from '../../Button.tsx';
import CoordinateInput from './CoordinateInput.tsx';

interface Props {
  points: readonly PointPair[];
  onChange: (index: number, next: PointPair) => void;
  onRemove: (index: number) => void;
}

/**
 * Control points as a table: where each is on the sheet, and where on the ground, typed or
 * corrected by hand. It holds no rule about how many points there must be — that belongs to the
 * transformation the caller chose.
 */
export default function ControlPointTable({ points, onChange, onRemove }: Props) {
  const { t } = useTranslation();
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
          title: t('geodata.placement.longitude'),
          key: 'lon',
          render: (_, { point, index }) => (
            <CoordinateInput
              value={point.x}
              min={-180}
              max={180}
              onCommit={(x) => onChange(index, { ...point, x })}
              aria-label={`${index + 1} — ${t('geodata.placement.longitude')}`}
              data-testid={`placement-point-${index}-lon`}
            />
          ),
        },
        {
          title: t('geodata.placement.latitude'),
          key: 'lat',
          render: (_, { point, index }) => (
            <CoordinateInput
              value={point.y}
              min={-85}
              max={85}
              onCommit={(y) => onChange(index, { ...point, y })}
              aria-label={`${index + 1} — ${t('geodata.placement.latitude')}`}
              data-testid={`placement-point-${index}-lat`}
            />
          ),
        },
        {
          title: '',
          key: 'remove',
          width: 48,
          render: (_, { index }) => (
            <Button
              size="small"
              icon={<DeleteOutlined />}
              aria-label={t('geodata.placement.removePoint')}
              onClick={() => onRemove(index)}
            />
          ),
        },
      ]}
    />
  );
}
