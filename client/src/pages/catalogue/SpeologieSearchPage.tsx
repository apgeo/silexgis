// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo, useState } from 'react';
import { ExportOutlined, ImportOutlined, SearchOutlined } from '@ant-design/icons';
import { Alert, Button, Card, Flex, Input, Select, Space, Table, Tag, Tooltip, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate } from 'react-router-dom';
import {
  useSpeologieBasins,
  useSpeologieSearch,
  useSpeologieStatus,
  type SpeologieCave,
} from '../../api/hooks.ts';
import SpeologieCaveDrawer from '../../components/catalogue/SpeologieCaveDrawer.tsx';
import { speologieErrorMessage } from '../../components/catalogue/errorMessage.ts';
import { ROMANIAN_COUNTIES } from '../../components/catalogue/counties.ts';

/**
 * Looking things up in the Romanian community cave catalogue.
 *
 * This screen creates nothing. It exists because the common act is not importing — it is wanting
 * to know what the national register says about a cave, and following the link out to read it
 * there. Importing is one button on a row, and it leads to a separate screen where the choices
 * that create things are made.
 *
 * Two things about the catalogue leak into this screen and cannot be hidden. Its search is
 * diacritic-sensitive, so the server asks about several spellings of whatever is typed and the
 * screen says which — a search that quietly asked something other than what was typed would be
 * worse than one that did not. And the catalogue publishes no coordinates at all, which is said
 * plainly here rather than discovered after importing forty caves that appear on no map.
 */
export default function SpeologieSearchPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();

  const [term, setTerm] = useState('');
  const [county, setCounty] = useState<string | undefined>();
  const [basin, setBasin] = useState<number | undefined>();
  const [query, setQuery] = useState<{ q?: string; county?: string; basin?: number }>({});
  const [page, setPage] = useState(1);
  const [selected, setSelected] = useState<readonly number[]>([]);
  const [detail, setDetail] = useState<SpeologieCave | null>(null);

  const status = useSpeologieStatus();
  const basins = useSpeologieBasins();
  const pageSize = 25;
  const results = useSpeologieSearch({ ...query, page, pageSize }, status.data?.configured === true);

  const rows = results.data?.items ?? [];

  const onSearch = () => {
    setPage(1);
    setSelected([]);
    setQuery({ q: term.trim() || undefined, county, basin });
  };

  const importIds = (ids: readonly number[]) => {
    // The chosen caves travel in the address rather than in memory, so the import screen survives
    // a reload and can be linked to — and so there is one route that means "import these", however
    // it was reached.
    void navigate(`/catalogue/speologie/import?ids=${ids.join(',')}`);
  };

  const errorMessage = useMemo(() => speologieErrorMessage(results.error, t), [results.error, t]);

  const columns = [
    {
      title: t('speologie.columns.name'),
      dataIndex: 'title',
      render: (_: unknown, row: SpeologieCave) => (
        <Space orientation="vertical" size={0}>
          <Typography.Link onClick={() => setDetail(row)}>{row.title}</Typography.Link>
          <Space size={4} wrap>
            {row.vanished ? <Tag color="red">{t('speologie.flags.vanished')}</Tag> : null}
            {row.sump ? <Tag color="blue">{t('speologie.flags.sump')}</Tag> : null}
          </Space>
        </Space>
      ),
    },
    { title: t('speologie.columns.county'), dataIndex: 'county', width: 90 },
    { title: t('speologie.columns.mountain'), dataIndex: 'mountain', width: 140 },
    {
      title: t('speologie.columns.basin'),
      key: 'basin',
      width: 200,
      render: (_: unknown, row: SpeologieCave) =>
        row.hydroBasin ? (
          <Tooltip title={row.hydroBasin.path}>
            <span>{row.hydroBasin.label}</span>
          </Tooltip>
        ) : (
          <Typography.Text type="secondary">—</Typography.Text>
        ),
    },
    { title: t('speologie.columns.length'), dataIndex: 'length', width: 110, align: 'right' as const },
    { title: t('speologie.columns.depth'), dataIndex: 'depth', width: 110, align: 'right' as const },
    { title: t('speologie.columns.altitude'), dataIndex: 'altitude', width: 110, align: 'right' as const },
    { title: t('speologie.columns.protectionClass'), dataIndex: 'protectionClass', width: 80 },
    {
      title: t('speologie.columns.state'),
      key: 'state',
      width: 190,
      render: (_: unknown, row: SpeologieCave) => {
        if (!row.alreadyImported) {
          return <Typography.Text type="secondary">{t('speologie.state.notImported')}</Typography.Text>;
        }
        // A cave the caller may not read is reported as present and not named. Saying nothing at
        // all would invite them to import a second copy of a cave that is already here.
        return row.existingCaveId ? (
          <Link to={`/caves/${row.existingCaveId}`}>{t('speologie.state.imported')}</Link>
        ) : (
          <Tooltip title={t('speologie.state.importedElsewhere')}>
            <Tag>{t('speologie.state.imported')}</Tag>
          </Tooltip>
        );
      },
    },
    {
      title: t('speologie.columns.actions'),
      key: 'actions',
      width: 230,
      render: (_: unknown, row: SpeologieCave) => (
        <Space size={4}>
          <Button
            size="small"
            icon={<ImportOutlined />}
            onClick={() => importIds([row.id])}
            data-testid={`speologie-import-${row.id}`}
          >
            {row.alreadyImported ? t('speologie.actions.refresh') : t('speologie.actions.import')}
          </Button>
          {row.url ? (
            <Button
              size="small"
              type="link"
              icon={<ExportOutlined />}
              href={row.url}
              target="_blank"
              rel="noreferrer noopener"
            >
              {t('speologie.openInPortal')}
            </Button>
          ) : null}
        </Space>
      ),
    },
  ];

  return (
    <Flex vertical gap={16}>
      <Flex align="baseline" justify="space-between" wrap gap={8}>
        <Space orientation="vertical" size={0}>
          <Typography.Title level={3} style={{ margin: 0 }}>
            {t('speologie.title')}
          </Typography.Title>
          <Typography.Text type="secondary">{t('speologie.subtitle')}</Typography.Text>
        </Space>
        <Typography.Link
          href={status.data?.portalUrl ?? 'https://www.speologie.org'}
          target="_blank"
          rel="noreferrer noopener"
        >
          {t('speologie.portalLink')}
        </Typography.Link>
      </Flex>

      {status.data && !status.data.configured ? (
        <Alert
          type="warning"
          showIcon
          title={t('speologie.notConfigured.title')}
          description={t('speologie.notConfigured.body')}
          data-testid="speologie-not-configured"
        />
      ) : null}

      <Card size="small">
        <Flex gap={8} wrap align="center">
          <Input
            style={{ maxWidth: 360 }}
            allowClear
            value={term}
            onChange={(e) => setTerm(e.target.value)}
            onPressEnter={onSearch}
            placeholder={t('speologie.search.termPlaceholder')}
            aria-label={t('speologie.search.term')}
            data-testid="speologie-term"
          />
          <Select
            style={{ minWidth: 220 }}
            allowClear
            showSearch
            optionFilterProp="label"
            value={county}
            onChange={setCounty}
            placeholder={t('speologie.search.countyPlaceholder')}
            aria-label={t('speologie.search.county')}
            data-testid="speologie-county"
            options={ROMANIAN_COUNTIES.map((c) => ({ value: c.code, label: `${c.code} — ${c.name}` }))}
          />
          <Select<number>
            style={{ minWidth: 300 }}
            allowClear
            showSearch
            optionFilterProp="label"
            value={basin}
            onChange={(id) => setBasin(id ?? undefined)}
            loading={basins.isPending}
            placeholder={t('speologie.search.basinPlaceholder')}
            aria-label={t('speologie.search.basin')}
            data-testid="speologie-basin"
            options={(basins.data ?? []).map((b) => ({
              value: b.id,
              // Indented by depth, because the tree is what makes this a filter worth having:
              // a massif and a valley inside it read as the same kind of thing in a flat list.
              label: `${'\u00a0\u00a0'.repeat(Math.max(0, b.depth - 1))}${b.label}`,
              title: b.path,
            }))}
          />
          <Button
            type="primary"
            icon={<SearchOutlined />}
            onClick={onSearch}
            disabled={status.data?.configured !== true}
            data-testid="speologie-search"
          >
            {t('speologie.search.submit')}
          </Button>
          {selected.length > 0 ? (
            <Button
              icon={<ImportOutlined />}
              onClick={() => importIds(selected)}
              data-testid="speologie-import-selected"
            >
              {t('speologie.actions.importSelected', { count: selected.length })}
            </Button>
          ) : null}
        </Flex>
        <Typography.Paragraph type="secondary" style={{ marginTop: 8, marginBottom: 0 }}>
          {t('speologie.search.hint')}
        </Typography.Paragraph>
      </Card>

      {errorMessage ? <Alert type="error" showIcon title={errorMessage} /> : null}

      {results.data && query.basin !== undefined ? (
        <Alert
          type="info"
          showIcon
          title={t('speologie.search.basinNarrowed', {
            count: results.data.items.length,
            scanned: results.data.scannedCount,
          })}
          description={t('speologie.search.basinNarrowedWhy')}
          data-testid="speologie-basin-narrowed"
        />
      ) : null}

      {results.data && results.data.spellings.length > 1 ? (
        <Alert
          type="info"
          showIcon
          title={t('speologie.search.spellings', {
            count: results.data.spellings.length,
            // A few, not all of them. The expansion is deliberately wide — two dozen spellings
            // of one word — and printing every one turns the sentence that explains the search
            // into a wall of near-identical words nobody reads.
            list:
              results.data.spellings.slice(0, 4).join(', ') +
              (results.data.spellings.length > 4 ? ' …' : ''),
          })}
          description={t('speologie.search.spellingsWhy')}
          data-testid="speologie-spellings"
        />
      ) : null}

      <Table<SpeologieCave>
        rowKey="id"
        size="small"
        scroll={{ x: 'max-content' }}
        loading={results.isFetching}
        dataSource={rows}
        columns={columns}
        pagination={false}
        locale={{
          emptyText: results.data ? (
            <Space orientation="vertical" size={4}>
              <span>{t('speologie.search.noResults')}</span>
              <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                {t('speologie.search.noResultsHint')}
              </Typography.Text>
            </Space>
          ) : undefined,
        }}
        rowSelection={{
          selectedRowKeys: selected as number[],
          onChange: (keys) => setSelected(keys as number[]),
        }}
        data-testid="speologie-results"
      />

      <Flex justify="space-between" align="center">
        <Button disabled={page <= 1 || results.isFetching} onClick={() => setPage((p) => p - 1)}>
          {t('speologie.paging.previous')}
        </Button>
        <Typography.Text type="secondary">{t('speologie.paging.page', { page })}</Typography.Text>
        <Button
          disabled={!results.data?.hasMore || results.isFetching}
          onClick={() => setPage((p) => p + 1)}
        >
          {t('speologie.paging.next')}
        </Button>
      </Flex>

      <SpeologieCaveDrawer cave={detail} onClose={() => setDetail(null)} />
    </Flex>
  );
}
