// SPDX-License-Identifier: AGPL-3.0-or-later
import { Alert, Card, Descriptions, Empty, List, Statistic, Table, Tag, Typography } from 'antd';
import { useTranslation } from 'react-i18next';

import type { AreaCaveExtreme, AreaKarstStatistics, KarstificationComponent } from '../../api/hooks.ts';
import { useAreaKarstStatistics } from '../../api/hooks.ts';

/**
 * What one karst area adds up to: how many caves are in it, how thickly they sit, how much passage
 * they hold, and a composite reading of how karstified the ground is.
 *
 * <p>
 * The card leads with the basis rather than the numbers, and that is deliberate. Membership is the
 * declared one — a cave is in this area because somebody put it here, not because its coordinate
 * falls inside the outline — and a reader comparing these totals with what they can see on the map
 * will find them disagreeing. The disagreement is the point: it is the registry's hierarchy against
 * the registry's geometry, and the count beneath says how far apart the two have drifted. Reading
 * these figures as a spatial answer would make every one of them wrong by an amount nothing on
 * screen records.
 * </p>
 * <p>
 * Everything here is counted over what this reader may see, so two accounts will legitimately get
 * different totals for the same area.
 * </p>
 */
export default function AreaKarstStatisticsCard({
  featureId,
  featureTypeCode,
}: {
  featureId: string;
  featureTypeCode: string | null;
}) {
  const { t } = useTranslation();

  // Only the kinds that stand for a tract of karst. A building or a marker has an outline too, and
  // caves per square kilometre of a building is not a reading anybody wants offered.
  const enabled = featureTypeCode === 'karst_area' || featureTypeCode === 'massif';
  const { data, isLoading, isError } = useAreaKarstStatistics(featureId, enabled);

  // A refusal and an area nobody may read answer identically, so there is nothing honest to show.
  if (!enabled || isError) return null;

  if (isLoading || !data) {
    return (
      <Card title={t('karstArea.title')} style={{ marginBottom: 16 }}>
        <Empty description={t('karstArea.loading')} />
      </Card>
    );
  }

  return (
    <Card title={t('karstArea.title')} style={{ marginBottom: 16 }} data-testid="area-karst-statistics">
      <Alert
        type="info"
        showIcon
        style={{ marginBottom: 16 }}
        message={t('karstArea.basisDeclared')}
        description={t('karstArea.basisDeclaredDetail', {
          drift: data.unparentedInsideCount,
          placeable: data.placeableCaveCount,
        })}
      />

      <Descriptions size="small" column={{ xs: 1, sm: 2, md: 3 }} bordered>
        <Descriptions.Item label={t('karstArea.caveCount')}>{data.caveCount}</Descriptions.Item>
        <Descriptions.Item label={t('karstArea.entranceCount')}>
          {data.entranceCount}
        </Descriptions.Item>
        <Descriptions.Item label={t('karstArea.areaKm2')}>
          {format(data.areaKm2, 2, t('karstArea.notMeasured'))}
        </Descriptions.Item>
        <Descriptions.Item label={t('karstArea.cavesPerKm2')}>
          {format(data.cavesPerKm2, 3, t('karstArea.notMeasured'))}
        </Descriptions.Item>
        <Descriptions.Item label={t('karstArea.surveyedLength')}>
          {format(data.surveyedLengthM, 0, t('karstArea.notMeasured'))}
        </Descriptions.Item>
        <Descriptions.Item label={t('karstArea.surveyedPerKm2')}>
          {format(data.surveyedMetresPerKm2, 0, t('karstArea.notMeasured'))}
        </Descriptions.Item>
        <Descriptions.Item label={t('karstArea.depressionCount')}>
          {data.depressionCount}
        </Descriptions.Item>
        <Descriptions.Item label={t('karstArea.depressionRatio')}>
          {format(
            data.depressionAreaRatio === null || data.depressionAreaRatio === undefined
              ? null
              : data.depressionAreaRatio * 100,
            2,
            t('karstArea.notMapped'),
          )}
        </Descriptions.Item>
        <Descriptions.Item label={t('karstArea.surveyedCaveCount')}>
          {t('karstArea.ofCaves', { measured: data.surveyedCaveCount, total: data.caveCount })}
        </Descriptions.Item>
      </Descriptions>

      <div style={{ marginTop: 16 }}>
        <Statistic
          title={t('karstArea.index')}
          value={
            data.karstification.score === null || data.karstification.score === undefined
              ? t('karstArea.indexUnknown')
              : data.karstification.score.toFixed(2)
          }
          suffix={<Tag>{t(`karstArea.class.${data.karstification.class}`)}</Tag>}
        />
        <List
          size="small"
          dataSource={data.karstification.components}
          renderItem={(component: KarstificationComponent) => (
            <List.Item>
              <Typography.Text type={component.value === null ? 'secondary' : undefined}>
                {t(`karstArea.component.${component.name}`)}
                {': '}
                {component.value === null || component.value === undefined
                  ? t('karstArea.componentAbsent')
                  : t('karstArea.componentPresent', {
                      value: component.value.toFixed(3),
                      reference: component.reference.toFixed(3),
                    })}
              </Typography.Text>
            </List.Item>
          )}
        />
      </div>

      <ExtremeList title={t('karstArea.deepest')} caves={data.deepestCaves} unit={t('karstArea.metresShort')} />
      <ExtremeList title={t('karstArea.longest')} caves={data.longestCaves} unit={t('karstArea.metresShort')} />

      <Table
        style={{ marginTop: 16 }}
        size="small"
        pagination={false}
        rowKey={(row) => String(row.rockTypeId ?? 'none')}
        dataSource={data.rockTypes as AreaKarstStatistics['rockTypes']}
        columns={[
          {
            title: t('karstArea.rockType'),
            render: (_: unknown, row) => row.name ?? t('karstArea.rockTypeUnrecorded'),
          },
          { title: t('karstArea.caveCount'), dataIndex: 'caveCount' },
        ]}
      />

      <Typography.Paragraph type="secondary" style={{ marginTop: 16, marginBottom: 0 }}>
        {t('karstArea.scopeNote')}
      </Typography.Paragraph>
    </Card>
  );
}

function ExtremeList({
  title,
  caves,
  unit,
}: {
  title: string;
  caves: readonly AreaCaveExtreme[];
  unit: string;
}) {
  if (caves.length === 0) return null;

  return (
    <List
      style={{ marginTop: 16 }}
      size="small"
      header={<Typography.Text strong>{title}</Typography.Text>}
      dataSource={caves as AreaCaveExtreme[]}
      renderItem={(cave) => (
        <List.Item>
          {cave.name} — {Math.round(cave.value)} {unit}
        </List.Item>
      )}
    />
  );
}

/** A number, or the words that say why there is none — never a zero standing in for a gap. */
function format(value: number | null | undefined, digits: number, absent: string): string {
  return value === null || value === undefined ? absent : value.toFixed(digits);
}
