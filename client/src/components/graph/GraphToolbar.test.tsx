// SPDX-License-Identifier: AGPL-3.0-or-later
import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import i18n from '../../i18n';
import GraphToolbar, { type GraphToolbarProps } from './GraphToolbar.tsx';

const props = (over: Partial<GraphToolbarProps> = {}): GraphToolbarProps => ({
  layout: 'force',
  onLayoutChange: vi.fn(),
  rendererKind: 'g6',
  onRendererChange: vi.fn(),
  onFit: vi.fn(),
  onRefresh: vi.fn(),
  onExpandSelected: vi.fn(),
  onCollapse: vi.fn(),
  canExpand: true,
  canCollapse: true,
  canRefresh: true,
  busy: false,
  ...over,
});

describe('GraphToolbar', () => {
  it('reports each control', () => {
    const p = props();
    render(<GraphToolbar {...p} />);
    fireEvent.click(screen.getByTestId('graph-fit'));
    fireEvent.click(screen.getByTestId('graph-expand'));
    fireEvent.click(screen.getByTestId('graph-collapse'));
    fireEvent.click(screen.getByTestId('graph-refresh'));
    fireEvent.click(screen.getByText(i18n.t('graph.layouts.radial')));
    expect(p.onFit).toHaveBeenCalled();
    expect(p.onExpandSelected).toHaveBeenCalled();
    expect(p.onCollapse).toHaveBeenCalled();
    expect(p.onRefresh).toHaveBeenCalled();
    expect(p.onLayoutChange).toHaveBeenCalledWith('radial');
  });

  it('disables what cannot be done, and offers no renderer switch while there is one renderer', () => {
    render(<GraphToolbar {...props({ canExpand: false, canCollapse: false, canRefresh: false })} />);
    expect(screen.getByTestId('graph-expand')).toBeDisabled();
    expect(screen.getByTestId('graph-collapse')).toBeDisabled();
    expect(screen.getByTestId('graph-refresh')).toBeDisabled();
    expect(screen.queryByText('G6')).toBeNull();
  });
});
