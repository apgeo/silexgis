// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { App as AntApp } from 'antd';
import { afterEach, describe, expect, it, vi } from 'vitest';
import i18n from '../../i18n';
import type { AdminSettings } from '../../api/hooks.ts';

const put = vi.fn();
// Only the transport is replaced: the refusal type the wording is chosen by stays the real one.
vi.mock('../../api/client.ts', async (original) => ({
  ...(await original<typeof import('../../api/client.ts')>()),
  api: { PUT: (...args: unknown[]) => put(...args) },
}));

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

describe('WorkspaceDefaultForm when the save does not go through', () => {
  it.each([
    ['refused as invalid', () => put.mockResolvedValue({ data: undefined, error: { code: 'validation.failed' } }), 'admin.settingsProblems.invalid'],
    ['refused for want of the right', () => put.mockResolvedValue({ data: undefined, error: { code: 'access.forbidden' } }), 'admin.settingsProblems.forbidden'],
    ['never answered', () => put.mockRejectedValue(new TypeError('Failed to fetch')), 'common.saveFailed'],
  ])('says why when it is %s, and does not report it saved', async (_how, fail, wording) => {
    fail();
    const onSaved = renderForm('{"main":{"width":24}}');

    fireEvent.click(screen.getByTestId('workspace-default-save'));

    expect(await screen.findByText(i18n.t(wording))).toBeInTheDocument();
    expect(onSaved).not.toHaveBeenCalled();
    expect(screen.queryByText(i18n.t('common.saved'))).toBeNull();
  });
});
