// SPDX-License-Identifier: AGPL-3.0-or-later
import { Alert, App, Card, Col, Descriptions, Empty, Flex, Row, Spin, Statistic, Table, Typography } from 'antd';
import { DeleteOutlined, ReloadOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import Button from '../../components/Button.tsx';
import {
  hasAccessAction,
  useCapabilities,
  useMeasureStorage,
  useReclaimStorage,
  useStorageReport,
  type StorageHolder,
  type StorageKindTotal,
  type StorageLargeItem,
  type StorageReclaimable,
} from '../../api/hooks.ts';
import { formatBytes, kindKey, levelAlertType, percentOfCap, reclaimGroupKeys } from '../../components/storage/storageFigures.ts';

/**
 * What fills this installation's file store, how fast it grows, and what could be deleted.
 *
 * Read from the latest daily report rather than measured on every visit: totals over every stored
 * file, a walk of the processing worker's job directories and the growth over a month are not
 * something to recompute because somebody opened a page. Measuring now is offered beside it.
 *
 * Deleting is one confirmed act per group, never a selection of items: each group is something
 * the server can name and recompute when the request arrives, and uploaded originals are in none
 * of them.
 */
export default function StoragePage() {
  const { t, i18n } = useTranslation();
  const { message, modal } = App.useApp();
  const { data: capabilities } = useCapabilities();
  const canAct = hasAccessAction(capabilities?.domains.settings, 'execute');
  const report = useStorageReport();
  const measure = useMeasureStorage();
  const reclaim = useReclaimStorage();

  const when = (value: string) => new Date(value).toLocaleString(i18n.language);
  const number = (value: number) => value.toLocaleString(i18n.language);
  const kindName = (kind: string) => t(kindKey(kind), { defaultValue: kind });

  const measureNow = async () => {
    try {
      await measure.mutateAsync();
      message.success(t('fileStorage.measured'));
    } catch {
      message.error(t('fileStorage.measureFailed'));
    }
  };

  const confirmReclaim = (row: StorageReclaimable) => {
    const keys = reclaimGroupKeys(row.group);
    modal.confirm({
      title: t('fileStorage.reclaimTitle', { group: t(keys.title) }),
      content: t('fileStorage.reclaimConfirm', { items: number(row.items), size: formatBytes(row.bytes) }),
      okText: t('fileStorage.reclaimOk'),
      okButtonProps: { danger: true },
      cancelText: t('common.cancel'),
      onOk: async () => {
        try {
          const result = await reclaim.mutateAsync(row.group);
          message.success(t('fileStorage.reclaimed', { items: number(result.items), size: formatBytes(result.freedBytes) }));
          // The report is a snapshot; measure again so the page shows the room given back.
          await measure.mutateAsync();
        } catch {
          message.error(t('fileStorage.reclaimFailed'));
        }
      },
    });
  };

  const header = (
    <Flex justify="space-between" align="center" wrap gap={8} style={{ marginBottom: 12 }}>
      <Flex vertical>
        <Typography.Title level={3} style={{ margin: 0 }}>
          {t('fileStorage.title')}
        </Typography.Title>
        <Typography.Text type="secondary">{t('fileStorage.intro')}</Typography.Text>
      </Flex>
      {canAct && (
        <Button
          icon={<ReloadOutlined />}
          loading={measure.isPending}
          onClick={() => void measureNow()}
          data-testid="storage-measure"
        >
          {t('fileStorage.measure')}
        </Button>
      )}
    </Flex>
  );

  if (report.isLoading) {
    return (
      <div style={{ padding: 24, maxWidth: 1200 }}>
        {header}
        <Spin />
      </div>
    );
  }
  if (report.isError) {
    return (
      <div style={{ padding: 24, maxWidth: 1200 }}>
        {header}
        <Alert type="error" showIcon title={t('fileStorage.loadFailed')} />
      </div>
    );
  }
  const r = report.data;
  if (!r) {
    return (
      <div style={{ padding: 24, maxWidth: 1200 }}>
        {header}
        <Empty description={t('fileStorage.noReport')} data-testid="storage-no-report" />
      </div>
    );
  }

  const alert = levelAlertType(r.level);
  const scanCapped = r.scanCapBytes !== null && r.scanCapBytes !== undefined;
  const capLine = (used: number, cap: number | null | undefined) => {
    const percent = percentOfCap(used, cap);
    return percent === null || cap === null || cap === undefined
      ? t('fileStorage.noCap')
      : t('fileStorage.ofCap', { cap: formatBytes(cap), percent });
  };
  const kindColumns = [
    { title: t('fileStorage.kind'), dataIndex: 'kind', key: 'kind', render: (kind: string) => kindName(kind) },
    { title: t('fileStorage.files'), dataIndex: 'files', key: 'files', align: 'right' as const, render: number },
    { title: t('fileStorage.size'), dataIndex: 'bytes', key: 'bytes', align: 'right' as const, render: formatBytes },
  ];
  const holderColumns = (nameTitle: string) => [
    {
      title: nameTitle,
      dataIndex: 'name',
      key: 'name',
      render: (name: string | null) => name ?? <Typography.Text type="secondary">{t('fileStorage.unnamed')}</Typography.Text>,
    },
    { title: t('fileStorage.files'), dataIndex: 'files', key: 'files', align: 'right' as const, render: number },
    { title: t('fileStorage.size'), dataIndex: 'bytes', key: 'bytes', align: 'right' as const, render: formatBytes },
  ];

  return (
    <div style={{ padding: 24, maxWidth: 1200 }} data-testid="storage-page">
      {header}
      <Typography.Paragraph type="secondary" data-testid="storage-taken-at">
        {t('fileStorage.takenAt', { when: when(r.takenAt) })}
      </Typography.Paragraph>

      {alert && (
        <Alert
          type={alert}
          showIcon
          style={{ marginBottom: 16 }}
          data-testid="storage-level"
          title={t(`fileStorage.levels.${r.level}`)}
        />
      )}

      <Row gutter={[16, 16]} style={{ marginBottom: 16 }}>
        <Col xs={24} sm={12} lg={6}>
          <Card size="small">
            <Statistic title={t('fileStorage.installation')} value={formatBytes(r.storeUsedBytes)} />
            <Typography.Text type="secondary">{capLine(r.storeUsedBytes, r.storeCapBytes)}</Typography.Text>
          </Card>
        </Col>
        <Col xs={24} sm={12} lg={6}>
          <Card size="small">
            <Statistic title={t('fileStorage.scans')} value={formatBytes(r.scanUsedBytes)} />
            <Typography.Text type="secondary">{capLine(r.scanUsedBytes, r.scanCapBytes)}</Typography.Text>
          </Card>
        </Col>
        <Col xs={24} sm={12} lg={6}>
          <Card size="small">
            <Statistic
              title={t('fileStorage.storeFree')}
              value={r.storeFreeBytes === null || r.storeFreeBytes === undefined ? '—' : formatBytes(r.storeFreeBytes)}
            />
            <Typography.Text type="secondary">{t('fileStorage.minFree', { size: formatBytes(r.minFreeBytes) })}</Typography.Text>
          </Card>
        </Col>
        <Col xs={24} sm={12} lg={6}>
          <Card size="small">
            <Statistic
              title={t('fileStorage.workerFree')}
              value={r.workerFreeBytes === null || r.workerFreeBytes === undefined ? '—' : formatBytes(r.workerFreeBytes)}
            />
            <Typography.Text type="secondary">
              {t('fileStorage.workDirectories', { size: formatBytes(r.workDirectoryBytes) })}
            </Typography.Text>
          </Card>
        </Col>
      </Row>

      <Card size="small" title={t('fileStorage.growth')} style={{ marginBottom: 16 }} data-testid="storage-growth">
        <Descriptions size="small" column={{ xs: 1, sm: 2, lg: 4 }}>
          <Descriptions.Item label={t('fileStorage.sevenDays')}>
            {r.growth.sevenDayBytes === null || r.growth.sevenDayBytes === undefined
              ? t('fileStorage.notYet')
              : formatBytes(r.growth.sevenDayBytes)}
          </Descriptions.Item>
          <Descriptions.Item label={t('fileStorage.thirtyDays')}>
            {r.growth.thirtyDayBytes === null || r.growth.thirtyDayBytes === undefined
              ? t('fileStorage.notYet')
              : formatBytes(r.growth.thirtyDayBytes)}
          </Descriptions.Item>
          <Descriptions.Item label={t('fileStorage.perDay')}>
            {r.growth.bytesPerDay === null || r.growth.bytesPerDay === undefined
              ? t('fileStorage.notYet')
              : formatBytes(r.growth.bytesPerDay)}
          </Descriptions.Item>
          <Descriptions.Item label={t('fileStorage.fullOn')}>
            {r.growth.fullOn ? new Date(r.growth.fullOn).toLocaleDateString(i18n.language) : t('fileStorage.notInSight')}
          </Descriptions.Item>
          {/* The point clouds' cap is a ceiling of its own: its pace and its day, when it is set. */}
          {scanCapped && (
            <Descriptions.Item label={t('fileStorage.scanPerDay')}>
              {r.growth.scanBytesPerDay === null || r.growth.scanBytesPerDay === undefined
                ? t('fileStorage.notYet')
                : formatBytes(r.growth.scanBytesPerDay)}
            </Descriptions.Item>
          )}
          {scanCapped && (
            <Descriptions.Item label={t('fileStorage.scansFullOn')}>
              <span data-testid="storage-scans-full-on">
                {r.growth.scansFullOn
                  ? new Date(r.growth.scansFullOn).toLocaleDateString(i18n.language)
                  : t('fileStorage.notInSight')}
              </span>
            </Descriptions.Item>
          )}
        </Descriptions>
      </Card>

      <Card size="small" title={t('fileStorage.reclaimable')} style={{ marginBottom: 16 }}>
        <Typography.Paragraph type="secondary">{t('fileStorage.reclaimableIntro')}</Typography.Paragraph>
        <Table<StorageReclaimable>
          size="small"
          rowKey="group"
          pagination={false}
          dataSource={r.reclaimable}
          data-testid="storage-reclaimable"
          onRow={(row) => ({ 'data-testid': `storage-group-${row.group}` }) as never}
          columns={[
            {
              title: t('fileStorage.group'),
              key: 'group',
              render: (_, row) => (
                <Flex vertical>
                  <Typography.Text>{t(reclaimGroupKeys(row.group).title)}</Typography.Text>
                  <Typography.Text type="secondary">{t(reclaimGroupKeys(row.group).hint)}</Typography.Text>
                </Flex>
              ),
            },
            { title: t('fileStorage.items'), dataIndex: 'items', key: 'items', align: 'right', render: number },
            { title: t('fileStorage.saving'), dataIndex: 'bytes', key: 'bytes', align: 'right', render: formatBytes },
            ...(canAct
              ? [
                  {
                    title: '',
                    key: 'act',
                    align: 'right' as const,
                    render: (_: unknown, row: StorageReclaimable) => (
                      <Button
                        danger
                        icon={<DeleteOutlined />}
                        disabled={row.items === 0 || reclaim.isPending}
                        onClick={() => confirmReclaim(row)}
                        data-testid={`storage-reclaim-${row.group}`}
                      >
                        {t('fileStorage.reclaim')}
                      </Button>
                    ),
                  },
                ]
              : []),
          ]}
        />
      </Card>

      <Row gutter={[16, 16]} style={{ marginBottom: 16 }}>
        <Col xs={24} lg={12}>
          <Card size="small" title={t('fileStorage.byFileKind')}>
            <Table<StorageKindTotal> size="small" rowKey="kind" pagination={false} dataSource={r.byFileKind} columns={kindColumns} />
          </Card>
        </Col>
        <Col xs={24} lg={12}>
          <Card size="small" title={t('fileStorage.scanByKind')}>
            <Table<StorageKindTotal> size="small" rowKey="kind" pagination={false} dataSource={r.scanByKind} columns={kindColumns} />
          </Card>
        </Col>
        <Col xs={24} lg={12}>
          <Card size="small" title={t('fileStorage.byCave')}>
            <Table<StorageHolder>
              size="small"
              rowKey="id"
              pagination={{ pageSize: 10, hideOnSinglePage: true }}
              dataSource={r.byCave}
              columns={holderColumns(t('fileStorage.cave'))}
            />
          </Card>
        </Col>
        <Col xs={24} lg={12}>
          <Card size="small" title={t('fileStorage.byAccount')}>
            <Table<StorageHolder>
              size="small"
              rowKey="id"
              pagination={{ pageSize: 10, hideOnSinglePage: true }}
              dataSource={r.byAccount}
              columns={holderColumns(t('fileStorage.account'))}
            />
          </Card>
        </Col>
      </Row>

      <Card size="small" title={t('fileStorage.largest')}>
        <Table<StorageLargeItem>
          size="small"
          rowKey="fileId"
          pagination={{ pageSize: 10, hideOnSinglePage: true }}
          dataSource={r.largest}
          scroll={{ x: true }}
          data-testid="storage-largest"
          columns={[
            { title: t('fileStorage.name'), dataIndex: 'name', key: 'name' },
            { title: t('fileStorage.kind'), dataIndex: 'kind', key: 'kind', render: (kind: string) => kindName(kind) },
            { title: t('fileStorage.size'), dataIndex: 'bytes', key: 'bytes', align: 'right', render: formatBytes },
            { title: t('fileStorage.owner'), dataIndex: 'ownerName', key: 'owner', render: (v: string | null) => v ?? '—' },
            { title: t('fileStorage.cave'), dataIndex: 'caveName', key: 'cave', render: (v: string | null) => v ?? '—' },
            {
              title: t('fileStorage.added'),
              dataIndex: 'createdAt',
              key: 'createdAt',
              render: (v: string) => new Date(v).toLocaleDateString(i18n.language),
            },
          ]}
        />
      </Card>
    </div>
  );
}
