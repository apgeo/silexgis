// SPDX-License-Identifier: AGPL-3.0-or-later
import { NodeCollapseOutlined, NodeExpandOutlined, ReloadOutlined, FullscreenOutlined } from '@ant-design/icons';
import { Flex, Segmented } from 'antd';
import { useTranslation } from 'react-i18next';
import Button from '../Button.tsx';
import { LAYOUT_KINDS, type LayoutKind } from '../../graph/renderer.ts';
import { hasRendererChoice, RENDERER_KINDS, type RendererKind } from '../../graph/rendererRegistry.ts';

const LAYOUT_LABEL_KEYS: Record<LayoutKind, string> = {
  force: 'graph.layouts.force',
  radial: 'graph.layouts.radial',
  layered: 'graph.layouts.layered',
};

const RENDERER_LABELS: Record<RendererKind, string> = { g6: 'G6' };

export interface GraphToolbarProps {
  layout: LayoutKind;
  onLayoutChange: (layout: LayoutKind) => void;
  rendererKind: RendererKind;
  onRendererChange: (kind: RendererKind) => void;
  onFit: () => void;
  onRefresh: () => void;
  onExpandSelected: () => void;
  onCollapse: () => void;
  /** A node is selected and there is more to ask about it. */
  canExpand: boolean;
  /** The graph has grown beyond the first item's own hop. */
  canCollapse: boolean;
  canRefresh: boolean;
  busy: boolean;
}

/** The controls over the drawing; it holds no state of its own. */
export default function GraphToolbar(props: GraphToolbarProps) {
  const { t } = useTranslation();
  return (
    <Flex role="toolbar" aria-label={t('graph.toolbar.label')} data-testid="graph-toolbar" gap={8} wrap align="center">
      <Segmented<LayoutKind>
        aria-label={t('graph.toolbar.layout')}
        value={props.layout}
        onChange={props.onLayoutChange}
        options={LAYOUT_KINDS.map((kind) => ({ value: kind, label: t(LAYOUT_LABEL_KEYS[kind]) }))}
      />
      {hasRendererChoice() && (
        <Segmented<RendererKind>
          value={props.rendererKind}
          onChange={props.onRendererChange}
          options={RENDERER_KINDS.map((kind) => ({ value: kind, label: RENDERER_LABELS[kind] }))}
        />
      )}
      <Button icon={<FullscreenOutlined />} onClick={props.onFit} data-testid="graph-fit">
        {t('graph.toolbar.fit')}
      </Button>
      <Button
        icon={<NodeExpandOutlined />}
        onClick={props.onExpandSelected}
        disabled={!props.canExpand}
        data-testid="graph-expand"
      >
        {t('graph.toolbar.expand')}
      </Button>
      <Button
        icon={<NodeCollapseOutlined />}
        onClick={props.onCollapse}
        disabled={!props.canCollapse}
        data-testid="graph-collapse"
      >
        {t('graph.toolbar.collapse')}
      </Button>
      <Button
        icon={<ReloadOutlined />}
        onClick={props.onRefresh}
        disabled={!props.canRefresh}
        loading={props.busy}
        data-testid="graph-refresh"
      >
        {t('graph.toolbar.refresh')}
      </Button>
    </Flex>
  );
}
