// SPDX-License-Identifier: AGPL-3.0-or-later
import { Card, Descriptions, Empty, Typography } from 'antd';
import { useCallback, useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';

import { useCaveOverburden, type CaveOverburdenSample } from '../../api/hooks.ts';
import { setOverburdenHighlight } from '../../workspace/overburdenHighlight.ts';
import { EnvelopeChart } from './DistributionCharts.tsx';
import SurveyBasisNote from './SurveyBasisNote.tsx';
import { overburdenGapReason, overburdenSeries } from './overburdenSeries.ts';

/**
 * How much rock is over your head along one cave's passages.
 *
 * <p>
 * <b>Absent rather than empty when the server has no answer.</b> The curve is the position of the
 * passage set against the surface above it, so it is withheld whole from a reader who may see the
 * cave but may not place it — and the refusal is spelled as "no such cave". A card of blanks would
 * announce that a guarded cave is here, so on a refusal this renders nothing at all.
 * </p>
 * <p>
 * <b>The four ways there can be no curve are four different statements</b> and each gets its own
 * sentence: the cave has no line work; the line work carries no altitudes, so there is nothing to
 * subtract a ground height from; nobody has prepared any elevation data on this installation; or
 * elevation data exists and none of it reaches this cave. Collapsing them into one blank would
 * have a reader go looking for the wrong thing.
 * </p>
 * <p>
 * <b>Pressing a reading says where it was taken, everywhere that can show it.</b> The horizontal
 * axis is distance along the passage, which nobody can turn into a place in their head: a thin
 * roof at four hundred metres along names no valley. So a press announces the reading's own
 * position on the workspace bus and the flat map and the 3D scene each mark it while they are on
 * screen. This panel reaches for neither of them — a cave page has no map on it, must not know
 * which views exist, and a press made with no view open should reach nobody rather than leave a
 * mark on one opened later. The mark is taken down when the page is left, when the profile is
 * replaced, and when the reader presses the chart away from the curve.
 * </p>
 * <p>
 * <b>A partly covered profile says so.</b> Where the ground could not be read the curve has a hole
 * in it, and the figures beside it are computed over the readings that got a ground height and
 * over nothing else — a mean that counted a missing reading as no rock would report a cave as
 * shallower the less of it is covered.
 * </p>
 */
export default function CaveOverburdenPanel({ caveId }: { caveId: string }) {
  const { t } = useTranslation();
  const { data, isLoading, isError } = useCaveOverburden(caveId);
  /** Which reading is being pointed at, by index into the profile's own list. */
  const [picked, setPicked] = useState<number | null>(null);

  const dash = t('overburden.notMeasured');
  const metres = useCallback(
    (value: number | null | undefined) =>
      value === null || value === undefined
        ? dash
        : t('overburden.unitMetres', { value: (Math.round(value * 10) / 10).toFixed(1) }),
    [dash, t],
  );

  // Held steady across renders so that the two callbacks below are steady too, and the chart
  // is not rebuilt from scratch every time the pointer moves.
  const samples = useMemo<readonly CaveOverburdenSample[]>(() => data?.samples ?? [], [data]);

  // What the pointer resting on a reading says. Composed here because only here is there a
  // translator and a reading to name; the chart is handed the sentence, not the numbers.
  const tooltipFormatter = useCallback(
    (index: number) => {
      const sample = samples[index];
      if (!sample) return '';
      const where = t('overburden.readoutWhere', { distance: metres(sample.distanceAlongM) });
      const thickness =
        sample.outcome === 'sampled' && sample.overburdenM !== null
          ? t('overburden.readoutThickness', { value: metres(sample.overburdenM) })
          : t(`overburden.readoutAbsent.${sample.outcome}`);
      const piece = t('overburden.readoutSegment', { index: sample.segmentIndex + 1 });
      return [where, thickness, piece].join('<br/>');
    },
    [samples, metres, t],
  );

  // A press announces where the reading was taken. A reading with no ground height still has a
  // position — the passage was surveyed there, only the surface over it is unknown — so it is
  // marked like any other: "here is the part of the cave nobody can tell you about" is an answer
  // worth showing on a map.
  const pick = useCallback(
    (index: number) => {
      const sample = samples[index];
      if (!sample) return;
      setPicked(index);
      setOverburdenHighlight({
        caveId,
        longitude: sample.longitude,
        latitude: sample.latitude,
        altitudeM: sample.passageAltitudeM,
        label:
          sample.outcome === 'sampled' && sample.overburdenM !== null
            ? t('overburden.markLabel', { value: metres(sample.overburdenM) })
            : t('overburden.markLabelAbsent'),
      });
    },
    [samples, caveId, metres, t],
  );

  const clearPick = useCallback(() => {
    setPicked(null);
    setOverburdenHighlight(null);
  }, []);

  // The mark belongs to a profile, and a new profile — another cave, or this one recomputed —
  // makes the old index meaningless, so arriving here takes down whatever was marked before.
  //
  // Leaving the page deliberately does not. The flat map and the 3D scene are separate routes from
  // a cave's page, so only one of them is ever mounted: a mark cleared on the way out is a mark no
  // view can ever draw, which is the whole point of pressing one. It stays up until the reader
  // presses the chart away from the curve, opens another cave, or turns the layer off, and it is
  // held in memory only — signing out is a redirect that takes the whole page down with it.
  useEffect(() => {
    setPicked(null);
    setOverburdenHighlight(null);
  }, [caveId, data]);

  if (isError) return null;

  const body = () => {
    if (isLoading || !data) return <Empty description={t('overburden.loading')} />;

    // No line work at all is a different statement from line work that carries no heights.
    if (data.basis === 'unavailable') {
      return <Empty description={t('overburden.nothingMeasured')} />;
    }

    // Refused rather than drawn along the surface. A plan-only drawing records no third
    // coordinate, so there is no passage altitude for a ground height to be measured down to.
    if (!data.hasAltitudes) {
      return (
        <div data-testid="overburden-no-altitudes">
          <Empty description={t('overburden.noAltitudesTitle')} />
          <Typography.Paragraph type="secondary" style={{ marginBottom: 0 }}>
            {t('overburden.noAltitudesReason')}
          </Typography.Paragraph>
        </div>
      );
    }

    // Two absences that look identical in the readings and are entirely different problems: no
    // elevation data has been prepared here at all, or some has and none of it reaches this cave.
    if (!data.hasTerrain) {
      return (
        <div data-testid="overburden-no-terrain">
          <Empty description={t('overburden.noTerrainBuilt')} />
        </div>
      );
    }

    if (data.coveredSampleCount === 0) {
      return (
        <div data-testid="overburden-no-coverage">
          <Empty description={t('overburden.noCoverageHere')} />
        </div>
      );
    }

    const series = overburdenSeries(data.samples);
    const gap = overburdenGapReason(data.samples);
    const chosen = picked === null ? undefined : data.samples[picked];

    return (
      <div>
        <EnvelopeChart
          testId="chart-cave-overburden"
          x={series.x}
          curve={series.overburdenM}
          lower={series.base}
          upper={series.overburdenM}
          xLabel={t('overburden.axisDistance')}
          yLabel={t('overburden.axisThickness')}
          height={260}
          tooltipFormatter={tooltipFormatter}
          onPointClick={pick}
          onEmptyClick={clearPick}
        />

        {chosen ? (
          <Typography.Paragraph data-testid="overburden-readout" style={{ marginTop: 8 }}>
            {t('overburden.readout', {
              distance: metres(chosen.distanceAlongM),
              thickness:
                chosen.outcome === 'sampled' && chosen.overburdenM !== null
                  ? metres(chosen.overburdenM)
                  : t(`overburden.readoutAbsent.${chosen.outcome}`),
              segment: chosen.segmentIndex + 1,
            })}
          </Typography.Paragraph>
        ) : (
          <Typography.Paragraph type="secondary" style={{ marginTop: 8 }}>
            {t('overburden.pressToLocate')}
          </Typography.Paragraph>
        )}

        <Descriptions
          size="small"
          column={{ xs: 1, md: 2 }}
          style={{ marginTop: 16 }}
          items={[
            { key: 'min', label: t('overburden.thinnest'), children: metres(data.minOverburdenM) },
            { key: 'max', label: t('overburden.thickest'), children: metres(data.maxOverburdenM) },
            { key: 'mean', label: t('overburden.mean'), children: metres(data.meanOverburdenM) },
            {
              key: 'length',
              label: t('overburden.passageLength'),
              children: metres(data.passageLengthM),
            },
            {
              key: 'coverage',
              label: t('overburden.coverage'),
              children: t('overburden.coverageValue', {
                covered: data.coveredSampleCount,
                total: data.samples.length,
              }),
            },
          ]}
        />

        <Typography.Paragraph type="secondary" style={{ marginTop: 8, marginBottom: 0 }}>
          {t('overburden.distanceNote')}
        </Typography.Paragraph>

        <Typography.Paragraph type="secondary" style={{ marginBottom: 0 }}>
          {t('overburden.figuresScope')}
        </Typography.Paragraph>

        {gap !== 'none' && (
          <Typography.Paragraph
            type="secondary"
            data-testid="overburden-partial"
            style={{ marginBottom: 0 }}
          >
            {t(`overburden.gap.${gap}`)}
          </Typography.Paragraph>
        )}
      </div>
    );
  };

  return (
    <Card title={t('overburden.title')} style={{ marginBottom: 16 }}>
      {body()}
      {data && <SurveyBasisNote basis={data.basis} />}
    </Card>
  );
}
