// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { Button, Flex, InputNumber, Space, Switch, Table, Tag, Tooltip, Typography } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import { useTranslation } from 'react-i18next';
import type { TripImportDecision, TripImportRow } from '../../api/hooks.ts';

interface Props {
  items: readonly TripImportRow[];
  decisions: Record<string, TripImportDecision>;
  onDecision: (line: number, decision: TripImportDecision | null) => void;
  /**
   * Every line of the sheet the server says may be taken — the whole file, not this page. What
   * "all" means here is the server's answer and never the twenty-five rows that happen to be on
   * screen, which is the only reading under which taking all of them and then confirming does
   * what the reviewer just asked for.
   */
  selectableLines: readonly number[];
  /** Takes or sets aside a whole set of lines at once. */
  onBulkDecision: (lines: readonly number[], action: TripImportDecision['action']) => void;
  page: number;
  pageSize: number;
  total: number;
  onPageChange: (page: number, pageSize: number) => void;
}

/**
 * One row of the sheet per line, with what the reader made of it beside what the sheet wrote.
 *
 * The per-row control is a single switch — take this row, or set it aside — because that is the
 * only decision the server accepts for a trip row. Everything else a reviewer can change is a
 * choice about the whole file and lives in the options above, so offering a per-row version of
 * it here would be a control that quietly does nothing.
 *
 * A row the reader could not read at all is not here: it is in the problem list, because a
 * table of rows that silently omits the broken ones reads exactly like a table with none.
 *
 * Above the table, the same decision for many rows at once. A sheet can be longer than one
 * confirmation accepts, and without this the only way back from that refusal was to flip several
 * thousand individual switches across a hundred pages — which is not a recovery path, it is a
 * dead end with a message above it.
 */
export default function TripImportRowTable({
  items,
  decisions,
  onDecision,
  selectableLines,
  onBulkDecision,
  page,
  pageSize,
  total,
  onPageChange,
}: Props) {
  const { t } = useTranslation();
  const [keep, setKeep] = useState<number | null>(null);

  const taken = (row: TripImportRow) =>
    (decisions[String(row.line)]?.action ?? row.decision?.action ?? 'create') !== 'skip';

  const columns: ColumnsType<TripImportRow> = [
    {
      title: t('tripImport.columns.take'),
      key: 'take',
      width: 90,
      render: (_, row) => (
        <Switch
          size="small"
          checked={taken(row)}
          onChange={(checked) => onDecision(row.line, checked ? { action: 'create' } : { action: 'skip' })}
          data-testid={`trip-import-take-${row.line}`}
        />
      ),
    },
    {
      title: t('tripImport.columns.line'),
      dataIndex: 'line',
      key: 'line',
      width: 70,
    },
    {
      title: t('tripImport.columns.when'),
      key: 'when',
      render: (_, row) => (
        <Space orientation="vertical" size={0}>
          <Typography.Text>{row.startDate ?? row.startDateText ?? '—'}</Typography.Text>
          {row.endDate && row.endDate !== row.startDate && (
            <Typography.Text type="secondary" style={{ fontSize: 12 }}>
              {row.endDate}
            </Typography.Text>
          )}
        </Space>
      ),
    },
    {
      title: t('tripImport.columns.title'),
      key: 'title',
      render: (_, row) => (
        <Space orientation="vertical" size={0}>
          <Typography.Text strong>{row.title ?? t('tripImport.untitledRow')}</Typography.Text>
          {row.resolution?.locationNote && (
            <Typography.Text type="secondary" style={{ fontSize: 12 }}>
              {row.resolution.locationNote}
            </Typography.Text>
          )}
        </Space>
      ),
    },
    {
      title: t('tripImport.columns.type'),
      key: 'type',
      render: (_, row) => {
        const match = row.resolution?.tripType;
        if (!match) {
          return <Typography.Text type="secondary">{row.tripType ?? '—'}</Typography.Text>;
        }
        return (
          <Tag color={match.state === 'matched' ? 'green' : match.willCreate ? 'blue' : 'default'}>
            {match.name ?? match.source}
          </Tag>
        );
      },
    },
    {
      title: t('tripImport.columns.places'),
      key: 'places',
      render: (_, row) =>
        (row.resolution?.caves ?? []).map((cave) => (
          <Tag key={cave.source} color={cave.state === 'matched' ? 'green' : cave.willCreate ? 'blue' : 'default'}>
            {cave.name ?? cave.source}
          </Tag>
        )),
    },
    {
      title: t('tripImport.columns.people'),
      key: 'people',
      render: (_, row) => {
        const people = [...(row.resolution?.proposers ?? []), ...(row.resolution?.participants ?? [])];
        return people.map((person, index) => (
          <Tag
            key={`${person.source}-${index}`}
            color={
              person.state === 'matched'
                ? 'green'
                : person.state === 'ambiguous'
                  ? 'gold'
                  : person.willCreate
                    ? 'blue'
                    : 'red'
            }
          >
            {person.name ?? person.source}
          </Tag>
        ));
      },
    },
    {
      title: t('tripImport.columns.warnings'),
      key: 'warnings',
      width: 120,
      render: (_, row) =>
        row.warnings.length === 0 ? null : (
          <Tooltip title={row.warnings.map((w) => t(`tripImport.problems.${w.code}`)).join(' · ')}>
            <Tag color="orange" data-testid={`trip-import-row-warnings-${row.line}`}>
              {t('tripImport.warningCount', { count: row.warnings.length })}
            </Tag>
          </Tooltip>
        ),
    },
  ];

  // Lines in file order, so "the first N" means the first N of the sheet rather than the first N
  // of whatever order the server happened to answer in.
  const ordered = [...selectableLines].sort((a, b) => a - b);

  return (
    <>
      <Flex gap={8} align="center" wrap style={{ marginBottom: 8 }}>
        <Button
          size="small"
          disabled={ordered.length === 0}
          onClick={() => onBulkDecision(ordered, 'create')}
          data-testid="trip-import-take-all"
        >
          {t('tripImport.takeAll', { count: ordered.length })}
        </Button>
        <Button
          size="small"
          disabled={ordered.length === 0}
          onClick={() => onBulkDecision(ordered, 'skip')}
          data-testid="trip-import-skip-all"
        >
          {t('tripImport.skipAll')}
        </Button>
        <Space size={4}>
          <Typography.Text type="secondary">{t('tripImport.keepFirst')}</Typography.Text>
          <InputNumber
            size="small"
            min={0}
            max={ordered.length}
            style={{ width: 110 }}
            value={keep}
            onChange={(value) => setKeep(typeof value === 'number' ? value : null)}
            data-testid="trip-import-keep-first"
          />
          <Button
            size="small"
            disabled={keep === null}
            onClick={() => {
              // Two statements, not one: the head is taken and the tail is set aside, because a
              // reviewer arriving here has usually already taken everything and needs the rest
              // put back rather than merely left alone.
              onBulkDecision(ordered.slice(0, keep ?? 0), 'create');
              onBulkDecision(ordered.slice(keep ?? 0), 'skip');
            }}
            data-testid="trip-import-keep-first-apply"
          >
            {t('tripImport.keepFirstApply')}
          </Button>
        </Space>
      </Flex>
      <Table<TripImportRow>
        rowKey="line"
        size="small"
        scroll={{ x: 'max-content' }}
        data-testid="trip-import-rows"
        columns={columns}
        dataSource={[...items]}
        pagination={{
          current: page,
          pageSize,
          total,
          showSizeChanger: true,
          onChange: onPageChange,
        }}
      />
    </>
  );
}
