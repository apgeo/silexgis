// SPDX-License-Identifier: AGPL-3.0-or-later
import { fireEvent, render, screen, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import '../../i18n';
import i18n from '../../i18n';
import { GraphModel } from '../../graph/graphModel.ts';
import { count, wireAnswer, wireEdge, wireNode, wirePlaceholder } from '../../graph/graphFixtures.ts';
import { toView } from '../../graph/view.ts';
import GraphListView from './GraphListView.tsx';
import { nodeLabel, type Translate } from '../../graph/visuals.ts';
import type { GraphText } from './useGraphPresentation.ts';

const S = wireNode('feature', 's', 'Cave S');
const a = wireNode('document', 'a', 'Survey notes');
// Against the contract a placeholder carries text here, so the list's own rule is what keeps it back.
const hidden = { ...wirePlaceholder('m1', 'tripLog'), title: 'Secret trip', subtitle: 'Secret place' };

function view(selection: { kind: 'node'; key: string } | null = null) {
  const model = new GraphModel();
  model.merge(
    wireAnswer(
      S,
      [
        { nodes: [a], edges: [wireEdge('attachment', 'plan', S.key, a.key, { directed: false })] },
        { nodes: [hidden], edges: [wireEdge('hierarchy', 'parent', S.key, hidden.key)] },
      ],
      [count('attachment', 1), count('hierarchy', 1)],
    ),
  );
  return toView(model, undefined, selection);
}

const text: GraphText = {
  labels: { node: () => '', edge: () => '' },
  node: (vn) => nodeLabel(vn, i18n.t as unknown as Translate),
  relation: (edge) => `rel:${edge.relation}`,
  typeName: (type) => `type:${type}`,
};

describe('GraphListView', () => {
  it('lists every visible node under its kind with the relations that reach it', () => {
    render(<GraphListView view={view()} text={text} onSelect={vi.fn()} />);
    expect(screen.getByRole('region', { name: 'type:feature' })).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'type:document' })).toBeInTheDocument();
    const items = screen.getAllByTestId('graph-list-item');
    expect(items).toHaveLength(3);
    const seed = items.find((i) => i.dataset.key === 'feature:s')!;
    expect(within(seed).getAllByTestId('graph-list-relation')).toHaveLength(2);
    expect(within(seed).getByText('rel:plan')).toBeInTheDocument();
  });

  it('shows a placeholder as Restricted, never as what it stands for', () => {
    render(<GraphListView view={view()} text={text} onSelect={vi.fn()} />);
    expect(screen.getAllByText(i18n.t('graph.restricted')).length).toBeGreaterThan(0);
    expect(screen.queryByText(/Secret/)).toBeNull();
    expect(screen.getAllByText('Survey notes').length).toBeGreaterThan(0);
    expect(screen.queryByText(/restricted:m1/)).toBeNull();
  });

  it('selects the same key a click on the node would, from a row or from a relation', () => {
    const onSelect = vi.fn();
    render(<GraphListView view={view()} text={text} onSelect={onSelect} />);
    const row = screen.getAllByTestId('graph-list-item').find((i) => i.dataset.key === 'document:a')!;
    fireEvent.click(within(row).getAllByRole('button')[0]);
    expect(onSelect).toHaveBeenLastCalledWith('document:a');
    const seed = screen.getAllByTestId('graph-list-item').find((i) => i.dataset.key === 'feature:s')!;
    fireEvent.click(within(seed).getByRole('button', { name: 'Survey notes' }));
    expect(onSelect).toHaveBeenLastCalledWith('document:a');
  });

  it('marks the selected row as pressed and says when there is nothing to list', () => {
    const { rerender } = render(<GraphListView view={view({ kind: 'node', key: 'document:a' })} text={text} onSelect={vi.fn()} />);
    const row = screen.getAllByTestId('graph-list-item').find((i) => i.dataset.key === 'document:a')!;
    expect(within(row).getAllByTestId('graph-list-select')[0]).toHaveAttribute('aria-pressed', 'true');
    rerender(<GraphListView view={toView(new GraphModel())} text={text} onSelect={vi.fn()} />);
    expect(screen.getByText(i18n.t('graph.list.empty'))).toBeInTheDocument();
  });
});
