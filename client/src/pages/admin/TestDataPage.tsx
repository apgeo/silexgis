// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useMemo, useState } from 'react';
import {
  Alert,
  App,
  Card,
  Collapse,
  Empty,
  Flex,
  Form,
  Input,
  InputNumber,
  Progress,
  Select,
  Space,
  Spin,
  Table,
  Tag,
  Typography,
} from 'antd';
import { DeleteOutlined, ExperimentOutlined, PlayCircleOutlined, StopOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import Button from '../../components/Button.tsx';
import { ApiError } from '../../api/client.ts';
import {
  testDataRunIsBusy,
  useCancelTestDataRun,
  useRemoveTestDataRun,
  useStartTestDataRun,
  useTestDataCoverage,
  useTestDataOverview,
  type TestDataProfile,
  type TestDataRun,
  type TestDataTable,
} from '../../api/hooks.ts';

type RunStatus = TestDataRun['status'];
type TableRole = TestDataTable['role'];

const STATUS_KEYS: Record<RunStatus, string> = {
  queued: 'testData.status.queued',
  running: 'testData.status.running',
  succeeded: 'testData.status.succeeded',
  failed: 'testData.status.failed',
  cancelled: 'testData.status.cancelled',
  removing: 'testData.status.removing',
  removed: 'testData.status.removed',
};

const STATUS_COLORS: Record<RunStatus, string> = {
  queued: 'default',
  running: 'processing',
  succeeded: 'success',
  failed: 'error',
  cancelled: 'warning',
  removing: 'processing',
  removed: 'default',
};

const ROLE_KEYS: Record<TableRole, string> = {
  generated: 'testData.role.generated',
  derived: 'testData.role.derived',
  planned: 'testData.role.planned',
  exempt: 'testData.role.exempt',
};

/** Counts the server reports by kind; a kind this client has no wording for is shown by its own name. */
const COUNT_KEYS: Record<string, string> = {
  massifs: 'testData.counts.massifs',
  karstAreas: 'testData.counts.karstAreas',
  caves: 'testData.counts.caves',
  entrances: 'testData.counts.entrances',
  protectedCaves: 'testData.counts.protectedCaves',
  tags: 'testData.counts.tags',
};

/** The parts of the generator a planned table waits for; a part this client has no wording for is shown by its code. */
const PART_KEYS: Record<string, string> = {
  geography: 'testData.part.geography',
  people: 'testData.part.people',
  underground: 'testData.part.underground',
  activity: 'testData.part.activity',
  documents: 'testData.part.documents',
  geodata: 'testData.part.geodata',
  accounts: 'testData.part.accounts',
};

const PROFILE_NAMES = ['small', 'default', 'large'] as const;
type ProfileName = (typeof PROFILE_NAMES)[number];

const PROFILE_KEYS: Record<ProfileName, string> = {
  small: 'testData.profiles.small',
  default: 'testData.profiles.default',
  large: 'testData.profiles.large',
};

/**
 * The administrators' test-data generator.
 *
 * A run is started from a shipped profile, edited here if wanted — the few knobs most people
 * change as fields, the whole document as JSON. The server refuses a run beside data it did not
 * generate until that is confirmed, which this page asks in a dialog naming how much is there.
 * Runs are listed newest first with their progress, what they wrote (counts only) and a way to
 * remove each; the page asks the server again every two seconds only while one is going.
 */
export default function TestDataPage() {
  const { t, i18n } = useTranslation();
  const { message, modal } = App.useApp();
  const overview = useTestDataOverview();
  const start = useStartTestDataRun();
  const cancel = useCancelTestDataRun();
  const remove = useRemoveTestDataRun();
  const [profileName, setProfileName] = useState<ProfileName>('small');
  const [profileText, setProfileText] = useState('');
  const [expanded, setExpanded] = useState<string[]>([]);

  const profiles = overview.data?.profiles;
  useEffect(() => {
    if (profiles && profileText === '') {
      setProfileText(JSON.stringify(profiles[profileName], null, 2));
    }
  }, [profiles, profileName, profileText]);

  const parsed = useMemo((): TestDataProfile | null => {
    try {
      return JSON.parse(profileText) as TestDataProfile;
    } catch {
      return null;
    }
  }, [profileText]);

  const choose = (name: ProfileName) => {
    setProfileName(name);
    if (profiles) {
      setProfileText(JSON.stringify(profiles[name], null, 2));
    }
  };

  const patch = (change: (p: TestDataProfile) => TestDataProfile) => {
    if (parsed) {
      setProfileText(JSON.stringify(change(parsed), null, 2));
    }
  };

  const when = (value: string | null | undefined) => (value ? new Date(value).toLocaleString(i18n.language) : '—');
  const number = (value: number) => value.toLocaleString(i18n.language);

  const run = async (confirmAlongsideExistingData: boolean) => {
    if (!parsed) {
      message.error(t('testData.jsonInvalid'));
      return;
    }
    try {
      await start.mutateAsync({ profile: parsed, confirmAlongsideExistingData });
      message.success(t('testData.started'));
    } catch (error) {
      if (error instanceof ApiError && error.code === 'testdata.existing_data_confirm_required') {
        const existing = error.problem?.existing as { caves?: number; trips?: number; documents?: number } | undefined;
        modal.confirm({
          title: t('testData.existingTitle'),
          content: t('testData.existingBody', {
            caves: number(existing?.caves ?? 0),
            trips: number(existing?.trips ?? 0),
            documents: number(existing?.documents ?? 0),
          }),
          okText: t('testData.existingOk'),
          cancelText: t('common.cancel'),
          onOk: () => run(true),
        });
        return;
      }
      if (error instanceof ApiError && error.code === 'testdata.run_in_progress') {
        message.warning(t('testData.runInProgress'));
        return;
      }
      if (error instanceof ApiError && error.code === 'testdata.profile_invalid') {
        message.error(t('testData.profileInvalid', { problems: error.detail ?? '' }));
        return;
      }
      message.error(t('testData.startFailed'));
    }
  };

  const confirmRemove = (row: TestDataRun) => {
    modal.confirm({
      title: t('testData.removeTitle'),
      content: t('testData.removeBody'),
      okText: t('testData.remove'),
      okButtonProps: { danger: true },
      cancelText: t('common.cancel'),
      onOk: async () => {
        try {
          await remove.mutateAsync(row.id);
        } catch {
          message.error(t('testData.removeFailed'));
        }
      },
    });
  };

  const stop = async (row: TestDataRun) => {
    try {
      await cancel.mutateAsync(row.id);
    } catch {
      message.error(t('testData.cancelFailed'));
    }
  };

  const header = (
    <Flex vertical style={{ marginBottom: 12 }}>
      <Typography.Title level={3} style={{ margin: 0 }}>
        <ExperimentOutlined /> {t('testData.title')}
      </Typography.Title>
      <Typography.Text type="secondary">{t('testData.intro')}</Typography.Text>
    </Flex>
  );

  if (overview.isLoading) {
    return (
      <div style={{ padding: 24, maxWidth: 1200 }}>
        {header}
        <Spin />
      </div>
    );
  }
  if (overview.isError || !overview.data) {
    const disabled = overview.error instanceof ApiError && overview.error.code === 'testdata.disabled';
    return (
      <div style={{ padding: 24, maxWidth: 1200 }}>
        {header}
        <Alert type={disabled ? 'info' : 'error'} showIcon title={t(disabled ? 'testData.disabled' : 'testData.loadFailed')} />
      </div>
    );
  }

  const data = overview.data;
  const busy = data.runs.some(testDataRunIsBusy);
  const existing = data.existing;
  const hasExisting = existing.caves + existing.trips + existing.documents > 0;

  return (
    <div style={{ padding: 24, maxWidth: 1200 }} data-testid="test-data-page">
      {header}

      <Card size="small" title={t('testData.newRun')} style={{ marginBottom: 16 }}>
        {hasExisting && (
          <Alert
            type="warning"
            showIcon
            style={{ marginBottom: 12 }}
            title={t('testData.existingNote', {
              caves: number(existing.caves),
              trips: number(existing.trips),
              documents: number(existing.documents),
            })}
          />
        )}
        <Form layout="vertical">
          <Space wrap size="large" align="start">
            <Form.Item label={t('testData.profile')} style={{ minWidth: 220 }}>
              <Select<ProfileName>
                value={profileName}
                onChange={choose}
                options={PROFILE_NAMES.map((name) => ({ value: name, label: t(PROFILE_KEYS[name]) }))}
                data-testid="test-data-profile"
              />
            </Form.Item>
            <Form.Item label={t('testData.caveCount')}>
              <InputNumber
                min={1}
                max={data.maxCaves}
                value={parsed?.caves?.count}
                disabled={!parsed}
                onChange={(value) => value !== null && patch((p) => ({ ...p, caves: { ...p.caves, count: value } }))}
                data-testid="test-data-caves"
              />
            </Form.Item>
            <Form.Item label={t('testData.seed')}>
              <InputNumber
                value={parsed?.seed}
                disabled={!parsed}
                onChange={(value) => value !== null && patch((p) => ({ ...p, seed: value }))}
                data-testid="test-data-seed"
              />
            </Form.Item>
          </Space>
          <Form.Item
            label={t('testData.json')}
            extra={t('testData.jsonHelp')}
            validateStatus={parsed ? undefined : 'error'}
            help={parsed ? undefined : t('testData.jsonInvalid')}
          >
            <Input.TextArea
              value={profileText}
              onChange={(event) => setProfileText(event.target.value)}
              autoSize={{ minRows: 6, maxRows: 12 }}
              spellCheck={false}
              style={{ fontFamily: 'monospace' }}
              data-testid="test-data-json"
            />
          </Form.Item>
          <Button
            type="primary"
            icon={<PlayCircleOutlined />}
            disabled={!parsed || busy}
            loading={start.isPending}
            onClick={() => void run(false)}
            data-testid="test-data-start"
          >
            {t('testData.start')}
          </Button>
        </Form>
      </Card>

      <Card size="small" title={t('testData.runs')} style={{ marginBottom: 16 }}>
        {data.runs.length === 0 ? (
          <Empty description={t('testData.noRuns')} />
        ) : (
          <Table<TestDataRun>
            rowKey="id"
            size="small"
            pagination={false}
            dataSource={data.runs}
            expandable={{
              expandedRowKeys: expanded,
              onExpandedRowsChange: (keys) => setExpanded(keys as string[]),
              expandedRowRender: (row) => <RunDetails run={row} />,
            }}
            columns={[
              { title: t('testData.created'), dataIndex: 'createdAt', render: (value: string) => when(value) },
              {
                title: t('testData.statusLabel'),
                render: (_, row) => (
                  <Space size={4} wrap>
                    <Tag color={STATUS_COLORS[row.status]}>{t(STATUS_KEYS[row.status])}</Tag>
                    {row.alongsideExistingData && <Tag>{t('testData.alongside')}</Tag>}
                  </Space>
                ),
              },
              {
                title: t('testData.progress'),
                render: (_, row) =>
                  row.progressTotal > 0 ? (
                    <Progress
                      size="small"
                      style={{ minWidth: 140 }}
                      percent={Math.round((100 * row.progressDone) / row.progressTotal)}
                      status={row.status === 'failed' ? 'exception' : row.status === 'running' ? 'active' : undefined}
                    />
                  ) : null,
              },
              {
                title: t('testData.countsLabel'),
                render: (_, row) => (
                  <Typography.Text>
                    {Object.entries(row.counts)
                      .map(([kind, count]) => `${COUNT_KEYS[kind] ? t(COUNT_KEYS[kind]) : kind}: ${number(count)}`)
                      .join(' · ')}
                  </Typography.Text>
                ),
              },
              { title: t('testData.seed'), dataIndex: 'seed' },
              {
                title: '',
                render: (_, row) => (
                  <Space>
                    {(row.status === 'queued' || row.status === 'running') && (
                      <Button size="small" icon={<StopOutlined />} onClick={() => void stop(row)} data-testid="test-data-cancel">
                        {t('testData.cancel')}
                      </Button>
                    )}
                    {(row.status === 'succeeded' || row.status === 'failed' || row.status === 'cancelled') && (
                      <Button
                        size="small"
                        danger
                        icon={<DeleteOutlined />}
                        disabled={busy}
                        onClick={() => confirmRemove(row)}
                        data-testid="test-data-remove"
                      >
                        {t('testData.remove')}
                      </Button>
                    )}
                  </Space>
                ),
              },
            ]}
          />
        )}
      </Card>

      <Collapse
        items={[
          {
            key: 'tables',
            label: t('testData.tables'),
            children: (
              <Table<TestDataTable>
                rowKey="table"
                size="small"
                pagination={{ pageSize: 50 }}
                dataSource={data.tables}
                columns={[
                  { title: t('testData.table'), dataIndex: 'table' },
                  {
                    title: t('testData.roleLabel'),
                    render: (_, row) => (
                      <Space size={4}>
                        <Tag>{t(ROLE_KEYS[row.role])}</Tag>
                        {row.role === 'planned' && row.part && <span>{PART_KEYS[row.part] ? t(PART_KEYS[row.part]) : row.part}</span>}
                      </Space>
                    ),
                  },
                  { title: t('testData.note'), dataIndex: 'note' },
                ]}
              />
            ),
          },
        ]}
      />
    </div>
  );
}

/** One run's notes, error and coverage, read when the row is opened. */
function RunDetails({ run }: { run: TestDataRun }) {
  const { t, i18n } = useTranslation();
  const coverage = useTestDataCoverage(run.status === 'removed' ? undefined : run.id);
  const number = (value: number) => value.toLocaleString(i18n.language);

  return (
    <Flex vertical gap={8} data-testid="test-data-run-details">
      {run.error && <Alert type="error" showIcon title={run.error} />}
      {run.notes.length > 0 && (
        <div>
          <Typography.Text strong>{t('testData.notes')}</Typography.Text>
          <ul style={{ margin: 0 }}>
            {run.notes.map((note) => (
              <li key={note}>{note}</li>
            ))}
          </ul>
        </div>
      )}
      {run.status !== 'removed' && (
        <div>
          <Typography.Text strong>{t('testData.coverage')}</Typography.Text>
          {coverage.isLoading ? (
            <Spin size="small" />
          ) : (
            <Table
              rowKey="table"
              size="small"
              pagination={false}
              dataSource={coverage.data ?? []}
              columns={[
                { title: t('testData.table'), dataIndex: 'table' },
                { title: t('testData.rows'), dataIndex: 'rows', render: (value: number) => number(value) },
                {
                  title: t('testData.emptyColumns'),
                  render: (_, row) => {
                    const empty = row.columns.filter((c) => c.nonNull === 0).map((c) => c.column);
                    return empty.length === 0 ? t('testData.noEmptyColumns') : empty.join(', ');
                  },
                },
              ]}
            />
          )}
        </div>
      )}
    </Flex>
  );
}
