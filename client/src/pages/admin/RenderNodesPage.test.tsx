// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { App } from 'antd';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { renderNodeServerUrl, type RenderNode, type RenderNodeIssued } from '../../api/hooks/flybyRenders.ts';
import RenderNodesPage from './RenderNodesPage.tsx';

// The administrator's list of render nodes: issuing one shows its token once, the list carries the
// token's first characters only, and a node is withdrawn after a confirmation.

const state = {
  nodes: [] as RenderNode[],
  issued: [] as string[],
  revoked: [] as string[],
};
const ISSUED: RenderNodeIssued = {
  node: {
    id: 'n2',
    name: 'invented-gpu',
    tokenPrefix: 'sgn_ABCDEFGH',
    createdAt: '2026-11-26T10:00:00Z',
    revokedAt: null,
    lastSeenAt: null,
    online: false,
    worker: null,
    device: null,
    blenderVersions: null,
    freeDiskBytes: null,
    skipReason: null,
    skippedRenders: null,
    skipNeedsBytes: null,
  },
  token: 'sgn_ABCDEFGH-an-invented-token-for-the-test-only',
};
vi.mock('../../api/hooks/flybyRenders.ts', async (original) => ({
  ...(await original<typeof import('../../api/hooks/flybyRenders.ts')>()),
  useRenderNodes: () => ({ isPending: false, isError: false, data: state.nodes }),
  useIssueRenderNode: () => ({
    isPending: false,
    mutateAsync: async (name: string) => {
      state.issued.push(name);
      return ISSUED;
    },
  }),
  useRevokeRenderNode: () => ({
    isPending: false,
    variables: undefined,
    mutateAsync: async (id: string) => {
      state.revoked.push(id);
    },
  }),
}));

beforeEach(() => {
  state.nodes = [
    {
      id: 'n1',
      name: 'club-workstation',
      tokenPrefix: 'sgn_QWERTYUI',
      createdAt: '2026-11-20T10:00:00Z',
      revokedAt: null,
      lastSeenAt: '2026-11-26T09:59:00Z',
      online: true,
      worker: '0.4.0.0',
      device: 'GPU (OPTIX)',
      blenderVersions: '4.5.16',
      freeDiskBytes: 1_000_000,
      skipReason: null,
      skippedRenders: null,
      skipNeedsBytes: null,
    },
  ];
  state.issued = [];
  state.revoked = [];
});

afterEach(() => {
  cleanup();
});

function show() {
  render(
    <App>
      <RenderNodesPage />
    </App>,
  );
}

describe('the render nodes page', () => {
  it('lists each node by its token prefix and whether it is calling, never by its token', () => {
    show();
    const row = within(screen.getByTestId('render-nodes-table')).getByText('club-workstation').closest('tr')!;
    expect(row).toHaveTextContent('sgn_QWERTYUI…');
    expect(row).toHaveTextContent('Waiting for work');
    expect(row).toHaveTextContent('GPU (OPTIX) · Blender 4.5.16 · therion-cli 0.4.0.0');
  });

  it('issues a node and shows its token once, with the command that starts the worker', async () => {
    show();
    fireEvent.change(screen.getByTestId('render-node-name'), { target: { value: '  invented-gpu ' } });
    fireEvent.click(screen.getByTestId('render-node-issue'));
    await waitFor(() => expect(screen.getByTestId('render-node-issued')).toBeInTheDocument());
    expect(state.issued).toEqual(['invented-gpu']);
    expect(screen.getByTestId('render-node-token')).toHaveTextContent(ISSUED.token);
    expect(screen.getByTestId('render-node-issued')).toHaveTextContent(
      `therion-cli render-worker --server ${renderNodeServerUrl(window.location.origin)} --token-file <file>`,
    );
  });

  it('withdraws a node after asking', async () => {
    show();
    fireEvent.click(screen.getByTestId('render-node-revoke-club-workstation'));
    const asked = (await screen.findByText(/Withdraw “club-workstation”\?/)).closest('.ant-popconfirm') as HTMLElement;
    fireEvent.click(within(asked).getByRole('button', { name: 'Withdraw' }));
    await waitFor(() => expect(state.revoked).toEqual(['n1']));
  });

  it("warns that a node receives every account's bundles, without the protected caves", () => {
    show();
    expect(screen.getByTestId('render-node-protected-warning')).toHaveTextContent(
      'A render node receives the bundles of every account that renders on the server',
    );
  });

  it('says why a node left waiting renders to other nodes', () => {
    state.nodes = [{ ...state.nodes[0]!, skipReason: 'disk', skippedRenders: 2, skipNeedsBytes: 12 * 1024 ** 3, freeDiskBytes: 5 * 1024 ** 3 }];
    show();
    expect(screen.getByTestId('render-node-skipped-club-workstation')).toHaveTextContent(
      'Left 2 waiting renders to other nodes: one needs about 12.0 GB of disk, and this node said it has 5.0 GB free.',
    );
    cleanup();

    state.nodes = [{ ...state.nodes[0]!, skipReason: 'container', skippedRenders: 1, skipNeedsBytes: null }];
    show();
    expect(screen.getByTestId('render-node-skipped-club-workstation')).toHaveTextContent(
      'Left 1 waiting render to other nodes: its film format is one this node does not write.',
    );
  });

  it('gives the worker the render-node routes of this server as a folder', () => {
    expect(renderNodeServerUrl('https://caves.example.org/')).toBe('https://caves.example.org/api/v1/render-nodes/');
  });
});
