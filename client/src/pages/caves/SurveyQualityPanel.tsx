// SPDX-License-Identifier: AGPL-3.0-or-later
import { Alert, Card, Descriptions, Table, Tag, Tooltip, Typography } from 'antd';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  useSurveyCompilations,
  type SurveyCompilationInfo,
  type SurveyLoopError,
} from '../../api/hooks.ts';

/**
 * How well a cave's surveys closed, as the compiler that compiled them reported it.
 *
 * Nothing on this panel is worked out here. Every figure is one the compiler printed in the log the
 * surveyor archived, under the compiler's own column names — REL-ERR, ABS-ERR, TOTAL-L, STS and the
 * three axis errors — so that a number here and a number in the reader's own log are visibly the
 * same number and a disagreement between them is a real disagreement rather than two tools using
 * one word differently. The two summaries this panel does work out — the median of each error
 * column, and how far the compiler's adjustment moved the total length — are labelled as being
 * read off the table rather than printed by the compiler, because the log prints neither.
 *
 * The two error measures are never shown alone and never stand in for one another. The absolute
 * error is a distance: how far apart the two arrivals at the closing station are. The relative
 * error is a ratio: that distance as a percentage of the loop's own length, which is what says
 * whether the distance is large for a loop of this size. A short loop can be much the worse ratio
 * while being much the smaller distance, so a reader shown one of them alone is told the wrong
 * thing about half the loops. Both are columns, both say in words which kind of quantity they are,
 * and the table says which one it is currently ordered by.
 *
 * The panel is silent for a cave with no archived log — there is nothing to report, and an empty
 * card saying so on every cave in the registry would be noise. The server withholds the whole list
 * from a reader without the exact-location permission, which arrives here as the same empty list,
 * so nothing here has to decide anything about protection.
 */
export default function SurveyQualityPanel({ caveId }: { caveId: string }) {
  const { t, i18n } = useTranslation();
  const { data: compilations } = useSurveyCompilations(caveId);

  if (!compilations || compilations.length === 0) {
    return null;
  }

  return (
    <Card title={t('surveyQuality.title')} style={{ marginTop: 16 }} data-testid="survey-closure">
      <Typography.Paragraph type="secondary" style={{ fontSize: 12 }}>
        {t('surveyQuality.explanation')}
      </Typography.Paragraph>
      {compilations.map((compilation) => (
        <Compilation key={compilation.id} compilation={compilation} locale={i18n.language} t={t} />
      ))}
    </Card>
  );
}

/** Which column the loop table is ordered by, said in the sentence above it. */
const ORDER_KEYS: Record<string, string> = {
  relativeErrorPercent: 'surveyQuality.orderedBy.relErr',
  absoluteErrorM: 'surveyQuality.orderedBy.absErr',
  totalLengthM: 'surveyQuality.orderedBy.totalLength',
};

/**
 * The middle value of a set of readings, or null when there are none. Averaged across the two
 * middle readings on an even count, which is the ordinary definition and matters here because a
 * survey with two loops is common.
 */
function median(values: number[]): number | null {
  if (values.length === 0) {
    return null;
  }

  const sorted = [...values].sort((a, b) => a - b);
  const middle = Math.floor(sorted.length / 2);
  return sorted.length % 2 === 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
}

const OUTCOME_LABEL_KEYS: Record<'succeeded' | 'succeededWithWarnings' | 'failed', string> = {
  succeeded: 'surveyQuality.outcomes.succeeded',
  succeededWithWarnings: 'surveyQuality.outcomes.succeededWithWarnings',
  failed: 'surveyQuality.outcomes.failed',
};

function Compilation({
  compilation,
  locale,
  t,
}: {
  compilation: SurveyCompilationInfo;
  locale: string;
  t: (key: string, options?: Record<string, unknown>) => string;
}) {
  const [sortField, setSortField] = useState('relativeErrorPercent');

  const medianRelative = median(compilation.loops.map((l) => l.relativeErrorPercent));
  const medianAbsolute = median(compilation.loops.map((l) => l.absoluteErrorM));
  const adjustment =
    compilation.totalLengthM === null || compilation.totalLengthAdjustedM === null
      ? null
      : compilation.totalLengthAdjustedM - compilation.totalLengthM;

  const number = (value: number | null | undefined, digits: number) =>
    value === null || value === undefined ? '—' : value.toFixed(digits);
  const when = (value: string | null | undefined) =>
    value ? new Date(value).toLocaleString(locale) : '—';

  return (
    <div style={{ marginBottom: 24 }} data-testid="survey-compilation">
      <Typography.Text strong>{compilation.sourceName}</Typography.Text>{' '}
      {compilation.status === 'pending' && <Tag>{t('surveyQuality.statuses.pending')}</Tag>}
      {compilation.status === 'unreadable' && (
        <Tag color="default">{t('surveyQuality.statuses.unreadable')}</Tag>
      )}
      {compilation.outcome && (
        <Tag color={compilation.outcome === 'failed' ? 'red' : undefined}>
          {t(OUTCOME_LABEL_KEYS[compilation.outcome])}
        </Tag>
      )}

      {/* Three states that read alike and must not: a log this application could not open says
          nothing about the survey at all; a compilation that stopped is a quality signal of its
          own; and a survey that simply has no loops is neither. */}
      {compilation.status === 'unreadable' && (
        <Alert
          type="warning"
          showIcon
          style={{ marginTop: 8 }}
          message={t('surveyQuality.unreadable')}
          description={compilation.readError ?? undefined}
        />
      )}
      {compilation.outcome === 'failed' && (
        <Alert
          type="error"
          showIcon
          style={{ marginTop: 8 }}
          message={t('surveyQuality.compilationFailed')}
          description={
            compilation.incompleteStage
              ? t('surveyQuality.stoppedAt', { stage: compilation.incompleteStage })
              : undefined
          }
        />
      )}

      <Descriptions
        size="small"
        column={{ xs: 1, sm: 2, lg: 3 }}
        style={{ marginTop: 12 }}
        items={[
          {
            // The tool's name and its version, because the log prints the version without repeating
            // the name and a bare "5.5.7+dev" names no compiler at all. The name is written here
            // rather than read from the record because Therion's is the only compilation log this
            // application knows how to read; the day a second one is read, the record has to carry
            // the name and this has to stop assuming it.
            key: 'compiler',
            label: t('surveyQuality.compiler'),
            children: compilation.compilerVersion
              ? t('surveyQuality.compilerValue', { version: compilation.compilerVersion })
              : '—',
          },
          {
            key: 'released',
            label: t('surveyQuality.compilerReleased'),
            children: compilation.compilerReleaseDate ?? '—',
          },
          {
            // A log carries no timestamp of its own, so this is the only date there is and it is
            // labelled as what it actually is rather than as the date of the run.
            key: 'read',
            label: t('surveyQuality.readAt'),
            children: when(compilation.readAt),
          },
          {
            key: 'revision',
            label: t('surveyQuality.revision'),
            children: compilation.logVersionNumber,
          },
          {
            key: 'loopCount',
            label: t('surveyQuality.loopCount'),
            children: compilation.loopCount ?? '—',
          },
          {
            key: 'averageLoopError',
            label: t('surveyQuality.averageLoopError'),
            children:
              compilation.averageLoopErrorPercent === null
                ? '—'
                : `${number(compilation.averageLoopErrorPercent, 2)} %`,
          },
          {
            // The average the compiler prints is pulled about by a single very short loop closing
            // to a terrible ratio; the median says what a typical loop of this survey did. Both
            // measures get one, because a median of the ratios and a median of the distances are
            // no more interchangeable than the columns they come from.
            key: 'medianLoopError',
            label: t('surveyQuality.medianLoopError'),
            children: medianRelative === null ? '—' : `${number(medianRelative, 2)} %`,
          },
          {
            key: 'medianAbsoluteError',
            label: t('surveyQuality.medianAbsoluteError'),
            children: medianAbsolute === null ? '—' : `${number(medianAbsolute, 2)} m`,
          },
          {
            key: 'totalLength',
            label: t('surveyQuality.totalLength'),
            children:
              compilation.totalLengthM === null ? '—' : `${number(compilation.totalLengthM, 1)} m`,
          },
          {
            key: 'totalLengthAdjusted',
            label: t('surveyQuality.totalLengthAdjusted'),
            children:
              compilation.totalLengthAdjustedM === null
                ? '—'
                : `${number(compilation.totalLengthAdjustedM, 1)} m`,
          },
          {
            // The two lengths are printed side by side above; this is the difference between them,
            // which is how far distributing the loop errors around the network moved the survey.
            // Shown as a signed figure because the adjustment can shorten a survey as well as
            // lengthen it, and which way it went is part of the answer.
            key: 'adjustment',
            label: t('surveyQuality.adjustment'),
            children:
              adjustment === null
                ? '—'
                : `${adjustment >= 0 ? '+' : '\u2212'}${number(Math.abs(adjustment), 1)} m`,
          },
          {
            key: 'compilationSeconds',
            label: t('surveyQuality.compilationSeconds'),
            children: compilation.compilationSeconds ?? '—',
          },
        ]}
      />

      {/* Only for a run that finished, and then only the claim the log actually supports. A run
          that stopped has no answer to give about loops, and saying "no loop errors" there would
          turn a failed compilation into a clean survey. Nor does an absent table mean an absent
          loop: the count and the table are printed by different stages, so a log that reports a
          count and no rows says how many loops there are and nothing about how they closed, and a
          log that reports neither says nothing at all. Only a count of zero is a survey with no
          closed loops in it. */}
      {compilation.status === 'read' &&
        compilation.outcome !== 'failed' &&
        compilation.loops.length === 0 && (
        <Typography.Paragraph type="secondary" style={{ fontSize: 12, marginTop: 12 }}>
          {compilation.loopCount === 0
            ? t('surveyQuality.noLoops')
            : compilation.loopCount === null || compilation.loopCount === undefined
              ? t('surveyQuality.noLoopTable')
              : t('surveyQuality.loopsWithoutTable', { loops: compilation.loopCount })}
        </Typography.Paragraph>
      )}

      {compilation.loops.length > 0 && (
        <>
          {/* Says which order the table is actually in, not which order it started in. The two
              error columns are both sortable, so a fixed sentence naming one of them would, the
              moment a reader sorted by the other, rank the loops by distance under the ratio's
              name — which is the single confusion this paragraph exists to prevent. */}
          <Typography.Paragraph type="secondary" style={{ fontSize: 12, marginTop: 12 }}>
            {`${t(ORDER_KEYS[sortField] ?? 'surveyQuality.orderedBy.table')} ${t('surveyQuality.orderingExplanation')}`}
          </Typography.Paragraph>
          <Table<SurveyLoopError>
            onChange={(_pagination, _filters, sorter) => {
              const active = Array.isArray(sorter) ? sorter[0] : sorter;
              setSortField(active?.order ? String(active.field ?? '') : '');
            }}
            scroll={{ x: 'max-content' }}
            rowKey="ordinal"
            size="small"
            dataSource={compilation.loops}
            pagination={compilation.loops.length > 10 ? { pageSize: 10, size: 'small' } : false}
            columns={[
              {
                title: t('surveyQuality.columns.ordinal'),
                dataIndex: 'ordinal',
                width: 70,
                align: 'right',
                render: (ordinal: number) => ordinal + 1,
              },
              {
                // The compiler's own heading, and under it what kind of quantity it is. The two
                // error columns are the pair a reader can least afford to confuse, so neither
                // relies on the reader knowing which of REL and ABS is the ratio.
                title: (
                  <MeasureHeading
                    heading={t('surveyQuality.columns.relErr')}
                    hint={t('surveyQuality.columns.relErrHint')}
                  />
                ),
                dataIndex: 'relativeErrorPercent',
                width: 150,
                align: 'right',
                defaultSortOrder: 'descend',
                sorter: (a, b) => a.relativeErrorPercent - b.relativeErrorPercent,
                render: (value: number) => `${value.toFixed(2)} %`,
              },
              {
                title: (
                  <MeasureHeading
                    heading={t('surveyQuality.columns.absErr')}
                    hint={t('surveyQuality.columns.absErrHint')}
                  />
                ),
                dataIndex: 'absoluteErrorM',
                width: 150,
                align: 'right',
                sorter: (a, b) => a.absoluteErrorM - b.absoluteErrorM,
                render: (value: number) => `${value.toFixed(2)} m`,
              },
              {
                title: t('surveyQuality.columns.totalLength'),
                dataIndex: 'totalLengthM',
                width: 120,
                align: 'right',
                sorter: (a, b) => a.totalLengthM - b.totalLengthM,
                render: (value: number) => `${value.toFixed(1)} m`,
              },
              {
                title: t('surveyQuality.columns.stationCount'),
                dataIndex: 'stationCount',
                width: 90,
                align: 'right',
              },
              {
                title: t('surveyQuality.columns.errorX'),
                dataIndex: 'errorXM',
                width: 100,
                align: 'right',
                render: (value: number) => `${value.toFixed(2)} m`,
              },
              {
                title: t('surveyQuality.columns.errorY'),
                dataIndex: 'errorYM',
                width: 100,
                align: 'right',
                render: (value: number) => `${value.toFixed(2)} m`,
              },
              {
                title: t('surveyQuality.columns.errorZ'),
                dataIndex: 'errorZM',
                width: 100,
                align: 'right',
                render: (value: number) => `${value.toFixed(2)} m`,
              },
              {
                // The compiler's own station chain, untouched. It can run to a hundred names, so it
                // is truncated on the row and readable whole on hover rather than allowed to set
                // the width of the table.
                title: t('surveyQuality.columns.stations'),
                dataIndex: 'stations',
                ellipsis: true,
                render: (stations: string) => (
                  <Tooltip title={stations}>
                    <span>{stations}</span>
                  </Tooltip>
                ),
              },
            ]}
          />
        </>
      )}
    </div>
  );
}

/** A column heading in the compiler's vocabulary, with what kind of quantity it is under it. */
function MeasureHeading({ heading, hint }: { heading: string; hint: string }) {
  return (
    <div>
      <div>{heading}</div>
      <Typography.Text type="secondary" style={{ fontSize: 11, fontWeight: 'normal' }}>
        {hint}
      </Typography.Text>
    </div>
  );
}
