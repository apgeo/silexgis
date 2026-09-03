// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const navigate = vi.fn();
vi.mock('react-router-dom', async () => ({
  ...(await vi.importActual<typeof import('react-router-dom')>('react-router-dom')),
  useNavigate: () => navigate,
}));
import '../../i18n';
import { ApiError } from '../../api/client.ts';
import { surveyModelPollInterval } from '../../api/hooks.ts';
import type { CaveSummary, SurveyModelInfo } from '../../api/hooks.ts';

const uploadMutate = vi.fn();
const deleteMutate = vi.fn();

function model(overrides: Partial<SurveyModelInfo> = {}): SurveyModelInfo {
  return {
    id: 'm1',
    caveId: 'c1',
    name: 'Grind walls',
    format: 'stl',
    fileId: 'f1',
    description: null,
    surveyedAt: null,
    modelUrl: '/api/v1/files/f1/content?token=t',
    status: 'ready',
    processingError: null,
    meshUrl: '/api/v1/files/f2/content?token=t',
    anchorLongitude: 25.2,
    anchorLatitude: 45.5,
    anchorHeightM: 1100,
    triangleCount: 40120,
    sourcePrecisionLost: false,
    createdAt: '2026-08-18T06:00:00Z',
    updatedAt: '2026-08-18T06:00:00Z',
    ...overrides,
  } as SurveyModelInfo;
}

/** The exact position the server is willing to hand this caller, or a deliberately blurred one. */
function summaryWith(exact: boolean): CaveSummary {
  return {
    entranceCount: 1,
    centerlineCount: 0,
    surveyModelCount: 1,
    attachmentCount: 0,
    tripLogCount: 0,
    mainEntrance: {
      id: 'e1',
      name: 'Main',
      geom: { type: 'Point', coordinates: [25.209_12, 45.519_44] },
      approximateLocation: !exact,
    },
    permissions: {
      canWrite: true,
      canDelete: true,
      canShare: true,
      canManagePermissions: true,
      canViewExactLocation: exact,
    },
  } as unknown as CaveSummary;
}

let models: SurveyModelInfo[] = [];
/** Undefined while the summary request is still in the air, which is a state of its own. */
let summary: CaveSummary | undefined = summaryWith(true);

vi.mock('../../api/hooks.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/hooks.ts')>('../../api/hooks.ts');
  return {
    // The polling rule itself is the real one: it is what the assertions below are about.
    surveyModelPollInterval: actual.surveyModelPollInterval,
    surveyModelUnsettled: actual.surveyModelUnsettled,
    surveyModelReadableByViewer: actual.surveyModelReadableByViewer,
    useSurveyModels: () => ({ data: models }),
    useCaveSummary: () => ({ data: summary }),
    useUploadSurveyModel: () => ({ mutateAsync: uploadMutate, isPending: false }),
    useDeleteSurveyModel: () => ({ mutateAsync: deleteMutate, isPending: false }),
  };
});

const { default: SurveyModelSection } = await import('./SurveyModelSection.tsx');

/** Where the two new buttons send the reader, recorded rather than followed. */
let opened: Parameters<typeof window.open>[] = [];

// The section routes to the map when a model is opened beside it, so it needs a router around it.
function show(canEdit = true) {
  return render(
    <MemoryRouter>
      <App>
        <SurveyModelSection caveId="c1" canEdit={canEdit} />
      </App>
    </MemoryRouter>,
  );
}

/** Choose a file the way a person does: through the input the uploader renders. */
function chooseFile(name: string) {
  const input = document.querySelector('input[type="file"]') as HTMLInputElement;
  fireEvent.change(input, { target: { files: [new File(['x'], name, { type: 'model/stl' })] } });
}

async function openUpload(fileName: string) {
  fireEvent.click(screen.getByRole('button', { name: /Upload model/ }));
  await screen.findByText('Choose a file, or drop it here');
  chooseFile(fileName);
  await screen.findByText(fileName);
}

beforeEach(() => {
  uploadMutate.mockReset().mockResolvedValue(model());
  deleteMutate.mockReset().mockResolvedValue(undefined);
  models = [];
  summary = summaryWith(true);
  navigate.mockClear();
  opened = [];
  // Recorded rather than opened: jsdom's window.open would otherwise be a no-op that swallows
  // both the address and the window name, and the window name is half of what is asserted.
  vi.spyOn(window, 'open').mockImplementation((...args) => {
    opened.push(args);
    return null;
  });
});

afterEach(cleanup);

describe('the survey model list', () => {
  it('says a mesh is still being converted, and what its walls are made of once it is not', async () => {
    models = [model({ status: 'processing', meshUrl: null, triangleCount: null })];
    show();
    expect(await screen.findByText('In progress')).toBeInTheDocument();
    expect(screen.getByText(/updates itself when they are ready/)).toBeInTheDocument();

    cleanup();
    models = [model()];
    show();
    expect(await screen.findByText('Ready')).toBeInTheDocument();
    expect(screen.getByText('40120 triangles')).toBeInTheDocument();
  });

  it("shows a failed conversion in the server's own words, which are the only ones that fit", async () => {
    models = [
      model({
        status: 'failed',
        meshUrl: null,
        triangleCount: null,
        processingError: 'EPSG:31700 is not a coordinate system this installation can resolve.',
      }),
    ];
    show();
    expect(await screen.findByText('Could not be processed')).toBeInTheDocument();
    expect(screen.getByText(/EPSG:31700 is not a coordinate system/)).toBeInTheDocument();
  });

  it('warns that the source lost precision, because re-exporting is what fixes it', async () => {
    models = [model({ sourcePrecisionLost: true })];
    show();
    expect(await screen.findByText(/lost precision before it arrived/)).toBeInTheDocument();
  });

  it('does not offer the survey viewer a mesh it cannot read, and does offer it a line plot', async () => {
    models = [model({ id: 'mesh' }), model({ id: 'plot', format: 'lox', name: 'Grind plot' })];
    show();
    // One view button for two rows: the line plot's.
    const viewButtons = await screen.findAllByRole('button', { name: /View in 3D/ });
    expect(viewButtons).toHaveLength(1);
  });

  it('offers the same model in all three places, and only for a format the viewer reads', async () => {
    // A mesh is not openable in the survey viewer anywhere, so none of the three ways of opening
    // one may appear against it. Counting rather than asserting presence: the failure worth
    // catching is a new button that forgot the format gate the "View in 3D" one has.
    models = [model({ id: 'mesh' }), model({ id: 'plot', format: 'lox', name: 'Grind plot' })];
    show();
    await screen.findByText('Grind plot');

    // Queried by label rather than by role-with-name: computing an accessible name for every
    // button in the tree is slow enough in this environment that three such queries in one case
    // timed out under a full-suite run while passing on its own.
    expect(screen.getAllByLabelText('Open beside the map')).toHaveLength(1);
    expect(screen.getAllByLabelText('Open in another window')).toHaveLength(1);
  });

  it('sends the map the model that was asked for, not the cave', async () => {
    models = [model({ id: 'plot', format: 'lox', name: 'Grind plot' })];
    show();

    await screen.findByText('Grind plot');
    fireEvent.click(screen.getByLabelText('Open beside the map'));

    // The id travels, because the map has no way to guess which of a cave's models was meant —
    // and guessing "the first readable one" is exactly the behaviour this button exists to fix.
    expect(navigate).toHaveBeenCalledWith('/map?model=plot');
  });

  it('opens a window per model rather than reusing one', async () => {
    // A shared window name would make asking for a second model replace the first, which is right
    // for the map's "show whatever is selected" pop-out and wrong for a model chosen by name.
    models = [
      model({ id: 'plot', format: 'lox', name: 'Grind plot' }),
      model({ id: 'other', format: 'lox', name: 'Other plot' }),
    ];
    show();

    await screen.findByText('Grind plot');
    const buttons = screen.getAllByLabelText('Open in another window');
    fireEvent.click(buttons[0]);
    fireEvent.click(buttons[1]);

    expect(opened.map((call) => call[1])).toEqual([
      'silexgis-viewer3d-plot',
      'silexgis-viewer3d-other',
    ]);
    expect(opened[0][0]).toBe('/panel/viewer3d?model=plot');
  });

  it('stops asking once nothing is left to wait for', () => {
    // A conversion in flight is watched closely; a settled list falls back to the slow refresh
    // that keeps the signed URLs on it alive, and never to the fast one.
    expect(surveyModelPollInterval([model({ status: 'pending' })])).toBe(2000);
    expect(surveyModelPollInterval([model({ status: 'processing' })])).toBe(2000);
    expect(surveyModelPollInterval([model({ status: 'ready' })])).toBe(8 * 60_000);
    expect(surveyModelPollInterval([model({ status: 'failed' })])).toBe(8 * 60_000);
    expect(surveyModelPollInterval([model({ status: 'ready' }), model({ status: 'processing' })]))
      .toBe(2000);
    expect(surveyModelPollInterval(undefined)).toBe(8 * 60_000);
  });
});

describe('uploading a survey model', () => {
  it("offers the cave's entrance as the origin when the position held is the true one", async () => {
    show();
    await openUpload('walls.stl');

    expect(screen.getByLabelText('Longitude of the origin')).toHaveValue('25.20912');
    expect(screen.getByLabelText('Latitude of the origin')).toHaveValue('45.51944');
    expect(screen.getByText(/Filled in from this cave's main entrance/)).toBeInTheDocument();
  });

  it('offers nothing when the position it holds is deliberately blurred, and says why', async () => {
    // The cave is protected and this account was never granted exact location: what the list
    // carries is an obfuscated point, and seeding the form with it would anchor the walls at a
    // position nobody chose while looking like a successful upload.
    summary = summaryWith(false);
    show();
    await openUpload('walls.stl');

    expect(screen.getByLabelText('Longitude of the origin')).toHaveValue('');
    expect(screen.getByLabelText('Latitude of the origin')).toHaveValue('');
    expect(screen.getByText(/deliberately approximate/)).toBeInTheDocument();
  });

  it('says a cave has no entrance position rather than accusing it of being blurred', async () => {
    // The account can see exact locations and the cave is not protected: there is simply no
    // entrance geometry on record yet. Telling this uploader their own cave is being obfuscated
    // from them would be false, and would send them asking about a rule that is not in force.
    summary = { ...summaryWith(true), mainEntrance: null } as unknown as CaveSummary;
    show();
    await openUpload('walls.stl');

    expect(screen.getByText(/No entrance position is recorded/)).toBeInTheDocument();
    expect(screen.queryByText(/deliberately approximate/)).not.toBeInTheDocument();
  });

  it('claims nothing about the position while the cave summary is still on its way', async () => {
    summary = undefined;
    show();
    await openUpload('walls.stl');

    // Neither statement is true yet, and one of them is an accusation.
    expect(screen.queryByText(/deliberately approximate/)).not.toBeInTheDocument();
    expect(screen.queryByText(/No entrance position is recorded/)).not.toBeInTheDocument();
    expect(screen.queryByText(/Filled in from this cave's main entrance/)).not.toBeInTheDocument();
  });

  it('fills the origin in when the cave summary answers after the form is already up', async () => {
    // Initial values are read once, when the form mounts. A summary that lands afterwards used to
    // leave the note saying the fields were filled in over two empty required ones.
    summary = undefined;
    const view = show();
    await openUpload('walls.stl');
    expect(screen.getByLabelText('Longitude of the origin')).toHaveValue('');

    summary = summaryWith(true);
    view.rerender(
      <MemoryRouter>
        <App>
          <SurveyModelSection caveId="c1" canEdit />
        </App>
      </MemoryRouter>,
    );

    await waitFor(() =>
      expect(screen.getByLabelText('Longitude of the origin')).toHaveValue('25.20912'),
    );
    expect(screen.getByText(/Filled in from this cave's main entrance/)).toBeInTheDocument();
  });

  it('asks a Therion plot where it sits, because that format never says so itself', async () => {
    // The plot is now read into stations and shots rather than only drawn, and a .lox has no field
    // for a coordinate system anywhere in it. Uploaded without a position it is not placed
    // approximately — it fails, and no screen afterwards can supply what was never asked for.
    show();
    await openUpload('cave.lox');

    expect(screen.getByText('Where this survey sits in the world')).toBeInTheDocument();
    expect(screen.getByLabelText('Longitude of the origin')).toHaveValue('25.20912');

    fireEvent.change(screen.getByLabelText("Altitude of the file's zero level (m)"), {
      target: { value: '1200' },
    });
    fireEvent.click(screen.getByRole('button', { name: /^Upload model$/ }));
    await waitFor(() => expect(uploadMutate).toHaveBeenCalled());
    expect(uploadMutate.mock.calls[0][0].declaration).toEqual({
      originHeightM: 1200,
      originLongitude: 25.209_12,
      originLatitude: 45.519_44,
    });
  });

  it('will not send a Therion plot with nothing to place it by', async () => {
    summary = { ...summaryWith(true), mainEntrance: null } as unknown as CaveSummary;
    show();
    await openUpload('cave.lox');

    fireEvent.click(screen.getByRole('button', { name: /^Upload model$/ }));

    // Both halves of the position are named, because both are missing.
    expect(await screen.findAllByText(/needs the position its zero point sits at/)).toHaveLength(2);
    expect(uploadMutate).not.toHaveBeenCalled();
  });

  it('offers a Survex plot the same questions and demands none of them', async () => {
    // A .3d states its own coordinate system only when the survey was compiled with one, and which
    // it is cannot be told from out here. So the fields are there for the export that says nothing,
    // and an upload is never refused for failing to repeat what the file already states.
    summary = { ...summaryWith(true), mainEntrance: null } as unknown as CaveSummary;
    show();
    await openUpload('cave.3d');

    expect(screen.getByText('Where this survey sits in the world')).toBeInTheDocument();
    expect(screen.getByLabelText('Longitude of the origin')).toHaveValue('');

    fireEvent.click(screen.getByRole('button', { name: /^Upload model$/ }));
    await waitFor(() => expect(uploadMutate).toHaveBeenCalled());
    expect(uploadMutate.mock.calls[0][0]).toMatchObject({ caveId: 'c1' });
  });

  it('sends the declaration a mesh needs, and only the half that was answered', async () => {
    show();
    await openUpload('walls.stl');
    fireEvent.change(screen.getByLabelText("Altitude of the file's zero level (m)"), {
      target: { value: '1100' },
    });

    fireEvent.click(screen.getByRole('button', { name: /^Upload model$/ }));
    await waitFor(() => expect(uploadMutate).toHaveBeenCalled());
    expect(uploadMutate.mock.calls[0][0].declaration).toEqual({
      originHeightM: 1100,
      originLongitude: 25.209_12,
      originLatitude: 45.519_44,
    });
  });

  it('sends a projected file its code and no position, which the file itself already answers', async () => {
    show();
    await openUpload('walls.stl');
    fireEvent.click(screen.getByRole('radio', { name: /A projected coordinate system/ }));
    fireEvent.change(await screen.findByLabelText('EPSG code'), { target: { value: '32635' } });
    fireEvent.change(screen.getByLabelText("Altitude of the file's zero level (m)"), {
      target: { value: '1100' },
    });

    fireEvent.click(screen.getByRole('button', { name: /^Upload model$/ }));
    await waitFor(() => expect(uploadMutate).toHaveBeenCalled());
    expect(uploadMutate.mock.calls[0][0].declaration).toEqual({
      originHeightM: 1100,
      sourceEpsg: 32635,
    });
  });

  it('will not send a mesh with nothing to place it by', async () => {
    show();
    await openUpload('walls.stl');
    // Every field the server needs, minus the altitude nothing in the file can supply.
    fireEvent.click(screen.getByRole('button', { name: /^Upload model$/ }));

    expect(
      await screen.findByText(/Give the altitude, in metres, that the file's zero level sits at/),
    ).toBeInTheDocument();
    expect(uploadMutate).not.toHaveBeenCalled();
  });

  it.each([
    ['survey_model.format_unsupported', /Upload a Therion .lox/],
    ['survey_model.size_invalid', /larger than 100 MB/],
    ['survey_model.crs_invalid', /as an EPSG code/],
    ['survey_model.height_invalid', /Give the altitude, in metres/],
    ['survey_model.origin_invalid', /needs the position its zero point sits at/],
    ['survey_model.not_found', /survey model no longer exists/],
    ['cave.not_found', /cave no longer exists/],
    ['acl.forbidden', /not allowed to change/],
  ])('turns %s into a sentence somebody can act on', async (code, expected) => {
    // A .3d, because these are about what the server's refusals are turned into and the upload has
    // to reach the server to be refused. It is the one format this screen demands nothing of.
    uploadMutate.mockRejectedValue(new ApiError(400, code, 'server wording'));
    show();
    await openUpload('cave.3d');

    fireEvent.click(screen.getByRole('button', { name: /^Upload model$/ }));
    expect(await screen.findByText(expected)).toBeInTheDocument();
  });

  it('falls back to a general failure for a refusal nobody has wording for', async () => {
    // A paraphrase of an unknown code would be a guess presented as an explanation.
    uploadMutate.mockRejectedValue(new ApiError(500, 'something.nobody.wrote'));
    show();
    await openUpload('cave.3d');

    fireEvent.click(screen.getByRole('button', { name: /^Upload model$/ }));
    expect(await screen.findByText('Upload failed')).toBeInTheDocument();
  });
});
