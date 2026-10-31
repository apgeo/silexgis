// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo } from 'react';
import { Alert, Card, Empty, Flex, Input, Select, Skeleton, Table, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { basisSentence } from './basisSentence';
import { useSearchParams } from 'react-router-dom';

import { ApiError } from '../../api/client.ts';
import { useCaveTypes, useRegistryCategories, useRockTypes } from '../../api/hooks.ts';
import type { RegistryCategoryGroup } from '../../api/hooks.ts';
import { SummaryBoxChart } from '../../components/statistics/DistributionCharts.tsx';
import type { FiveNumber } from '../../components/statistics/distributions.ts';
import {
  readRegistryCategoriesFilter,
  registryCategoriesQuery,
  RegistryCategoryNames,
  RegistryMeasureNames,
  writeRegistryCategoriesFilter,
  type RegistryCategoriesFilter,
} from './registryStatisticsFilter.ts';

/** A group's box, or null where the answer withheld it. */
function boxOf(group: RegistryCategoryGroup): FiveNumber | null {
  const { lowerDecile, lowerQuartile, median, upperQuartile, upperDecile } = group;
  return lowerDecile === null || lowerQuartile === null || median === null
    || upperQuartile === null || upperDecile === null
    ? null
    : // The whiskers end at the tenth and ninetieth percentiles, not at the least and greatest:
      // those two are single caves' values, which the registry publishes only as the bounds of a
      // whole scope.
      {
        min: lowerDecile,
        q1: lowerQuartile,
        median,
        q3: upperQuartile,
        max: upperDecile,
        count: group.measuredCount,
      };
}

const figure = (value: number | null) =>
  value === null ? '' : value.toLocaleString(undefined, { maximumFractionDigits: 1 });

/**
 * One measurement compared across the cave types or rock types of the caves this reader may
 * read — "do the caves in this limestone run longer than those in that dolomite".
 *
 * <p>
 * A group measured on too few caves keeps its row and its counts and loses its box: drawn over a
 * handful of caves, the box would be those caves' own figures. Dropping the row instead would make
 * the counts stop adding up to the caves in scope and invite the reader to think nothing was left
 * out, so the row stays and says why it has no box.
 * </p>
 */
export default function RegistryCategoriesPage() {
  const { t } = useTranslation();
  const [searchParams, setSearchParams] = useSearchParams();

  const filter = useMemo(() => readRegistryCategoriesFilter(searchParams), [searchParams]);
  const apply = (change: Partial<RegistryCategoriesFilter>) => {
    setSearchParams(writeRegistryCategoriesFilter({ ...filter, ...change }));
  };

  const query = useMemo(() => registryCategoriesQuery(filter), [filter]);
  const { data, isError, error } = useRegistryCategories(query);
  const { data: caveTypes } = useCaveTypes();
  const { data: rockTypes } = useRockTypes();

  const vocabulary = data?.category === 'caveType' ? caveTypes : rockTypes;
  const labelOf = (id: number | null) =>
    id === null
      ? t('registryStats.noCategory')
      : (vocabulary?.find((entry) => Number(entry.id) === id)?.name ?? String(id));

  const groups = data?.groups ?? [];
  const measureLabel = t(`registryStats.measures.${data?.measure ?? filter.measure}`, {
    defaultValue: data?.measure ?? filter.measure,
  });

  return (
    <div style={{ padding: 24 }} data-testid="registry-categories">
      <Typography.Title level={3} style={{ marginTop: 0 }}>
        {t('registryStats.categoriesTitle')}
      </Typography.Title>
      <Typography.Paragraph type="secondary">{t('registryStats.asVisibleToYou')}</Typography.Paragraph>

      <Card size="small" style={{ marginBottom: 16 }}>
        <Flex gap={12} wrap align="flex-end">
          <label>
            <Typography.Text type="secondary">{t('registryStats.measure')}</Typography.Text>
            <Select
              data-testid="registry-categories-measure"
              style={{ width: 240, display: 'block' }}
              value={filter.measure}
              onChange={(value: string) => apply({ measure: value })}
              options={RegistryMeasureNames.map((name) => ({
                value: name,
                label: t(`registryStats.measures.${name}`),
              }))}
            />
          </label>
          <label>
            <Typography.Text type="secondary">{t('registryStats.groupBy')}</Typography.Text>
            <Select
              data-testid="registry-categories-category"
              style={{ width: 200, display: 'block' }}
              value={filter.category}
              onChange={(value: RegistryCategoriesFilter['category']) => apply({ category: value })}
              options={RegistryCategoryNames.map((name) => ({
                value: name,
                label: t(`registryStats.categories.${name}`),
              }))}
            />
          </label>
          <label>
            <Typography.Text type="secondary">{t('registryStats.region')}</Typography.Text>
            <Input
              data-testid="registry-categories-region"
              // Keyed on the narrowing in force so the box follows the address, as on the regions page.
              key={filter.region ?? ''}
              allowClear
              style={{ width: 200, display: 'block' }}
              placeholder={t('registryStats.regionPlaceholder')}
              defaultValue={filter.region}
              onBlur={(event) => apply({ region: event.target.value.trim() || undefined })}
              onPressEnter={(event) =>
                apply({ region: event.currentTarget.value.trim() || undefined })
              }
            />
          </label>
        </Flex>
      </Card>

      {isError ? (
        <Alert
          type="error"
          showIcon
          title={t('registryStats.categoriesFailed')}
          description={error instanceof ApiError ? error.detail : undefined}
        />
      ) : !data ? (
        <Skeleton active />
      ) : groups.length === 0 ? (
        <Empty description={t('registryStats.nothingInScope')} />
      ) : (
        <Flex vertical gap={16}>
          <Card size="small" title={t('registryStats.categoriesCard', { measure: measureLabel })}>
            <SummaryBoxChart
              testId="registry-categories-chart"
              categories={groups.map((group) => labelOf(group.categoryId))}
              series={[{ name: measureLabel, summaries: groups.map(boxOf) }]}
              yLabel={measureLabel}
              height={360}
            />
            <Typography.Paragraph type="secondary" style={{ marginTop: 8, marginBottom: 0 }}>
              {t('registryStats.categoriesNote', { count: data.minimumGroupCaveCount })}
            </Typography.Paragraph>
          </Card>
          <Table<RegistryCategoryGroup>
            size="small"
            pagination={false}
            data-testid="registry-categories-table"
            rowKey={(group) => String(group.categoryId)}
            dataSource={groups}
            columns={[
              {
                title: t(`registryStats.categories.${data.category}`),
                key: 'category',
                render: (_: unknown, group) => labelOf(group.categoryId),
              },
              { title: t('registryStats.caves'), dataIndex: 'caveCount', align: 'right' },
              { title: t('registryStats.measured'), dataIndex: 'measuredCount', align: 'right' },
              {
                title: t('registryStats.box'),
                key: 'box',
                render: (_: unknown, group) => {
                  const box = boxOf(group);
                  return box === null ? (
                    <Typography.Text type="secondary">{t('registryStats.tooFewForABox')}</Typography.Text>
                  ) : (
                    [box.min, box.q1, box.median, box.q3, box.max].map(figure).join(' · ')
                  );
                },
              },
            ]}
          />
          <Typography.Text type="secondary">{basisSentence(t, data.basisKind, data.basis)}</Typography.Text>
        </Flex>
      )}
    </div>
  );
}
