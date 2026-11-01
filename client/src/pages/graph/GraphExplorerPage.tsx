// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo, useRef, useState } from 'react';
import { Alert, Card, Empty, Flex, Spin, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { useSearchParams } from 'react-router-dom';
import GraphCanvas, { type GraphCanvasHandle } from '../../components/graph/GraphCanvas.tsx';
import GraphDetailsDrawer from '../../components/graph/GraphDetailsDrawer.tsx';
import GraphFilterPanel from '../../components/graph/GraphFilterPanel.tsx';
import GraphLegend from '../../components/graph/GraphLegend.tsx';
import GraphListView from '../../components/graph/GraphListView.tsx';
import GraphSearchBox from '../../components/graph/GraphSearchBox.tsx';
import GraphSeedSearch from '../../components/graph/GraphSeedSearch.tsx';
import GraphToolbar from '../../components/graph/GraphToolbar.tsx';
import { useGraphExplorer } from '../../components/graph/useGraphExplorer.ts';
import { useGraphText, useGraphTheme } from '../../components/graph/useGraphPresentation.ts';
import { getExplorerModel, selectView, useExplorerStore, type ExplorerSeed } from '../../graph/explorerStore.ts';
import { facetsOf, relationSamples } from '../../graph/filters.ts';
import { nextMatch, previousMatch } from '../../graph/search.ts';
import { GRAPH_CATEGORIES, GRAPH_NODE_TYPES, type NodeKey } from '../../graph/types.ts';

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
  const searchText = useExplorerStore((s) => s.searchText);
  const store = useExplorerStore.getState;

  const view = useMemo(() => selectView({ version, filters, selection }, { searchText, showMore: true }), [version, filters, selection, searchText]);

  const facets = useMemo(
    // `version` is read so the lists follow the model, which React does not otherwise see change.
    () => (version >= 0 ? facetsOf(getExplorerModel().nodes.values(), getExplorerModel().edges.values()) : { categories: [], types: [] }),
    [version],
  );
  const samples = useMemo(() => (version >= 0 ? relationSamples(getExplorerModel().edges.values()) : new Map()), [version]);

  const selectedNode = view.selection?.kind === 'node' ? view.selection.key : null;
  const canExpand = useMemo(
    // `version` is read so the answer follows the model, which React does not otherwise see change.
    () => version >= 0 && selectedNode !== null && getExplorerModel().requestFor(selectedNode) !== null,
    [version, selectedNode],
  );

  const [detailsOpen, setDetailsOpen] = useState(false);
  const selectNode = (key: NodeKey) => store().select({ kind: 'node', key });
  // A "+N more" stub is a control, not an item: pressing it loads that category's next page.
  const loadMore = (nodeKey: NodeKey, category: string) => void controller.expand(nodeKey, category);
  const pressNode = (key: NodeKey, thenExpand: boolean) => {
    const pressed = view.nodes.find((n) => n.key === key);
    if (pressed?.more) {
      loadMore(pressed.more.nodeKey, pressed.more.category);
      return;
    }
    selectNode(key);
    if (thenExpand) {
      void controller.expand(key);
    }
  };
  const canRemove = selectedNode !== null && selectedNode !== view.seedKey;
  const removeNode = (key: NodeKey) => {
    store().removeNode(key);
    setDetailsOpen(false);
  };
  const stepMatch = (step: typeof nextMatch) => {
    const found = step(view.matches, selectedNode);
    if (found !== null) {
      selectNode(found);
      canvas.current?.focus(found);
    }
  };
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
              <GraphSeedSearch fullWidth autoFocus />
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
        onDetails={() => setDetailsOpen(true)}
        canDetails={view.selection !== null}
        onRemove={() => selectedNode !== null && removeNode(selectedNode)}
        canRemove={canRemove}
      />
      <Flex gap={12} wrap align="center">
        <GraphSeedSearch />
        <GraphSearchBox
          value={searchText}
          onChange={(text) => store().setSearchText(text)}
          matchCount={view.matches.length}
          position={selectedNode === null ? 0 : view.matches.indexOf(selectedNode) + 1}
          onNext={() => stepMatch(nextMatch)}
          onPrevious={() => stepMatch(previousMatch)}
        />
      </Flex>
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
            onNodeClick={(key) => pressNode(key, false)}
            onNodeDoubleClick={(key) => pressNode(key, true)}
            onEdgeClick={(key) => store().select({ kind: 'edge', key })}
            onCanvasClick={() => store().select(null)}
          />
          </div>
          <div style={{ padding: '8px 12px', borderTop: '1px solid var(--ant-color-border-secondary, #f0f0f0)' }}>
            <GraphLegend view={view} text={text} theme={theme} />
          </div>
        </Card>
        <Flex vertical gap={12} style={{ flex: '1 1 300px', minWidth: 0 }}>
          <Card title={t('graph.filters.title')} size="small" styles={{ body: { maxHeight: 360, overflow: 'auto' } }}>
            <GraphFilterPanel
              facets={facets}
              samples={samples}
              filters={filters}
              text={text}
              onToggleCategory={(c, on) => store().toggleCategory(c, on)}
              onToggleRelation={(c, r, on) => store().toggleRelation(c, r, on)}
              onToggleType={(ty, on) => store().toggleType(ty, on)}
              onOnlyCategory={(c) => store().onlyCategory(c, GRAPH_CATEGORIES)}
              onOnlyType={(ty) => store().onlyType(ty, GRAPH_NODE_TYPES)}
              onReset={() => store().resetFilters()}
            />
          </Card>
          <Card
            title={t('graph.list.title')}
            style={{ maxHeight: 'calc(100dvh - 160px)', overflow: 'auto' }}
          >
            <GraphListView
              view={view}
              text={text}
              onMore={loadMore}
              onSelect={(key) => {
                selectNode(key);
                canvas.current?.focus(key);
              }}
            />
          </Card>
        </Flex>
      </Flex>
      <GraphDetailsDrawer
        open={detailsOpen}
        view={view}
        text={text}
        onClose={() => setDetailsOpen(false)}
        onSelect={(key) => {
          selectNode(key);
          canvas.current?.focus(key);
        }}
        onRemove={removeNode}
      />
    </Flex>
  );
}
