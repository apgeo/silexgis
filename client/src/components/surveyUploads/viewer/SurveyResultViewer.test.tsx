// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../../i18n';
import type { SurveyLogFigures } from '../../../api/hooks.ts';
import type { SurveyResultViewerProps } from './SurveyResultViewer.tsx';

vi.mock('../../caveview/SurveyModelTabs.tsx', () => ({
  default: ({ model }: { model: { modelUrl: string } }) => <div data-testid="survey-model-tabs" data-url={model.modelUrl} />,
}));
vi.mock('./SurveyLineMap.tsx', () => ({
  default: ({ layers }: { layers: unknown }) => (
    <div data-testid="survey-viewer-map" data-catalogue={layers === undefined ? 'none' : 'installation'} />
  ),
}));
vi.mock('./SurveyUploadAnalysesView.tsx', () => ({
  default: ({ analyses }: { analyses: unknown }) => (
    <div data-testid="survey-viewer-analyses-stub" data-measured={analyses ? 'yes' : 'no'} />
  ),
}));
const useMapLayers = vi.fn(() => ({ data: [], isPending: false }));
vi.mock('../../../api/hooks.ts', async () => {
  const actual = await vi.importActual<typeof import('../../../api/hooks.ts')>('../../../api/hooks.ts');
  return { ...actual, useMapLayers: () => useMapLayers() };
});
const downloadFile = vi.fn(async (_url: string) => {});
vi.mock('../../../api/download.ts', () => ({ downloadFile: (url: string) => downloadFile(url) }));

const { default: SurveyResultViewer } = await import('./SurveyResultViewer.tsx');

const figures: SurveyLogFigures = {
  outcome: 'succeeded',
  compilerVersion: '6.3.4',
  compilationSeconds: 1,
  errorCount: 0,
  warningCount: 0,
  outputCoordinateSystem: 'EPSG:31700',
  meridianConvergenceDeg: -1.2345,
  declinations: [{ date: '2026.10.25', degrees: 5.5 }],
  hasNoControlPoints: false,
  stationCount: 42,
  shotCount: 57,
  connectedComponents: 1,
  totalLengthM: 312.4,
  totalLengthAdjustedM: 311.95,
  planLengthM: 280.1,
  verticalLengthM: 95.3,
  verticalRange: { lengthM: 61.2, fromStation: '1.17@invented', fromValueM: 1234.5, toStation: '1.23@invented', toValueM: 1173.3 },
  northSouthRange: { lengthM: 120, fromStation: '1.4@invented', fromValueM: 5040123, toStation: '1.9@invented', toValueM: 5040003 },
  eastWestRange: null,
  loopCount: 2,
  averageLoopErrorPercent: 1,
  loops: [
    { relativeErrorPercent: 1.2, absoluteErrorM: 0.3, lengthM: 25, stationCount: 6, errorXM: 0.1, errorYM: 0.2, errorZM: 0.1, stations: '1.1@invented - 1.2@invented - 1.1@invented' },
    { relativeErrorPercent: 0.8, absoluteErrorM: 0.2, lengthM: 25, stationCount: 5, errorXM: 0.1, errorYM: 0.1, errorZM: 0.1, stations: '1.3@invented - 1.4@invented - 1.3@invented' },
  ],
};

function show(over: Partial<SurveyResultViewerProps> = {}) {
  const props: SurveyResultViewerProps = {
    title: 'Invented survey',
    caveName: 'Invented cave',
    version: '2026-10-25T10:15:00Z',
    shareUrl: 'https://silex.example/p/survey/tok/view',
    shareKind: 'public',
    openInApp: null,
    model: { modelUrl: '/f/lox?token=1', name: 'cave', format: 'lox' },
    figures: { data: figures, isPending: false, isError: false },
    analyses: null,
    geo: { kind: 'unplaced' },
    ...over,
  };
  const client = new QueryClient();
  return render(
    <QueryClientProvider client={client}>
      <App>
        <MemoryRouter>
          <SurveyResultViewer {...props} />
        </MemoryRouter>
      </App>
    </QueryClientProvider>,
  );
}

afterEach(() => {
  cleanup();
  downloadFile.mockClear();
  useMapLayers.mockClear();
});

const lineWork = { type: 'FeatureCollection' as const, features: [] };

describe('the page of one upload\'s results', () => {
  it('shows the title, the model, the figures and copies its link', async () => {
    const writeText = vi.fn(async () => {});
    Object.assign(navigator, { clipboard: { writeText } });
    show();

    expect(screen.getByTestId('survey-viewer-title')).toHaveTextContent('Invented survey');
    expect(screen.getByTestId('survey-model-tabs')).toHaveAttribute('data-url', '/f/lox?token=1');
    expect(screen.getByTestId('survey-viewer-total-length')).toHaveTextContent('312.4 m');
    expect(screen.getByText('Compiled')).toBeInTheDocument();
    expect(screen.getByTestId('survey-viewer-share-kind')).toHaveAttribute('data-share-kind', 'public');

    fireEvent.click(screen.getByTestId('survey-viewer-copy-link'));
    await waitFor(() => expect(writeText).toHaveBeenCalledWith('https://silex.example/p/survey/tok/view'));
  });

  it('shows every figure the log states: the stations and values at each extent, the coordinate system and each loop', () => {
    show();
    expect(screen.getByTestId('survey-viewer-vertical-range')).toHaveTextContent('61.2 m');
    expect(screen.getByTestId('survey-viewer-vertical-range')).toHaveTextContent('1.17@invented');
    expect(screen.getByTestId('survey-viewer-vertical-range')).toHaveTextContent('1,234.5');
    expect(screen.getByTestId('survey-viewer-crs')).toHaveTextContent('EPSG:31700');
    expect(screen.getByTestId('survey-viewer-loops')).toHaveTextContent('1.3@invented - 1.4@invented - 1.3@invented');
    expect(screen.queryByTestId('survey-viewer-no-control-points')).toBeNull();
  });

  it('says when the compiler had to invent the fixed point', () => {
    show({ figures: { data: { ...figures, hasNoControlPoints: true }, isPending: false, isError: false } });
    expect(screen.getByTestId('survey-viewer-no-control-points')).toBeInTheDocument();
  });

  it('says a survey that could not be placed has no map', () => {
    show({ geo: { kind: 'unplaced' }, shareKind: 'inApp' });
    expect(screen.getByTestId('survey-viewer-geo-unplaced')).toBeInTheDocument();
    expect(screen.queryByTestId('survey-viewer-map')).toBeNull();
  });

  it('offers the line work in each format and draws the map over the installation background when the survey was placed', async () => {
    show({
      geo: { kind: 'shown', lineWork, downloadUrl: (f) => `/api/v1/survey-uploads/u-1/geodata?format=${f}`, catalogue: true },
      shareKind: 'inApp',
    });
    expect(screen.getByTestId('survey-viewer-map')).toHaveAttribute('data-catalogue', 'installation');
    expect(screen.getByTestId('survey-viewer-geodata-geojson')).toBeInTheDocument();
    expect(screen.getByTestId('survey-viewer-geodata-gpx')).toBeInTheDocument();

    fireEvent.click(screen.getByTestId('survey-viewer-geodata-kml'));
    await waitFor(() => expect(downloadFile).toHaveBeenCalledWith('/api/v1/survey-uploads/u-1/geodata?format=kml'));
  });

  it('draws a public link\'s map without asking for the installation\'s map catalogue', async () => {
    show({
      geo: { kind: 'shown', lineWork, downloadUrl: (f) => `/api/v1/public/survey-uploads/tok/geodata?format=${f}`, catalogue: false },
    });
    expect(screen.getByTestId('survey-viewer-map')).toHaveAttribute('data-catalogue', 'none');
    expect(useMapLayers).not.toHaveBeenCalled();

    fireEvent.click(screen.getByTestId('survey-viewer-geodata-gpx'));
    await waitFor(() => expect(downloadFile).toHaveBeenCalledWith('/api/v1/public/survey-uploads/tok/geodata?format=gpx'));
  });

  it('shows the upload\'s own analyses once the figures have come', () => {
    show({ analyses: { surveyModelId: 'm-1' } as never });
    expect(screen.getByTestId('survey-viewer-analyses-stub')).toHaveAttribute('data-measured', 'yes');
    cleanup();
    show({ figures: { data: undefined, isPending: true, isError: false } });
    expect(screen.queryByTestId('survey-viewer-analyses-stub')).toBeNull();
  });

  it('says when there is no model and no figures', () => {
    show({ model: null, figures: { data: null, isPending: false, isError: false } });
    expect(screen.getByTestId('survey-viewer-no-model')).toBeInTheDocument();
    expect(screen.getByTestId('survey-viewer-no-figures')).toBeInTheDocument();
  });
});
