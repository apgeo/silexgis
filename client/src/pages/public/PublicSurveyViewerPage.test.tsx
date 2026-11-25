// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ReactNode } from 'react';
import { App } from 'antd';
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import i18n from '../../i18n';
import { ApiError } from '../../api/client.ts';
import { PUBLIC_LINK_SIGN_IN_REQUIRED } from '../../api/hooks.ts';

/**
 * One upload's results behind its public link: what a wrong, withdrawn or members-only link says,
 * what the map pane is told for each state of the line work, and what a signed-out and a
 * signed-in reader are offered. The viewer itself is somebody else's to test; here it is a stub
 * that shows what it was handed.
 */

const t = i18n.getFixedT('en');

let upload: { data?: unknown; error?: unknown; isPending: boolean; refetch: ReturnType<typeof vi.fn> };
let figures: { data?: unknown; isPending: boolean; isError: boolean };
let lineWork: { data?: unknown; isPending: boolean; isError: boolean };
let inApp: { data?: unknown };
let user: unknown;
const signIn = vi.fn();
const viewerProps = vi.fn();

vi.mock('../../api/hooks.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/hooks.ts')>('../../api/hooks.ts');
  return {
    ...actual,
    usePublicSurveyUpload: () => upload,
    usePublicSurveyUploadFigures: (_token: string, enabled: boolean) => (enabled ? figures : { isPending: true, isError: false }),
    usePublicSurveyUploadLineWork: (_token: string, enabled: boolean) => (enabled ? lineWork : { isPending: true, isError: false }),
    useSurveyUploadByPublicLink: () => inApp,
  };
});
vi.mock('../../auth/auth.tsx', () => ({ useAuth: () => ({ user, signIn }) }));
vi.mock('../../components/surveyUploads/viewer/SurveyResultViewer.tsx', () => ({
  default: (props: { title: string; geo: { kind: string }; openInApp: ReactNode }) => {
    viewerProps(props);
    return (
      <div>
        <h1>{props.title}</h1>
        <span data-testid="geo-kind">{props.geo.kind}</span>
        {props.openInApp}
      </div>
    );
  },
}));

const { default: PublicSurveyViewerPage } = await import('./PublicSurveyViewerPage.tsx');

const settledView = {
  title: 'Invented survey',
  caveName: 'Invented cave',
  version: 1,
  status: 'succeeded',
  language: null,
  artefacts: [],
};

beforeEach(() => {
  upload = { data: settledView, error: null, isPending: false, refetch: vi.fn() };
  figures = { data: { placed: true, compiler: null, analyses: null }, isPending: false, isError: false };
  lineWork = { data: { type: 'FeatureCollection', features: [] }, isPending: false, isError: false };
  inApp = { data: undefined };
  user = null;
  signIn.mockReset().mockResolvedValue(undefined);
  viewerProps.mockReset();
});

afterEach(cleanup);

function show() {
  return render(
    <App>
      <MemoryRouter initialEntries={['/p/survey/abc/view']}>
        <Routes>
          <Route path="/p/survey/:token/view" element={<PublicSurveyViewerPage />} />
        </Routes>
      </MemoryRouter>
    </App>,
  );
}

describe('a link that does not open', () => {
  it('says a link the server does not know is dead, and offers no retry', () => {
    upload = { error: new ApiError(404, 'survey_upload.public_link_not_found'), isPending: false, refetch: vi.fn() };
    show();
    expect(screen.getByText(t('publicSurvey.notFound'))).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: t('common.retry') })).toBeNull();
  });

  it('says a failed read failed, and reads again on retry', () => {
    upload = { error: new TypeError('Failed to fetch'), isPending: false, refetch: vi.fn() };
    show();
    expect(screen.getByText(t('publicSurvey.loadFailed'))).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: t('common.retry') }));
    expect(upload.refetch).toHaveBeenCalledTimes(1);
  });

  it('offers a members-only link a sign-in that comes back here, and says when the sign-in server is out of reach', async () => {
    upload = { error: new ApiError(401, PUBLIC_LINK_SIGN_IN_REQUIRED), isPending: false, refetch: vi.fn() };
    signIn.mockRejectedValue(new TypeError('Failed to fetch'));
    show();

    expect(screen.getByText(t('publicSurvey.signInRequired'))).toBeInTheDocument();
    await act(async () => {
      fireEvent.click(screen.getByTestId('public-survey-sign-in-button'));
    });

    expect(signIn).toHaveBeenCalledWith('/p/survey/abc/view');
    expect(await screen.findByText(t('auth.serverUnreachable'))).toBeInTheDocument();
  });
});

describe('the map pane', () => {
  it('is handed the line work once the run placed one', () => {
    show();
    expect(screen.getByText('Invented survey')).toBeInTheDocument();
    expect(screen.getByTestId('geo-kind')).toHaveTextContent('shown');
    const geo = viewerProps.mock.lastCall![0].geo as { downloadUrl: (format: string) => string };
    expect(geo.downloadUrl('kml')).toBe('/api/v1/public/survey-uploads/abc/geodata?format=kml');
  });

  it('says the line work failed rather than that the survey was never placed', () => {
    lineWork = { isPending: false, isError: true };
    show();
    expect(screen.getByTestId('geo-kind')).toHaveTextContent('failed');
  });

  it('says the survey was not placed when the run placed nothing', () => {
    figures = { data: { placed: false }, isPending: false, isError: false };
    show();
    expect(screen.getByTestId('geo-kind')).toHaveTextContent('unplaced');
  });

  it('waits while the upload is still being worked on', () => {
    upload = { data: { ...settledView, status: 'running' }, error: null, isPending: false, refetch: vi.fn() };
    show();
    expect(screen.getByTestId('geo-kind')).toHaveTextContent('loading');
  });
});

describe('opening the upload in the application', () => {
  it('offers a signed-out reader to sign in first', () => {
    show();
    expect(screen.getByTestId('survey-viewer-sign-in-to-open')).toHaveTextContent(t('surveyViewer.header.signInToOpen'));
    expect(screen.queryByTestId('survey-viewer-open-in-app')).toBeNull();
  });

  it('offers a signed-in reader the upload only where the server says it answers them', () => {
    user = { id: 'u-1' };
    show();
    expect(screen.queryByTestId('survey-viewer-sign-in-to-open')).toBeNull();
    expect(screen.queryByTestId('survey-viewer-open-in-app')).toBeNull();
    cleanup();

    inApp = { data: { statusPageUrl: '/survey-uploads/up-1' } };
    show();
    expect(screen.getByTestId('survey-viewer-open-in-app')).toBeInTheDocument();
  });
});
