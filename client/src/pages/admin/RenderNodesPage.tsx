// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { Alert, App, Card, Flex, Form, Input, Popconfirm, Skeleton, Table, Tag, Typography } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import { useTranslation } from 'react-i18next';
import Button from '../../components/Button.tsx';
import ReadFailedAlert from '../../components/ReadFailedAlert.tsx';
import { formatBytes } from '../../components/storage/storageFigures.ts';
import { ApiError } from '../../api/client.ts';
import {
  renderNodeServerUrl,
  useIssueRenderNode,
  useRenderNodes,
  useRevokeRenderNode,
  type RenderNode,
  type RenderNodeIssued,
} from '../../api/hooks/flybyRenders.ts';

const NAME_MAX_LENGTH = 100;

/**
 * The render nodes: machines the operator runs ThIDE's render worker on, which take a flyby's
 * render from this server and send its film back. Issuing one makes its token, shown here once;
 * withdrawing one refuses its next call. Nothing here starts anything on a node — a node calls in.
 */
export default function RenderNodesPage() {
  const { t, i18n } = useTranslation();
  const { message } = App.useApp();
  const nodes = useRenderNodes();
  const issue = useIssueRenderNode();
  const revoke = useRevokeRenderNode();
  const [name, setName] = useState('');
  const [issued, setIssued] = useState<RenderNodeIssued | null>(null);
  const trimmed = name.trim();
  const when = (value: string | null | undefined) => (value ? new Date(value).toLocaleString(i18n.language) : null);
  const serverUrl = renderNodeServerUrl(window.location.origin);

  const issueNode = async () => {
    try {
      setIssued(await issue.mutateAsync(trimmed));
      setName('');
    } catch (error) {
      message.error(
        error instanceof ApiError && error.code === 'render_node.name_taken'
          ? t('renderNodes.nameTaken')
          : t('common.saveFailed'),
      );
    }
  };

  const revokeNode = async (node: RenderNode) => {
    try {
      await revoke.mutateAsync(node.id);
      setIssued((current) => (current?.node.id === node.id ? null : current));
      message.success(t('renderNodes.revoked'));
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  const columns: ColumnsType<RenderNode> = [
    { title: t('renderNodes.columns.name'), dataIndex: 'name', key: 'name' },
    {
      title: t('renderNodes.columns.token'),
      dataIndex: 'tokenPrefix',
      key: 'token',
      render: (prefix: string) => <Typography.Text code>{prefix}…</Typography.Text>,
    },
    {
      title: t('renderNodes.columns.state'),
      key: 'state',
      render: (_, node) =>
        node.revokedAt ? (
          <Tag color="red">{t('renderNodes.states.revoked')}</Tag>
        ) : node.online ? (
          <Tag color="green">{t('renderNodes.states.online')}</Tag>
        ) : (
          <Tag>{t('renderNodes.states.quiet')}</Tag>
        ),
    },
    {
      title: t('renderNodes.columns.lastSeen'),
      key: 'lastSeen',
      render: (_, node) => when(node.lastSeenAt) ?? t('renderNodes.never'),
    },
    {
      title: t('renderNodes.columns.machine'),
      key: 'machine',
      render: (_, node) => (
        <Flex vertical gap={2}>
          <span>
            {[node.device, node.blenderVersions ? `Blender ${node.blenderVersions}` : null, node.worker ? `therion-cli ${node.worker}` : null]
              .filter(Boolean)
              .join(' · ')}
          </span>
          {!node.revokedAt && node.skipReason && (
            <Typography.Text type="warning" data-testid={`render-node-skipped-${node.name}`}>
              {node.skipReason === 'disk'
                ? t('renderNodes.skipped.disk', {
                    count: node.skippedRenders ?? 0,
                    needs: formatBytes(node.skipNeedsBytes ?? 0),
                    free: formatBytes(node.freeDiskBytes ?? 0),
                  })
                : t('renderNodes.skipped.container', { count: node.skippedRenders ?? 0 })}
            </Typography.Text>
          )}
        </Flex>
      ),
    },
    {
      title: '',
      key: 'actions',
      render: (_, node) =>
        node.revokedAt ? null : (
          <Popconfirm
            title={t('renderNodes.revokeConfirm', { name: node.name })}
            okText={t('renderNodes.revoke')}
            onConfirm={() => void revokeNode(node)}
          >
            <Button size="small" danger loading={revoke.isPending && revoke.variables === node.id} data-testid={`render-node-revoke-${node.name}`}>
              {t('renderNodes.revoke')}
            </Button>
          </Popconfirm>
        ),
    },
  ];

  if (nodes.isPending) {
    return <Skeleton active paragraph={{ rows: 4 }} />;
  }
  if (nodes.isError) {
    return (
      <ReadFailedAlert isError error={nodes.error} held={false} onRetry={() => nodes.refetch()} title={t('renderNodes.loadFailed')} />
    );
  }

  return (
    <Flex vertical gap={16}>
      <Card size="small" title={t('renderNodes.title')}>
        <Flex vertical gap={12}>
          <Typography.Paragraph type="secondary" style={{ margin: 0 }}>
            {t('renderNodes.intro')}
          </Typography.Paragraph>
          <Alert type="info" showIcon title={t('renderNodes.protectedWarning')} data-testid="render-node-protected-warning" />

          {issued && (
            <Alert
              type="warning"
              showIcon
              closable
              onClose={() => setIssued(null)}
              title={t('renderNodes.issuedTitle', { name: issued.node.name })}
              data-testid="render-node-issued"
              description={
                <Flex vertical gap={4}>
                  <Typography.Text code copyable={{ text: issued.token }} data-testid="render-node-token">
                    {issued.token}
                  </Typography.Text>
                  <Typography.Text type="secondary">{t('renderNodes.issuedWarning')}</Typography.Text>
                  <Typography.Text type="secondary">{t('renderNodes.howToRun')}</Typography.Text>
                  <Typography.Text code copyable>
                    {`therion-cli render-worker --server ${serverUrl} --token-file <file>`}
                  </Typography.Text>
                </Flex>
              }
            />
          )}

          <Form layout="inline" onFinish={() => void issueNode()} style={{ rowGap: 8 }}>
            <Form.Item label={t('renderNodes.form.name')}>
              <Input
                value={name}
                maxLength={NAME_MAX_LENGTH}
                placeholder={t('renderNodes.form.namePlaceholder')}
                onChange={(event) => setName(event.target.value)}
                style={{ width: 260 }}
                data-testid="render-node-name"
              />
            </Form.Item>
            <Form.Item>
              <Button type="primary" htmlType="submit" loading={issue.isPending} disabled={trimmed === ''} data-testid="render-node-issue">
                {t('renderNodes.issue')}
              </Button>
            </Form.Item>
          </Form>

          <Table<RenderNode>
            rowKey="id"
            size="small"
            columns={columns}
            dataSource={nodes.data}
            pagination={false}
            locale={{ emptyText: t('renderNodes.empty') }}
            data-testid="render-nodes-table"
          />
        </Flex>
      </Card>
    </Flex>
  );
}
