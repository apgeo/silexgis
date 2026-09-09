// SPDX-License-Identifier: AGPL-3.0-or-later
import { Card, Col, Empty, Row, Segmented, Typography } from 'antd';
import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';

import {
  useRegistryClustering,
  type CaveListItem,
  type CaveListParams,
} from '../../api/hooks.ts';
import {
  clusteredScatter,
  clusteringScopeFor,
  scatterClusteringMeasures,
  scatterPointsFor,
} from './caveClustering.ts';
import ClusterPopulationNote from './ClusterPopulationNote.tsx';
import {
  CategoryBoxChart,
  CcdfChart,
  CorrelationChart,
  HistogramChart,
  type CorrelationSeries,
} from './DistributionCharts.tsx';

interface CaveDistributionPanelProps {
  caves: CaveListItem[];
  /** Names for cave-type ids, so the box plot can say "Limestone" rather than "3". */
  typeName: (id: number) => string;
  /**
   * The narrowing the list is showing, so the grouping can be asked the same question the points
   * answer. Without it the colours would describe some other set of caves and say nothing about
   * having done so.
   */
  scope: CaveListParams;
}

type View = 'histogram' | 'ccdf' | 'correlation' | 'byType';

/**
 * How the caves in front of the reader are distributed — length, the rank-size shape of that
 * length, length against depth, and length by cave type.
 *
 * <p>
 * Everything here is computed from the rows the list already fetched, so it describes **the caves
 * on this page** and nothing wider. The panel says so underneath, and that sentence is
 * load-bearing rather than decorative: a reader who takes these for registry-wide figures will
 * draw a conclusion the data does not support, and two readers on different pages will disagree
 * about the same registry, which looks exactly like a defect.
 * </p>
 * <p>
 * A cave with no surveyed length is absent from these charts, not plotted as zero — an unmeasured
 * cave and a cave measured at nothing are different claims, and only one of them is ever true.
 * </p>
 */
export default function CaveDistributionPanel({ caves, typeName, scope }: CaveDistributionPanelProps) {
  const { t } = useTranslation();
  const [view, setView] = useState<View>('histogram');

  // The grouping is asked only for a narrowing it can be asked about, and only while the chart
  // that shows it is the one on screen. A null scope is not a failure: it says the list is
  // narrowed by something the registry-wide grouping has no parameter for, so the honest picture
  // is the uncoloured one.
  const clusteringScope = clusteringScopeFor(scope);
  const { data: answered } = useRegistryClustering(
    clusteringScope ?? { measures: scatterClusteringMeasures },
    clusteringScope !== null && view === 'correlation',
  );

  // Not asking is not the same as having no answer. A request that is switched off still names a
  // cache entry, and the entry an unaskable narrowing falls back to is the one the unnarrowed
  // grouping filled in: view the scatter with no filters, then type a search term, and the answer
  // for the whole registry is still there to be read. Drawing it would colour these caves by a
  // grouping of other caves while the note beside it says they are uncoloured — the exact
  // contradiction the null scope exists to prevent. So the scope, not the cache, decides whether
  // there is a grouping on this screen at all.
  const clustering = clusteringScope === null ? undefined : answered;

  const lengths = useMemo(
    () => caves.map((c) => c.surveyedLength).filter((v): v is number => typeof v === 'number' && v > 0),
    [caves],
  );

  const pairs = useMemo(() => scatterPointsFor(caves).map((p) => p.point), [caves]);

  /**
   * The same points, split by the group the server put each cave in.
   *
   * The label a reader sees counts from one because a group numbered zero reads as an absence.
   * It is a renaming of a label that carries no rank either way — group two is not larger, better
   * or more interesting than group one, which is why the colours come from a categorical palette
   * and why nothing here sorts the groups by size.
   */
  const clusterSeries = useMemo<CorrelationSeries[] | undefined>(() => {
    const groups = clusteredScatter(caves, clustering);
    if (groups === null) return undefined;
    return groups.map((group) => ({
      cluster: group.cluster,
      points: group.points,
      name:
        group.cluster === null
          ? t('karstStats.clusterUngrouped')
          : t('karstStats.clusterGroup', { group: group.cluster + 1 }),
    }));
  }, [caves, clustering, t]);

  const groups = useMemo(() => {
    const byType = new Map<number, number[]>();
    for (const cave of caves) {
      if (typeof cave.surveyedLength !== 'number' || cave.surveyedLength <= 0) continue;
      const bucket = byType.get(cave.caveTypeId) ?? [];
      bucket.push(cave.surveyedLength);
      byType.set(cave.caveTypeId, bucket);
    }
    return [...byType.entries()]
      .map(([id, values]) => ({ label: typeName(id) || String(id), values }))
      .sort((a, b) => b.values.length - a.values.length);
  }, [caves, typeName]);

  const lengthLabel = t('karstStats.lengthAxis');

  const body = () => {
    if (lengths.length === 0) {
      return <Empty description={t('karstStats.nothingMeasured')} />;
    }
    switch (view) {
      case 'ccdf':
        return <CcdfChart values={lengths} xLabel={lengthLabel} />;
      case 'correlation':
        // The account of who could not be grouped is drawn above the picture, not beneath it.
        // A reader who meets the colours first has already drawn a conclusion by the time they
        // reach the sentence saying which caves the colours could say nothing about.
        return (
          <>
            <ClusterPopulationNote clustering={clustering} unaskable={clusteringScope === null} />
            <CorrelationChart
              pairs={pairs}
              series={clusterSeries}
              xLabel={lengthLabel}
              yLabel={t('karstStats.depthAxis')}
            />
          </>
        );
      case 'byType':
        return <CategoryBoxChart groups={groups} yLabel={lengthLabel} logScale />;
      default:
        return <HistogramChart values={lengths} logCount xLabel={lengthLabel} />;
    }
  };

  return (
    <Card
      size="small"
      title={t('karstStats.title')}
      style={{ marginTop: 16 }}
      extra={
        <Segmented
          size="small"
          value={view}
          onChange={(v) => setView(v as View)}
          options={[
            { value: 'histogram', label: t('karstStats.viewHistogram') },
            { value: 'ccdf', label: t('karstStats.viewRankSize') },
            { value: 'correlation', label: t('karstStats.viewLengthDepth') },
            { value: 'byType', label: t('karstStats.viewByType') },
          ]}
        />
      }
    >
      <div data-testid="cave-distributions">
        <Row>
          <Col span={24}>{body()}</Col>
        </Row>
        <Typography.Paragraph type="secondary" style={{ marginTop: 8, marginBottom: 0 }}>
          {t('karstStats.scopeNote', { shown: lengths.length, total: caves.length })}
        </Typography.Paragraph>
      </div>
    </Card>
  );
}
