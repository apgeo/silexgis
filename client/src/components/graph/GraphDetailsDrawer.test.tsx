// SPDX-License-Identifier: AGPL-3.0-or-later
import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { count, wireAnswer, wireEdge, wireLink, wireNode, wirePlaceholder } from '../../graph/graphFixtures.ts';
import { GraphModel } from '../../graph/graphModel.ts';
import { toView } from '../../graph/view.ts';
import GraphDetailsDrawer from './GraphDetailsDrawer.tsx';
import type { GraphText } from './useGraphPresentation.ts';

const text: GraphText = {
  labels: { node: () => '', edge: () => '' },
  node: (vn) => vn.node?.title ?? 'Restricted',
  relation: (edge) => edge.relation,
  categoryName: (c) => `cat:${c}`,
  typeName: (ty) => `type:${ty}`,
};

const S = wireNode('feature', 's', 'Cave S', { route: '/caves/s', subtitle: 'invented' });
const D = wireNode('document', 'd', 'Notes D');

function show(model: GraphModel, selectKey: string, onRemove = vi.fn(), onSelect = vi.fn()) {
  const view = toView(model, undefined, { kind: 'node', key: selectKey });
  render(
    <MemoryRouter>
      <GraphDetailsDrawer open view={view} text={text} onClose={() => {}} onSelect={onSelect} onRemove={onRemove} />
    </MemoryRouter>,
  );
  return { onRemove, onSelect };
}

describe('GraphDetailsDrawer', () => {
  it('shows an item with its page link and relations grouped by category, and can remove a non-seed item', () => {
    const model = new GraphModel();
    model.merge(wireAnswer(S, [{ nodes: [D], edges: [wireEdge('attachment', 'plan', S.key, D.key)] }], [count('attachment', 1)]), {});
    const { onSelect, onRemove } = show(model, D.key);
    expect(screen.getByTestId('graph-details-node')).toBeInTheDocument();
    expect(screen.queryByTestId('graph-details-open')).toBeNull(); // the document fixture has no route
    expect(screen.getByTestId('graph-details-category')).toHaveTextContent('cat:attachment');
    fireEvent.click(screen.getByRole('button', { name: 'Cave S' }));
    expect(onSelect).toHaveBeenCalledWith(S.key);
    fireEvent.click(screen.getByTestId('graph-details-remove'));
    expect(onRemove).toHaveBeenCalledWith(D.key);
  });

  it('links to the item page and offers no removal for the starting item', () => {
    const model = new GraphModel();
    model.merge(wireAnswer(S, [], []), {});
    show(model, S.key);
    expect(screen.getByTestId('graph-details-open')).toHaveAttribute('href', '/caves/s');
    expect(screen.queryByTestId('graph-details-remove')).toBeNull();
  });

  it('shows the members of a hub and a link to the link page', () => {
    const model = new GraphModel();
    const A = wireNode('feature', 'a', 'Cave A');
    const link = wireLink('1', 'related-to', false, [{ node: S }, { node: D }, { node: A }]);
    model.merge(wireAnswer(S, [link], [count('resLink', 1)]), {});
    show(model, 'resLink:1');
    expect(screen.getByTestId('graph-details-members')).toHaveTextContent('Notes D');
    expect(screen.getByTestId('graph-details-members')).toHaveTextContent('Cave A');
    expect(screen.getByTestId('graph-details-link-page')).toHaveAttribute('href', '/links/L1');
  });

  it('says only "restricted" for a placeholder and offers no action', () => {
    const model = new GraphModel();
    const P = wirePlaceholder('m1');
    model.merge(wireAnswer(S, [{ nodes: [P], edges: [wireEdge('featureLink', 'related', S.key, P.key)] }], [count('featureLink', 1)]), {});
    show(model, P.key);
    expect(screen.getByTestId('graph-details-restricted')).toBeInTheDocument();
    expect(screen.queryByTestId('graph-details-open')).toBeNull();
    expect(screen.queryByTestId('graph-details-remove')).toBeNull();
    expect(screen.queryByTestId('graph-details-category')).toBeNull();
  });
});
