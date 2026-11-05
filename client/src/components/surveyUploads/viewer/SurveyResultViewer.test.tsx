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
vi.mock('./SurveyLineMap.tsx', () => ({ default: () => <div data-testid="survey-viewer-map" /> }));
vi.mock('../../../api/hooks.ts', async () => {
  const actual = await vi.importActual<typeof import('../../../api/hooks.ts')>('../../../api/hooks.ts');
  return { ...actual, useMapLayers: () => ({ data: [], isPending: false }) };
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
  stationCount: 42,
  shotCount: 57,
  connectedComponents: 1,
  totalLengthM: 312.4,
  totalLengthAdjustedM: 311.95,
  planLengthM: 280.1,
  verticalLengthM: 95.3,
  verticalRangeM: 61.2,
  northSouthRangeM: 120,
  eastWestRangeM: 80.5,
  loopCount: 2,
  averageLoopErrorPercent: 1,
  loops: [
    { relativeErrorPercent: 1.2, absoluteErrorM: 0.3, lengthM: 25, stationCount: 6 },
    { relativeErrorPercent: 0.8, absoluteErrorM: 0.2, lengthM: 25, stationCount: 5 },
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
    geo: { kind: 'withheld' },
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
});

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

  it('says a public link carries no map, and draws none', () => {
    show({ geo: { kind: 'withheld' } });
    expect(screen.getByTestId('survey-viewer-geo-withheld')).toBeInTheDocument();
    expect(screen.queryByTestId('survey-viewer-map')).toBeNull();
    expect(screen.queryByTestId('survey-viewer-geodata-kml')).toBeNull();
  });

  it('says a survey that could not be placed has no map', () => {
    show({ geo: { kind: 'unplaced' }, shareKind: 'inApp' });
    expect(screen.getByTestId('survey-viewer-geo-unplaced')).toBeInTheDocument();
    expect(screen.queryByTestId('survey-viewer-map')).toBeNull();
  });

  it('offers the line work in each format and draws the map when the survey was placed', async () => {
    show({ geo: { kind: 'shown', uploadId: 'u-1', lineWork: { type: 'FeatureCollection', features: [] } }, shareKind: 'inApp' });
    expect(screen.getByTestId('survey-viewer-map')).toBeInTheDocument();
    expect(screen.getByTestId('survey-viewer-geodata-geojson')).toBeInTheDocument();
    expect(screen.getByTestId('survey-viewer-geodata-gpx')).toBeInTheDocument();

    fireEvent.click(screen.getByTestId('survey-viewer-geodata-kml'));
    await waitFor(() => expect(downloadFile).toHaveBeenCalledWith('/api/v1/survey-uploads/u-1/geodata?format=kml'));
  });

  it('says when there is no model and no figures', () => {
    show({ model: null, figures: { data: null, isPending: false, isError: false } });
    expect(screen.getByTestId('survey-viewer-no-model')).toBeInTheDocument();
    expect(screen.getByTestId('survey-viewer-no-figures')).toBeInTheDocument();
  });
});
