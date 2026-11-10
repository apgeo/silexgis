// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import type { ReactNode } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { SurveyUpload, SurveyUploadPointCloud } from '../../api/hooks.ts';

/**
 * "Make a surface" on a scan's card: offered once the scan is placed, the dialog's profile
 * preselected as it is for an upload, the cave the walls go to (the scan's own unless another is
 * picked), the request sent, and the result pointed at once the run has saved it. Every id, name
 * and value below is invented.
 */

const post = vi.fn();
vi.mock('../../api/client.ts', async (original) => ({
  ...(await original<typeof import('../../api/client.ts')>()),
  api: { POST: (...args: unknown[]) => post(...args) },
}));

let placed = true;
const profiles = [
  { id: 'p-defaults', stepId: 'mesh.surface', name: 'Surface defaults', description: 'Own values.', scope: 'installation', builtIn: true, values: {} },
  { id: 'p-quick', stepId: 'mesh.surface', name: 'Quick surface', description: 'Coarser and faster.', scope: 'installation', builtIn: true, values: { voxelCm: 5 } },
];
let remembered: { stepId: string; profileId: string }[] = [];

vi.mock('../../api/hooks.ts', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../api/hooks.ts')>()),
  useSurveyModels: () => ({
    isPending: false,
    data: [{ id: 'm1', anchorLongitude: placed ? 25.1 : null, anchorLatitude: placed ? 45.2 : null }],
  }),
  useProcessingProfiles: () => ({ data: profiles }),
  useRememberedProcessingProfiles: () => ({ data: remembered }),
  useScanStepParameterSchema: () => ({
    data: {
      schema: { type: 'object', properties: { voxelCm: { type: 'number', default: 2 } } },
      defaults: { voxelCm: 2 },
    },
  }),
}));

// The cave search asks the server; here it offers one invented cave to pick.
vi.mock('../selector/ObjectSelector.tsx', () => ({
  default: ({ onPick, exclude }: { onPick?: (hit: { id: string; title: string } | null) => void; exclude?: string[] }) => (
    <button type="button" data-testid="pick-cave" data-exclude={(exclude ?? []).join(',')} onClick={() => onPick?.({ id: 'c2', title: 'Invented other cave' })}>
      pick
    </button>
  ),
}));

const { default: ScanSurfacePanel } = await import('./ScanSurfacePanel.tsx');

function cloud(): SurveyUploadPointCloud {
  return { modelId: 'm1', tilesetUrl: '/tiles/tileset.json' } as SurveyUploadPointCloud;
}

function upload(status: SurveyUpload['status'] = 'succeeded') {
  return { id: 'u1', status, cave: { id: 'c1', name: 'Invented cave' } as SurveyUpload['cave'], pointCloud: cloud() };
}

function tree(client: QueryClient, children: ReactNode) {
  return (
    <QueryClientProvider client={client}>
      <MemoryRouter>
        <App>{children}</App>
      </MemoryRouter>
    </QueryClientProvider>
  );
}

/** Renders the panel; `again` draws it anew with the upload as the page would after a poll. */
function wrap(children: ReactNode) {
  const client = new QueryClient();
  const view = render(tree(client, children));
  return { ...view, again: (next: ReactNode) => view.rerender(tree(client, next)) };
}

const queued = {
  data: { id: 'u1', status: 'queued', cave: { id: 'c1', name: 'Invented cave' } },
  response: new Response(null, { status: 202 }),
};

function sendButton() {
  return within(screen.getByRole('dialog')).getByRole('button', { name: 'Make the surface' });
}

beforeEach(() => {
  post.mockReset();
  placed = true;
  remembered = [];
});
afterEach(cleanup);

describe('making a surface of a scan', () => {
  it('is not offered before the scan is tiled', () => {
    const { container } = wrap(<ScanSurfacePanel upload={{ ...upload(), pointCloud: { ...cloud(), tilesetUrl: null } }} />);
    expect(container.querySelector('[data-testid="scan-surface"]')).toBeNull();
  });

  it('asks for the scan to be placed first, and cannot be started until it is', () => {
    placed = false;
    wrap(<ScanSurfacePanel upload={upload()} />);
    expect(screen.getByTestId('scan-surface-unplaced')).toBeTruthy();
    expect(screen.getByTestId('scan-surface-make')).toHaveProperty('disabled', true);
  });

  it('cannot be started while the scan’s run is going', () => {
    wrap(<ScanSurfacePanel upload={upload('running')} />);
    expect(screen.getByTestId('scan-surface-make')).toHaveProperty('disabled', true);
  });

  it('sends the remembered profile for the scan’s own cave, and says where the walls will go', async () => {
    remembered = [{ stepId: 'mesh.surface', profileId: 'p-quick' }];
    post.mockResolvedValue(queued);
    const { again } = wrap(<ScanSurfacePanel upload={upload()} />);

    fireEvent.click(screen.getByTestId('scan-surface-make'));
    expect(within(screen.getByTestId('scan-surface-profile')).getByText(/Quick surface/)).toBeTruthy();
    expect(screen.getByText('Coarser and faster.')).toBeTruthy();
    fireEvent.click(sendButton());

    await waitFor(() => expect(post).toHaveBeenCalledTimes(1));
    const [path, init] = post.mock.calls[0] as [string, { params: { path: { id: string } }; body: unknown }];
    expect(path).toBe('/api/v1/survey-uploads/{id}/surface');
    expect(init.params.path.id).toBe('u1');
    expect(init.body).toEqual({ caveId: 'c1', profileId: 'p-quick' });

    again(<ScanSurfacePanel upload={upload('running')} />);
    await waitFor(() => expect(screen.getByTestId('scan-surface-running').textContent).toContain('Invented cave'));
  });

  it('saves for another cave once one is picked, and links to it when the run has saved the walls', async () => {
    post.mockResolvedValue(queued);
    const view = wrap(<ScanSurfacePanel upload={upload()} />);

    fireEvent.click(screen.getByTestId('scan-surface-make'));
    fireEvent.click(within(screen.getByTestId('scan-surface-target')).getByText('Another cave'));
    expect(sendButton()).toHaveProperty('disabled', true);
    // The scan's own cave is the other choice already, so the search leaves it out.
    expect(screen.getByTestId('pick-cave').getAttribute('data-exclude')).toBe('c1');
    fireEvent.click(screen.getByTestId('pick-cave'));
    fireEvent.click(sendButton());

    await waitFor(() => expect(post).toHaveBeenCalledTimes(1));
    // No profile picked and none remembered: the built-in that changes nothing is preselected.
    expect((post.mock.calls[0]![1] as { body: unknown }).body).toEqual({ caveId: 'c2', profileId: 'p-defaults' });

    view.again(<ScanSurfacePanel upload={upload('succeeded')} />);
    const saved = await screen.findByTestId('scan-surface-saved');
    expect(saved.textContent).toContain('Invented other cave');
    expect(screen.getByTestId('scan-surface-open-cave').getAttribute('href')).toBe('/caves/c2');
  });

  it('says why the server refused, in its own words for the code', async () => {
    post.mockResolvedValue({
      error: { code: 'survey_upload.surface_needs_placement' },
      response: new Response(null, { status: 409 }),
    });
    wrap(<ScanSurfacePanel upload={upload()} />);

    fireEvent.click(screen.getByTestId('scan-surface-make'));
    fireEvent.click(sendButton());

    expect(await screen.findByText('Place the scan first: its surface is saved standing where the scan stands.', { selector: '.ant-message *' })).toBeTruthy();
    expect(screen.queryByTestId('scan-surface-running')).toBeNull();
  });
});
