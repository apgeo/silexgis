// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { ApiError } from '../../api/client.ts';
import type { SurveyUpload } from '../../api/hooks.ts';

/**
 * The status page of one upload: what a reader sees, and what only someone who may write the cave
 * is offered.
 */

const rerunMutate = vi.fn();
const mintMutate = vi.fn();
const revokeMutate = vi.fn();
let upload: SurveyUpload | undefined;
let uploadError: unknown;
let canWrite: boolean;
let groups: { id: string; versions: { id: string; version: string; status: string; statusPageUrl: string }[] }[];

vi.mock('../../api/hooks.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/hooks.ts')>('../../api/hooks.ts');
  return {
    ...actual,
    useSurveyUpload: () => ({ data: upload, error: uploadError, isPending: !upload && !uploadError, refetch: vi.fn() }),
    useCaveSurveys: () => ({ data: groups }),
    useRefreshCaveWhenUploadSettles: () => undefined,
    useCaveSummary: () => ({ data: { permissions: { canWrite } } }),
    useRerunSurveyUpload: () => ({ mutateAsync: rerunMutate, isPending: false }),
    useMintSurveyUploadPublicLink: () => ({ mutateAsync: mintMutate, isPending: false }),
    useRevokeSurveyUploadPublicLink: () => ({ mutateAsync: revokeMutate, isPending: false }),
  };
});

const { default: SurveyUploadStatusPage } = await import('./SurveyUploadStatusPage.tsx');

function made(over: Partial<SurveyUpload> = {}): SurveyUpload {
  return {
    id: 'u1',
    status: 'succeeded',
    stage: 'unpack',
    progress: 100,
    message: 'Archived and read, not compiled.',
    error: null,
    cave: { id: 'c1', name: 'Invented Cave', url: '/caves/c1' },
    caveSurvey: { id: 's1', title: 'Invented survey' },
    version: '2026-10-25T10:15:00Z',
    caveCreated: false,
    survey: { title: 'Invented survey', name: 'inv1', date: null, team: null },
    files: [
      { name: 'inv1.zip', role: 'archive', status: 'stored', declaredSize: 2048, receivedSize: 2048, sha256: null, refusalCode: null, sourceId: null, documentId: null, contentUrl: '/files/zip' },
    ],
    artefacts: [],
    compiler: null,
    outputs: [],
    compile: false,
    logTail: 'line one of the log',
    statusPageUrl: '/caves/c1/surveys/s1/uploads/u1',
    publicLink: null,
    publicLinkRefusal: null,
    createdAt: '2026-10-25T10:15:00Z',
    expiresAt: '2026-10-26T10:15:00Z',
    startedAt: '2026-10-25T10:15:01Z',
    finishedAt: '2026-10-25T10:15:03Z',
    ...over,
  } as SurveyUpload;
}

function show() {
  return render(
    <App>
      <MemoryRouter initialEntries={['/caves/c1/surveys/s1/uploads/u1']}>
        <Routes>
          <Route path="/caves/:caveId/surveys/:surveyId/uploads/:uploadId" element={<SurveyUploadStatusPage />} />
        </Routes>
      </MemoryRouter>
    </App>,
  );
}

beforeEach(() => {
  groups = [];
  rerunMutate.mockReset().mockResolvedValue(undefined);
  mintMutate.mockReset().mockResolvedValue(undefined);
  revokeMutate.mockReset().mockResolvedValue(undefined);
  upload = made();
  uploadError = undefined;
  canWrite = true;
});

afterEach(cleanup);

describe('the upload status page', () => {
  it('shows the survey, the stages, the note and the input files', () => {
    show();

    expect(screen.getByRole('heading', { name: 'Invented survey' })).toBeInTheDocument();
    expect(screen.getByTestId('survey-upload-stages')).toBeInTheDocument();
    expect(screen.getByTestId('survey-upload-message')).toHaveTextContent('not compiled');
    expect(screen.getByText('inv1.zip')).toBeInTheDocument();
  });

  it('shows the log tail and the run-again button to someone who may write, and neither to a reader', () => {
    show();
    expect(screen.getByTestId('survey-upload-log-tail')).toHaveTextContent('line one of the log');
    expect(screen.getByTestId('survey-upload-rerun')).toBeInTheDocument();

    cleanup();
    canWrite = false;
    upload = made({ logTail: null });
    show();
    expect(screen.queryByTestId('survey-upload-log-tail')).toBeNull();
    expect(screen.queryByTestId('survey-upload-rerun')).toBeNull();
    expect(screen.getByTestId('survey-upload-stages')).toBeInTheDocument();
  });

  it('never offers a log to a reader even if one arrived', () => {
    canWrite = false;
    upload = made({ logTail: 'leaked' });
    show();
    expect(screen.queryByTestId('survey-upload-log-tail')).toBeNull();
  });

  it('offers no run-again while the upload is still running, and no delete ever', () => {
    upload = made({ status: 'running', stage: 'scaffold', progress: 30, finishedAt: null, message: null });
    show();
    expect(screen.queryByTestId('survey-upload-rerun')).toBeNull();
    expect(screen.queryByRole('button', { name: /delete/i })).toBeNull();
  });

  it('runs the upload again', async () => {
    show();
    fireEvent.click(screen.getByTestId('survey-upload-rerun'));
    await waitFor(() => expect(rerunMutate).toHaveBeenCalledWith({ id: 'u1' }));
  });

  it('shows a public link in full with copy, and withdraws it only after confirmation', async () => {
    upload = made({ publicLink: { url: '/p/survey/abc', createdAt: '2026-10-25T10:16:00Z' } });
    show();

    expect(screen.getByTestId('survey-upload-link')).toHaveTextContent(`${window.location.origin}/p/survey/abc`);
    expect(screen.getByRole('button', { name: /copy/i })).toBeInTheDocument();

    fireEvent.click(screen.getByTestId('survey-upload-link-revoke'));
    expect(revokeMutate).not.toHaveBeenCalled();
    fireEvent.click(await screen.findByRole('button', { name: 'OK' }));
    await waitFor(() => expect(revokeMutate).toHaveBeenCalledWith('u1'));
  });

  it('offers to make a link when there is none, to someone who may write', () => {
    show();
    expect(screen.getByTestId('survey-upload-no-link')).toBeInTheDocument();
    expect(screen.getByTestId('survey-upload-link-make')).toBeInTheDocument();

    cleanup();
    canWrite = false;
    show();
    expect(screen.queryByTestId('survey-upload-link-make')).toBeNull();
  });

  it('opens the public link in a new tab', () => {
    upload = made({ publicLink: { url: '/p/survey/abc', createdAt: '2026-10-25T10:16:00Z' } });
    show();
    const open = screen.getByTestId('survey-upload-link-open');
    expect(open.getAttribute('href')).toBe(`${window.location.origin}/p/survey/abc`);
    expect(open.getAttribute('target')).toBe('_blank');
  });

  it('says why there is no link when the server gives a reason, and offers no button for a protected cave', () => {
    upload = made({ publicLinkRefusal: 'protected-cave' });
    show();
    expect(screen.getByTestId('survey-upload-no-link')).toHaveTextContent('position is protected');
    expect(screen.queryByTestId('survey-upload-link-make')).toBeNull();

    cleanup();
    upload = made({ publicLinkRefusal: 'account-off' });
    show();
    expect(screen.getByTestId('survey-upload-no-link')).toHaveTextContent('switched off');
    expect(screen.getByTestId('survey-upload-link-make')).toBeInTheDocument();
  });

  it('says a withdrawn link was withdrawn and offers a new one, but none on an installation that makes no links', () => {
    upload = made({ publicLinkRefusal: 'revoked' });
    show();
    expect(screen.getByTestId('survey-upload-no-link')).toHaveTextContent('withdrawn');
    expect(screen.getByTestId('survey-upload-link-make')).toBeInTheDocument();

    cleanup();
    upload = made({ publicLinkRefusal: 'installation-off' });
    show();
    expect(screen.getByTestId('survey-upload-no-link')).toHaveTextContent('public links switched off');
    expect(screen.queryByTestId('survey-upload-link-make')).toBeNull();
  });

  it('reads a refusal code it has no wording for as no reason known', () => {
    upload = made({ publicLinkRefusal: 'some-later-reason' });
    show();
    expect(screen.getByTestId('survey-upload-no-link')).toHaveTextContent('can each be the reason');
  });

  it('turns the server\'s refusal to make a link into its sentence', async () => {
    mintMutate.mockRejectedValueOnce(new ApiError(409, 'survey_upload.public_link_refused_protected'));
    show();
    fireEvent.click(screen.getByTestId('survey-upload-link-make'));
    expect(await screen.findByText(/position is protected, so its survey results are not published/)).toBeInTheDocument();
  });

  it('lists the survey\'s versions and points to a newer one', () => {
    groups = [
      {
        id: 's1',
        versions: [
          { id: 'u2', version: '2026-10-26T10:15:00Z', status: 'succeeded', statusPageUrl: '/caves/c1/surveys/s1/uploads/u2' },
          { id: 'u1', version: '2026-10-25T10:15:00Z', status: 'succeeded', statusPageUrl: '/caves/c1/surveys/s1/uploads/u1' },
        ],
      },
    ];
    show();
    expect(screen.getByTestId('survey-upload-versions')).toBeInTheDocument();
    expect(screen.getByTestId('survey-upload-newer')).toBeInTheDocument();
    expect(screen.getAllByRole('link').some((a) => a.getAttribute('href') === '/caves/c1/surveys/s1/uploads/u2')).toBe(true);
  });

  it('says so when the upload is not found', () => {
    upload = undefined;
    uploadError = new ApiError(404, 'survey_upload.not_found');
    show();
    expect(screen.getByTestId('survey-upload-unavailable')).toHaveTextContent('not found');
  });
});
