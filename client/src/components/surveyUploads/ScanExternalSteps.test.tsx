// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import type { ReactNode } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { externalOutputName } from './externalOutputName.ts';

/**
 * A step done on another computer, from the card that offers it: which step, the download of what
 * it reads, and each product sent back under the name the step takes it under — whatever it was
 * called on the desktop. And the round trip of a finished scan: its session refined elsewhere and
 * sent back as the scan's next version. Every id, name and file below is invented.
 */

const put = vi.fn();
const post = vi.fn();
const download = vi.fn();

vi.mock('../../api/client.ts', async (original) => ({
  ...(await original<typeof import('../../api/client.ts')>()),
  api: {
    PUT: (...args: unknown[]) => put(...args),
    POST: (...args: unknown[]) => post(...args),
  },
}));

vi.mock('../../api/download.ts', async (original) => ({
  ...(await original<typeof import('../../api/download.ts')>()),
  downloadFile: (...args: unknown[]) => download(...args),
}));

const { ExternalStepPanel, RefineOnDesktopPanel } = await import('./ScanExternalSteps.tsx');

function wrap(children: ReactNode) {
  return render(
    <QueryClientProvider client={new QueryClient()}>
      <MemoryRouter>
        <App>{children}</App>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

const answer = (status: string) => ({
  data: { id: 'u1', status, cave: { id: 'c1' }, statusPageUrl: '/caves/c1/surveys/s1/uploads/u2' },
  response: new Response(null, { status: 200 }),
});

function chooseFile(testId: string, file: File) {
  const button = screen.getByTestId(testId);
  const input = button.closest('.ant-upload')!.querySelector('input[type=file]') as HTMLInputElement;
  fireEvent.change(input, { target: { files: [file] } });
}

beforeEach(() => {
  put.mockReset();
  post.mockReset();
  download.mockReset();
});
afterEach(cleanup);

describe('the name a product is sent back under', () => {
  it('is the step’s name with the chosen file’s extension, else the step’s first name', () => {
    expect(externalOutputName(['session.zip'], 'My refined run.ZIP')).toBe('session.zip');
    expect(externalOutputName(['cloud.laz', 'cloud.las'], 'export.las')).toBe('cloud.las');
    expect(externalOutputName(['cloud.laz', 'cloud.las'], 'export')).toBe('cloud.laz');
  });
});

describe('the wait for a step done on another computer', () => {
  const external = {
    stepId: 'hdmapping.odometry',
    stepName: 'LiDAR odometry',
    tool: 'hdmapping',
    outputs: [{ names: ['session.zip'], received: false }],
  };

  it('names the step, downloads the bundle from the upload’s own route, and sends the file under the step’s name', async () => {
    put.mockResolvedValue(answer('queued'));
    wrap(<ExternalStepPanel id="u1" external={external} />);

    expect(screen.getByTestId('scan-external-wait')).toHaveTextContent('LiDAR odometry');
    fireEvent.click(screen.getByTestId('scan-external-bundle'));
    await waitFor(() => expect(download).toHaveBeenCalledWith('/api/v1/survey-uploads/u1/external/bundle'));

    chooseFile('scan-external-send-session.zip', new File(['zip'], 'desktop run.zip'));
    await waitFor(() => expect(put).toHaveBeenCalledTimes(1));
    const [route, options] = put.mock.calls[0] as [string, { params: { path: { id: string; name: string } } }];
    expect(route).toBe('/api/v1/survey-uploads/{id}/external/{name}');
    expect(options.params.path).toEqual({ id: 'u1', name: 'session.zip' });
  });

  it('marks a product already received and offers no picker for it', () => {
    wrap(<ExternalStepPanel id="u1" external={{ ...external, outputs: [{ names: ['session.zip'], received: true }] }} />);
    expect(screen.getByTestId('scan-external-received-session.zip')).toBeInTheDocument();
    expect(screen.queryByTestId('scan-external-send-session.zip')).toBeNull();
  });
});

describe('refining a finished scan on a desktop', () => {
  it('offers the session’s signed address and sends the refined one as the next version of the scan’s survey', async () => {
    post
      .mockResolvedValueOnce({ data: { id: 'u2' }, response: new Response(null, { status: 200 }) })
      .mockResolvedValueOnce(answer('queued'));
    put.mockResolvedValue({ data: {}, response: new Response(null, { status: 200 }) });
    wrap(
      <RefineOnDesktopPanel
        upload={{ cave: { id: 'c1', name: 'Invented cave', url: '/caves/c1' }, caveSurvey: { id: 's1', title: 'Scan one' } }}
        sessionUrl="/files/session-signed"
      />,
    );

    expect(screen.getByTestId('scan-refine-session')).toHaveAttribute('href', '/files/session-signed');
    chooseFile('scan-refine-send', new File(['zip'], 'refined.zip'));
    await waitFor(() => expect(post).toHaveBeenCalledTimes(2));
    const [, opened] = post.mock.calls[0] as [string, { body: Record<string, unknown> }];
    expect(opened.body).toMatchObject({
      source: 'mandeye',
      cave: null,
      caveSurvey: { id: 's1' },
      files: [{ name: 'session.zip', size: 3, role: 'archive' }],
    });
    const [, sent] = put.mock.calls[0] as [string, { params: { path: { id: string; name: string } } }];
    expect(sent.params.path).toEqual({ id: 'u2', name: 'session.zip' });
  });
});
