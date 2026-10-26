// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { groupCaveSurveys, type SurveyUploadListItem } from '../../api/hooks.ts';

/**
 * A cave's surveys: grouped from its uploads, opened to the versions and then to what each held.
 */

let items: SurveyUploadListItem[] | undefined;

vi.mock('../../api/hooks.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/hooks.ts')>('../../api/hooks.ts');
  return {
    ...actual,
    useCaveSurveys: () => ({ data: items ? actual.groupCaveSurveys(items) : undefined, isPending: items === undefined }),
    useSurveyUpload: (id: string) => ({
      data: {
        id,
        artefacts: [{ kind: 'lox', name: `model-${id}.lox`, sizeBytes: 4096, contentUrl: '/f', viewUrl: null }],
        // Only the first survey's newest version is past the warning size.
        olderVersions: {
          count: 1,
          sizeBytes: 600 * 1024 * 1024,
          warn: id === 'u2',
          warnBytes: 500 * 1024 * 1024,
          keepVersions: 0,
        },
      },
    }),
  };
});

const { default: CaveSurveysSection } = await import('./CaveSurveysSection.tsx');

function item(id: string, surveyId: string, title: string, version: string, over: Partial<SurveyUploadListItem> = {}): SurveyUploadListItem {
  return {
    id,
    caveId: 'c1',
    caveName: 'Invented Cave',
    caveSurvey: { id: surveyId, title },
    version,
    source: 'topodroid',
    surveyTitle: title,
    surveyName: null,
    surveyDate: null,
    uploadedBy: { id: 'p', displayName: 'Ion A.' },
    status: 'succeeded',
    createdAt: version,
    archive: { name: `${id}.zip`, sizeBytes: 2048, contentUrl: `/files/${id}` },
    therionSources: [],
    statusPageUrl: `/caves/c1/surveys/${surveyId}/uploads/${id}`,
    ...over,
  } as SurveyUploadListItem;
}

function show() {
  return render(
    <MemoryRouter>
      <CaveSurveysSection caveId="c1" />
    </MemoryRouter>,
  );
}

beforeEach(() => {
  items = [
    item('u1', 's1', 'First survey', '2026-10-20T10:00:00Z'),
    item('u2', 's1', 'First survey', '2026-10-25T10:00:00Z'),
    item('u3', 's2', 'Second survey', '2026-10-22T10:00:00Z', { source: 'therion' }),
    item('u4', 's2', 'Second survey', '2026-10-26T10:00:00Z', { status: 'abandoned' }),
  ];
});

afterEach(cleanup);

describe('the cave surveys section', () => {
  it('warns about the survey whose older versions take more than the installation warns at, naming the setting', () => {
    show();
    const warning = screen.getByTestId('cave-survey-size-warning-s1');
    expect(warning.textContent).toContain('First survey');
    expect(warning.textContent).toContain('600');
    expect(warning.textContent).toContain('SILEXGIS__SurveyUploads__KeepArtefactVersions');
    expect(screen.queryByTestId('cave-survey-size-warning-s2')).toBeNull();
  });

  it('draws nothing for a cave with no uploads', () => {
    items = [];
    const { container } = show();
    expect(container).toBeEmptyDOMElement();
  });

  it('lists each survey once, newest first, counting only versions that were handed over', () => {
    show();

    const rows = screen.getAllByRole('row').filter((row) => row.getAttribute('data-row-key'));
    expect(rows.map((row) => row.getAttribute('data-row-key'))).toEqual(['s1', 's2']);
    expect(rows[0]).toHaveTextContent('First survey');
    expect(rows[0]).toHaveTextContent('2'); // two versions
    expect(rows[1]).toHaveTextContent('1'); // the abandoned session is not a version
    expect(groupCaveSurveys(items!)[1]!.versionCount).toBe(1);
  });

  it('opens a survey to its versions newest first, and a version to its files and results', () => {
    show();
    fireEvent.click(screen.getAllByRole('button', { name: /expand row/i })[0]!);

    const versions = screen.getByTestId('cave-survey-versions-s1');
    const keys = Array.from(versions.querySelectorAll('tr[data-row-key]')).map((row) => row.getAttribute('data-row-key'));
    expect(keys).toEqual(['u2', 'u1']);
    expect(versions.querySelector('a[href="/caves/c1/surveys/s1/uploads/u2"]')).not.toBeNull();

    fireEvent.click(versions.querySelectorAll('button.ant-table-row-expand-icon')[0]!);
    const contents = screen.getByTestId('cave-survey-version-u2');
    expect(contents).toHaveTextContent('u2.zip');
    expect(contents).toHaveTextContent('model-u2.lox');
  });
});
