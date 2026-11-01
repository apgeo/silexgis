// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo, useRef } from 'react';
import { Alert, Card, Empty, Flex, Spin, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { useSearchParams } from 'react-router-dom';
import GraphCanvas, { type GraphCanvasHandle } from '../../components/graph/GraphCanvas.tsx';
import GraphListView from '../../components/graph/GraphListView.tsx';
import GraphToolbar from '../../components/graph/GraphToolbar.tsx';
import { useGraphExplorer } from '../../components/graph/useGraphExplorer.ts';
import { useGraphText, useGraphTheme } from '../../components/graph/useGraphPresentation.ts';
import { getExplorerModel, selectView, useExplorerStore, type ExplorerSeed } from '../../graph/explorerStore.ts';
import type { NodeKey } from '../../graph/types.ts';

/**
 * The relations graph: one item and what is related to it, grown one step at a time.
 *
 * <p>
 * The seed is in the address (`?type=&id=`), so a graph is a link. The explored graph itself is
 * held by the explorer store, outside React state; this page reads a derived view of it, hands that
 * to the canvas and to the list, and turns what the person does into store calls. The list is not
 * decoration: it is the canvas's accessible equivalent, and choosing a row does what pressing the
 * node does.
 * </p>
 */
export default function GraphExplorerPage() {
  const { t } = useTranslation();
  const [searchParams] = useSearchParams();
  const type = searchParams.get('type');
  const id = searchParams.get('id');
  const seed: ExplorerSeed | null = type && id ? { type, id } : null;

  const controller = useGraphExplorer(seed);
  const text = useGraphText();
  const theme = useGraphTheme();
  const canvas = useRef<GraphCanvasHandle | null>(null);

  const version = useExplorerStore((s) => s.version);
  const filters = useExplorerStore((s) => s.filters);
  const selection = useExplorerStore((s) => s.selection);
  const layout = useExplorerStore((s) => s.layout);
  const rendererKind = useExplorerStore((s) => s.rendererKind);
  const lastExpanded = useExplorerStore((s) => s.lastExpanded);
  const store = useExplorerStore.getState;

  const view = useMemo(() => selectView({ version, filters, selection }), [version, filters, selection]);

  const selectedNode = view.selection?.kind === 'node' ? view.selection.key : null;
  const canExpand = useMemo(
    // `version` is read so the answer follows the model, which React does not otherwise see change.
    () => version >= 0 && selectedNode !== null && getExplorerModel().requestFor(selectedNode) !== null,
    [version, selectedNode],
  );

  const selectNode = (key: NodeKey) => store().select({ kind: 'node', key });
  const expandSelected = () => {
    if (selectedNode !== null) {
      void controller.expand(selectedNode);
    }
  };

  if (seed === null) {
    return (
      <Flex vertical align="center" style={{ padding: 48 }} data-testid="graph-no-seed">
        <Empty
          image={Empty.PRESENTED_IMAGE_SIMPLE}
          description={
            <>
              <Typography.Title level={4}>{t('graph.noSeed.title')}</Typography.Title>
              <Typography.Paragraph type="secondary" style={{ maxWidth: 480 }}>
                {t('graph.noSeed.body')}
              </Typography.Paragraph>
            </>
          }
        />
      </Flex>
    );
  }

  if (controller.status === 'notFound' || controller.status === 'error') {
    return (
      <div style={{ padding: 24 }}>
        <Alert
          type={controller.status === 'notFound' ? 'warning' : 'error'}
          showIcon
          title={t(controller.status === 'notFound' ? 'graph.errors.notFound' : 'graph.errors.generic')}
          data-testid="graph-load-error"
        />
      </div>
    );
  }

  return (
    <Flex vertical gap={12} style={{ padding: 16, minHeight: 'calc(100dvh - 48px)' }}>
      <Flex justify="space-between" align="center" wrap gap={8}>
        <Typography.Title level={3} style={{ margin: 0 }}>
          {t('graph.title')}
        </Typography.Title>
        <Typography.Text type="secondary" data-testid="graph-summary">
          {controller.status === 'loading'
            ? <><Spin size="small" /> {t('graph.loading')}</>
            : t('graph.summary', { nodes: view.nodeCount, edges: view.edgeCount })}
        </Typography.Text>
      </Flex>
      <GraphToolbar
        layout={layout}
        onLayoutChange={(next) => store().setLayout(next)}
        rendererKind={rendererKind}
        onRendererChange={(kind) => store().setRendererKind(kind)}
        onFit={() => canvas.current?.fitView()}
        onRefresh={() => void controller.refresh()}
        onExpandSelected={expandSelected}
        onCollapse={() => store().collapseToSeed()}
        canExpand={canExpand}
        canCollapse={view.nodeCount > 1 || getExplorerModel().expandedKeys().length > 1}
        canRefresh={controller.status === 'ready'}
        busy={controller.pending > 0}
      />
      {controller.actionFailed && <Alert type="warning" showIcon title={t('graph.errors.action')} data-testid="graph-action-error" />}
      <Flex gap={12} wrap style={{ flex: 1, minHeight: 420 }}>
        <Card styles={{ body: { padding: 0 } }} style={{ flex: '3 1 480px' }}>
          <div style={{ height: 'max(420px, calc(100dvh - 240px))' }}>
          <GraphCanvas
            ref={canvas}
            view={view}
            labels={text.labels}
            theme={theme}
            layout={layout}
            rendererKind={rendererKind}
            anchorKey={lastExpanded}
            onNodeClick={selectNode}
            onNodeDoubleClick={(key) => {
              selectNode(key);
              void controller.expand(key);
            }}
            onEdgeClick={(key) => store().select({ kind: 'edge', key })}
            onCanvasClick={() => store().select(null)}
          />
          </div>
        </Card>
        <Card
          title={t('graph.list.title')}
          style={{ flex: '1 1 300px', maxHeight: 'calc(100dvh - 160px)', overflow: 'auto' }}
        >
          <GraphListView
            view={view}
            text={text}
            onSelect={(key) => {
              selectNode(key);
              canvas.current?.focus(key);
            }}
          />
        </Card>
      </Flex>
    </Flex>
  );
}
