// SPDX-License-Identifier: AGPL-3.0-or-later
import { Card, Col, Descriptions, Empty, Row, Statistic, Table, Typography } from 'antd';
import { useTranslation } from 'react-i18next';

import type { CrossSectionDistribution } from '../../api/hooks.ts';
import { useCaveCrossSection } from '../../api/hooks.ts';
import { SummaryBoxChart } from './DistributionCharts.tsx';
import type { FiveNumber } from './distributions.ts';
import SurveyBasisNote from './SurveyBasisNote.tsx';

/**
 * How big a cave's passages are, from the wall distances recorded at its stations.
 *
 * <p>
 * A missing figure is a dash, never a zero, and every figure is shown with the count it was worked
 * out over. A surveyor who did not reach a wall recorded nothing, and nothing is not a wall at
 * distance zero — so a station missing a wall leaves the figure that needs it instead of shrinking
 * it. That makes the denominators load-bearing rather than decorative: a volume over a tenth of a
 * cave and a volume over all of it are the same number with two entirely different meanings, and
 * the count beside them is the only thing that separates the two.
 * </p>
 * <p>
 * Absent rather than empty when the server has no answer, which covers a cave this reader may see
 * but may not place: the refusal is spelled as "no such cave", and a card of blanks would announce
 * that a guarded cave is here.
 * </p>
 */
export default function CaveCrossSectionPanel({ caveId }: { caveId: string }) {
  const { t } = useTranslation();
  const { data, isLoading, isError } = useCaveCrossSection(caveId);

  if (isError) return null;

  const dash = t('crossSection.notMeasured');
  const metres = (value: number | null | undefined) =>
    typeof value === 'number' ? t('crossSection.unitMetres', { value: round(value) }) : dash;
  const squareMetres = (value: number | null | undefined) =>
    typeof value === 'number' ? t('crossSection.unitSquareMetres', { value: round(value) }) : dash;
  const cubicMetres = (value: number | null | undefined) =>
    typeof value === 'number' ? t('crossSection.unitCubicMetres', { value: Math.round(value) }) : dash;

  const body = () => {
    if (isLoading || !data) return <Empty description={t('crossSection.loading')} />;
    if (!data.hasReadings || !data.summary) {
      return <Empty description={t('crossSection.noReadings')} />;
    }

    const summary = data.summary;
    const tiles = [
      {
        key: 'width',
        label: t('crossSection.width'),
        value: metres(summary.width?.median),
        over: summary.widthStationCount,
      },
      {
        key: 'height',
        label: t('crossSection.height'),
        value: metres(summary.height?.median),
        over: summary.heightStationCount,
      },
      {
        key: 'area',
        label: t('crossSection.area'),
        value: squareMetres(summary.area?.median),
        over: summary.areaStationCount,
      },
      {
        key: 'ratio',
        label: t('crossSection.ratio'),
        value:
          typeof summary.widthHeightRatio?.median === 'number'
            ? summary.widthHeightRatio.median.toFixed(2)
            : dash,
        over: summary.widthHeightRatio?.count ?? 0,
      },
    ];

    const volume = summary.volume;

    return (
      <div data-testid="cave-cross-section">
        <Row gutter={[16, 16]}>
          {tiles.map((tile) => (
            <Col key={tile.key} xs={12} md={6}>
              <Statistic
                title={`${tile.label} (${t('crossSection.median')})`}
                value={tile.value}
                suffix={
                  <Typography.Text type="secondary" style={{ fontSize: '0.75em' }}>
                    {t('crossSection.measuredOver', { count: tile.over })}
                  </Typography.Text>
                }
              />
            </Col>
          ))}
        </Row>

        <Descriptions
          size="small"
          column={{ xs: 1, md: 2 }}
          style={{ marginTop: 16 }}
          items={[
            {
              key: 'stations',
              label: t('crossSection.stations'),
              children: summary.stationCount,
            },
            {
              key: 'readings',
              label: t('crossSection.readings'),
              children: summary.readingCount,
            },
            {
              key: 'volume',
              label: t('crossSection.volume'),
              children: cubicMetres(volume.volumeM3),
            },
            {
              key: 'widthSpread',
              label: t('crossSection.width'),
              children: summary.width
                ? `${t('crossSection.range', {
                    min: metres(summary.width.minimum),
                    max: metres(summary.width.maximum),
                  })} · ${t('crossSection.quartiles', {
                    lower: metres(summary.width.lowerQuartile),
                    upper: metres(summary.width.upperQuartile),
                  })}`
                : dash,
            },
            {
              key: 'heightSpread',
              label: t('crossSection.height'),
              children: summary.height
                ? `${t('crossSection.range', {
                    min: metres(summary.height.minimum),
                    max: metres(summary.height.maximum),
                  })} · ${t('crossSection.quartiles', {
                    lower: metres(summary.height.lowerQuartile),
                    upper: metres(summary.height.upperQuartile),
                  })}`
                : dash,
            },
          ]}
        />

        <Typography.Paragraph type="secondary" style={{ marginTop: 8, marginBottom: 0 }}>
          {volume.volumeM3 === null || volume.volumeM3 === undefined
            ? t('crossSection.volumeNotComputed')
            : t('crossSection.volumeScope', {
                measured: volume.measuredLegCount,
                total: volume.legCount,
                measuredLength: metres(volume.measuredLengthM),
                totalLength: metres(volume.lengthM),
              })}
        </Typography.Paragraph>

        <Typography.Paragraph type="secondary" style={{ marginTop: 8, marginBottom: 0 }}>
          {t('crossSection.ratioNote')}
        </Typography.Paragraph>

        {summary.scaling && (
          <Typography.Paragraph style={{ marginTop: 8, marginBottom: 0 }}>
            <Typography.Text strong>{t('crossSection.scaling')}</Typography.Text>{' '}
            {t('crossSection.scalingValue', { exponent: summary.scaling.exponent.toFixed(2) })}{' '}
            <Typography.Text type="secondary">
              (
              {t('crossSection.scalingFit', {
                count: summary.scaling.stationCount,
                r2: summary.scaling.rSquared.toFixed(2),
              })}
              )
            </Typography.Text>
            <br />
            <Typography.Text type="secondary">{t('crossSection.scalingNote')}</Typography.Text>
          </Typography.Paragraph>
        )}

        {(summary.width || summary.height) && (
          <div data-testid="cave-cross-section-spread" style={{ marginTop: 16 }}>
            <Typography.Title level={5}>{t('crossSection.spreadTitle')}</Typography.Title>
            <SummaryBoxChart
              testId="chart-cross-section-spread"
              categories={[t('crossSection.width'), t('crossSection.height')]}
              series={[
                {
                  name: t('crossSection.title'),
                  summaries: [box(summary.width), box(summary.height)],
                },
              ]}
              yLabel={t('crossSection.axisMetres')}
              height={200}
            />
            <Typography.Paragraph type="secondary" style={{ marginTop: 8, marginBottom: 0 }}>
              {t('crossSection.spreadNote')}
            </Typography.Paragraph>
          </div>
        )}

        {summary.bands.length > 0 && (
          <div data-testid="cave-cross-section-bands" style={{ marginTop: 16 }}>
            <Typography.Title level={5}>{t('crossSection.bandChartTitle')}</Typography.Title>
            <SummaryBoxChart
              testId="chart-cross-section-bands"
              categories={summary.bands.map((band) =>
                t('crossSection.bandLabel', { from: metres(band.fromM), to: metres(band.toM) }))}
              series={[
                { name: t('crossSection.width'), summaries: summary.bands.map((b) => box(b.width)) },
                { name: t('crossSection.height'), summaries: summary.bands.map((b) => box(b.height)) },
              ]}
              yLabel={t('crossSection.axisMetres')}
              height={240}
            />

            <Typography.Title level={5} style={{ marginTop: 16 }}>{t('crossSection.bandsTitle')}</Typography.Title>
            <Table
              size="small"
              pagination={false}
              rowKey={(band) => `${band.fromM}`}
              dataSource={[...summary.bands].reverse()}
              columns={[
                {
                  title: t('crossSection.bandsTitle'),
                  key: 'band',
                  render: (_: unknown, band) =>
                    t('crossSection.bandLabel', {
                      from: metres(band.fromM),
                      to: metres(band.toM),
                    }),
                },
                {
                  title: t('crossSection.stations'),
                  key: 'stations',
                  render: (_: unknown, band) => band.stationCount,
                },
                {
                  title: t('crossSection.width'),
                  key: 'width',
                  render: (_: unknown, band) => metres(band.width?.median),
                },
                {
                  title: t('crossSection.height'),
                  key: 'height',
                  render: (_: unknown, band) => metres(band.height?.median),
                },
                {
                  title: t('crossSection.area'),
                  key: 'area',
                  render: (_: unknown, band) => squareMetres(band.area?.median),
                },
              ]}
            />
            <Typography.Paragraph type="secondary" style={{ marginTop: 8, marginBottom: 0 }}>
              {t('crossSection.bandsNote', { height: metres(summary.bandWidthM) })}
            </Typography.Paragraph>
          </div>
        )}
      </div>
    );
  };

  return (
    <Card title={t('crossSection.title')} style={{ marginBottom: 16 }}>
      {body()}
      {data && <SurveyBasisNote basis={data.basis} />}
    </Card>
  );
}

/**
 * A server-computed five-number summary in the shape the chart layer draws, or null where there is
 * none. The quartiles are carried across rather than recomputed: a box drawn from numbers derived a
 * second way would eventually disagree with the same figures printed beside it.
 */
function box(distribution: CrossSectionDistribution | null | undefined): FiveNumber | null {
  return distribution
    ? {
        min: distribution.minimum,
        q1: distribution.lowerQuartile,
        median: distribution.median,
        q3: distribution.upperQuartile,
        max: distribution.maximum,
        count: distribution.count,
      }
    : null;
}

/** One decimal on a measured length: a wall distance is read off a tape, not a micrometer. */
function round(value: number) {
  return Math.round(value * 10) / 10;
}
