// SPDX-License-Identifier: AGPL-3.0-or-later
import { Card, Col, Empty, Row, Segmented, Typography } from 'antd';
import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';

import type { CaveListItem } from '../../api/hooks.ts';
import { CategoryBoxChart, CcdfChart, CorrelationChart, HistogramChart } from './DistributionCharts.tsx';

interface CaveDistributionPanelProps {
  caves: CaveListItem[];
  /** Names for cave-type ids, so the box plot can say "Limestone" rather than "3". */
  typeName: (id: number) => string;
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
export default function CaveDistributionPanel({ caves, typeName }: CaveDistributionPanelProps) {
  const { t } = useTranslation();
  const [view, setView] = useState<View>('histogram');

  const lengths = useMemo(
    () => caves.map((c) => c.surveyedLength).filter((v): v is number => typeof v === 'number' && v > 0),
    [caves],
  );

  const pairs = useMemo(
    () =>
      caves
        .filter((c) => typeof c.surveyedLength === 'number' && typeof c.depth === 'number')
        .map((c) => [c.surveyedLength as number, Math.abs(c.depth as number)] as [number, number])
        .filter(([x, y]) => x > 0 && y > 0),
    [caves],
  );

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
        return <CorrelationChart pairs={pairs} xLabel={lengthLabel} yLabel={t('karstStats.depthAxis')} />;
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
