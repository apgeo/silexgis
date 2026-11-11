// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, describe, expect, it } from 'vitest';
import '../../i18n';
import en from '../../i18n/locales/en.json';
import ro from '../../i18n/locales/ro.json';
import { surveyUploadStageStates } from '../../api/hooks.ts';
import SurveyUploadProgressPanel, { type SurveyUploadProgressView } from './SurveyUploadProgress.tsx';
import { currentStageIndex } from './currentStage.ts';
import { SURVEY_UPLOAD_REQUEST_ERROR_KEYS, SURVEY_UPLOAD_RUN_ERROR_KEYS } from './surveyUploadProblems.ts';

/**
 * The view the status page and the public page share: the bar, the stages, the note or the error,
 * and the results.
 */

function show(view: Partial<SurveyUploadProgressView> & Pick<SurveyUploadProgressView, 'status'>, logShown = true) {
  const full: SurveyUploadProgressView = {
    progress: 0,
    message: null,
    error: null,
    stages: surveyUploadStageStates({ status: view.status, stage: null }),
    artefacts: [],
    ...view,
  };
  return render(
    <MemoryRouter>
      <SurveyUploadProgressPanel view={full} logShown={logShown} />
    </MemoryRouter>,
  );
}

afterEach(cleanup);

describe('the progress view', () => {
  it('shows the percentage and every stage of a running upload', () => {
    show({
      status: 'running',
      progress: 40,
      stages: surveyUploadStageStates({ status: 'running', stage: 'scaffold' }),
    });

    expect(screen.getByText('40%')).toBeInTheDocument();
    for (const stage of ['Archiving', 'Reading the archive', 'Preparing the compilation', 'Compiling', 'Storing the results', 'Reading the results']) {
      expect(screen.getByText(stage)).toBeInTheDocument();
    }
    expect(screen.getByTestId('survey-upload-status')).toHaveTextContent('Running');
  });

  it('shows a finished upload with only a note as a success, with nothing produced', () => {
    show({
      status: 'succeeded',
      progress: 100,
      message: 'Archived and read, not compiled.',
      stages: surveyUploadStageStates({ status: 'succeeded', stage: 'unpack', compile: false }),
    });

    expect(screen.getByTestId('survey-upload-message')).toHaveTextContent('Archived and read, not compiled.');
    expect(screen.queryByTestId('survey-upload-error')).toBeNull();
    expect(screen.getByTestId('survey-upload-no-artefacts')).toBeInTheDocument();
    expect(screen.getByTestId('survey-upload-status')).toHaveTextContent('Finished');
  });

  it('words a known error code and still shows the code', () => {
    show({
      status: 'failed',
      progress: 20,
      error: { code: 'survey_pipeline.archive_invalid', message: 'no manifest' },
      stages: surveyUploadStageStates({ status: 'failed', stage: 'unpack' }),
    });

    expect(screen.getByTestId('survey-upload-error')).toHaveTextContent('not a TopoDroid survey archive');
    expect(screen.getByTestId('survey-upload-error-code')).toHaveTextContent('survey_pipeline.archive_invalid');
  });

  it('points a failed compile to the log only when the page draws one', () => {
    const failed = {
      status: 'failed' as const,
      progress: 60,
      error: { code: 'survey_pipeline.compile_failed', message: null },
      stages: surveyUploadStageStates({ status: 'failed', stage: 'compile' }),
    };
    show(failed);
    expect(screen.getByTestId('survey-upload-error')).toHaveTextContent('The log below says where.');
    cleanup();
    show(failed, false);
    expect(screen.getByTestId('survey-upload-error')).toHaveTextContent('Therion could not compile the survey.');
    expect(screen.getByTestId('survey-upload-error')).not.toHaveTextContent('below');
  });

  it('falls back to the server sentence for a code it has no wording for', () => {
    show({
      status: 'failed',
      error: { code: 'survey_pipeline.brand_new', message: 'something the server said' },
    });

    expect(screen.getByTestId('survey-upload-error')).toHaveTextContent('something the server said');
  });

  it('lists the results with a download for each and a view only where there is one', () => {
    show({
      status: 'succeeded',
      progress: 100,
      artefacts: [
        { kind: 'lox', name: 'cave-20261025T101500Z.lox', sizeBytes: 4096, contentUrl: '/files/1', viewUrl: '/caves/c/models/1' },
        { kind: 'plan-pdf', name: 'plan.pdf', sizeBytes: 2048, contentUrl: '/files/2', viewUrl: null },
      ],
    });

    expect(screen.getAllByRole('link', { name: 'Download' })).toHaveLength(2);
    expect(screen.getAllByRole('link', { name: 'View' })).toHaveLength(1);
    expect(screen.getByText('Therion 3D model (.lox)')).toBeInTheDocument();
    // On a fill of their own, so they stand out of the card in the dark theme as in the light one.
    for (const download of screen.getAllByRole('link', { name: 'Download' })) {
      expect(download).toHaveClass('ant-btn-variant-filled');
    }
  });

  it('lists an output that was asked for and not produced with its reason and no download', () => {
    show({
      status: 'succeeded',
      progress: 100,
      artefacts: [
        { kind: 'plan-pdf', name: 'plan.pdf', sizeBytes: 2048, contentUrl: '/files/2', viewUrl: null, error: null },
        {
          kind: 'kml',
          name: 'cave-20261025T101500Z.kml',
          sizeBytes: null,
          contentUrl: null,
          viewUrl: null,
          error: { code: 'survey_pipeline.output_missing', message: 'Therion wrote no kml file.' },
        },
      ],
    });

    expect(screen.getAllByRole('link', { name: 'Download' })).toHaveLength(1);
    expect(screen.getByTestId('survey-upload-artefact-error')).toHaveTextContent(en.surveyUploads.problems.outputMissing);
  });

  it('draws every finished step of a successful run alike, the first not lit up as the current one', () => {
    const { container } = show({
      status: 'succeeded',
      progress: 100,
      stages: surveyUploadStageStates({ status: 'succeeded', stage: 'read' }),
    });

    const steps = Array.from(container.querySelectorAll('.ant-steps-item'));
    expect(steps.length).toBeGreaterThan(1);
    expect(steps.every((step) => step.classList.contains('ant-steps-item-finish'))).toBe(true);
    expect(container.querySelector('.ant-steps-item-active')).toBeNull();
  });

  it('marks the step a running upload is on as the current one, and no other', () => {
    const { container } = show({
      status: 'running',
      progress: 40,
      stages: surveyUploadStageStates({ status: 'running', stage: 'scaffold' }),
    });

    const active = container.querySelectorAll('.ant-steps-item-active');
    expect(active).toHaveLength(1);
    expect(active[0]).toHaveTextContent('Preparing the compilation');
  });
});

describe('the current step', () => {
  it('is the first step neither finished nor passed over', () => {
    expect(currentStageIndex([{ state: 'done' }, { state: 'skipped' }, { state: 'active' }, { state: 'pending' }])).toBe(2);
    expect(currentStageIndex([{ state: 'done' }, { state: 'failed' }, { state: 'pending' }])).toBe(1);
  });

  it('is past the last step once every step has finished', () => {
    expect(currentStageIndex([{ state: 'done' }, { state: 'skipped' }, { state: 'done' }])).toBe(3);
  });
});

describe('the wording for errors', () => {
  it('has an English and a Romanian sentence for every code it names', () => {
    const lookup = (tree: unknown, path: string) =>
      path.split('.').reduce<unknown>((node, part) => (node as Record<string, unknown> | undefined)?.[part], tree);
    for (const key of [...Object.values(SURVEY_UPLOAD_RUN_ERROR_KEYS), ...Object.values(SURVEY_UPLOAD_REQUEST_ERROR_KEYS)]) {
      expect(typeof lookup(en, key), `en ${key}`).toBe('string');
      expect(typeof lookup(ro, key), `ro ${key}`).toBe('string');
    }
  });
});
