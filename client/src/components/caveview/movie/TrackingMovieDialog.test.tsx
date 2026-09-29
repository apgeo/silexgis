// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { useEffect } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { TrackedTrip, TrackingEvent, TrackingState } from '../../../api/hooks.ts';
import type { MovieFormatSupport } from '../../../caveview/movie/encode/movieEncoder.ts';
import type { MovieTripData } from '../../../caveview/movie/movieParty.ts';
import type { MovieRecording } from '../../../caveview/movie/movieRecorder.ts';
import type { MovieTripSpan } from '../../../caveview/movie/movieTimeline.ts';
import { movieSlug } from '../../../caveview/movie/movieOutput.ts';
import { DEFAULT_MOVIE_SETTINGS, normaliseMovieSettings } from '../../../caveview/movie/movieSettings.ts';
import i18n from '../../../i18n';
import { useUiPrefsStore } from '../../../stores/uiPrefsStore.ts';
import type { MoviePreviewHostProps } from './MoviePreviewHost.tsx';
import type { MovieTripsState } from './useMovieTrips.ts';

const MODEL = 'model-1';
const ARMED = '2026-09-12T08:00:00Z';
const CLOSED = '2026-09-12T12:00:00Z';

// ---- the reads, as each test sets them ----------------------------------------------------------
const reads = vi.hoisted(() => ({
  tracked: [] as TrackedTrip[],
  trackedError: null as unknown,
  movie: {
    loading: false,
    error: null,
    failed: [],
    logFailed: [],
    trips: [],
    spans: [],
    empty: [],
  } as unknown as MovieTripsState,
  movieIdsAsked: [] as (readonly string[])[],
}));
vi.mock('../../../api/hooks.ts', () => ({
  useSurveyModel: () => ({ data: { name: 'Main survey', caveId: 'cave-1' } }),
  useCave: (id: string | undefined) => ({ data: id === 'cave-1' ? { name: 'Pestera 1' } : undefined }),
  useSurveyModelTrackedTrips: () => ({
    data: reads.trackedError === null ? reads.tracked : undefined,
    isPending: false,
    error: reads.trackedError,
  }),
}));
vi.mock('./useMovieTrips.ts', () => ({
  useMovieTrips: (_model: string, ids: readonly string[]) => {
    reads.movieIdsAsked.push(ids);
    return reads.movie;
  },
}));

// ---- the browser's encoders, the recorder and the save --------------------------------------------
const probe = vi.hoisted(() => ({
  answer: [] as MovieFormatSupport[],
}));
vi.mock('../../../caveview/movie/encode/movieEncoder.ts', async (original) => ({
  ...(await original<typeof import('../../../caveview/movie/encode/movieEncoder.ts')>()),
  probeMovieFormats: () => Promise.resolve(probe.answer),
}));
const recordMovie = vi.hoisted(() => vi.fn<(recording: MovieRecording) => Promise<Blob>>());
vi.mock('../../../caveview/movie/movieRecorder.ts', async (original) => ({
  ...(await original<typeof import('../../../caveview/movie/movieRecorder.ts')>()),
  recordMovie,
}));
const saveBlob = vi.hoisted(() => vi.fn());
vi.mock('../../../api/download.ts', () => ({ saveBlob }));
// The test canvas has no text drawing; what the captions say is the caption module's own business.
const drawMovieCaptions = vi.hoisted(() => vi.fn());
vi.mock('../../../caveview/movie/movieCaptions.ts', async (original) => ({
  ...(await original<typeof import('../../../caveview/movie/movieCaptions.ts')>()),
  drawMovieCaptions,
}));

// ---- the preview: no viewer, only the handle a loaded one would give -----------------------------
function fakeViewer() {
  return {
    hasLegs: true,
    hasStations: true,
    hasStationLabels: true,
    hasStationComments: false,
    hasEntrances: true,
    hasEntrance_dots: true,
    hasSplays: false,
    hasWalls: true,
    hasScraps: false,
    hasDuplicateLegs: false,
    hasSurfaceLegs: false,
    hasTraces: false,
    hasWarnings: false,
    hasBox: true,
    hasGrid: true,
    hasRealTerrain: false,
    getCameraAngles: vi.fn(() => ({ azimuth: 0.5, polar: 1 })),
    setCameraAngles: vi.fn(),
    setLiveMarkerClusterLabel: vi.fn(),
    addLiveMarker: vi.fn(),
    moveLiveMarker: vi.fn(),
    removeLiveMarker: vi.fn(),
    addTrail: vi.fn(),
    updateTrail: vi.fn(),
    removeTrail: vi.fn(),
  };
}
const preview = vi.hoisted(() => ({
  viewer: null as unknown,
  recordingSeen: [] as boolean[],
  // What the preview was last asked to show, as the dialog hands it over.
  direction: null as string | null,
  viewRequest: null as number | null,
  // Called on each render of the preview with the view it was handed, so its order against the
  // viewer's calls can be read.
  saw: null as unknown as (direction: string) => void,
}));
vi.mock('./MoviePreviewHost.tsx', () => ({
  default: function FakePreviewHost({ onReady, overlay, recording, view, viewRequest }: MoviePreviewHostProps) {
    preview.recordingSeen.push(recording === true);
    preview.direction = view.direction;
    preview.saw?.(view.direction);
    preview.viewRequest = viewRequest ?? null;
    useEffect(() => {
      onReady?.({
        viewer: preview.viewer as never,
        cv2: {
          SHADING_HEIGHT: 0,
          SHADING_LENGTH: 1,
          SHADING_INCLINATION: 2,
          SHADING_SINGLE: 4,
          SHADING_SURVEY: 5,
          SHADING_DEPTH: 9,
          SHADING_DEPTH_CURSOR: 11,
        } as never,
      });
    }, [onReady]);
    return <div data-testid="movie-preview">{overlay}</div>;
  },
}));

const { default: TrackingMovieDialog } = await import('./TrackingMovieDialog.tsx');

// ---- invented trips --------------------------------------------------------------------------------
function trackedTrip(tripLogId: string, title: string, armed = true): TrackedTrip {
  return {
    tripLogId,
    title,
    tripDate: '2026-09-12',
    tripDateEnd: null,
    state: 'closed',
    armedAt: armed ? ARMED : null,
    closedAt: armed ? CLOSED : null,
    watchesThisModel: true,
    reportCount: armed ? 3 : 0,
    firstReportAt: armed ? ARMED : null,
    lastReportAt: armed ? CLOSED : null,
  };
}

function tracking(): TrackingState {
  return {
    state: 'closed',
    surveyModelId: MODEL,
    surveyModelMissing: false,
    referenceStationName: null,
    depthFilter: [],
    armedAt: ARMED,
    closedAt: CLOSED,
    positionsWithheld: false,
    publishesRealNames: true,
    publishedAt: null,
    publishedUntil: null,
    teams: [],
    participants: [],
  } as unknown as TrackingState;
}

function movieTrip(tripLogId: string, title: string): { trip: MovieTripData; span: MovieTripSpan } {
  const events: TrackingEvent[] = [];
  return {
    trip: {
      tripLogId,
      title,
      tracking: tracking(),
      events,
      nameOf: () => 'Ion A.',
    },
    span: {
      tripLogId,
      window: { from: Date.parse(ARMED), to: Date.parse(CLOSED) },
      moments: [],
    },
  };
}

function ready(...trips: { trip: MovieTripData; span: MovieTripSpan }[]): MovieTripsState {
  return {
    loading: false,
    error: null,
    failed: [],
    logFailed: [],
    trips: trips.map((t) => t.trip),
    spans: trips.map((t) => t.span),
    empty: [],
  };
}

const ALL_FORMATS: MovieFormatSupport[] = [
  { format: 'gif', supported: true, codec: null },
  { format: 'webm', supported: true, codec: 'vp09.00.10.08' },
  { format: 'mp4', supported: true, codec: 'avc1.42001f' },
];

function open(initialTripIds: string[] = []) {
  const onClose = vi.fn();
  render(
    <App>
      <TrackingMovieDialog surveyModelId={MODEL} initialTripIds={initialTripIds} onClose={onClose} />
    </App>,
  );
  return { onClose };
}

beforeEach(() => {
  preview.viewer = fakeViewer();
  preview.recordingSeen = [];
  preview.direction = null;
  preview.viewRequest = null;
  preview.saw = vi.fn();
  probe.answer = ALL_FORMATS;
  reads.tracked = [
    trackedTrip('trip-a', 'Alpha'),
    trackedTrip('trip-b', 'Bravo'),
    trackedTrip('trip-c', 'Never armed', false),
  ];
  reads.trackedError = null;
  reads.movie = ready();
  reads.movieIdsAsked = [];
  recordMovie.mockReset();
  saveBlob.mockReset();
  drawMovieCaptions.mockReset();
  useUiPrefsStore.setState({ movieSettings: undefined });
});

afterEach(() => {
  cleanup();
  localStorage.removeItem('silexgis.uiPrefs');
});

describe('the tracking movie dialog', () => {
  it('shows the preview, every group of settings, the notice about what the file shows, and the tracked trips', async () => {
    open();

    expect(await screen.findByTestId('movie-preview')).toBeInTheDocument();
    for (const group of ['Trips', 'Output', 'Motion', 'Cavers', 'View', 'Captions']) {
      expect(screen.getByText(group)).toBeInTheDocument();
    }
    expect(screen.getByTestId('movie-privacy')).toHaveTextContent('Share it only with people who may see both.');
    expect(within(screen.getByTestId('movie-trips')).getByText('Alpha')).toBeInTheDocument();
    // A trip that was never armed is listed, cannot be ticked, and says why.
    const never = screen.getByTestId('movie-trip-trip-c');
    expect(within(never).getByRole('checkbox')).toBeDisabled();
    expect(never).toHaveTextContent('There is nothing to replay');
    // Nothing is chosen, so there is nothing to export yet — and the reader is told what to do.
    expect(screen.getByTestId('movie-export')).toBeDisabled();
    expect(screen.getByText('Choose at least one trip to preview and export a movie.')).toBeInTheDocument();
  });

  it('keeps the trips in the order they were ticked, which is what keeps each trip its colour', async () => {
    open(['trip-b']);
    fireEvent.click(within(await screen.findByTestId('movie-trip-trip-a')).getByRole('checkbox'));

    await waitFor(() => expect(reads.movieIdsAsked.at(-1)).toEqual(['trip-b', 'trip-a']));
  });

  it('offers a format this browser cannot write at the chosen size only as a refusal saying why', async () => {
    probe.answer = [
      { format: 'gif', supported: true, codec: null },
      { format: 'webm', supported: false, codec: null },
      { format: 'mp4', supported: true, codec: 'avc1.42001f' },
    ];
    open();

    await waitFor(() => expect(screen.getByTestId('movie-format-webm')).toBeDisabled());
    expect(screen.getByTestId('movie-format-mp4')).not.toBeDisabled();
    expect(screen.getByTestId('movie-format-gif')).not.toBeDisabled();
    // Written out, since a disabled button's tooltip cannot be relied on.
    expect(screen.getByTestId('movie-format-refusal')).toHaveTextContent('This browser has no video encoder');
  });

  it('refuses to export a remembered format this browser cannot write, and says so', async () => {
    useUiPrefsStore.setState({
      movieSettings: normaliseMovieSettings({
        ...DEFAULT_MOVIE_SETTINGS,
        format: 'webm',
      }),
    });
    probe.answer = [
      { format: 'gif', supported: true, codec: null },
      { format: 'webm', supported: false, codec: null },
      { format: 'mp4', supported: false, codec: null },
    ];
    reads.movie = ready(movieTrip('trip-a', 'Alpha'));
    // A browser with a video encoder that cannot use it at this size: the refusal names the size.
    vi.stubGlobal('VideoEncoder', class {});
    try {
      open(['trip-a']);

      expect(await screen.findByTestId('movie-format-refused')).toHaveTextContent(
        'This browser cannot write WebM at 640 × 360, 10 frames a second.',
      );
      expect(screen.getByTestId('movie-export')).toBeDisabled();
    } finally {
      vi.unstubAllGlobals();
    }

    // One with no video encoder at all is told that instead.
    cleanup();
    open(['trip-a']);
    expect(await screen.findByTestId('movie-format-refused')).toHaveTextContent('This browser has no video encoder');
  });

  it('remembers the settings in this browser, repaired, and starts the next movie from them', async () => {
    open();
    fireEvent.click(await screen.findByText('Motion'));
    const rotation = await screen.findByTestId('movie-rotation');
    fireEvent.click(rotation);

    await waitFor(() => expect(useUiPrefsStore.getState().movieSettings?.rotation.enabled).toBe(false));
    // The rest is what the reader had, not something else.
    expect(useUiPrefsStore.getState().movieSettings?.format).toBe(DEFAULT_MOVIE_SETTINGS.format);
    const stored = JSON.parse(localStorage.getItem('silexgis.uiPrefs')!) as {
      state: Record<string, unknown>;
    };
    expect(stored.state.movieSettings).toBeDefined();
    // Which trips were in it is not a preference and is not kept.
    expect(JSON.stringify(stored.state)).not.toContain('trip-a');

    cleanup();
    open();
    fireEvent.click(await screen.findByText('Motion'));
    expect(await screen.findByTestId('movie-rotation')).not.toBeChecked();
  });

  it('shows the frame count and an estimate, and warns about a GIF too large to send', async () => {
    useUiPrefsStore.setState({
      movieSettings: normaliseMovieSettings({
        ...DEFAULT_MOVIE_SETTINGS,
        size: '800x600',
        fps: 25,
        durationS: 22,
      }),
    });
    open();

    // 22 s at 25 fps, and the 2 s still at the end: 550 + 50.
    expect(await screen.findByTestId('movie-summary')).toHaveTextContent('600 frames');
    expect(screen.getByTestId('movie-too-large')).toBeInTheDocument();

    cleanup();
    useUiPrefsStore.setState({
      movieSettings: normaliseMovieSettings({
        ...DEFAULT_MOVIE_SETTINGS,
        size: '320x180',
      }),
    });
    open();
    expect(await screen.findByTestId('movie-summary')).toHaveTextContent('220 frames');
    expect(screen.queryByTestId('movie-too-large')).not.toBeInTheDocument();
  });

  it('exports from the preview viewer with the chosen trips, and saves the file under a readable name', async () => {
    const alpha = movieTrip('trip-a', 'Alpha');
    const bravo = movieTrip('trip-b', 'Bravo');
    reads.movie = ready(bravo, alpha);
    const file = new Blob(['GIF89a'], { type: 'image/gif' });
    recordMovie.mockImplementation(async (recording) => {
      recording.onProgress?.({ stage: 'rendering', done: 1, total: 2, step: 1, steps: 2 });
      return file;
    });
    open(['trip-b', 'trip-a']);

    const exportButton = await screen.findByTestId('movie-export');
    await waitFor(() => expect(exportButton).not.toBeDisabled());
    fireEvent.click(exportButton);

    await waitFor(() => expect(saveBlob).toHaveBeenCalledTimes(1));
    const recording = recordMovie.mock.calls[0][0];
    expect(recording.viewer).toBe(preview.viewer);
    expect(recording.surveyModelId).toBe(MODEL);
    expect(recording.trips.map((trip) => trip.tripLogId)).toEqual(['trip-b', 'trip-a']);
    expect(recording.settings.format).toBe('gif');
    expect(recording.signal?.aborted).toBe(false);
    // Several trips are called by the cave and the days they span on the frame; the file is called
    // by the cave and the one date it was made, not by a second date in the reader's own order.
    const days = new Date(2026, 8, 12).toLocaleDateString(i18n.language);
    expect(recording.title).toBe(`Pestera 1 · ${days}`);
    const [saved, name] = saveBlob.mock.calls[0] as [Blob, string];
    expect(saved).toBe(file);
    expect(name).toMatch(new RegExp(`^silexgis-${movieSlug('Pestera 1')}-\\d{4}-\\d{2}-\\d{2}\\.gif$`));
    // The preview carries the captions the file will.
    expect(drawMovieCaptions).toHaveBeenCalled();
    // The preview was left alone while the recording held the viewer.
    expect(preview.recordingSeen).toContain(true);
    expect(screen.queryByTestId('movie-export-failed')).not.toBeInTheDocument();
  });

  it('shows the progress of an export, and a cancel ends it without a word or a file', async () => {
    reads.movie = ready(movieTrip('trip-a', 'Alpha'));
    let started!: (recording: MovieRecording) => void;
    const running = new Promise<MovieRecording>((resolve) => (started = resolve));
    recordMovie.mockImplementation(
      (recording) =>
        new Promise<Blob>((_, reject) => {
          // A GIF: 16 palette samples, then the third of its 40 frames.
          recording.onProgress?.({ stage: 'rendering', done: 19, total: 56, step: 3, steps: 40 });
          recording.signal?.addEventListener('abort', () =>
            reject(new DOMException('The movie export was cancelled.', 'AbortError')),
          );
          started(recording);
        }),
    );
    const errors = vi.spyOn(console, 'error');
    open(['trip-a']);

    const exportButton = await screen.findByTestId('movie-export');
    await waitFor(() => expect(exportButton).not.toBeDisabled());
    fireEvent.click(exportButton);
    const recording = await running;

    // The frame of the movie it is at, not a count with the colour samples in it.
    expect(await screen.findByTestId('movie-progress')).toHaveTextContent('Frame 3 of 40');
    expect(screen.getByTestId('movie-progress')).not.toHaveTextContent('Frame 19');
    // Nothing can be changed under a running export.
    expect(screen.getByTestId('movie-format-webm')).toBeDisabled();
    expect(screen.getByTestId('movie-trip-trip-a').querySelector('input')).toBeDisabled();

    await act(async () => {
      fireEvent.click(screen.getByTestId('movie-cancel'));
    });

    expect(recording.signal?.aborted).toBe(true);
    await waitFor(() => expect(screen.queryByTestId('movie-progress')).not.toBeInTheDocument());
    expect(saveBlob).not.toHaveBeenCalled();
    expect(screen.queryByTestId('movie-export-failed')).not.toBeInTheDocument();
    expect(errors).not.toHaveBeenCalled();
    errors.mockRestore();
  });

  it('captions the preview with the frame being recorded, not the one the slider was left at', async () => {
    reads.movie = ready(movieTrip('trip-a', 'Alpha'));
    let report!: (progress: Parameters<NonNullable<MovieRecording['onProgress']>>[0]) => void;
    let finish!: () => void;
    recordMovie.mockImplementation(
      (recording) =>
        new Promise<Blob>((resolve) => {
          report = (progress) => recording.onProgress?.(progress);
          finish = () => resolve(new Blob(['GIF89a'], { type: 'image/gif' }));
        }),
    );
    open(['trip-a']);
    const exportButton = await screen.findByTestId('movie-export');
    await waitFor(() => expect(exportButton).not.toBeDisabled());
    await waitFor(() => expect(drawMovieCaptions).toHaveBeenCalled());
    const progressOf = (call: unknown[]) => (call[3] as { progress: number | null }).progress;
    // The slider is at the first frame, whose bar is empty.
    expect(progressOf(drawMovieCaptions.mock.calls.at(-1)!)).toBe(0);
    fireEvent.click(exportButton);
    await waitFor(() => expect(recordMovie).toHaveBeenCalled());

    // Choosing a GIF's colours shows no frame of the movie in order, so nothing is captioned.
    drawMovieCaptions.mockClear();
    act(() => report({ stage: 'sampling', done: 1, total: 236, step: 1, steps: 16 }));
    expect(drawMovieCaptions).not.toHaveBeenCalled();
    // Frame 110 of the default 220 is half way through the replay's 200.
    act(() => report({ stage: 'rendering', done: 126, total: 236, step: 110, steps: 220 }));
    await waitFor(() => expect(drawMovieCaptions).toHaveBeenCalled());
    expect(progressOf(drawMovieCaptions.mock.calls.at(-1)!)).toBeCloseTo(109 / 199, 2);
    // The slider and the moment beside it follow the same frame.
    expect(screen.getByTestId('movie-position').dataset.frame).toBe('109');

    await act(async () => finish());
    await waitFor(() => expect(saveBlob).toHaveBeenCalled());
  });

  it('says so when an export fails', async () => {
    reads.movie = ready(movieTrip('trip-a', 'Alpha'));
    recordMovie.mockRejectedValue(new Error('GIF encoder, frame: out of memory'));
    open(['trip-a']);

    const exportButton = await screen.findByTestId('movie-export');
    await waitFor(() => expect(exportButton).not.toBeDisabled());
    fireEvent.click(exportButton);

    expect(await screen.findByTestId('movie-export-failed')).toHaveTextContent('GIF encoder, frame: out of memory');
    expect(saveBlob).not.toHaveBeenCalled();
  });

  it('closing the dialog in the middle of an export calls the export off', async () => {
    reads.movie = ready(movieTrip('trip-a', 'Alpha'));
    let signal: AbortSignal | undefined;
    recordMovie.mockImplementation(
      (recording) =>
        new Promise<Blob>((_, reject) => {
          signal = recording.signal;
          recording.signal?.addEventListener('abort', () => reject(new DOMException('cancelled', 'AbortError')));
        }),
    );
    open(['trip-a']);
    const exportButton = await screen.findByTestId('movie-export');
    await waitFor(() => expect(exportButton).not.toBeDisabled());
    fireEvent.click(exportButton);
    await waitFor(() => expect(signal).toBeDefined());

    cleanup();

    expect(signal?.aborted).toBe(true);
  });

  it('starts from the north elevation, offers the viewer\'s five views, and turns to the chosen one again on asking', async () => {
    open();
    fireEvent.click(await screen.findByText('View'));
    const choice = await screen.findByTestId('movie-view-direction');
    expect(choice).toHaveTextContent('N elevation — facing north');
    expect(preview.direction).toBe('north');

    fireEvent.mouseDown(within(choice).getByRole('combobox'));
    for (const name of ['Plan (from above)', 'N elevation — facing north', 'S elevation — facing south', 'E elevation — facing east', 'W elevation — facing west']) {
      expect((await screen.findAllByTitle(name)).length).toBeGreaterThan(0);
    }
    fireEvent.click(screen.getAllByTitle('E elevation — facing east').at(-1)!);
    await waitFor(() => expect(preview.direction).toBe('east'));
    // Remembered with the rest of the settings.
    await waitFor(() => expect(useUiPrefsStore.getState().movieSettings?.view.direction).toBe('east'));

    const again = screen.getByTestId('movie-view-again');
    await waitFor(() => expect(again).not.toBeDisabled());
    const before = preview.viewRequest ?? 0;
    fireEvent.click(again);
    await waitFor(() => expect(preview.viewRequest).toBe(before + 1));
  });

  it('stops playing the preview before turning it to another view, which play would otherwise undo', async () => {
    reads.movie = ready(movieTrip('trip-a', 'Alpha'));
    open(['trip-a']);
    const handle = await screen.findByRole('slider', { name: 'Moment in the movie' });
    await waitFor(() => expect(handle).not.toHaveAttribute('aria-disabled', 'true'));
    fireEvent.click(await screen.findByText('View'));
    const viewer = preview.viewer as ReturnType<typeof fakeViewer>;
    fireEvent.click(screen.getByTestId('movie-play'));
    expect(screen.getByTestId('movie-play')).toHaveAttribute('aria-label', 'Stop the preview');

    fireEvent.mouseDown(within(screen.getByTestId('movie-view-direction')).getByRole('combobox'));
    fireEvent.click((await screen.findAllByTitle('W elevation — facing west')).at(-1)!);
    await waitFor(() => expect(preview.direction).toBe('west'));
    expect(screen.getByTestId('movie-play')).toHaveAttribute('aria-label', 'Play the preview');
    // Play put the camera back where it began before the preview was handed the new view.
    const putBack = viewer.setCameraAngles.mock.calls.findLastIndex(([angles]) => angles.azimuth !== undefined);
    const sawWest = (preview.saw as ReturnType<typeof vi.fn>).mock.calls.findIndex(([direction]) => direction === 'west');
    expect(viewer.setCameraAngles.mock.invocationCallOrder[putBack]).toBeLessThan(
      (preview.saw as ReturnType<typeof vi.fn>).mock.invocationCallOrder[sawWest],
    );

    // The same for turning to it again.
    fireEvent.click(screen.getByTestId('movie-play'));
    expect(screen.getByTestId('movie-play')).toHaveAttribute('aria-label', 'Stop the preview');
    fireEvent.click(screen.getByTestId('movie-view-again'));
    await waitFor(() => expect(screen.getByTestId('movie-play')).toHaveAttribute('aria-label', 'Play the preview'));
  });

  it('offers no turn to the starting view while an export runs', async () => {
    reads.movie = ready(movieTrip('trip-a', 'Alpha'));
    let started!: (recording: MovieRecording) => void;
    const running = new Promise<MovieRecording>((resolve) => (started = resolve));
    recordMovie.mockImplementation(
      (recording) =>
        new Promise<Blob>((_, reject) => {
          recording.signal?.addEventListener('abort', () =>
            reject(new DOMException('The movie export was cancelled.', 'AbortError')),
          );
          started(recording);
        }),
    );
    open(['trip-a']);
    fireEvent.click(await screen.findByText('View'));
    const again = await screen.findByTestId('movie-view-again');
    const exportButton = screen.getByTestId('movie-export');
    await waitFor(() => expect(exportButton).not.toBeDisabled());
    expect(again).not.toBeDisabled();
    fireEvent.click(exportButton);
    await running;
    await waitFor(() => expect(screen.getByTestId('movie-view-again')).toBeDisabled());
    await act(async () => {
      fireEvent.click(screen.getByTestId('movie-cancel'));
    });
  });

  it('opens on the starting view the reader chose last time', async () => {
    useUiPrefsStore.setState({ movieSettings: normaliseMovieSettings({ view: { direction: 'plan' } }) });
    open();
    fireEvent.click(await screen.findByText('View'));
    expect(await screen.findByTestId('movie-view-direction')).toHaveTextContent('Plan (from above)');
    expect(preview.direction).toBe('plan');
  });

  it('gates the layers on what the model has', async () => {
    open();
    fireEvent.click(await screen.findByText('View'));

    // The fake model has no splays and does have walls.
    await waitFor(() => expect(screen.getByTestId('movie-layer-splays')).toBeDisabled());
    expect(screen.getByTestId('movie-layer-walls')).not.toBeDisabled();
    expect(screen.getByTestId('movie-layer-walls')).toBeChecked();
    // The heads-up display starts off, since it prints altitudes into every frame.
    expect(screen.getByTestId('movie-layer-HUD')).not.toBeChecked();
  });

  it('shortens quiet stretches side by side as in calendar order, and can be told not to in either', async () => {
    reads.movie = ready(movieTrip('trip-a', 'Alpha'));
    recordMovie.mockResolvedValue(new Blob(['GIF89a'], { type: 'image/gif' }));
    open(['trip-a']);
    fireEvent.click(await screen.findByText('Motion'));
    fireEvent.click(await screen.findByText('Side by side'));

    // The switch that decides it is live in this mode too, since it changes the movie.
    const shorten = await screen.findByTestId('movie-shorten-quiet');
    expect(shorten).not.toBeDisabled();
    expect(shorten).toBeChecked();

    const exportButton = screen.getByTestId('movie-export');
    await waitFor(() => expect(exportButton).not.toBeDisabled());
    fireEvent.click(exportButton);
    await waitFor(() => expect(recordMovie).toHaveBeenCalledTimes(1));
    // The trip's four hours hold no report, so the stretch is cut to the default half hour.
    expect(recordMovie.mock.calls[0][0].timeline.mode).toBe('together');
    expect(recordMovie.mock.calls[0][0].timeline.length).toBe(30 * 60_000);
    await waitFor(() => expect(exportButton).not.toBeDisabled());

    fireEvent.click(shorten);
    await waitFor(() => expect(shorten).not.toBeChecked());
    fireEvent.click(exportButton);
    await waitFor(() => expect(recordMovie).toHaveBeenCalledTimes(2));
    expect(recordMovie.mock.calls[1][0].timeline.length).toBe(4 * 3_600_000);
  });

  it('does not carry a written title to the next movie, and names a file only by a title it shows', async () => {
    reads.movie = ready(movieTrip('trip-a', 'Alpha'));
    recordMovie.mockResolvedValue(new Blob(['GIF89a'], { type: 'image/gif' }));
    open(['trip-a']);
    fireEvent.click(await screen.findByText('Captions'));
    fireEvent.change(await screen.findByTestId('movie-title-text'), { target: { value: 'Another cave entirely' } });
    fireEvent.click(screen.getByTestId('movie-caption-title'));

    await waitFor(() => expect(useUiPrefsStore.getState().movieSettings?.captions.title).toBe(false));
    expect(useUiPrefsStore.getState().movieSettings?.captions.titleText).toBe('');
    expect(localStorage.getItem('silexgis.uiPrefs')).not.toContain('Another cave entirely');

    // The title is written but not drawn, so it does not name the file either.
    const exportButton = screen.getByTestId('movie-export');
    await waitFor(() => expect(exportButton).not.toBeDisabled());
    fireEvent.click(exportButton);
    await waitFor(() => expect(saveBlob).toHaveBeenCalledTimes(1));
    expect(recordMovie.mock.calls[0][0].title).toBeNull();
    expect(saveBlob.mock.calls[0][1]).toMatch(/^silexgis-alpha-\d{4}-\d{2}-\d{2}\.gif$/);

    // Drawn, it names the file.
    await waitFor(() => expect(exportButton).not.toBeDisabled());
    fireEvent.click(screen.getByTestId('movie-caption-title'));
    fireEvent.click(exportButton);
    await waitFor(() => expect(saveBlob).toHaveBeenCalledTimes(2));
    expect(saveBlob.mock.calls[1][1]).toMatch(/^silexgis-another-cave-entirely-\d{4}-\d{2}-\d{2}\.gif$/);

    // The next movie starts with the reader's choice to draw a title, and no words in it.
    cleanup();
    open(['trip-a']);
    fireEvent.click(await screen.findByText('Captions'));
    expect(await screen.findByTestId('movie-title-text')).toHaveValue('');
    expect(screen.getByTestId('movie-caption-title')).toBeChecked();
  });

  it('shows any moment of the movie on the slider, and playing it moves the moment on', async () => {
    reads.movie = ready(movieTrip('trip-a', 'Alpha'));
    open(['trip-a']);

    const handle = await screen.findByRole('slider', { name: 'Moment in the movie' });
    await waitFor(() => expect(handle).not.toHaveAttribute('aria-disabled', 'true'));
    await waitFor(() => expect(drawMovieCaptions).toHaveBeenCalled());
    const clockOf = (call: unknown[]) => (call[3] as { clock: unknown }).clock;
    const first = clockOf(drawMovieCaptions.mock.calls.at(-1)!);
    const firstMoment = screen.getByTestId('movie-moment').textContent;
    expect(firstMoment).not.toBe('');

    fireEvent.keyDown(handle, { key: 'End', code: 'End', keyCode: 35 });
    await waitFor(() => expect(Number(screen.getByTestId('movie-position').dataset.frame)).toBeGreaterThan(0));
    // The captions follow the slider to the moment it shows, and so does the moment written beside it.
    expect(clockOf(drawMovieCaptions.mock.calls.at(-1)!)).not.toEqual(first);
    expect(screen.getByTestId('movie-moment').textContent).not.toBe(firstMoment);
    expect(handle).toHaveAttribute('aria-valuetext', screen.getByTestId('movie-moment').textContent);

    fireEvent.keyDown(handle, { key: 'Home', code: 'Home', keyCode: 36 });
    await waitFor(() => expect(screen.getByTestId('movie-position').dataset.frame).toBe('0'));
    fireEvent.click(screen.getByTestId('movie-play'));
    await waitFor(() => expect(Number(screen.getByTestId('movie-position').dataset.frame)).toBeGreaterThan(0), {
      timeout: 3000,
    });
    // Playing turns the camera with the movie.
    expect((preview.viewer as ReturnType<typeof fakeViewer>).setCameraAngles).toHaveBeenCalled();
    fireEvent.click(screen.getByTestId('movie-play'));
  });

  it('never plays the preview from before its start, whatever time the browser stamps the first frame with', async () => {
    reads.movie = ready(movieTrip('trip-a', 'Alpha'));
    open(['trip-a']);
    const handle = await screen.findByRole('slider', { name: 'Moment in the movie' });
    await waitFor(() => expect(handle).not.toHaveAttribute('aria-disabled', 'true'));
    // The browser stamps a frame with when it began drawing it, which can be before play was pressed.
    const frames: FrameRequestCallback[] = [];
    const raf = vi.spyOn(window, 'requestAnimationFrame').mockImplementation((callback) => {
      frames.push(callback);
      return frames.length;
    });
    fireEvent.click(screen.getByTestId('movie-play'));
    expect(frames).toHaveLength(1);
    act(() => frames[0](performance.now() - 5000));
    expect(screen.getByTestId('movie-position').dataset.frame).toBe('0');
    fireEvent.click(screen.getByTestId('movie-play'));
    raf.mockRestore();
  });

  it('warns before a video long enough to take a long time to render', async () => {
    useUiPrefsStore.setState({
      movieSettings: normaliseMovieSettings({
        ...DEFAULT_MOVIE_SETTINGS,
        format: 'webm',
        size: '1920x1080',
        fps: 30,
        durationS: 120,
      }),
    });
    open();
    expect(await screen.findByTestId('movie-too-long')).toHaveTextContent('may take a long time');

    cleanup();
    useUiPrefsStore.setState({
      movieSettings: normaliseMovieSettings({ ...DEFAULT_MOVIE_SETTINGS, format: 'webm', size: '640x360' }),
    });
    open();
    await screen.findByTestId('movie-summary');
    expect(screen.queryByTestId('movie-too-long')).not.toBeInTheDocument();
  });

  it('says in words why the greyed layers are greyed', async () => {
    open();
    fireEvent.click(await screen.findByText('View'));
    expect(await screen.findByTestId('movie-layers-missing')).toHaveTextContent('this model has none of them');
    // The fake model stands on no terrain, which is why the depth shadings cannot be chosen.
    expect(screen.getByText(/The depth shadings need the model to stand on real terrain/)).toBeInTheDocument();
  });

  it('shows the viewer’s sliders as what they mean', async () => {
    open();
    fireEvent.click(await screen.findByText('View'));
    // True height, and the thinnest line, one pixel.
    expect(await screen.findByTestId('movie-zscale')).toHaveTextContent('×1');
    expect(screen.getByTestId('movie-linewidth')).toHaveTextContent('1 px');
    fireEvent.click(screen.getByText('Captions'));
    expect(await screen.findByTestId('movie-caption-size')).toHaveTextContent('100 %');
  });

  it('bounds a GIF’s length by the same number it is cut to, unrounded', async () => {
    useUiPrefsStore.setState({
      movieSettings: normaliseMovieSettings({ ...DEFAULT_MOVIE_SETTINGS, durationS: 58, holdEndS: 2.5 }),
    });
    open();
    // 600 frames less 25 still ones, at 10 a second: 57.5 s, which the field may hold.
    const field = await screen.findByTestId('movie-duration');
    expect(field).toHaveAttribute('aria-valuemax', '57.5');
    expect(field).toHaveValue('57.5');
    expect(screen.getByText(/its replay lasts at most 57.5 s/)).toBeInTheDocument();
  });

  it('calls a movie of one trip by that trip’s title, and names its file after it', async () => {
    reads.movie = ready(movieTrip('trip-a', 'Alpha'));
    recordMovie.mockResolvedValue(new Blob(['GIF89a'], { type: 'image/gif' }));
    open(['trip-a']);

    const exportButton = await screen.findByTestId('movie-export');
    await waitFor(() => expect(exportButton).not.toBeDisabled());
    fireEvent.click(exportButton);

    await waitFor(() => expect(saveBlob).toHaveBeenCalledTimes(1));
    expect(recordMovie.mock.calls[0][0].title).toBe('Alpha');
    expect(saveBlob.mock.calls[0][1]).toMatch(/^silexgis-alpha-\d{4}-\d{2}-\d{2}\.gif$/);
  });
});

describe('the trip picker', () => {
  it('lists the tracked trips with the one it was opened for ticked, and one never armed not tickable', async () => {
    open(['trip-b']);

    const bravo = await screen.findByTestId('movie-trip-trip-b');
    expect(within(bravo).getByRole('checkbox')).toBeChecked();
    expect(bravo).toHaveTextContent('Reports: 3');
    expect(within(screen.getByTestId('movie-trip-trip-a')).getByRole('checkbox')).not.toBeChecked();
    const never = screen.getByTestId('movie-trip-trip-c');
    expect(within(never).getByRole('checkbox')).toBeDisabled();
    expect(never).toHaveTextContent('There is nothing to replay');
    // Only the ticked trip is read.
    expect(reads.movieIdsAsked.at(-1)).toEqual(['trip-b']);
  });

  it('leaves out a trip it was opened for that the model’s list does not offer, since it could not be unticked', async () => {
    open(['trip-elsewhere', 'trip-a']);

    await screen.findByTestId('movie-trips');
    expect(reads.movieIdsAsked.at(-1)).toEqual(['trip-a']);
    expect(within(screen.getByTestId('movie-trip-trip-a')).getByRole('checkbox')).toBeChecked();
  });

  it('lets a ticked trip that turned out to have nothing to replay be unticked, and exports nothing meanwhile', async () => {
    reads.movie = { ...ready(), empty: ['trip-a'] };
    open(['trip-a']);

    const alpha = await screen.findByTestId('movie-trip-trip-a');
    expect(alpha).toHaveTextContent('There is nothing to replay');
    expect(screen.getByTestId('movie-export')).toBeDisabled();
    const box = within(alpha).getByRole('checkbox');
    expect(box).not.toBeDisabled();

    fireEvent.click(box);

    await waitFor(() => expect(reads.movieIdsAsked.at(-1)).toEqual([]));
    expect(screen.getByText('Choose at least one trip to preview and export a movie.')).toBeInTheDocument();
  });

  it('holds the export back while a chosen trip’s log cannot be read to its end, saying what the replay says', async () => {
    reads.movie = {
      ...ready(movieTrip('trip-a', 'Alpha')),
      error: new Error('the tracking log has more pages than one read follows'),
      failed: ['trip-b'],
      logFailed: ['trip-b'],
    };
    open(['trip-a', 'trip-b']);

    expect(await screen.findByTestId('movie-log-unavailable')).toHaveTextContent('The log could not be read');
    expect(screen.getByTestId('movie-trip-trip-b')).toHaveTextContent('The log could not be read');
    // The trip that did arrive is not exported on its own as if it were the whole choice.
    expect(screen.getByTestId('movie-export')).toBeDisabled();
    expect(screen.getByText('A chosen trip could not be read. Untick it, or close and try again.')).toBeInTheDocument();
  });

  it('says so when a chosen trip could not be read for another reason', async () => {
    reads.movie = { ...ready(), error: new Error('offline'), failed: ['trip-a'] };
    open(['trip-a']);

    expect(await screen.findByTestId('movie-trips-failed')).toHaveTextContent("A chosen trip's tracking could not be read.");
    expect(screen.getByTestId('movie-trip-trip-a')).toHaveTextContent('This trip could not be read.');
    expect(screen.queryByTestId('movie-log-unavailable')).not.toBeInTheDocument();
  });

  it('says so when the model has no tracked trips, or they cannot be read', async () => {
    reads.tracked = [];
    open();
    expect(await screen.findByText('No trip has been tracked on this model yet.')).toBeInTheDocument();
    cleanup();

    reads.trackedError = new Error('offline');
    open();
    expect((await screen.findAllByText('The trips tracked on this model could not be read.')).length).toBeGreaterThan(0);
  });

  it('holds the export back when the list of tracked trips could not be read, even for the trip it was opened for', async () => {
    reads.trackedError = new Error('offline');
    reads.movie = ready(movieTrip('trip-a', 'Alpha'));
    open(['trip-a']);
    await screen.findAllByText('The trips tracked on this model could not be read.');
    await waitFor(() => expect(screen.getByTestId('movie-summary')).toBeInTheDocument());
    expect(screen.getByTestId('movie-export')).toBeDisabled();
  });
});

describe('the dialog and the keyboard', () => {
  it('keeps the keys pressed on its controls from the viewer’s document-wide shortcuts, and lets the modal have Escape and Tab', async () => {
    open(['trip-a']);
    const seen: string[] = [];
    const listener = (event: KeyboardEvent) => seen.push(event.key);
    document.addEventListener('keydown', listener);
    try {
      fireEvent.click(await screen.findByText('Captions'));
      const title = await screen.findByTestId('movie-title-text');
      fireEvent.keyDown(title, { key: 'a' });
      fireEvent.keyDown(title, { key: 'Backspace' });
      fireEvent.keyDown(screen.getByTestId('movie-export'), { key: ' ' });
      expect(seen).toEqual([]);
      fireEvent.keyDown(title, { key: 'Escape' });
      fireEvent.keyDown(title, { key: 'Tab' });
      expect(seen).toEqual(['Escape', 'Tab']);
    } finally {
      document.removeEventListener('keydown', listener);
    }
  });

  it('names every setting for assistive technology', async () => {
    open(['trip-a']);
    await screen.findByTestId('movie-settings');
    for (const group of ['Motion', 'Cavers', 'View', 'Captions']) {
      fireEvent.click(screen.getByText(group));
    }
    await screen.findByTestId('movie-layer-legs');
    const settings = screen.getByTestId('movie-settings');
    for (const role of ['combobox', 'spinbutton', 'switch', 'radiogroup', 'slider', 'checkbox', 'textbox'] as const) {
      const all = within(settings).queryAllByRole(role);
      const named = within(settings).queryAllByRole(role, { name: /\S/ });
      expect(all.length, role).toBeGreaterThan(0);
      expect(named.length, `${role}: ${all.length - named.length} without a name`).toBe(all.length);
    }
  });
});

describe('the preview’s party', () => {
  /** A trip with one caver reported at one station of the model, so a marker is drawn. */
  function tripAtStation(tripLogId: string, title: string) {
    const made = movieTrip(tripLogId, title);
    const event = {
      id: 'event-1',
      caverId: 'caver-1',
      teamId: null,
      kind: 'atStation',
      surveyModelId: MODEL,
      stationName: 'p8.1',
      depthEnteredM: null,
      note: null,
      recordedAt: ARMED,
    } as unknown as TrackingEvent;
    // The fold draws the roster, so the caver has to be on it as well as on the log.
    const participant = {
      caverId: 'caver-1',
      teamId: null,
      lastKind: 'atStation',
      lastRecordedAt: ARMED,
      positionRecordedAt: ARMED,
      stationName: 'p8.1',
      depthM: null,
      positionSurveyModelId: MODEL,
    };
    return {
      trip: {
        ...made.trip,
        events: [event],
        tracking: { ...made.trip.tracking, participants: [participant] } as unknown as TrackingState,
      },
      span: { ...made.span, moments: [Date.parse(ARMED)] },
    };
  }

  it('draws a new viewer’s party once, names its groups first, and leaves an unmoved marker alone on the next change', async () => {
    const viewer = preview.viewer as ReturnType<typeof fakeViewer>;
    reads.movie = ready(tripAtStation('trip-a', 'Alpha'));
    open(['trip-a']);
    await waitFor(() => expect(viewer.addLiveMarker).toHaveBeenCalledTimes(1));
    // Labelled by first name, the default.
    expect(viewer.addLiveMarker).toHaveBeenCalledWith('trip-a:caver-1', 'p8.1', expect.objectContaining({ label: 'Ion' }));
    // The grouped markers are named before anybody stands on the model.
    expect(viewer.setLiveMarkerClusterLabel.mock.invocationCallOrder[0]).toBeLessThan(
      viewer.addLiveMarker.mock.invocationCallOrder[0],
    );
    // A change that moves nobody: the trails switch, with a single station walked.
    fireEvent.click(await screen.findByText('Cavers'));
    fireEvent.click(screen.getByTestId('movie-trails'));
    await waitFor(() => expect(screen.getByTestId('movie-trails')).toBeChecked());
    expect(viewer.addLiveMarker).toHaveBeenCalledTimes(1);
    expect(viewer.moveLiveMarker).not.toHaveBeenCalled();
  });
});
