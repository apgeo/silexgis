// SPDX-License-Identifier: AGPL-3.0-or-later
import { Alert, Descriptions, Empty, Flex, Table, Tag, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import type { SurveyLogExtent, SurveyLogFigures } from '../../../api/hooks.ts';
import LoopClosureChart from '../../statistics/LoopClosureChart.tsx';
import { formatFigure } from './formatFigure.ts';

const OUTCOME_COLOR = { succeeded: 'success', succeededWithWarnings: 'warning', failed: 'error' } as const;

/**
 * What the compiler's log says about a survey: how much was surveyed, how deep and how wide it
 * reaches and between which stations, how many stations and shots, the coordinate system and
 * declinations it was computed with, and how its loops close — with a chart and a table of each
 * loop's misclosure when there are loops.
 *
 * Every figure is one the server read out of the log, which the reader of this page may download
 * whole. A figure the log never stated is shown as unknown rather than as zero, because a run that
 * stopped early never reaches the statistics.
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
  // An extent with the stations at its ends and their height, northing or easting beneath it.
  const extent = (range: SurveyLogExtent | null | undefined, testId: string) =>
    range ? (
      <span data-testid={testId}>
        {metres(range.lengthM)}
        <br />
        <Typography.Text type="secondary" style={{ fontSize: 12 }}>
          {t('surveyViewer.figures.extentEnds', {
            from: range.fromStation,
            fromValue: formatFigure(range.fromValueM, lang),
            to: range.toStation,
            toValue: formatFigure(range.toValueM, lang),
          })}
        </Typography.Text>
      </span>
    ) : (
      '—'
    );
  const degrees = (value: number) => t('surveyViewer.figures.degrees', { value: formatFigure(value, lang, 4) });

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
          { key: 'depth', label: t('surveyViewer.figures.verticalRange'), children: extent(figures.verticalRange, 'survey-viewer-vertical-range') },
          { key: 'ns', label: t('surveyViewer.figures.northSouthRange'), children: extent(figures.northSouthRange, 'survey-viewer-ns-range') },
          { key: 'ew', label: t('surveyViewer.figures.eastWestRange'), children: extent(figures.eastWestRange, 'survey-viewer-ew-range') },
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
          {
            key: 'crs',
            label: t('surveyViewer.figures.coordinateSystem'),
            children: <span data-testid="survey-viewer-crs">{figures.outputCoordinateSystem ?? '—'}</span>,
          },
          {
            key: 'convergence',
            label: t('surveyViewer.figures.meridianConvergence'),
            children:
              figures.meridianConvergenceDeg === null || figures.meridianConvergenceDeg === undefined
                ? '—'
                : degrees(figures.meridianConvergenceDeg),
          },
          {
            key: 'declinations',
            label: t('surveyViewer.figures.declinations'),
            children:
              figures.declinations.length === 0
                ? '—'
                : figures.declinations.map((d) => `${d.date}: ${degrees(d.degrees)}`).join(', '),
          },
        ]}
      />
      {figures.hasNoControlPoints && (
        <Alert type="info" showIcon title={t('surveyViewer.figures.noControlPoints')} data-testid="survey-viewer-no-control-points" />
      )}
      {figures.loops.length > 0 && <LoopClosureChart loops={figures.loops} />}
      {figures.loops.length > 0 && (
        <Table
          size="small"
          data-testid="survey-viewer-loops"
          rowKey="n"
          pagination={figures.loops.length > 10 ? { pageSize: 10 } : false}
          dataSource={figures.loops.map((loop, index) => ({ ...loop, n: index + 1 }))}
          columns={[
            { key: 'n', title: t('surveyViewer.figures.loopAxis'), dataIndex: 'n' },
            {
              key: 'percent',
              title: t('surveyViewer.figures.loopErrorAxis'),
              render: (_: unknown, loop) => formatFigure(loop.relativeErrorPercent, lang, 2),
            },
            { key: 'error', title: t('surveyViewer.figures.loopError'), render: (_: unknown, loop) => metres(loop.absoluteErrorM) },
            { key: 'length', title: t('surveyViewer.figures.loopLength'), render: (_: unknown, loop) => metres(loop.lengthM) },
            { key: 'stations', title: t('surveyViewer.figures.loopStations'), render: (_: unknown, loop) => loop.stations },
          ]}
        />
      )}
    </Flex>
  );
}
