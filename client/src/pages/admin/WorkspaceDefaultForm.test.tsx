// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { App as AntApp } from 'antd';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { AdminSettings } from '../../api/hooks.ts';

const put = vi.fn();
vi.mock('../../api/client.ts', () => ({ api: { PUT: (...args: unknown[]) => put(...args) } }));

const { default: WorkspaceDefaultForm } = await import('./WorkspaceDefaultForm.tsx');

afterEach(() => {
  cleanup();
  put.mockReset();
});

function renderForm(panelDefaults: string, onSaved = vi.fn()) {
  const settings = { interface: { panelDefaults } } as unknown as AdminSettings;
  render(
    <QueryClientProvider client={new QueryClient()}>
      <AntApp>
        <WorkspaceDefaultForm settings={settings} onSaved={onSaved} />
      </AntApp>
    </QueryClientProvider>,
  );
  return onSaved;
}

describe('the starting arrangement form', () => {
  it('writes the chosen preset into the starting arrangement and keeps the panels\' defaults', async () => {
    put.mockResolvedValue({ data: { interface: { panelDefaults: '{}' } }, error: undefined });
    const onSaved = renderForm('{"main":{"width":24}}');
    fireEvent.mouseDown(screen.getByRole('combobox'));
    fireEvent.click(await screen.findByText('Trip planning'));
    fireEvent.click(screen.getByTestId('workspace-default-save'));
    await waitFor(() => expect(onSaved).toHaveBeenCalled());
    const [path, { body }] = put.mock.calls[0] as [string, { body: { panelDefaults: string } }];
    expect(path).toBe('/api/v1/admin/settings/interface');
    expect(JSON.parse(body.panelDefaults)).toEqual({ main: { width: 24 }, workspacePreset: 'trip' });
  });

  it('does not offer the arrangement that needs a second window, and starts on the one stored', async () => {
    renderForm('{"workspacePreset":"map3d"}');
    expect(screen.getByTitle('Map + 3D')).toBeTruthy();
    fireEvent.mouseDown(screen.getByRole('combobox'));
    await screen.findAllByText('Survey review');
    expect(screen.queryByText('Two screens')).toBeNull();
  });

  it('says so, and offers nothing to save, when the stored arrangement cannot be read', () => {
    renderForm('[1,2,3]');
    expect(screen.getByText(/could not be read/)).toBeTruthy();
    expect(screen.queryByTestId('workspace-default-save')).toBeNull();
  });
});
