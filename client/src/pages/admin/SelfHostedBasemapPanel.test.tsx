// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { App as AntApp } from 'antd';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { BasemapDownload, BasemapStatus } from '../../api/hooks/basemap.ts';

const { statusSpy, estimateSpy, startSpy, cancelSpy } = vi.hoisted(() => ({
  statusSpy: vi.fn(),
  estimateSpy: vi.fn(),
  startSpy: vi.fn(),
  cancelSpy: vi.fn(),
}));

vi.mock('../../api/hooks/basemap.ts', () => ({
  isUnderway: (d: BasemapDownload | null | undefined) => d?.state === 'queued' || d?.state === 'running',
  useBasemapStatus: () => statusSpy(),
  useBasemapBuilds: () => ({ data: [], isFetching: false, isError: false }),
  useEstimateBasemap: () => ({ mutateAsync: estimateSpy, isPending: false }),
  useStartBasemapDownload: () => ({ mutateAsync: startSpy, isPending: false }),
  useCancelBasemapDownload: () => ({ mutateAsync: cancelSpy, isPending: false }),
}));

const { default: SelfHostedBasemapPanel } = await import('./SelfHostedBasemapPanel.tsx');
const { formatBytes } = await import('./basemapSize.ts');

const APUSENI = { west: 22.2, south: 46.2, east: 23.4, north: 47.0 };

function status(overrides: Partial<BasemapStatus> = {}): BasemapStatus {
  return {
    selfHosted: true,
    current: null,
    download: null,
    entries: ['Self-hosted basemap (light)'],
    presets: [
      { key: 'romania', region: { west: 20.2, south: 43.6, east: 29.8, north: 48.3 } },
      { key: 'apuseni', region: APUSENI },
    ],
    maxZoom: 15,
    maxBytes: 5_000_000_000,
    source: 'https://build.protomaps.com/{build}.pmtiles',
    ...overrides,
  };
}

function download(overrides: Partial<BasemapDownload> = {}): BasemapDownload {
  return {
    id: 7,
    state: 'running',
    percent: 40,
    region: APUSENI,
    maxZoom: 15,
    build: '20261024',
    errorCode: null,
    error: null,
    createdAt: '2026-10-24T10:00:00Z',
    startedAt: '2026-10-24T10:00:01Z',
    completedAt: null,
    ...overrides,
  };
}

function answer(value: BasemapStatus) {
  statusSpy.mockReturnValue({ data: value, isError: false });
}

function show() {
  return render(
    <QueryClientProvider client={new QueryClient()}>
      <AntApp>
        <SelfHostedBasemapPanel />
      </AntApp>
    </QueryClientProvider>,
  );
}

afterEach(cleanup);
beforeEach(() => {
  statusSpy.mockReset();
  estimateSpy.mockReset().mockResolvedValue({ build: '20261024', bytes: 36_000_000, freeBytes: 90_000_000_000, refusalCode: null });
  startSpy.mockReset().mockResolvedValue(download({ state: 'queued', percent: 0, build: null }));
  cancelSpy.mockReset().mockResolvedValue(download({ state: 'cancelled' }));
  answer(status());
});

describe('the installation’s own basemap, as an administrator manages it', () => {
  it('says that nothing is served yet and which backgrounds wait for a download', () => {
    show();

    expect(screen.getByTestId('basemap-none')).toHaveTextContent('No basemap has been downloaded yet.');
    expect(screen.getByTestId('basemap-none')).toHaveTextContent('Self-hosted basemap (light)');
    expect(screen.queryByTestId('basemap-current')).toBeNull();
  });

  it('offers no download when self-hosting is switched off, and says why', () => {
    answer(status({ selfHosted: false }));
    show();

    expect(screen.getByTestId('basemap-off')).toBeInTheDocument();
    expect(screen.queryByTestId('basemap-form')).toBeNull();
    expect(screen.queryByTestId('basemap-none')).toBeNull();
  });

  it('weighs the chosen region before anything is downloaded, and downloads it when asked', async () => {
    show();

    fireEvent.click(screen.getByTestId('basemap-estimate'));
    await waitFor(() =>
      expect(screen.getByTestId('basemap-estimate-result')).toHaveTextContent('About 36.0 MB, from build 20261024.'),
    );
    // The karst region by default, to the last level, at the latest build.
    expect(estimateSpy).toHaveBeenCalledWith({ region: APUSENI, maxZoom: 15, build: null });
    expect(startSpy).not.toHaveBeenCalled();

    fireEvent.click(screen.getByTestId('basemap-download'));
    await waitFor(() => expect(startSpy).toHaveBeenCalledWith({ region: APUSENI, maxZoom: 15, build: null }));
  });

  it('will not download a region the installation would refuse, and says why', async () => {
    estimateSpy.mockResolvedValue({ build: '20261024', bytes: 49_000_000_000, freeBytes: null, refusalCode: 'basemap.too_large' });
    show();

    fireEvent.click(screen.getByTestId('basemap-estimate'));
    await waitFor(() => expect(screen.getByTestId('basemap-estimate-result')).toHaveTextContent('Too large for this installation'));
    expect(screen.getByTestId('basemap-download')).toBeDisabled();
  });

  it('weighs a box of the administrator’s own', async () => {
    show();

    fireEvent.click(screen.getByText('A box of my own'));
    fireEvent.change(screen.getByLabelText('North'), { target: { value: '46.8' } });
    fireEvent.blur(screen.getByLabelText('North'));
    fireEvent.click(screen.getByTestId('basemap-estimate'));

    await waitFor(() => expect(estimateSpy).toHaveBeenCalledWith({ region: { ...APUSENI, north: 46.8 }, maxZoom: 15, build: null }));
  });

  it('shows how far a download has got, can call it off, and takes no second one meanwhile', async () => {
    answer(status({ download: download() }));
    show();

    expect(screen.getByTestId('basemap-download-tag')).toHaveTextContent('running');
    expect(screen.getByTestId('basemap-progress')).toBeInTheDocument();
    expect(screen.getByTestId('basemap-download')).toBeDisabled();
    expect(screen.getByTestId('basemap-estimate')).toBeDisabled();

    fireEvent.click(screen.getByTestId('basemap-cancel'));
    await waitFor(() => expect(cancelSpy).toHaveBeenCalledWith(7));
  });

  it('says what is served now, and asks before replacing it', async () => {
    answer(
      status({
        current: {
          sourceId: 'basemap-20261024-20261024112444',
          build: '20261024',
          extractedAt: '2026-10-24T11:24:44Z',
          sizeBytes: 35_865_983,
          region: APUSENI,
          maxZoom: 15,
        },
        download: download({ state: 'succeeded', percent: 100 }),
      }),
    );
    show();

    expect(screen.getByTestId('basemap-current')).toHaveTextContent('20261024');
    expect(screen.getByTestId('basemap-current')).toHaveTextContent('35.9 MB');
    fireEvent.click(screen.getByTestId('basemap-download'));
    expect((await screen.findAllByText('Replace the basemap being served?')).length).toBeGreaterThan(0);
    expect(startSpy).not.toHaveBeenCalled();
  });

  it('says why a download failed, in its own words', () => {
    answer(status({ download: download({ state: 'failed', errorCode: 'basemap.disk_full', error: 'basemap.disk_full: …' }) }));
    show();

    expect(screen.getByTestId('basemap-error')).toHaveTextContent('There is not enough room on the disk');
  });

  it('writes sizes the way the extracting tool reports them', () => {
    expect(formatBytes(36_000_000)).toBe('36.0 MB');
    expect(formatBytes(1_300_000_000)).toBe('1.3 GB');
    expect(formatBytes(640_000)).toBe('640 kB');
  });
});
