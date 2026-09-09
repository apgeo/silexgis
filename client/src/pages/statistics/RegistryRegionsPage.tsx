// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo } from 'react';
import { DownloadOutlined } from '@ant-design/icons';
import {
  Alert,
  App,
  Button,
  Card,
  Empty,
  Flex,
  Input,
  Select,
  Skeleton,
  Table,
  Typography,
} from 'antd';
import { useTranslation } from 'react-i18next';
import { useSearchParams } from 'react-router-dom';

import { ApiError } from '../../api/client.ts';
import { downloadFile, registryRegionsExportUrl } from '../../api/download.ts';
import { useCaveTypes, useRegistryRegions, useRockTypes } from '../../api/hooks.ts';
import type { RegistryRegionRow } from '../../api/hooks.ts';
import {
  readRegistryRegionsFilter,
  registryRegionsQuery,
  writeRegistryRegionsFilter,
  type RegistryRegionsFilter,
} from './registryStatisticsFilter.ts';

/**
 * What a narrowed set of the registry adds up to, region by region.
 *
 * <p>
 * The one thing this page exists to get right is that <em>the rows do not add up to the total,
 * and that is the answer</em>. The registry counts the total over every cave in the set this
 * reader may read, and counts the rows over the subset they may also place — so a cave whose
 * position is protected from them is in the total and under no region. Left unexplained, a table
 * whose column does not reach its own stated total reads as a bug, and the reader's next move is
 * to distrust both numbers. So the shortfall is named, in the same breath as the figures, as
 * something about what may be seen rather than something about the caves.
 * </p>
 * <p>
 * For the same reason there is no percentage column and no row gathering the difference. Either
 * would offer the reader an arithmetic that reconciles, and the arithmetic does not: the missing
 * caves have regions, and the point is precisely that this reader is not being told which.
 * </p>
 */
export default function RegistryRegionsPage() {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const [searchParams, setSearchParams] = useSearchParams();

  const filter = useMemo(() => readRegistryRegionsFilter(searchParams), [searchParams]);

  const apply = (change: Partial<RegistryRegionsFilter>) => {
    setSearchParams(writeRegistryRegionsFilter({ ...filter, ...change }));
  };

  const query = useMemo(() => registryRegionsQuery(filter), [filter]);
  const { data, isError, error } = useRegistryRegions(query);
  const { data: caveTypes } = useCaveTypes();
  const { data: rockTypes } = useRockTypes();

  // What the rows themselves come to. Not offered as a correction to the total and never
  // subtracted from it — it is the second of two figures counted under two different rules.
  const placed = useMemo(
    () => (data?.regions ?? []).reduce((sum, row) => sum + row.caveCount, 0),
    [data?.regions],
  );

  const onExport = () => {
    downloadFile(registryRegionsExportUrl(query)).catch(() =>
      message.error(t('common.saveFailed')),
    );
  };

  const columns = [
    {
      title: t('registryStats.region'),
      dataIndex: 'region',
      key: 'region',
      // Sortable by the name the caller supplied, because that is a rearrangement of what they
      // can already see. The count is deliberately not sortable: ordering a table by a quantity
      // the registry computed over rows this reader may not see hands them the relative sizes of
      // the things the answer declined to state.
      sorter: (a: RegistryRegionRow, b: RegistryRegionRow) =>
        (a.region ?? '').localeCompare(b.region ?? ''),
      render: (region: string | null) =>
        region === null || region === '' ? (
          // An unrecorded region is a row of its own and is labelled as one: how much of a
          // registry has been placed at all is part of what a breakdown says.
          <Typography.Text type="secondary">{t('registryStats.noRegion')}</Typography.Text>
        ) : (
          region
        ),
    },
    {
      title: t('registryStats.caves'),
      dataIndex: 'caveCount',
      key: 'caveCount',
      align: 'right' as const,
      width: 160,
    },
  ];

  return (
    <div style={{ padding: 24 }} data-testid="registry-regions">
      <Flex justify="space-between" align="center" wrap gap={8} style={{ marginBottom: 16 }}>
        <Typography.Title level={3} style={{ margin: 0 }}>
          {t('registryStats.regionsTitle')}
        </Typography.Title>
        {/* Never offered for something the screen is not showing: the file is the same answer. */}
        <Button
          icon={<DownloadOutlined />}
          data-testid="registry-regions-export"
          disabled={data === undefined}
          onClick={onExport}
        >
          {t('common.export')}
        </Button>
      </Flex>

      <Typography.Paragraph type="secondary" data-testid="registry-regions-access">
        {t('registryStats.asVisibleToYou')}
      </Typography.Paragraph>

      <Card size="small" style={{ marginBottom: 16 }}>
        <Flex gap={12} wrap align="flex-end">
          <label>
            <Typography.Text type="secondary">{t('registryStats.caveType')}</Typography.Text>
            <Select
              data-testid="registry-regions-cave-type"
              allowClear
              style={{ width: 200, display: 'block' }}
              placeholder={t('registryStats.anyValue')}
              value={filter.caveTypeId}
              onChange={(value?: number) => apply({ caveTypeId: value })}
              options={caveTypes?.map((type) => ({ value: Number(type.id), label: type.name }))}
            />
          </label>
          <label>
            <Typography.Text type="secondary">{t('registryStats.rockType')}</Typography.Text>
            <Select
              data-testid="registry-regions-rock-type"
              allowClear
              style={{ width: 200, display: 'block' }}
              placeholder={t('registryStats.anyValue')}
              value={filter.rockTypeId}
              onChange={(value?: number) => apply({ rockTypeId: value })}
              options={rockTypes?.map((type) => ({ value: Number(type.id), label: type.name }))}
            />
          </label>
          <label>
            <Typography.Text type="secondary">{t('registryStats.region')}</Typography.Text>
            <Input
              data-testid="registry-regions-region"
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
        <Typography.Paragraph type="secondary" style={{ marginTop: 8, marginBottom: 0 }}>
          {t('registryStats.filterNote')}
        </Typography.Paragraph>
      </Card>

      {isError ? (
        /* The server's own sentence goes under the heading, because it is the half that names
           which control is wrong — a narrowing carried in the address is passed on unchecked, so
           the refusal is the only place a reader is told what the registry would not accept. The
           exception's own message is an internal, untranslated string. */
        <Alert
          type="error"
          showIcon
          data-testid="registry-regions-error"
          message={t('registryStats.regionsFailed')}
          description={error instanceof ApiError ? error.detail : undefined}
        />
      ) : data === undefined ? (
        <Skeleton active paragraph={{ rows: 8 }} />
      ) : (
        <Card size="small" title={t('registryStats.regionsCard')}>
          {/* Both counts, side by side, because a breakdown quoted without the set it was taken
              from is a list of numbers whose scale nobody can judge. */}
          <Typography.Paragraph data-testid="registry-regions-counts">
            {t('registryStats.regionsCounts', { placed, total: data.caveCount })}
          </Typography.Paragraph>

          {placed < data.caveCount && (
            <Alert
              type="info"
              showIcon
              style={{ marginBottom: 16 }}
              data-testid="registry-regions-shortfall"
              message={t('registryStats.regionsShortfall', {
                missing: data.caveCount - placed,
                total: data.caveCount,
              })}
            />
          )}

          {data.regions.length === 0 ? (
            <Empty
              data-testid="registry-regions-empty"
              description={t('registryStats.regionsEmpty')}
            />
          ) : (
            <Table
              data-testid="registry-regions-table"
              size="small"
              // Never the region itself where the region is absent. The registry groups on the
              // column as it stands, so a cave saved with an empty region and a cave saved with
              // none are two rows — and keyed by the region alone they would be one key twice,
              // which React reports on the console and antd resolves by losing row identity.
              rowKey={(row) => (row.region === null ? '\u0000 no region recorded' : row.region)}
              columns={columns}
              dataSource={data.regions}
              pagination={false}
              // The rows arrive in the order the registry publishes them — by name, with the
              // unrecorded region last — and are not re-ordered on arrival.
              sortDirections={['ascend', 'descend']}
            />
          )}

          {/* The server's own sentence about what it counted over, in its own words. */}
          <Typography.Paragraph
            type="secondary"
            data-testid="registry-regions-basis"
            style={{ marginTop: 16, marginBottom: 0 }}
          >
            {data.basis}
          </Typography.Paragraph>
        </Card>
      )}
    </div>
  );
}
