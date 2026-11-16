// SPDX-License-Identifier: AGPL-3.0-or-later
import { Descriptions, Table, Tag, Typography } from 'antd';
import { useTranslation } from 'react-i18next';

import type { AreaLithology, AreaLithologyBreakdown } from '../../api/hooks.ts';
import { lithologyLabel } from './lithology.ts';

/**
 * An area split by the geology mapped over it: each rock class's outcrop inside the outline and
 * the entrances standing on it, and the density over karstifiable ground alone.
 *
 * <p>
 * Unlike the declared totals around it, this split is spatial, and so it is counted only over the
 * units and entrances this reader may place exactly. The note under the heading says so, because
 * its entrance counts will not add up to the area's declared entrance count and are not meant to.
 * </p>
 */
export default function AreaLithologySection({ data }: { data: AreaLithologyBreakdown }) {
  const { t } = useTranslation();

  return (
    <div style={{ marginTop: 16 }} data-testid="area-lithology">
      <Typography.Text strong>{t('geology.area.title')}</Typography.Text>
      <Typography.Paragraph type="secondary" style={{ marginBottom: 8 }}>
        {t('geology.area.basisPlaceable')}
      </Typography.Paragraph>

      {data.classes.length === 0 ? (
        <Typography.Paragraph data-testid="area-lithology-none">
          {t('geology.area.noneMapped')}
        </Typography.Paragraph>
      ) : (
        <Table<AreaLithology>
          size="small"
          pagination={false}
          rowKey={(row) => row.code ?? ''}
          dataSource={data.classes as AreaLithology[]}
          columns={[
            {
              title: t('geology.area.lithology'),
              render: (_: unknown, row) => (
                <>
                  {lithologyLabel(t, row.code)}{' '}
                  {row.karstifiable === true && <Tag color="blue">{t('geology.karstifiable')}</Tag>}
                  {!row.recognised && row.code !== null && <Tag>{t('geology.unrecognised')}</Tag>}
                </>
              ),
            },
            {
              title: t('geology.area.areaKm2'),
              render: (_: unknown, row) => row.areaKm2.toFixed(3),
            },
            {
              title: t('geology.area.share'),
              render: (_: unknown, row) =>
                row.areaShare === null ? t('geology.area.notMeasured') : `${(row.areaShare * 100).toFixed(1)} %`,
            },
            { title: t('geology.area.entrances'), dataIndex: 'entranceCount' },
            {
              title: t('geology.area.entrancesPerKm2'),
              render: (_: unknown, row) =>
                row.entrancesPerKm2 === null ? t('geology.area.notMeasured') : row.entrancesPerKm2.toFixed(3),
            },
          ]}
        />
      )}

      <Descriptions size="small" column={{ xs: 1, sm: 2 }} bordered style={{ marginTop: 8 }}>
        <Descriptions.Item label={t('geology.area.karstifiableArea')}>
          {data.karstifiableAreaKm2.toFixed(3)}
        </Descriptions.Item>
        <Descriptions.Item label={t('geology.area.karstifiableDensity')}>
          <span data-testid="area-lithology-karst-density">
            {data.karstifiableEntrancesPerKm2 === null
              ? t('geology.area.notMeasured')
              : data.karstifiableEntrancesPerKm2.toFixed(3)}
          </span>
        </Descriptions.Item>
        <Descriptions.Item label={t('geology.area.unmappedArea')}>
          {data.unmappedAreaKm2.toFixed(3)}
        </Descriptions.Item>
        <Descriptions.Item label={t('geology.area.unmappedEntrances')}>
          {data.unmappedEntranceCount}
        </Descriptions.Item>
      </Descriptions>
    </div>
  );
}
