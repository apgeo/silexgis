// SPDX-License-Identifier: AGPL-3.0-or-later
import { Alert, Card, Col, Descriptions, Empty, Row, Statistic } from 'antd';
import { useTranslation } from 'react-i18next';

import { useCaveSurveyStatistics, type MorphometryComparison } from '../../api/hooks.ts';
import SurveyBasisNote from './SurveyBasisNote.tsx';

/**
 * What a cave's own line work measures, set against what the record says about it.
 *
 * <p>
 * Every figure here is worked out from the survey when the page is opened, so it follows the line
 * work rather than a number typed in beside it. That is the whole reason the two can disagree, and
 * the disagreement is shown as a warning a reader can act on rather than as a difference they have
 * to notice: a registry's typed-in length is frequently older than the survey it is being compared
 * against, so neither side is automatically the wrong one and the panel does not claim otherwise.
 * </p>
 * <p>
 * A missing figure is a dash, never a zero. "0 m of passage" is a statement about a cave, and this
 * panel must not make it while it is still asking or when the survey did not answer.
 * </p>
 */
export default function CaveStatisticsPanel({ caveId }: { caveId: string }) {
  const { t } = useTranslation();
  const { data, isLoading, isError } = useCaveSurveyStatistics(caveId);

  // A cave the caller may not read — or may read but not place exactly — is refused, and then a
  // panel of blanks would be a claim about the cave rather than about the reader's access.
  if (isError) return null;

  const dash = '—';
  const metres = (value: number | null | undefined) =>
    typeof value === 'number' ? t('trips.metres', { value: Math.round(value * 10) / 10 }) : dash;
  // Two decimals on a ratio: these are quotients of two measured lengths, and a third decimal
  // claims a precision the survey behind them does not carry.
  const ratio = (value: number | null | undefined) =>
    typeof value === 'number' ? value.toFixed(2) : dash;
  const count = (value: number | null | undefined) => (typeof value === 'number' ? value : dash);

  const indices = data?.indices;
  const nothingMeasured = data !== undefined && data.basis === 'unavailable';

  const tiles = [
    { key: 'length', label: t('statistics.cave.totalLength'), value: metres(indices?.totalLengthM) },
    { key: 'plan', label: t('statistics.cave.planLength'), value: metres(indices?.planLengthM) },
    { key: 'vertical', label: t('statistics.cave.verticalExtent'), value: metres(indices?.verticalExtentM) },
    { key: 'extent', label: t('statistics.cave.maximumExtent'), value: metres(indices?.maximumExtentM) },
    { key: 'verticality', label: t('statistics.cave.verticality'), value: ratio(indices?.verticality) },
    { key: 'horizontality', label: t('statistics.cave.horizontality'), value: ratio(indices?.horizontality) },
    { key: 'linearity', label: t('statistics.cave.linearity'), value: ratio(indices?.linearity) },
    { key: 'sinuosity', label: t('statistics.cave.sinuosity'), value: ratio(indices?.sinuosity) },
    { key: 'segments', label: t('statistics.cave.segments'), value: count(indices?.segmentCount) },
    { key: 'paths', label: t('statistics.cave.paths'), value: count(indices?.pathCount) },
  ];

  const comparison = (label: string, subject: MorphometryComparison | undefined) => ({
    key: label,
    label,
    children: describe(subject),
  });

  function describe(subject: MorphometryComparison | undefined): string {
    if (!subject) return dash;
    if (subject.agreement === 'notComputed') return t('statistics.cave.notComputed');
    if (subject.agreement === 'notDeclared') {
      return t('statistics.cave.notDeclared', { computed: metres(subject.computedM) });
    }
    return t('statistics.cave.against', {
      computed: metres(subject.computedM),
      declared: metres(subject.declaredM),
      difference: metres(Math.abs(subject.differenceM ?? 0)),
    });
  }

  const body = nothingMeasured ? (
    <Empty description={t('statistics.cave.nothingMeasured')} />
  ) : (
    <div data-testid="cave-survey-statistics">
      <Row gutter={[16, 16]}>
        {tiles.map((tile) => (
          <Col key={tile.key} xs={12} sm={8} md={6}>
            <Statistic title={tile.label} value={tile.value} loading={isLoading} />
          </Col>
        ))}
      </Row>

      {data && (
        <Descriptions
          size="small"
          column={{ xs: 1, sm: 1, md: 2 }}
          style={{ marginTop: 16 }}
          items={[
            comparison(t('statistics.cave.lengthAgainstRecord'), data.length),
            comparison(t('statistics.cave.depthAgainstRecord'), data.depth),
          ]}
        />
      )}

      {data?.declaredDisagrees && (
        <Alert
          type="warning"
          showIcon
          style={{ marginTop: 16 }}
          message={t('statistics.cave.disagreementTitle')}
          description={t('statistics.cave.disagreementBody')}
        />
      )}

      {data && <SurveyBasisNote basis={data.basis} />}
    </div>
  );

  return (
    <Card size="small" title={t('statistics.cave.title')} style={{ marginTop: 16 }}>
      {body}
    </Card>
  );
}
