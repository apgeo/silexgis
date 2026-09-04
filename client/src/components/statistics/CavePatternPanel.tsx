// SPDX-License-Identifier: AGPL-3.0-or-later
import { Alert, Card, Empty, Space, Table, Tag, Typography } from 'antd';
import { useTranslation } from 'react-i18next';

import {
  useCavePattern,
  type PatternFigure,
  type PatternRule,
  type PatternRuleOutcome,
  type PatternRuleTrace,
  type SpeleogeneticPatternKind,
} from '../../api/hooks.ts';
import SurveyBasisNote from './SurveyBasisNote.tsx';

/**
 * What kind of cave a survey's shape suggests, with the rules that said so.
 *
 * <p>
 * The reasoning is shown by default and is not put behind a disclosure, because the reasoning is
 * the point. A pattern name on its own is an assertion a reader cannot check; the rules that fired,
 * the figures they fired on, and the rules that looked and stayed silent are what turn it into a
 * reading somebody who knows the cave can disagree with. A reader who has to open something to see
 * why is a reader who will take the label on trust.
 * </p>
 * <p>
 * The rules that did not fire are listed beside the ones that did, and so are the rules that had
 * nothing to read. A rule never shown staying silent is not a rule, it is a constant — and a rule
 * that could not be assessed at all is a different statement from one that looked and disagreed.
 * </p>
 * <p>
 * Absent rather than empty when the server has no answer, which covers a cave this reader may see
 * but may not place.
 * </p>
 */
export default function CavePatternPanel({ caveId }: { caveId: string }) {
  const { t } = useTranslation();
  const { data, isLoading, isError } = useCavePattern(caveId);

  if (isError) return null;

  const body = () => {
    if (isLoading || !data) return <Empty description={t('passagePattern.loading')} />;

    const { suggestion } = data;
    const outcomeColour: Record<PatternRuleOutcome, string | undefined> = {
      fired: 'blue',
      didNotFire: undefined,
      notAssessable: undefined,
    };

    return (
      <div data-testid="cave-pattern">
        <Space orientation="vertical" size={12} style={{ width: '100%' }}>
          <Typography.Title level={4} style={{ marginBottom: 0 }}>
            {t('passagePattern.suggested')}:{' '}
            <span data-testid="cave-pattern-label">
              {t(`passagePattern.pattern.${suggestion.pattern as SpeleogeneticPatternKind}`)}
            </span>
          </Typography.Title>

          <Typography.Text type="secondary">
            {t('passagePattern.assessed', {
              fired: suggestion.firedRuleCount,
              assessable: suggestion.assessableRuleCount,
              total: suggestion.rules.length,
            })}
          </Typography.Text>

          {data.network && (
            <Typography.Text type="secondary">
              {t('passagePattern.network')}:{' '}
              {t('passagePattern.networkFigures', {
                nodes: data.network.reducedNodeCount,
                loops: data.network.cyclomaticNumber,
                ends: data.network.extremityCount,
              })}
            </Typography.Text>
          )}

          {suggestion.caveats.map((caveat) => (
            <Alert
              key={caveat}
              type="warning"
              showIcon
              // `title`, not the older `message`: the deprecated prop writes a warning to the
              // console on every render, and a warning nobody meant is a recorded defect.
              title={t(`passagePattern.caveat.${caveat}`)}
            />
          ))}

          <Typography.Paragraph type="secondary" style={{ marginBottom: 0 }}>
            {t('passagePattern.notAnAssertion')}
          </Typography.Paragraph>

          <div data-testid="cave-pattern-rules">
            <Typography.Title level={5}>{t('passagePattern.rulesTitle')}</Typography.Title>
            <Table
              size="small"
              pagination={false}
              rowKey={(row: PatternRuleTrace) => row.rule}
              dataSource={suggestion.rules}
              columns={[
                {
                  title: t('passagePattern.rulesTitle'),
                  key: 'rule',
                  render: (_: unknown, row) => t(`passagePattern.rule.${row.rule as PatternRule}`),
                },
                {
                  title: t('passagePattern.outcome.fired'),
                  key: 'outcome',
                  render: (_: unknown, row) => (
                    <Tag color={outcomeColour[row.outcome as PatternRuleOutcome]}>
                      {t(`passagePattern.outcome.${row.outcome as PatternRuleOutcome}`)}
                    </Tag>
                  ),
                },
                {
                  title: t('passagePattern.figures'),
                  key: 'figures',
                  render: (_: unknown, row) =>
                    row.figures
                      .map(
                        (figure) =>
                          `${t(`passagePattern.figure.${figure.figure as PatternFigure}`)}: ${
                            typeof figure.value === 'number'
                              ? figure.value.toFixed(2)
                              : t('crossSection.notMeasured')
                          }`,
                      )
                      .join(' · '),
                },
                {
                  title: t('passagePattern.supports'),
                  key: 'supports',
                  render: (_: unknown, row) =>
                    row.supports
                      .map((kind) => t(`passagePattern.pattern.${kind as SpeleogeneticPatternKind}`))
                      .join(', '),
                },
              ]}
            />
          </div>

          <div data-testid="cave-pattern-scores">
            <Typography.Title level={5}>{t('passagePattern.scoresTitle')}</Typography.Title>
            <Table
              size="small"
              pagination={false}
              rowKey={(row) => row.kind}
              dataSource={suggestion.scores}
              columns={[
                {
                  title: t('passagePattern.scoresTitle'),
                  key: 'kind',
                  render: (_: unknown, row) =>
                    t(`passagePattern.pattern.${row.kind as SpeleogeneticPatternKind}`),
                },
                {
                  title: t('passagePattern.weight'),
                  key: 'score',
                  render: (_: unknown, row) => row.score.toFixed(2),
                },
              ]}
            />
          </div>
        </Space>
      </div>
    );
  };

  return (
    <Card title={t('passagePattern.title')} style={{ marginBottom: 16 }}>
      {body()}
      {data && <SurveyBasisNote basis={data.basis} />}
    </Card>
  );
}
