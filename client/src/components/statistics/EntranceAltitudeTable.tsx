// SPDX-License-Identifier: AGPL-3.0-or-later
import { Table, Tag } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';

import type { EntranceAltitudeRow, EntranceAltitudeVerdict } from '../../api/hooks/entranceAltitudes.ts';
import Button from '../Button.tsx';

const VERDICT_COLOUR: Record<EntranceAltitudeVerdict, string | undefined> = {
  agrees: 'green',
  disagrees: 'red',
  notRecorded: undefined,
  notModelled: undefined,
};

/**
 * Entrances with their recorded altitude beside the modelled one, widest gap first as the server
 * ordered them. The table keeps the server's order: a client that re-sorted could not be wrong about
 * which entrance is worst, but it could hide that the ranking was taken over what this reader may
 * place and nothing else.
 */
export default function EntranceAltitudeTable({
  rows,
  canFill,
  filling,
  onFill,
  linkCaves,
}: {
  rows: readonly EntranceAltitudeRow[];
  /** Whether the reader may write caves at all; the server still decides per cave. */
  canFill: boolean;
  /** The entrance being filled, if one is. */
  filling: string | null;
  onFill: (entranceId: string) => void;
  /** Whether each row's cave name links to the cave, for the area view. */
  linkCaves: boolean;
}) {
  const { t } = useTranslation();
  const metres = (value: number | null | undefined) =>
    value === null || value === undefined
      ? t('altitudeCheck.notMeasured')
      : t('altitudeCheck.unitMetres', { value: (Math.round(value * 10) / 10).toFixed(1) });

  const columns: ColumnsType<EntranceAltitudeRow> = [
    {
      title: t('altitudeCheck.colEntrance'),
      key: 'entrance',
      render: (_: unknown, row) => {
        const cave = row.caveName ?? t('altitudeCheck.unnamedCave');
        const label = row.entranceName ? `${cave} · ${row.entranceName}` : cave;
        return linkCaves ? <Link to={`/caves/${row.caveId}`}>{label}</Link> : label;
      },
    },
    {
      title: t('altitudeCheck.colRecorded'),
      key: 'recorded',
      align: 'right',
      render: (_: unknown, row) => metres(row.recordedAltitudeM),
    },
    {
      title: t('altitudeCheck.colModelled'),
      key: 'modelled',
      align: 'right',
      render: (_: unknown, row) => metres(row.modelledAltitudeM),
    },
    {
      title: t('altitudeCheck.colDifference'),
      key: 'difference',
      align: 'right',
      render: (_: unknown, row) => metres(row.differenceM),
    },
    {
      title: t('altitudeCheck.colVerdict'),
      key: 'verdict',
      render: (_: unknown, row) => (
        <Tag color={VERDICT_COLOUR[row.verdict]}>{t(`altitudeCheck.verdict.${row.verdict}`)}</Tag>
      ),
    },
  ];

  if (canFill) {
    columns.push({
      title: '',
      key: 'fill',
      render: (_: unknown, row) =>
        row.fillableFromTerrain ? (
          <Button
            size="small"
            data-testid="altitude-fill"
            loading={filling === row.entranceId}
            onClick={() => onFill(row.entranceId)}
          >
            {t('altitudeCheck.fill')}
          </Button>
        ) : null,
    });
  }

  return (
    <Table<EntranceAltitudeRow>
      size="small"
      rowKey="entranceId"
      pagination={false}
      columns={columns}
      dataSource={[...rows]}
      scroll={{ x: true }}
    />
  );
}
