// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { ApiError } from '../../api/client.ts';

/** The viewer behind a public link, reached without an account: what it says when it cannot show the survey. */

// An invented settled survey, as the public route describes it.
const settledView = {
  title: 'Invented survey',
  caveName: 'Invented cave',
  version: '2026-10-25T10:15:00Z',
  language: null,
  status: 'succeeded',
  artefacts: [],
};

let view: unknown;
let failure: unknown;
let figures: Record<string, unknown> = {};
let lineWork: Record<string, unknown> = {};
const refetch = vi.fn();

vi.mock('../../api/hooks.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/hooks.ts')>('../../api/hooks.ts');
  return {
    ...actual,
    usePublicSurveyUpload: () => ({ data: view, error: failure, isPending: !view && !failure, refetch }),
    usePublicSurveyUploadFigures: () => figures,
    usePublicSurveyUploadLineWork: () => lineWork,
    useSurveyUploadByPublicLink: () => ({ data: undefined }),
  };
});

const signIn = vi.fn(async (_returnTo: string) => {});
vi.mock('../../auth/auth.tsx', () => ({ useAuth: () => ({ user: null, signIn }) }));

/** The viewer itself is heavy and tested where it lives; what it is handed is what matters here. */
const viewerProps = vi.fn();
vi.mock('../../components/surveyUploads/viewer/SurveyResultViewer.tsx', () => ({
  default: (props: { geo: { kind: string }; openInApp: React.ReactNode }) => {
    viewerProps(props);
    return <div data-testid="viewer" data-geo={props.geo.kind}>{props.openInApp}</div>;
  },
}));

const { default: PublicSurveyViewerPage } = await import('./PublicSurveyViewerPage.tsx');

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

afterEach(() => {
  cleanup();
  view = undefined;
  failure = undefined;
  figures = {};
  lineWork = {};
  refetch.mockReset();
  signIn.mockReset().mockResolvedValue(undefined);
  viewerProps.mockReset();
});

describe('PublicSurveyViewerPage', () => {
  it('offers to read again when the survey could not be read, rather than leaving a dead end', () => {
    failure = new TypeError('Failed to fetch');
    show();

    expect(screen.getByTestId('public-survey-unavailable')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
    expect(refetch).toHaveBeenCalledTimes(1);
  });

  it('offers nothing to retry for a link the server does not know', () => {
    failure = new ApiError(404, 'survey_upload.public_link_not_found');
    show();

    expect(screen.getByTestId('public-survey-unavailable')).toBeTruthy();
    expect(screen.queryByRole('button', { name: 'Retry' })).toBeNull();
  });

  it('asks a reader of a link made for the organization to sign in, and says when that server cannot be reached', async () => {
    failure = new ApiError(401, 'survey_upload.public_link_sign_in_required');
    signIn.mockRejectedValueOnce(new TypeError('Failed to fetch'));
    show();

    fireEvent.click(screen.getByTestId('public-survey-sign-in-button'));
    expect(signIn).toHaveBeenCalledWith('/p/survey/abc/view');
    expect(await screen.findByText('Cannot reach the server')).toBeTruthy();
  });

  it('tells a failed line-work read from a survey that was never placed', () => {
    view = settledView;
    figures = { data: { placed: true, compiler: null, analyses: null }, isPending: false, isError: false };
    lineWork = { data: undefined, isPending: false, isError: true };
    show();

    expect(screen.getByTestId('viewer').getAttribute('data-geo')).toBe('failed');
  });

  it('calls a survey unplaced only when its figures say so', () => {
    view = settledView;
    figures = { data: { placed: false, compiler: null, analyses: null }, isPending: false, isError: false };
    lineWork = { data: undefined, isPending: false, isError: false };
    show();

    expect(screen.getByTestId('viewer').getAttribute('data-geo')).toBe('unplaced');
  });
});
