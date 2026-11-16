// SPDX-License-Identifier: AGPL-3.0-or-later
import { Card, Empty, InputNumber, Space, Table, Tag, Typography } from 'antd';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';

import { useFeaturePassages, type FeaturePassage } from '../../api/hooks.ts';

interface FeaturePassagesCardProps {
  featureId: string;
  /** The feature's GeoJSON geometry type, or null when it carries no geometry. */
  geometryType: string | null;
  /** The feature's kind: only a plain feature (a doline, a sinkhole, an area) is asked about. */
  kind: string;
}

/** Geometry classes there is a position to look beneath. */
const askable = new Set(['Point', 'Polygon', 'MultiPolygon']);

/** The reach asked for until the reader chooses another, metres. Matches the server's default. */
const DEFAULT_REACH_M = 50;

/**
 * The caves whose passage lies beneath, or near, a doline, a sinkhole or an outline — with the plan
 * offset to the nearest passage and the rock between it and the surface.
 *
 * <p>
 * <b>Absent rather than empty when the server has no answer.</b> The list is built only from
 * features and caves the reader may place exactly, and a refusal is spelled as "no such feature":
 * a card of blanks would announce that a guarded feature is here and read as "nothing runs under
 * this". An answer with no caves in it, by contrast, is said in words — it is an answer.
 * </p>
 * <p>
 * Read the other way, the same card is the inverse question: opened on an outline, with a reach of
 * nought, it lists the passages that run under that polygon.
 * </p>
 */
export default function FeaturePassagesCard({ featureId, geometryType, kind }: FeaturePassagesCardProps) {
  const { t } = useTranslation();
  const [reach, setReach] = useState<number>(DEFAULT_REACH_M);
  const enabled = kind === 'generic' && geometryType !== null && askable.has(geometryType);
  const { data, isError } = useFeaturePassages(featureId, reach, enabled);

  if (!enabled || isError) {
    return null;
  }

  const dash = t('featurePassages.notMeasured');
  const metres = (value: number | null | undefined) =>
    value === null || value === undefined
      ? dash
      : t('featurePassages.unitMetres', { value: (Math.round(value * 10) / 10).toFixed(1) });

  return (
    <Card title={t('featurePassages.title')} style={{ marginBottom: 16 }} data-testid="feature-passages">
      <Space style={{ marginBottom: 8 }}>
        <Typography.Text>{t('featurePassages.reach')}</Typography.Text>
        <InputNumber
          min={0}
          max={500}
          step={10}
          value={reach}
          onChange={(value) => {
            if (value !== null) setReach(value);
          }}
          aria-label={t('featurePassages.reach')}
        />
      </Space>
      <Typography.Paragraph type="secondary">
        {t('featurePassages.intro', { distance: reach })}
      </Typography.Paragraph>

      {data && !data.hasTerrain && (
        <Typography.Paragraph type="secondary" data-testid="feature-passages-no-terrain">
          {t('featurePassages.noTerrain')}
        </Typography.Paragraph>
      )}

      {data && data.passages.length === 0 ? (
        <Empty description={t('featurePassages.empty')} />
      ) : (
        <Table<FeaturePassage>
          size="small"
          rowKey="caveId"
          loading={!data}
          pagination={false}
          dataSource={data ? [...data.passages] : []}
          columns={[
            {
              key: 'cave',
              title: t('featurePassages.cave'),
              render: (_, row) => (
                <Space size={4}>
                  <Link to={`/caves/${row.caveId}`}>{row.caveName ?? row.caveId}</Link>
                  {row.isApproximation && <Tag>{t('featurePassages.approximate')}</Tag>}
                </Space>
              ),
            },
            {
              key: 'offset',
              title: t('featurePassages.offset'),
              render: (_, row) =>
                row.lateralOffsetM === 0 ? t('featurePassages.offsetUnder') : metres(row.lateralOffsetM),
            },
            {
              key: 'altitude',
              title: t('featurePassages.altitude'),
              render: (_, row) => (row.hasAltitudes ? metres(row.passageAltitudeM) : t('featurePassages.noAltitudes')),
            },
            {
              key: 'overburden',
              title: t('featurePassages.overburden'),
              render: (_, row) => metres(row.overburdenM),
            },
          ]}
        />
      )}

      {data?.truncated && (
        <Typography.Paragraph type="secondary" style={{ marginTop: 8, marginBottom: 0 }}>
          {t('featurePassages.truncated')}
        </Typography.Paragraph>
      )}
    </Card>
  );
}
