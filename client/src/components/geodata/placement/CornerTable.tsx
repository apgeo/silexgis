// SPDX-License-Identifier: AGPL-3.0-or-later
import { Table } from 'antd';
import { useTranslation } from 'react-i18next';
import { CORNER_IDS, type CornerId, type Corners } from '../../../geodata/placement/placement.ts';
import { CORNER_LABEL_KEYS } from './cornerLabels.ts';
import CoordinateInput from './CoordinateInput.tsx';

interface Props {
  corners: Corners;
  onChange: (next: Corners) => void;
}

/** The four corners of the sheet as numbers, so a placement read off another map can be typed. */
export default function CornerTable({ corners, onChange }: Props) {
  const { t } = useTranslation();
  const set = (id: CornerId, axis: 0 | 1, value: number) => {
    const point: [number, number] = [...corners[id]];
    point[axis] = value;
    onChange({ ...corners, [id]: point });
  };
  return (
    <Table<{ id: CornerId }>
      size="small"
      pagination={false}
      rowKey="id"
      dataSource={CORNER_IDS.map((id) => ({ id }))}
      data-testid="placement-corner-table"
      columns={[
        { title: t('geodata.placement.corner'), key: 'corner', render: (_, { id }) => t(CORNER_LABEL_KEYS[id]) },
        {
          title: t('geodata.placement.longitude'),
          key: 'lon',
          render: (_, { id }) => (
            <CoordinateInput
              value={corners[id][0]}
              min={-180}
              max={180}
              onCommit={(value) => set(id, 0, value)}
              aria-label={`${t(CORNER_LABEL_KEYS[id])} — ${t('geodata.placement.longitude')}`}
              data-testid={`placement-corner-${id}-lon`}
            />
          ),
        },
        {
          title: t('geodata.placement.latitude'),
          key: 'lat',
          render: (_, { id }) => (
            <CoordinateInput
              value={corners[id][1]}
              min={-85}
              max={85}
              onCommit={(value) => set(id, 1, value)}
              aria-label={`${t(CORNER_LABEL_KEYS[id])} — ${t('geodata.placement.latitude')}`}
              data-testid={`placement-corner-${id}-lat`}
            />
          ),
        },
      ]}
    />
  );
}
