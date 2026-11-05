// SPDX-License-Identifier: AGPL-3.0-or-later
import { Descriptions, Empty, Flex, Tag, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import type { SurveyLogFigures } from '../../../api/hooks.ts';
import LoopClosureChart from '../../statistics/LoopClosureChart.tsx';
import { formatFigure } from './formatFigure.ts';

const OUTCOME_COLOR = { succeeded: 'success', succeededWithWarnings: 'warning', failed: 'error' } as const;

/**
 * What the compiler's log says about a survey: how much was surveyed, how deep and how wide it
 * reaches, how many stations and shots, and how its loops close — with a chart of each loop's
 * misclosure when there are loops.
 *
 * Every figure is a size the server read out of the log. A figure the log never stated is shown as
 * unknown rather than as zero, because a run that stopped early never reaches the statistics.
 */
export default function SurveyLogFiguresView({ figures }: { figures: SurveyLogFigures | null | undefined }) {
  const { t, i18n } = useTranslation();
  const lang = i18n.language;

  if (!figures) {
    return (
      <Empty
        image={Empty.PRESENTED_IMAGE_SIMPLE}
        description={t('surveyViewer.figures.none')}
        data-testid="survey-viewer-no-figures"
      />
    );
  }

  const metres = (value: number | null | undefined) =>
    value === null || value === undefined ? '—' : t('surveyViewer.figures.metres', { value: formatFigure(value, lang) });

  return (
    <Flex vertical gap={12} data-testid="survey-viewer-figures">
      <Flex gap={8} align="center" wrap>
        <Tag color={OUTCOME_COLOR[figures.outcome]} data-testid="survey-viewer-outcome">
          {t(`surveyViewer.figures.outcome.${figures.outcome}`)}
        </Tag>
        {figures.compilerVersion && (
          <Typography.Text type="secondary">
            {t('surveyViewer.figures.compiler', { version: figures.compilerVersion })}
          </Typography.Text>
        )}
      </Flex>
      <Descriptions
        size="small"
        bordered
        column={{ xs: 1, sm: 2, lg: 3 }}
        items={[
          { key: 'length', label: t('surveyViewer.figures.totalLength'), children: <span data-testid="survey-viewer-total-length">{metres(figures.totalLengthM)}</span> },
          { key: 'adjusted', label: t('surveyViewer.figures.adjustedLength'), children: metres(figures.totalLengthAdjustedM) },
          { key: 'plan', label: t('surveyViewer.figures.planLength'), children: metres(figures.planLengthM) },
          { key: 'vertical', label: t('surveyViewer.figures.verticalLength'), children: metres(figures.verticalLengthM) },
          { key: 'depth', label: t('surveyViewer.figures.verticalRange'), children: metres(figures.verticalRangeM) },
          { key: 'ns', label: t('surveyViewer.figures.northSouthRange'), children: metres(figures.northSouthRangeM) },
          { key: 'ew', label: t('surveyViewer.figures.eastWestRange'), children: metres(figures.eastWestRangeM) },
          { key: 'stations', label: t('surveyViewer.figures.stations'), children: formatFigure(figures.stationCount, lang, 0) },
          { key: 'shots', label: t('surveyViewer.figures.shots'), children: formatFigure(figures.shotCount, lang, 0) },
          { key: 'components', label: t('surveyViewer.figures.components'), children: formatFigure(figures.connectedComponents, lang, 0) },
          { key: 'loops', label: t('surveyViewer.figures.loops'), children: formatFigure(figures.loopCount, lang, 0) },
          {
            key: 'loopError',
            label: t('surveyViewer.figures.averageLoopError'),
            children:
              figures.averageLoopErrorPercent === null || figures.averageLoopErrorPercent === undefined
                ? '—'
                : t('surveyViewer.figures.percent', { value: formatFigure(figures.averageLoopErrorPercent, lang, 2) }),
          },
          { key: 'errors', label: t('surveyViewer.figures.errors'), children: formatFigure(figures.errorCount, lang, 0) },
          { key: 'warnings', label: t('surveyViewer.figures.warnings'), children: formatFigure(figures.warningCount, lang, 0) },
        ]}
      />
      {figures.loops.length > 0 && <LoopClosureChart loops={figures.loops} />}
    </Flex>
  );
}
