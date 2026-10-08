// SPDX-License-Identifier: AGPL-3.0-or-later
import { App, ConfigProvider } from 'antd';
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { useEffect, useState } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { TrackedTrip, TrackingEvent, TrackingState } from '../../../api/hooks.ts';
import { MemoryMovieSink } from '../../../caveview/movie/encode/memoryMovieSink.ts';
import type { MovieFormatSupport } from '../../../caveview/movie/encode/movieEncoder.ts';
import type { MovieSink } from '../../../caveview/movie/encode/movieSink.ts';
import type { MovieFileChoice } from '../../../caveview/movie/movieFileSink.ts';
import type { MovieTripData } from '../../../caveview/movie/movieParty.ts';
import {
  MovieStillUnwrittenError,
  type MovieRecording,
  type MovieStillRecording,
} from '../../../caveview/movie/movieRecorder.ts';
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
    newReports: new Map(),
    rereadLive: () => Promise.resolve([]),
  } as unknown as MovieTripsState,
  movieIdsAsked: [] as (readonly string[])[],
  /** Where each read was told a trip still under way ends. */
  movieEndsAsked: [] as number[],
  /** Whether each read was told to leave the logs of trips under way alone. */
  moviePausedAsked: [] as boolean[],
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
  useMovieTrips: (_model: string, ids: readonly string[], liveEnd: number, paused: boolean) => {
    reads.movieIdsAsked.push(ids);
    reads.movieEndsAsked.push(liveEnd);
    reads.moviePausedAsked.push(paused);
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
const recordMovieStill = vi.hoisted(() => vi.fn<(recording: MovieStillRecording, index: number) => Promise<Blob>>());
vi.mock('../../../caveview/movie/movieRecorder.ts', async (original) => ({
  ...(await original<typeof import('../../../caveview/movie/movieRecorder.ts')>()),
  recordMovie,
  recordMovieStill,
  recordMovieToSink,
}));
const recordMovieToSink = vi.hoisted(() => vi.fn<(recording: MovieRecording, sink: MovieSink) => Promise<Blob | null>>());
const saveBlob = vi.hoisted(() => vi.fn());
vi.mock('../../../api/download.ts', () => ({ saveBlob }));
// Where a long video is saved: whether this browser would be asked, and what the reader answered.
const disk = vi.hoisted(() => ({
  goes: false,
  choose: vi.fn<(name: string, format: string) => Promise<MovieFileChoice>>(),
}));
vi.mock('../../../caveview/movie/movieFileSink.ts', async (original) => ({
  ...(await original<typeof import('../../../caveview/movie/movieFileSink.ts')>()),
  movieGoesToDisk: (format: string) => disk.goes && format !== 'gif',
  chooseMovieFile: disk.choose,
}));
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
  // How many previews have been taken down — which is where a real one ends its capture session,
  // disposes of its viewer and gives its WebGL context back.
  released: 0,
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
    useEffect(
      () => () => {
        preview.released++;
      },
      [],
    );
    return <div data-testid="movie-preview">{overlay}</div>;
  },
}));

// The real form, counted each time it is drawn.
const settingsForm = vi.hoisted(() => ({ renders: 0 }));
vi.mock('./MovieSettingsForm.tsx', async (original) => {
  const actual = await original<typeof import('./MovieSettingsForm.tsx')>();
  const Form = actual.default;
  return {
    ...actual,
    default: function CountedSettingsForm(props: Parameters<typeof Form>[0]) {
      settingsForm.renders++;
      return <Form {...props} />;
    },
  };
});

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
    quietAfterSeconds: null,
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
    newReports: new Map(),
    // A test of a trip still under way says what its log reads on being asked again; one that does
    // not, and has the log asked for all the same, fails on this.
    rereadLive: () => Promise.reject(new Error('this test did not expect a log to be read again')),
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

/**
 * The dialog under a parent that does close it when asked, as the pages that open it do. Without
 * motion, because the dialog's body is only taken down once the closing animation has ended, and
 * nothing ends an animation here.
 */
function openClosable(initialTripIds: string[] = []) {
  const onClose = vi.fn();
  function Page() {
    const [modelId, setModelId] = useState<string | null>(MODEL);
    return (
      <TrackingMovieDialog
        surveyModelId={modelId}
        initialTripIds={initialTripIds}
        onClose={() => {
          onClose();
          setModelId(null);
        }}
      />
    );
  }
  render(
    <ConfigProvider theme={{ token: { motion: false } }}>
      <App>
        <Page />
      </App>
    </ConfigProvider>,
  );
  return { onClose };
}

beforeEach(() => {
  preview.viewer = fakeViewer();
  preview.released = 0;
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
  reads.movieEndsAsked = [];
  reads.moviePausedAsked = [];
  recordMovie.mockReset();
  recordMovieStill.mockReset();
  recordMovieToSink.mockReset();
  disk.goes = false;
  disk.choose.mockReset();
  saveBlob.mockReset();
  drawMovieCaptions.mockReset();
  useUiPrefsStore.setState({ movieSettings: undefined, movieGifCalibration: undefined });
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

  it('estimates the next GIF from what the last one made here came to, and says that it does', async () => {
    reads.movie = ready(movieTrip('trip-a', 'Alpha'));
    // The default movie — 640 × 360, 220 frames, turning — reckoned at about 2 MB, comes to 3,000,000 bytes.
    recordMovie.mockResolvedValue(new Blob([new Uint8Array(3_000_000)], { type: 'image/gif' }));
    open(['trip-a']);

    const summary = await screen.findByTestId('movie-summary');
    expect(summary).toHaveTextContent('220 frames · about 2 MB');
    expect(summary).not.toHaveTextContent('going by the last GIF');
    const exportButton = screen.getByTestId('movie-export');
    await waitFor(() => expect(exportButton).not.toBeDisabled());
    fireEvent.click(exportButton);
    await waitFor(() => expect(saveBlob).toHaveBeenCalledTimes(1));

    // The same movie again is now reckoned at what it came to, and the dialog says why the figure moved.
    await waitFor(() =>
      expect(screen.getByTestId('movie-summary')).toHaveTextContent(
        '220 frames · about 2.9 MB, going by the last GIF made here',
      ),
    );
    // Two numbers are kept in this browser, and nothing about the movie they came from.
    const kept = useUiPrefsStore.getState().movieGifCalibration;
    expect(Object.keys(kept ?? {})).toEqual(['turning']);
    expect(kept?.turning).toBeGreaterThan(0.05);
    expect(kept?.turning).toBeLessThan(0.1);

    // The next dialog starts from what was learnt...
    cleanup();
    open(['trip-a']);
    expect(await screen.findByTestId('movie-summary')).toHaveTextContent('going by the last GIF made here');

    // ...but a still camera's GIF is another kind of file, and is still reckoned from the built-in figure.
    cleanup();
    useUiPrefsStore.setState({
      movieSettings: normaliseMovieSettings({
        ...DEFAULT_MOVIE_SETTINGS,
        rotation: { ...DEFAULT_MOVIE_SETTINGS.rotation, enabled: false },
      }),
    });
    open(['trip-a']);
    const still = await screen.findByTestId('movie-summary');
    expect(still).toHaveTextContent('220 frames');
    expect(still).not.toHaveTextContent('going by the last GIF');
  });

  it('learns nothing about GIFs from a video, or from a file too small to say anything', async () => {
    reads.movie = ready(movieTrip('trip-a', 'Alpha'));
    recordMovie.mockResolvedValue(new Blob(['GIF89a'], { type: 'image/gif' }));
    open(['trip-a']);
    const exportButton = await screen.findByTestId('movie-export');
    await waitFor(() => expect(exportButton).not.toBeDisabled());
    fireEvent.click(exportButton);
    await waitFor(() => expect(saveBlob).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(screen.queryByTestId('movie-cancel')).not.toBeInTheDocument());
    expect(useUiPrefsStore.getState().movieGifCalibration).toBeUndefined();

    cleanup();
    useUiPrefsStore.setState({ movieSettings: normaliseMovieSettings({ ...DEFAULT_MOVIE_SETTINGS, format: 'webm' }) });
    recordMovie.mockResolvedValue(new Blob([new Uint8Array(3_000_000)], { type: 'video/webm' }));
    open(['trip-a']);
    const again = await screen.findByTestId('movie-export');
    await waitFor(() => expect(again).not.toBeDisabled());
    fireEvent.click(again);
    await waitFor(() => expect(saveBlob).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(screen.queryByTestId('movie-cancel')).not.toBeInTheDocument());
    expect(useUiPrefsStore.getState().movieGifCalibration).toBeUndefined();
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
    // It is saved under the name the dialog was showing.
    expect(screen.getByTestId('movie-file-name')).toHaveTextContent(name);
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

  it('does not draw its settings again for every frame an export records', async () => {
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
    fireEvent.click(exportButton);
    await waitFor(() => expect(recordMovie).toHaveBeenCalled());
    act(() => report({ stage: 'rendering', done: 1, total: 220, step: 1, steps: 220 }));
    await waitFor(() => expect(screen.getByTestId('movie-position').dataset.frame).toBe('0'));

    const before = settingsForm.renders;
    for (let step = 2; step <= 60; step++) {
      act(() => report({ stage: 'rendering', done: step, total: 220, step, steps: 220 }));
    }
    // The progress and the slider follow every frame...
    expect(screen.getByTestId('movie-position').dataset.frame).toBe('59');
    expect(screen.getByTestId('movie-progress')).toHaveTextContent('60');
    // ...while the settings, which cannot change during an export, are not drawn again for them.
    // Drawn for each of a long export's frames, a form of this size was a large share of its time.
    expect(settingsForm.renders - before).toBe(0);

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

    // No title is drawn, so nothing names the file: not the words written, not the trip, not the
    // cave. The dialog says so before the export, and the file is saved under what it said.
    const exportButton = screen.getByTestId('movie-export');
    await waitFor(() => expect(exportButton).not.toBeDisabled());
    const neutral = screen.getByTestId('movie-file-name').textContent ?? '';
    expect(neutral).toMatch(/silexgis-movie-\d{4}-\d{2}-\d{2}\.gif$/);
    for (const word of ['alpha', 'pestera', 'another']) {
      expect(neutral.toLowerCase()).not.toContain(word);
    }
    fireEvent.click(exportButton);
    await waitFor(() => expect(saveBlob).toHaveBeenCalledTimes(1));
    expect(recordMovie.mock.calls[0][0].title).toBeNull();
    expect(saveBlob.mock.calls[0][1]).toMatch(/^silexgis-movie-\d{4}-\d{2}-\d{2}\.gif$/);
    expect(neutral).toContain(saveBlob.mock.calls[0][1] as string);

    // Drawn, it names the file — and the dialog shows that the moment the caption is switched on.
    await waitFor(() => expect(exportButton).not.toBeDisabled());
    fireEvent.click(screen.getByTestId('movie-caption-title'));
    await waitFor(() =>
      expect(screen.getByTestId('movie-file-name')).toHaveTextContent(/silexgis-another-cave-entirely-/),
    );
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

    // Nothing from here on waits for the machine: a key on the slider is answered before the event
    // returns, and the frames of the preview's play are handed over by the test, each stamped with
    // the time the test says it is. Left to the real frame clock, how far play had got after a wait
    // was a question about how busy the machine was.
    const frameShown = () => screen.getByTestId('movie-position').dataset.frame;
    fireEvent.keyDown(handle, { key: 'End', code: 'End', keyCode: 35 });
    // The default movie: 20 s at 10 frames a second and a 2 s still, so the last frame is the 220th.
    expect(frameShown()).toBe('219');
    // The captions follow the slider to the moment it shows, and so does the moment written beside it.
    expect(clockOf(drawMovieCaptions.mock.calls.at(-1)!)).not.toEqual(first);
    expect(screen.getByTestId('movie-moment').textContent).not.toBe(firstMoment);
    expect(handle).toHaveAttribute('aria-valuetext', screen.getByTestId('movie-moment').textContent);

    fireEvent.keyDown(handle, { key: 'Home', code: 'Home', keyCode: 36 });
    expect(frameShown()).toBe('0');

    const frames: FrameRequestCallback[] = [];
    const raf = vi.spyOn(window, 'requestAnimationFrame').mockImplementation((callback) => {
      frames.push(callback);
      return frames.length;
    });
    const viewer = preview.viewer as ReturnType<typeof fakeViewer>;
    viewer.setCameraAngles.mockClear();
    try {
      // Play is pressed at a moment the test names, so every frame's distance from it is known.
      const pressed = vi.spyOn(performance, 'now').mockReturnValue(50_000);
      fireEvent.click(screen.getByTestId('movie-play'));
      pressed.mockRestore();
      expect(frames).toHaveLength(1);
      // Nothing has moved before a frame is drawn: the camera was only brought to rest.
      expect(frameShown()).toBe('0');
      expect(viewer.setCameraAngles.mock.calls).toEqual([[{}]]);

      // A quarter of a second on, at ten frames a second, the movie is at its third frame...
      act(() => frames[0](50_250));
      expect(frameShown()).toBe('2');
      // ...and playing turns the camera with the movie: from where it stood when play was pressed,
      // round by what the movie has turned by that frame, at the tilt it had.
      const turned = viewer.setCameraAngles.mock.calls.at(-1)![0] as { azimuth: number; polar: number };
      expect(turned.polar).toBe(1);
      expect(turned.azimuth).not.toBe(0.5);

      // It goes on asking for frames, and each shows the moment the time passed says, however
      // unevenly they come: a second after play was pressed is the eleventh frame.
      expect(frames).toHaveLength(2);
      act(() => frames[1](51_000));
      expect(frameShown()).toBe('10');
      expect(frames).toHaveLength(3);

      // Pressed again, it stops where it is and asks for no more frames.
      fireEvent.click(screen.getByTestId('movie-play'));
      expect(frameShown()).toBe('10');
      expect(frames).toHaveLength(3);
    } finally {
      raf.mockRestore();
    }
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

  it('offers the surface over the cave only where the survey’s file carries one, and starts with it off', async () => {
    // A choice remembered from a model that has terrain, opened on one that has none of its own.
    useUiPrefsStore.setState({
      movieSettings: normaliseMovieSettings({
        ...DEFAULT_MOVIE_SETTINGS,
        view: { ...DEFAULT_MOVIE_SETTINGS.view, terrain: true },
      }),
    });
    open();
    fireEvent.click(await screen.findByText('View'));
    const refused = await screen.findByTestId('movie-terrain');
    expect(refused).toBeDisabled();
    expect(refused).not.toBeChecked();
    expect(screen.getByText(/carries no terrain of its own/)).toBeInTheDocument();

    // The same dialog over a model whose file does carry its terrain.
    cleanup();
    useUiPrefsStore.setState({ movieSettings: undefined });
    preview.viewer = { ...fakeViewer(), hasTerrain: true, hasRealTerrain: true } as never;
    open();
    fireEvent.click(await screen.findByText('View'));
    const offered = await screen.findByTestId('movie-terrain');
    await waitFor(() => expect(offered).toBeEnabled());
    // Off until asked for, and the reason is said beside it.
    expect(offered).not.toBeChecked();
    expect(screen.getByText(/can be placed on a map by whoever gets the file/)).toBeInTheDocument();
    expect(screen.queryByText(/carries no terrain of its own/)).not.toBeInTheDocument();

    fireEvent.click(offered);
    await waitFor(() => expect(screen.getByTestId('movie-terrain')).toBeChecked());
    expect(useUiPrefsStore.getState().movieSettings?.view.terrain).toBe(true);
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

  it('shows the name the file will get before anything is exported, and follows the format', async () => {
    reads.movie = ready(movieTrip('trip-a', 'Alpha'));
    open(['trip-a']);

    const name = await screen.findByTestId('movie-file-name');
    expect(name).toHaveTextContent(/^The file will be saved as silexgis-alpha-\d{4}-\d{2}-\d{2}\.gif$/);
    expect(recordMovie).not.toHaveBeenCalled();
    fireEvent.click(screen.getByTestId('movie-format-webm'));
    await waitFor(() => expect(screen.getByTestId('movie-file-name')).toHaveTextContent(/\.webm$/));
  });

  it('shows no file name while there is no trip to make a movie of', async () => {
    open();
    await screen.findByTestId('movie-summary');
    expect(screen.queryByTestId('movie-file-name')).not.toBeInTheDocument();
  });
});

describe('a video long enough to be written to its file as it is made', () => {
  function chosenFile(name = 'my weekend.webm') {
    const sink = new MemoryMovieSink();
    return { sink, choice: { kind: 'file', name, sink } satisfies MovieFileChoice };
  }

  async function openLongVideo() {
    disk.goes = true;
    useUiPrefsStore.setState({ movieSettings: normaliseMovieSettings({ ...DEFAULT_MOVIE_SETTINGS, format: 'webm' }) });
    reads.movie = ready(movieTrip('trip-a', 'Alpha'));
    open(['trip-a']);
    const exportButton = await screen.findByTestId('movie-export');
    await waitFor(() => expect(exportButton).not.toBeDisabled());
    return exportButton;
  }

  it('says beforehand that Export will ask where to save it, and asks nothing of a short one or of a GIF', async () => {
    const exportButton = await openLongVideo();
    expect(screen.getByTestId('movie-file-name')).toHaveTextContent(/Export asks where to save it, as silexgis-.*\.webm unless/);

    // The same movie as a GIF is held in memory whatever its size, and is saved as every GIF is.
    recordMovie.mockResolvedValue(new Blob(['GIF89a'], { type: 'image/gif' }));
    fireEvent.click(screen.getByTestId('movie-format-gif'));
    await waitFor(() => expect(screen.getByTestId('movie-file-name')).toHaveTextContent(/^The file will be saved as .*\.gif$/));
    fireEvent.click(exportButton);
    await waitFor(() => expect(saveBlob).toHaveBeenCalledTimes(1));
    expect(disk.choose).not.toHaveBeenCalled();
    expect(recordMovieToSink).not.toHaveBeenCalled();
  });

  it('asks on the press, records into the chosen file, and saves nothing a second time', async () => {
    const exportButton = await openLongVideo();
    const { sink, choice } = chosenFile();
    disk.choose.mockResolvedValue(choice);
    let finish!: () => void;
    recordMovieToSink.mockImplementation(
      () =>
        new Promise<null>((resolve) => {
          finish = () => resolve(null);
        }),
    );
    fireEvent.click(exportButton);

    await waitFor(() => expect(recordMovieToSink).toHaveBeenCalledTimes(1));
    // Asked under the name the dialog was showing, for the format being made.
    expect(disk.choose).toHaveBeenCalledTimes(1);
    expect(disk.choose.mock.calls[0][0]).toMatch(/^silexgis-.*\.webm$/);
    expect(disk.choose.mock.calls[0][1]).toBe('webm');
    expect(recordMovieToSink.mock.calls[0][1]).toBe(sink);
    expect(recordMovieToSink.mock.calls[0][0].settings.format).toBe('webm');
    // While it runs the dialog names the file the reader chose, not the one it had suggested.
    expect(screen.getByTestId('movie-file-name')).toHaveTextContent('The file will be saved as my weekend.webm');
    await act(async () => finish());

    expect(await screen.findByText('Saved my weekend.webm')).toBeInTheDocument();
    expect(recordMovie).not.toHaveBeenCalled();
    expect(saveBlob).not.toHaveBeenCalled();
    // The recorder was given the sink, and ending it was the recorder's.
    expect(sink.state).toBe('open');
    expect(screen.queryByTestId('movie-export-failed')).not.toBeInTheDocument();
  });

  it('starts nothing when the reader closes the question: no export, no word, and Export can be pressed again', async () => {
    const exportButton = await openLongVideo();
    disk.choose.mockResolvedValue({ kind: 'dismissed' });
    fireEvent.click(exportButton);

    await waitFor(() => expect(disk.choose).toHaveBeenCalledTimes(1));
    await act(async () => {});
    expect(recordMovieToSink).not.toHaveBeenCalled();
    expect(recordMovie).not.toHaveBeenCalled();
    expect(saveBlob).not.toHaveBeenCalled();
    expect(screen.queryByTestId('movie-progress')).not.toBeInTheDocument();
    expect(screen.queryByTestId('movie-cancel')).not.toBeInTheDocument();
    expect(screen.queryByTestId('movie-export-failed')).not.toBeInTheDocument();
    expect(exportButton).not.toBeDisabled();

    // The positive case beside it: the same press, answered, does export.
    const { choice } = chosenFile();
    disk.choose.mockResolvedValue(choice);
    recordMovieToSink.mockResolvedValue(null);
    fireEvent.click(exportButton);
    await waitFor(() => expect(recordMovieToSink).toHaveBeenCalledTimes(1));
  });

  it('asks once, however often Export is pressed while the question is open', async () => {
    const exportButton = await openLongVideo();
    let answer!: (choice: MovieFileChoice) => void;
    disk.choose.mockImplementation(() => new Promise<MovieFileChoice>((resolve) => (answer = resolve)));
    fireEvent.click(exportButton);
    fireEvent.click(exportButton);
    await waitFor(() => expect(disk.choose).toHaveBeenCalledTimes(1));
    await act(async () => answer({ kind: 'dismissed' }));
    expect(disk.choose).toHaveBeenCalledTimes(1);
  });

  it('makes the movie in memory, and saves it as any other, where the browser would not ask after all', async () => {
    const exportButton = await openLongVideo();
    disk.choose.mockResolvedValue({ kind: 'memory' });
    const file = new Blob(['webm'], { type: 'video/webm' });
    recordMovie.mockResolvedValue(file);
    fireEvent.click(exportButton);

    await waitFor(() => expect(saveBlob).toHaveBeenCalledTimes(1));
    expect(recordMovieToSink).not.toHaveBeenCalled();
    const [saved, name] = saveBlob.mock.calls[0] as [Blob, string];
    expect(saved).toBe(file);
    expect(name).toMatch(/^silexgis-.*\.webm$/);
  });

  it('saves the file the recorder answers when it could not be written as it was made', async () => {
    const exportButton = await openLongVideo();
    const { choice } = chosenFile('chosen.webm');
    disk.choose.mockResolvedValue(choice);
    const file = new Blob(['webm'], { type: 'video/webm' });
    recordMovieToSink.mockResolvedValue(file);
    fireEvent.click(exportButton);

    await waitFor(() => expect(saveBlob).toHaveBeenCalledTimes(1));
    expect(saveBlob.mock.calls[0]).toEqual([file, 'chosen.webm']);
  });

  it('a cancel ends it without a word, and a failure says so, as for any export', async () => {
    const exportButton = await openLongVideo();
    const { choice } = chosenFile();
    disk.choose.mockResolvedValue(choice);
    recordMovieToSink.mockImplementation(
      (recording) =>
        new Promise<null>((_resolve, reject) => {
          recording.signal?.addEventListener('abort', () => reject(new DOMException('cancelled', 'AbortError')));
        }),
    );
    fireEvent.click(exportButton);
    fireEvent.click(await screen.findByTestId('movie-cancel'));
    await waitFor(() => expect(screen.queryByTestId('movie-cancel')).not.toBeInTheDocument());
    expect(screen.queryByTestId('movie-export-failed')).not.toBeInTheDocument();
    expect(saveBlob).not.toHaveBeenCalled();

    // The button was replaced by Cancel while the export ran: it is the new one that is pressed.
    recordMovieToSink.mockRejectedValue(new Error('WEBM encoder, write: the disk is full'));
    const again = await screen.findByTestId('movie-export');
    await waitFor(() => expect(again).not.toBeDisabled());
    fireEvent.click(again);
    expect(await screen.findByTestId('movie-export-failed')).toHaveTextContent('the disk is full');
    expect(saveBlob).not.toHaveBeenCalled();
  });

  it('takes the chosen file away when the export ends before a frame is made', async () => {
    disk.goes = true;
    useUiPrefsStore.setState({ movieSettings: normaliseMovieSettings({ ...DEFAULT_MOVIE_SETTINGS, format: 'webm' }) });
    const live = movieTrip('trip-a', 'Alpha');
    live.trip.tracking = { ...live.trip.tracking, state: 'armed', closedAt: null } as TrackingState;
    reads.movie = { ...ready(live), rereadLive: vi.fn(() => Promise.reject(new Error('the log could not be read'))) };
    const { sink, choice } = chosenFile();
    disk.choose.mockResolvedValue(choice);
    open(['trip-a']);
    const exportButton = await screen.findByTestId('movie-export');
    await waitFor(() => expect(exportButton).not.toBeDisabled());
    fireEvent.click(exportButton);

    expect(await screen.findByTestId('movie-log-unavailable')).toBeInTheDocument();
    expect(recordMovieToSink).not.toHaveBeenCalled();
    await waitFor(() => expect(sink.state).toBe('aborted'));
  });
});

describe('the remembered shading', () => {
  it('is remembered by its name, and handed to the export by its name', async () => {
    reads.movie = ready(movieTrip('trip-a', 'Alpha'));
    recordMovie.mockResolvedValue(new Blob(['GIF89a'], { type: 'image/gif' }));
    open(['trip-a']);
    fireEvent.click(await screen.findByText('View'));
    const choice = await screen.findByTestId('movie-shading');
    await waitFor(() => expect(within(choice).getByRole('combobox')).not.toBeDisabled());
    expect(choice).toHaveTextContent('As the viewer draws it');

    fireEvent.mouseDown(within(choice).getByRole('combobox'));
    fireEvent.click((await screen.findAllByTitle('By leg length')).at(-1)!);

    await waitFor(() => expect(useUiPrefsStore.getState().movieSettings?.view.shading).toBe('length'));
    // Nothing of the viewer's own numbering is kept in the browser.
    expect(JSON.parse(localStorage.getItem('silexgis.uiPrefs')!).state.movieSettings.view).not.toHaveProperty(
      'shadingMode',
    );
    const exportButton = screen.getByTestId('movie-export');
    await waitFor(() => expect(exportButton).not.toBeDisabled());
    fireEvent.click(exportButton);
    await waitFor(() => expect(recordMovie).toHaveBeenCalled());
    expect(recordMovie.mock.calls[0][0].settings.view.shading).toBe('length');
  });

  it('reads a shading remembered as the viewer’s number once, and writes it back by name without being asked', async () => {
    // What an earlier version left in the browser: the viewer's number for "by survey".
    const { shading: _name, ...view } = DEFAULT_MOVIE_SETTINGS.view;
    useUiPrefsStore.setState({
      movieSettings: { ...DEFAULT_MOVIE_SETTINGS, format: 'webm', view: { ...view, shadingMode: 6 } } as never,
    });
    open();

    // Written back although the reader changed nothing — and the rest of what was remembered with it.
    await waitFor(() => expect(useUiPrefsStore.getState().movieSettings?.view.shading).toBe('survey'));
    expect(useUiPrefsStore.getState().movieSettings?.view).not.toHaveProperty('shadingMode');
    expect(useUiPrefsStore.getState().movieSettings?.format).toBe('webm');
    fireEvent.click(await screen.findByText('View'));
    expect(await screen.findByTestId('movie-shading')).toHaveTextContent('By survey');
  });

  it('does not write settings back that are already in today’s shape', async () => {
    const stored = normaliseMovieSettings({ ...DEFAULT_MOVIE_SETTINGS, format: 'webm' });
    useUiPrefsStore.setState({ movieSettings: stored });
    open();
    await screen.findByTestId('movie-summary');
    // The very object that was stored: nothing replaced it.
    expect(useUiPrefsStore.getState().movieSettings).toBe(stored);
  });
});

describe('closing the dialog while an export runs', () => {
  // The question is a dialog of its own, over the movie's; counted by its own frame, since its
  // title is written in it twice — once for the eye and once for assistive technology.
  const questions = () => document.querySelectorAll('.ant-modal-confirm').length;
  const asked = () => waitFor(() => expect(questions()).toBe(1));
  const notAsked = () => expect(questions()).toBe(0);
  const escape = (on: Element) => fireEvent.keyDown(on, { key: 'Escape', code: 'Escape', keyCode: 27 });

  /** An export that runs until it is finished or stopped, under a parent that closes when asked. */
  async function exporting() {
    reads.movie = ready(movieTrip('trip-a', 'Alpha'));
    const run: { signal?: AbortSignal; finish?: () => void } = {};
    recordMovie.mockImplementation(
      (recording) =>
        new Promise<Blob>((resolve, reject) => {
          run.signal = recording.signal;
          run.finish = () => resolve(new Blob(['GIF89a'], { type: 'image/gif' }));
          recording.signal?.addEventListener('abort', () => reject(new DOMException('cancelled', 'AbortError')));
        }),
    );
    const { onClose } = openClosable(['trip-a']);
    const exportButton = await screen.findByTestId('movie-export');
    await waitFor(() => expect(exportButton).not.toBeDisabled());
    fireEvent.click(exportButton);
    await screen.findByTestId('movie-cancel');
    await waitFor(() => expect(run.signal).toBeDefined());
    return { onClose, run };
  }

  it('closes at once on Escape when no export is running', async () => {
    reads.movie = ready(movieTrip('trip-a', 'Alpha'));
    const { onClose } = openClosable(['trip-a']);
    const exportButton = await screen.findByTestId('movie-export');
    await waitFor(() => expect(exportButton).not.toBeDisabled());

    escape(exportButton);

    await waitFor(() => expect(onClose).toHaveBeenCalledTimes(1));
    notAsked();
    // The preview goes with the dialog, and with it the viewer it holds.
    await waitFor(() => expect(preview.released).toBe(1));
  });

  it('asks on Escape, and keeping going loses nothing of the export', async () => {
    const { onClose, run } = await exporting();

    escape(screen.getByTestId('movie-cancel'));

    await asked();
    expect(document.querySelector('.ant-modal-confirm-title')).toHaveTextContent('Stop making the movie?');
    expect(document.querySelector('.ant-modal-confirm-content')).toHaveTextContent('no file is saved');
    // It opens on the answer that loses nothing, so Enter pressed by habit keeps the export.
    await waitFor(() => expect(screen.getByRole('button', { name: 'Keep going' })).toHaveFocus());
    expect(screen.getByRole('button', { name: 'Stop and close' })).not.toHaveFocus();
    expect(run.signal?.aborted).toBe(false);
    expect(onClose).not.toHaveBeenCalled();
    // A second request to close while the question is up does not ask twice, and does not slip
    // past the question.
    escape(screen.getByTestId('movie-cancel'));
    fireEvent.click(document.querySelector('.ant-modal-close')!);
    expect(questions()).toBe(1);
    expect(onClose).not.toHaveBeenCalled();
    expect(run.signal?.aborted).toBe(false);

    fireEvent.click(screen.getByRole('button', { name: 'Keep going' }));

    await waitFor(notAsked);
    expect(run.signal?.aborted).toBe(false);
    expect(onClose).not.toHaveBeenCalled();
    expect(preview.released).toBe(0);
    // The export it was asked about finishes and is saved as if nothing had been pressed.
    await act(async () => run.finish?.());
    await waitFor(() => expect(saveBlob).toHaveBeenCalledTimes(1));
    expect(screen.getByTestId('movie-export')).toBeInTheDocument();
  });

  it('asks on the X, and stopping calls the export off, closes, and gives back what the dialog held', async () => {
    const errors = vi.spyOn(console, 'error');
    const { onClose, run } = await exporting();

    fireEvent.click(document.querySelector('.ant-modal-close')!);

    await asked();
    expect(run.signal?.aborted).toBe(false);
    expect(preview.released).toBe(0);

    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'Stop and close' }));
    });

    // The recording is told to stop — which is what has it close its encoder and end its capture —
    // at once, not when the dialog has finished going away.
    expect(run.signal?.aborted).toBe(true);
    expect(onClose).toHaveBeenCalledTimes(1);
    // And the preview is taken down with the dialog, as on any close: that is where the viewer is
    // disposed of and its WebGL context given back.
    await waitFor(() => expect(preview.released).toBe(1));
    await waitFor(notAsked);
    // Calling it off is not a failure: nothing saved, nothing reported, nothing logged.
    expect(saveBlob).not.toHaveBeenCalled();
    expect(screen.queryByTestId('movie-export-failed')).not.toBeInTheDocument();
    expect(errors).not.toHaveBeenCalled();
    errors.mockRestore();
  });

  it('takes the question away when the export ends under it, and leaves the dialog open', async () => {
    const { onClose, run } = await exporting();
    escape(screen.getByTestId('movie-cancel'));
    await asked();

    await act(async () => run.finish?.());

    await waitFor(() => expect(saveBlob).toHaveBeenCalledTimes(1));
    await waitFor(notAsked);
    expect(onClose).not.toHaveBeenCalled();
    expect(preview.released).toBe(0);
    // With no export left to ask about, closing closes at once again. By the X, not by Escape: the
    // modal library hands Escape to the topmost open dialog by an id that is one fixed value under
    // test, so a question that has come and gone takes the movie dialog's turn away with it here —
    // which no browser does, where every dialog has an id of its own.
    fireEvent.click(document.querySelector('.ant-modal-close')!);
    await waitFor(() => expect(onClose).toHaveBeenCalledTimes(1));
    notAsked();
  });

  it('still stops at once on Cancel export, with no question, and stays open', async () => {
    const { onClose, run } = await exporting();

    await act(async () => {
      fireEvent.click(screen.getByTestId('movie-cancel'));
    });

    expect(run.signal?.aborted).toBe(true);
    notAsked();
    expect(onClose).not.toHaveBeenCalled();
    expect(preview.released).toBe(0);
    await waitFor(() => expect(screen.queryByTestId('movie-progress')).not.toBeInTheDocument());
    // The export it stopped is over, so there is nothing to ask about on the way out.
    escape(screen.getByTestId('movie-export'));
    await waitFor(() => expect(onClose).toHaveBeenCalledTimes(1));
    notAsked();
  });

  it('ignores a click beside the dialog, export or no export', async () => {
    const { onClose, run } = await exporting();
    const beside = document.querySelector('.ant-modal-wrap')!;

    fireEvent.mouseDown(beside);
    fireEvent.mouseUp(beside);
    fireEvent.click(beside);

    notAsked();
    expect(run.signal?.aborted).toBe(false);
    expect(onClose).not.toHaveBeenCalled();
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

  it('says a ticked trip is still under way and how many reports have come in since, and nothing of the kind for a finished one', async () => {
    const live = movieTrip('trip-a', 'Alpha');
    live.trip.tracking = { ...live.trip.tracking, state: 'armed', closedAt: null } as TrackingState;
    const done = movieTrip('trip-b', 'Bravo');
    reads.movie = { ...ready(live, done), newReports: new Map([['trip-a', 0]]) };
    open(['trip-a', 'trip-b']);

    expect(await screen.findByTestId('movie-trip-live-trip-a')).toHaveTextContent(
      'Still under way · new reports since opening: 0',
    );
    // The finished trip beside it is in the movie too, and has nothing to say about arriving reports.
    expect(within(screen.getByTestId('movie-trip-trip-b')).getByRole('checkbox')).toBeChecked();
    expect(screen.queryByTestId('movie-trip-live-trip-b')).not.toBeInTheDocument();

    // The count is the one the trips' reads keep, which is where the log is read again; the row
    // follows it. Unticking the finished trip is only what has the dialog drawn again here.
    reads.movie = { ...ready(live), newReports: new Map([['trip-a', 2]]) };
    fireEvent.click(within(screen.getByTestId('movie-trip-trip-b')).getByRole('checkbox'));
    await waitFor(() =>
      expect(screen.getByTestId('movie-trip-live-trip-a')).toHaveTextContent(
        'Still under way · new reports since opening: 2',
      ),
    );
  });

  it('ends a trip still under way when the export starts, on a log read again at that moment', async () => {
    const report = (id: string, recordedAt: string) =>
      ({ id, recordedAt, surveyModelId: MODEL, kind: 'note', caverIds: [] }) as unknown as TrackingEvent;
    const live = movieTrip('trip-a', 'Alpha');
    live.trip.tracking = { ...live.trip.tracking, state: 'armed', closedAt: null } as TrackingState;
    // As it was folded when the dialog opened, which for this trip was two hours into it.
    const openedAt = Date.parse(ARMED) + 2 * 3_600_000;
    live.span.window = { from: Date.parse(ARMED), to: openedAt };
    live.trip.events = [report('r1', ARMED)];
    // What the log reads when the export asks for it again: a report recorded elsewhere, an hour
    // after the dialog was opened.
    const arrived = report('r2', new Date(openedAt + 3_600_000).toISOString());
    const rereadLive = vi.fn(() => Promise.resolve([{ ...live.trip, events: [arrived, ...live.trip.events] }]));
    reads.movie = { ...ready(live), rereadLive };
    recordMovie.mockResolvedValue(new Blob(['GIF89a'], { type: 'image/gif' }));
    open(['trip-a']);

    const exportButton = await screen.findByTestId('movie-export');
    await waitFor(() => expect(exportButton).not.toBeDisabled());
    const endsBefore = new Set(reads.movieEndsAsked);
    expect(endsBefore.size).toBe(1);
    expect(rereadLive).not.toHaveBeenCalled();
    const pressedAt = Date.now();
    fireEvent.click(exportButton);
    await waitFor(() => expect(saveBlob).toHaveBeenCalledTimes(1));
    const finishedAt = Date.now();

    // The log was read again, once, and the movie was recorded from what it read then — the report
    // that arrived while the dialog stood open is in the file, not only the stretch of time.
    expect(rereadLive).toHaveBeenCalledTimes(1);
    const { timeline, trips } = recordMovie.mock.calls[0][0];
    expect(trips.map((trip) => trip.events.map((event) => event.id))).toEqual([['r2', 'r1']]);
    // The movie that was recorded runs to the moment its export began...
    const end = timeline.clock(timeline.length);
    expect(end.kind).toBe('calendar');
    const endAt = (end as { at: number }).at;
    expect(endAt).toBeGreaterThanOrEqual(pressedAt);
    expect(endAt).toBeLessThanOrEqual(finishedAt);
    expect(endAt).toBeGreaterThan(openedAt);
    // ...and the preview is moved to that same end, so what is looked at afterwards is what was made.
    expect(reads.movieEndsAsked.at(-1)).toBe(endAt);
    // While the export ran the reads were told to leave the logs alone, and not before or after.
    expect(reads.moviePausedAsked[0]).toBe(false);
    expect(reads.moviePausedAsked).toContain(true);
    await waitFor(() => expect(reads.moviePausedAsked.at(-1)).toBe(false));
  });

  it('exports nothing of a trip still under way whose log cannot be read again, and says what the replay says', async () => {
    const live = movieTrip('trip-a', 'Alpha');
    live.trip.tracking = { ...live.trip.tracking, state: 'armed', closedAt: null } as TrackingState;
    const rereadLive = vi.fn(() => Promise.reject(new Error('the tracking log has more pages than one read follows')));
    reads.movie = { ...ready(live), rereadLive };
    open(['trip-a']);

    const exportButton = await screen.findByTestId('movie-export');
    await waitFor(() => expect(exportButton).not.toBeDisabled());
    expect(screen.queryByTestId('movie-log-unavailable')).not.toBeInTheDocument();
    fireEvent.click(exportButton);

    expect(await screen.findByTestId('movie-log-unavailable')).toHaveTextContent('The log could not be read');
    expect(recordMovie).not.toHaveBeenCalled();
    expect(saveBlob).not.toHaveBeenCalled();
    // Nothing is left running, and the export can be asked for again — which reads again.
    await waitFor(() => expect(screen.queryByTestId('movie-cancel')).not.toBeInTheDocument());
    await waitFor(() => expect(exportButton).not.toBeDisabled());
    recordMovie.mockResolvedValue(new Blob(['GIF89a'], { type: 'image/gif' }));
    rereadLive.mockImplementation(() => Promise.resolve([live.trip]) as never);
    fireEvent.click(exportButton);
    await waitFor(() => expect(saveBlob).toHaveBeenCalledTimes(1));
    expect(screen.queryByTestId('movie-log-unavailable')).not.toBeInTheDocument();
  });

  it('leaves a finished trip’s movie exactly as it was previewed, whenever it is exported', async () => {
    reads.movie = ready(movieTrip('trip-a', 'Alpha'));
    recordMovie.mockResolvedValue(new Blob(['GIF89a'], { type: 'image/gif' }));
    open(['trip-a']);

    const exportButton = await screen.findByTestId('movie-export');
    await waitFor(() => expect(exportButton).not.toBeDisabled());
    fireEvent.click(exportButton);
    await waitFor(() => expect(saveBlob).toHaveBeenCalledTimes(1));

    const { timeline } = recordMovie.mock.calls[0][0];
    expect(timeline.clock(timeline.length)).toEqual({ kind: 'calendar', at: Date.parse(CLOSED) });
    // Nothing was read again as of another moment — and no log was read again at all, which the
    // stand-in for the trips' reads would have refused.
    expect(new Set(reads.movieEndsAsked).size).toBe(1);
    expect(screen.queryByTestId('movie-trip-live-trip-a')).not.toBeInTheDocument();
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
  it('keeps the keys pressed on its controls from the viewer’s document-wide shortcuts, and lets the modal have Escape', async () => {
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
      // Tab as well, from the controls the viewer does not leave alone by itself: a Tab that
      // reached it with the pointer on the preview was cancelled, and the focus never moved.
      fireEvent.keyDown(screen.getByTestId('movie-export'), { key: 'Tab' });
      fireEvent.keyDown(screen.getByTestId('movie-play'), { key: 'Tab', shiftKey: true });
      fireEvent.keyDown(screen.getByTestId('movie-caption-title'), { key: 'Tab' });
      expect(seen).toEqual([]);
      // The X in the corner and the dialog's own frame — where the focus is put on opening — are
      // drawn outside the dialog's body, and are the dialog's all the same: a Tab that left the X,
      // an Enter that pressed it, the first Tab after opening.
      const frame = screen.getByRole('dialog');
      const corner = frame.querySelector<HTMLElement>('.ant-modal-close');
      expect(corner).not.toBeNull();
      expect(corner!.closest('.movie-dialog')).toBeNull();
      fireEvent.keyDown(corner!, { key: 'Tab' });
      fireEvent.keyDown(corner!, { key: 'Tab', shiftKey: true });
      fireEvent.keyDown(corner!, { key: 'Enter' });
      fireEvent.keyDown(corner!, { key: ' ' });
      fireEvent.keyDown(frame, { key: 'Tab' });
      expect(seen).toEqual([]);
      // Escape is the one key the modal hears outside the dialog's own tree.
      fireEvent.keyDown(title, { key: 'Escape' });
      expect(seen).toEqual(['Escape']);
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

describe('who appears in the movie', () => {
  const ROSTER: Record<string, string> = { 'caver-1': 'Ana Popescu', 'caver-2': 'Bogdan Ionescu' };

  /** A trip with two cavers on its roster, each reported at a station of the model. */
  function tripOfTwo(tripLogId: string, title: string) {
    const made = movieTrip(tripLogId, title);
    const stations: Record<string, string> = { 'caver-1': 'p8.1', 'caver-2': 'p8.2' };
    const ids = Object.keys(ROSTER);
    return {
      trip: {
        ...made.trip,
        nameOf: (caverId: string) => ROSTER[caverId] ?? '?',
        events: ids.map((caverId, index) => ({
          id: `${tripLogId}-event-${index}`,
          caverId,
          teamId: null,
          kind: 'atStation',
          surveyModelId: MODEL,
          stationName: stations[caverId],
          depthEnteredM: null,
          note: null,
          recordedAt: ARMED,
        })) as unknown as TrackingEvent[],
        tracking: {
          ...made.trip.tracking,
          participants: ids.map((caverId) => ({
            caverId,
            teamId: null,
            lastKind: 'atStation',
            lastRecordedAt: ARMED,
            positionRecordedAt: ARMED,
            stationName: stations[caverId],
            depthM: null,
            positionSurveyModelId: MODEL,
          })),
        } as unknown as TrackingState,
      },
      span: { ...made.span, moments: [Date.parse(ARMED)] },
    };
  }

  const cavers = (tripLogId: string) => screen.getByTestId(`movie-trip-cavers-${tripLogId}`);
  const unfold = (tripLogId: string) => fireEvent.click(screen.getByTestId(`movie-trip-cavers-toggle-${tripLogId}`));
  const caverBox = (tripLogId: string, name: string) => within(cavers(tripLogId)).getByRole('checkbox', { name });

  it('lists a ticked trip’s roster folded away, everybody ticked, and nothing under a trip that is not ticked', async () => {
    reads.movie = ready(tripOfTwo('trip-a', 'Alpha'));
    open(['trip-a']);

    const toggle = await screen.findByTestId('movie-trip-cavers-toggle-trip-a');
    expect(toggle).toHaveTextContent('Who appears: 2 of 2');
    expect(toggle).toHaveAttribute('aria-expanded', 'false');
    // Folded: the names are not on the screen until it is opened.
    expect(within(cavers('trip-a')).queryAllByRole('checkbox')).toHaveLength(0);
    expect(screen.queryByTestId('movie-trip-cavers-trip-b')).not.toBeInTheDocument();

    unfold('trip-a');
    expect(toggle).toHaveAttribute('aria-expanded', 'true');
    expect(caverBox('trip-a', 'Ana Popescu')).toBeChecked();
    expect(caverBox('trip-a', 'Bogdan Ionescu')).toBeChecked();
    expect(cavers('trip-a')).toHaveTextContent('it is not remembered');
    // The roster is the trip's, not the trip's own tick box: unticking a person leaves the trip in.
    fireEvent.click(caverBox('trip-a', 'Bogdan Ionescu'));
    expect(within(screen.getByTestId('movie-trip-trip-a')).getAllByRole('checkbox')[0]).toBeChecked();
    expect(toggle).toHaveTextContent('Who appears: 1 of 2');
  });

  it('takes an unticked caver off the preview and out of the file, and puts them back when ticked again', async () => {
    const viewer = preview.viewer as ReturnType<typeof fakeViewer>;
    reads.movie = ready(tripOfTwo('trip-a', 'Alpha'));
    recordMovie.mockResolvedValue(new Blob(['GIF89a'], { type: 'image/gif' }));
    open(['trip-a']);

    // Both stand on the preview first, and a file made now has nobody left out.
    await waitFor(() => expect(viewer.addLiveMarker).toHaveBeenCalledTimes(2));
    const exportButton = await screen.findByTestId('movie-export');
    await waitFor(() => expect(exportButton).not.toBeDisabled());
    fireEvent.click(exportButton);
    await waitFor(() => expect(saveBlob).toHaveBeenCalledTimes(1));
    expect([...recordMovie.mock.calls[0][0].excluded]).toEqual([]);
    await waitFor(() => expect(screen.queryByTestId('movie-cancel')).not.toBeInTheDocument());

    unfold('trip-a');
    fireEvent.click(caverBox('trip-a', 'Bogdan Ionescu'));
    await waitFor(() => expect(viewer.removeLiveMarker).toHaveBeenCalledWith('trip-a:caver-2'));
    expect(viewer.removeLiveMarker).not.toHaveBeenCalledWith('trip-a:caver-1');

    await waitFor(() => expect(exportButton).not.toBeDisabled());
    fireEvent.click(exportButton);
    await waitFor(() => expect(saveBlob).toHaveBeenCalledTimes(2));
    expect([...recordMovie.mock.calls[1][0].excluded]).toEqual(['trip-a:caver-2']);
    await waitFor(() => expect(screen.queryByTestId('movie-cancel')).not.toBeInTheDocument());

    fireEvent.click(caverBox('trip-a', 'Bogdan Ionescu'));
    await waitFor(() => expect(viewer.addLiveMarker).toHaveBeenCalledTimes(3));
    expect(viewer.addLiveMarker).toHaveBeenLastCalledWith('trip-a:caver-2', 'p8.2', expect.anything());
  });

  it('paces the preview and the file by the reports of the people who appear', async () => {
    // Ana is reported at the start and the end of the trip, Bogdan alone half-way through.
    const made = tripOfTwo('trip-a', 'Alpha');
    const MIDDLE = new Date((Date.parse(ARMED) + Date.parse(CLOSED)) / 2).toISOString();
    const [ana, bogdan] = made.trip.events;
    const events = [ana, { ...bogdan, recordedAt: MIDDLE }, { ...ana, id: 'trip-a-event-last', recordedAt: CLOSED }];
    const moments = events.map((event) => Date.parse(event.recordedAt));
    reads.movie = ready({ trip: { ...made.trip, events }, span: { ...made.span, moments } });
    recordMovie.mockResolvedValue(new Blob(['GIF89a'], { type: 'image/gif' }));
    recordMovieStill.mockResolvedValue(new Blob(['png'], { type: 'image/png' }));
    open(['trip-a']);

    const exportButton = await screen.findByTestId('movie-export');
    await waitFor(() => expect(exportButton).not.toBeDisabled());
    fireEvent.click(exportButton);
    await waitFor(() => expect(saveBlob).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(screen.queryByTestId('movie-cancel')).not.toBeInTheDocument());
    const withHim = recordMovie.mock.calls[0][0].timeline;

    unfold('trip-a');
    fireEvent.click(caverBox('trip-a', 'Bogdan Ionescu'));
    await waitFor(() => expect(exportButton).not.toBeDisabled());
    fireEvent.click(exportButton);
    await waitFor(() => expect(saveBlob).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(screen.queryByTestId('movie-cancel')).not.toBeInTheDocument());
    const withoutHim = recordMovie.mock.calls[1][0].timeline;

    // Quiet stretches are shortened by default: with him the clock stops around the middle of the
    // trip, without him nothing in the file marks the moment he was reported.
    expect(withoutHim.length).toBeLessThan(withHim.length);
    // A picture is of the same movie as the file.
    const still = screen.getByTestId('movie-still');
    await waitFor(() => expect(still).not.toBeDisabled());
    fireEvent.click(still);
    await waitFor(() => expect(recordMovieStill).toHaveBeenCalledTimes(1));
    expect(recordMovieStill.mock.calls[0][0].timeline.length).toBe(withoutHim.length);
  });

  it('keeps the choice for this dialog only: not with the remembered settings, not in storage, not in the address', async () => {
    reads.movie = ready(tripOfTwo('trip-a', 'Alpha'));
    const address = window.location.href;
    open(['trip-a']);
    unfold('trip-a');
    fireEvent.click(caverBox('trip-a', 'Bogdan Ionescu'));
    expect(screen.getByTestId('movie-trip-cavers-toggle-trip-a')).toHaveTextContent('Who appears: 1 of 2');

    // A setting changed afterwards is what writes the remembered settings: they are written here,
    // so what is checked below is a store that has just been saved, not one nothing has touched.
    fireEvent.click(screen.getByText('Captions'));
    fireEvent.click(await screen.findByTestId('movie-caption-note'));
    await waitFor(() => expect(useUiPrefsStore.getState().movieSettings?.captions.note).toBe(true));

    const remembered = JSON.stringify(useUiPrefsStore.getState());
    const stored = Object.keys(localStorage).map((key) => `${key}=${localStorage.getItem(key)}`).join('\n');
    const session = Object.keys(sessionStorage).map((key) => `${key}=${sessionStorage.getItem(key)}`).join('\n');
    // The store did reach the browser's storage, with the setting just changed in it.
    expect(stored).toContain('"note":true');
    for (const held of [remembered, stored, session, window.location.href]) {
      expect(held).not.toContain('caver-2');
      expect(held).not.toContain('Bogdan');
    }
    expect(window.location.href).toBe(address);
  });

  it('leaves somebody unticked out still after the settings are reset or a preset is pressed', async () => {
    reads.movie = ready(tripOfTwo('trip-a', 'Alpha'));
    recordMovieStill.mockResolvedValue(new Blob(['png'], { type: 'image/png' }));
    open(['trip-a']);
    unfold('trip-a');
    fireEvent.click(caverBox('trip-a', 'Bogdan Ionescu'));

    fireEvent.click(screen.getByTestId('movie-reset'));
    fireEvent.click(screen.getByTestId('movie-preset-chat'));
    await waitFor(() => expect(useUiPrefsStore.getState().movieSettings?.size).toBe('480x270'));

    expect(screen.getByTestId('movie-trip-cavers-toggle-trip-a')).toHaveTextContent('Who appears: 1 of 2');
    expect(caverBox('trip-a', 'Bogdan Ionescu')).not.toBeChecked();
    // And a picture taken now is of the movie with that person left out.
    const still = screen.getByTestId('movie-still');
    await waitFor(() => expect(still).not.toBeDisabled());
    fireEvent.click(still);
    await waitFor(() => expect(saveBlob).toHaveBeenCalledTimes(1));
    expect([...recordMovieStill.mock.calls[0][0].excluded]).toEqual(['trip-a:caver-2']);
  });

  it('starts a trip with everybody again once it has been taken out of the movie and put back', async () => {
    reads.movie = ready(tripOfTwo('trip-a', 'Alpha'), tripOfTwo('trip-b', 'Bravo'));
    open(['trip-a', 'trip-b']);
    unfold('trip-a');
    unfold('trip-b');
    fireEvent.click(caverBox('trip-a', 'Bogdan Ionescu'));
    fireEvent.click(caverBox('trip-b', 'Ana Popescu'));
    expect(screen.getByTestId('movie-trip-cavers-toggle-trip-a')).toHaveTextContent('Who appears: 1 of 2');
    expect(screen.getByTestId('movie-trip-cavers-toggle-trip-b')).toHaveTextContent('Who appears: 1 of 2');

    const alpha = within(screen.getByTestId('movie-trip-trip-a')).getAllByRole('checkbox')[0];
    fireEvent.click(alpha);
    expect(screen.queryByTestId('movie-trip-cavers-trip-a')).not.toBeInTheDocument();
    fireEvent.click(alpha);
    expect(screen.getByTestId('movie-trip-cavers-toggle-trip-a')).toHaveTextContent('Who appears: 2 of 2');
    // The other trip's choice is its own, and stands.
    expect(screen.getByTestId('movie-trip-cavers-toggle-trip-b')).toHaveTextContent('Who appears: 1 of 2');
  });
});

describe('the time-lapse figure', () => {
  it('has a switch of its own, on to begin with, that goes with the clock’s', async () => {
    open(['trip-a']);
    fireEvent.click(await screen.findByText('Captions'));
    const speed = await screen.findByTestId('movie-caption-speed');
    expect(speed).toBeChecked();
    expect(speed).not.toBeDisabled();

    fireEvent.click(speed);
    await waitFor(() => expect(useUiPrefsStore.getState().movieSettings?.captions.speed).toBe(false));
    fireEvent.click(speed);
    await waitFor(() => expect(useUiPrefsStore.getState().movieSettings?.captions.speed).toBe(true));

    // With no clock there is nothing for the figure to stand beside.
    fireEvent.click(screen.getByRole('switch', { name: 'Clock' }));
    await waitFor(() => expect(speed).toBeDisabled());
    expect(useUiPrefsStore.getState().movieSettings?.captions.speed).toBe(true);
  });
});

describe('a still picture of the preview', () => {
  it('saves the moment the slider is on as a PNG named as the movie is, from the preview’s own viewer', async () => {
    reads.movie = ready(movieTrip('trip-a', 'Alpha'));
    const picture = new Blob(['png'], { type: 'image/png' });
    recordMovieStill.mockResolvedValue(picture);
    open(['trip-a']);

    const still = await screen.findByTestId('movie-still');
    expect(still).toHaveAccessibleName('Save this moment as a picture');
    await waitFor(() => expect(still).not.toBeDisabled());
    const handle = screen.getByRole('slider', { name: 'Moment in the movie' });
    fireEvent.keyDown(handle, { key: 'End', code: 'End', keyCode: 35 });
    fireEvent.click(still);

    await waitFor(() => expect(saveBlob).toHaveBeenCalledTimes(1));
    const [recording, index] = recordMovieStill.mock.calls[0];
    expect(index).toBe(219);
    expect(recording.viewer).toBe(preview.viewer);
    expect(recording.surveyModelId).toBe(MODEL);
    expect(recording.trips.map((trip) => trip.tripLogId)).toEqual(['trip-a']);
    expect(recording.title).toBe('Alpha');
    expect([...recording.excluded]).toEqual([]);
    const [saved, name] = saveBlob.mock.calls[0] as [Blob, string];
    expect(saved).toBe(picture);
    expect(name).toMatch(/^silexgis-alpha-\d{4}-\d{2}-\d{2}\.png$/);
    // The movie's own name with another ending: one rule names both.
    expect(screen.getByTestId('movie-file-name')).toHaveTextContent(name.replace(/\.png$/, '.gif'));
    // No movie was made for it, the dialog never went into an export, and nothing was learnt about GIFs.
    expect(recordMovie).not.toHaveBeenCalled();
    expect(preview.recordingSeen).not.toContain(true);
    expect(screen.queryByTestId('movie-progress')).not.toBeInTheDocument();
    expect(useUiPrefsStore.getState().movieGifCalibration).toBeUndefined();
    await waitFor(() => expect(still).not.toBeDisabled());
  });

  it('names the picture neutrally when the title caption is off', async () => {
    reads.movie = ready(movieTrip('trip-a', 'Alpha'));
    recordMovieStill.mockResolvedValue(new Blob(['png'], { type: 'image/png' }));
    useUiPrefsStore.setState({
      movieSettings: {
        ...DEFAULT_MOVIE_SETTINGS,
        captions: { ...DEFAULT_MOVIE_SETTINGS.captions, title: false },
      },
    });
    open(['trip-a']);

    const still = await screen.findByTestId('movie-still');
    await waitFor(() => expect(still).not.toBeDisabled());
    fireEvent.click(still);
    await waitFor(() => expect(saveBlob).toHaveBeenCalledTimes(1));
    expect(recordMovieStill.mock.calls[0][0].title).toBeNull();
    expect(saveBlob.mock.calls[0][1]).toMatch(/^silexgis-movie-\d{4}-\d{2}-\d{2}\.png$/);
  });

  it('stops the preview playing first, so the picture is taken from where the movie starts', async () => {
    reads.movie = ready(movieTrip('trip-a', 'Alpha'));
    const viewer = preview.viewer as ReturnType<typeof fakeViewer>;
    // What the camera had been told by the time the picture was asked for.
    let toldBefore: unknown[][] = [];
    recordMovieStill.mockImplementation(async () => {
      toldBefore = [...viewer.setCameraAngles.mock.calls];
      return new Blob(['png'], { type: 'image/png' });
    });
    const raf = vi.spyOn(window, 'requestAnimationFrame').mockImplementation(() => 1);
    try {
      open(['trip-a']);
      const play = await screen.findByTestId('movie-play');
      await waitFor(() => expect(play).not.toBeDisabled());
      fireEvent.click(play);
      expect(play).toHaveAccessibleName('Stop the preview');
      viewer.setCameraAngles.mockClear();

      fireEvent.click(screen.getByTestId('movie-still'));
      await waitFor(() => expect(saveBlob).toHaveBeenCalledTimes(1));
      expect(play).toHaveAccessibleName('Play the preview');
      // The camera was put back where play began before the recorder read it.
      expect(toldBefore).toEqual([[{ azimuth: 0.5, polar: 1 }]]);
    } finally {
      raf.mockRestore();
    }
  });

  it('says so when the picture cannot be made, and saves nothing', async () => {
    reads.movie = ready(movieTrip('trip-a', 'Alpha'));
    recordMovieStill.mockRejectedValue(new Error('the drawing was lost'));
    open(['trip-a']);

    const still = await screen.findByTestId('movie-still');
    await waitFor(() => expect(still).not.toBeDisabled());
    fireEvent.click(still);

    const alert = await screen.findByTestId('movie-still-failed');
    expect(alert).toHaveTextContent('The picture could not be made.');
    expect(alert).toHaveTextContent('the drawing was lost');
    expect(screen.queryByTestId('movie-export-failed')).not.toBeInTheDocument();
    expect(saveBlob).not.toHaveBeenCalled();
    await waitFor(() => expect(still).not.toBeDisabled());
  });

  it('says a picture the browser gave no file of in the reader’s language, with no English under it', async () => {
    reads.movie = ready(movieTrip('trip-a', 'Alpha'));
    recordMovieStill.mockRejectedValue(new MovieStillUnwrittenError());
    await i18n.changeLanguage('ro');
    try {
      open(['trip-a']);
      const still = await screen.findByTestId('movie-still');
      await waitFor(() => expect(still).not.toBeDisabled());
      fireEvent.click(still);

      const alert = await screen.findByTestId('movie-still-failed');
      expect(alert).toHaveTextContent('Imaginea nu a putut fi făcută.');
      expect(alert).not.toHaveTextContent('picture');
      expect(alert).not.toHaveTextContent('PNG');
    } finally {
      await i18n.changeLanguage('en');
    }
  });

  it('is held back by what holds an export back, and is not offered while one runs', async () => {
    // A chosen trip that could not be read: a picture quietly missing it would be believed too.
    reads.movie = { ...ready(movieTrip('trip-a', 'Alpha')), failed: ['trip-b'] };
    open(['trip-a', 'trip-b']);
    const still = await screen.findByTestId('movie-still');
    await screen.findByTestId('movie-trips-failed');
    expect(still).toBeDisabled();
    expect(screen.getByTestId('movie-export')).toBeDisabled();
    cleanup();

    // The positive case beside it: the same dialog with nothing unread offers the picture.
    reads.movie = ready(movieTrip('trip-a', 'Alpha'));
    let finish: (file: Blob) => void = () => {};
    recordMovie.mockImplementation(() => new Promise<Blob>((resolve) => (finish = resolve)));
    open(['trip-a']);
    const offered = await screen.findByTestId('movie-still');
    await waitFor(() => expect(offered).not.toBeDisabled());
    fireEvent.click(screen.getByTestId('movie-export'));
    await screen.findByTestId('movie-cancel');
    expect(offered).toBeDisabled();
    fireEvent.click(offered);
    expect(recordMovieStill).not.toHaveBeenCalled();
    await act(async () => finish(new Blob(['GIF89a'], { type: 'image/gif' })));
    await waitFor(() => expect(offered).not.toBeDisabled());
  });
});

describe('the presets', () => {
  /** Settings a reader has made their own in the groups that say what the file discloses. */
  const CHOSEN = normaliseMovieSettings({
    ...DEFAULT_MOVIE_SETTINGS,
    format: 'webm',
    size: '1920x1080',
    fps: 25,
    durationS: 40,
    quality: 'low',
    rotation: { ...DEFAULT_MOVIE_SETTINGS.rotation, enabled: false },
    timeline: { ...DEFAULT_MOVIE_SETTINGS.timeline, mode: 'together' },
    cavers: { ...DEFAULT_MOVIE_SETTINGS.cavers, labels: 'initials', labelPlate: false, showOut: false },
    view: { ...DEFAULT_MOVIE_SETTINGS.view, HUD: true, grid: true, direction: 'plan' },
    captions: { ...DEFAULT_MOVIE_SETTINGS.captions, title: false, clock: false, note: true },
  });
  const PRIVATE_GROUPS = ['rotation', 'timeline', 'cavers', 'view', 'captions'] as const;
  const stored = () => useUiPrefsStore.getState().movieSettings!;

  async function openWith(settings = CHOSEN) {
    useUiPrefsStore.setState({ movieSettings: settings });
    reads.movie = ready(movieTrip('trip-a', 'Alpha'));
    open(['trip-a']);
    await screen.findByTestId('movie-preset-chat');
  }

  it('are offered above the groups, each saying what it sets', async () => {
    await openWith();
    const presets = screen.getByRole('group', { name: 'Presets' });
    expect(within(presets).getByRole('button', { name: 'For a chat' })).toHaveAttribute('title', expect.stringContaining('480 × 270'));
    expect(within(presets).getByRole('button', { name: 'HD video' })).toHaveAttribute('title', expect.stringContaining('1280 × 720'));
    expect(presets).toHaveTextContent('never who appears, the view or the captions');
    // Putting everything back is not one of them: it stands outside the group that promises to
    // change the file only, and says in so many words that the captions come back with it.
    const reset = screen.getByRole('button', { name: 'Reset to defaults' });
    expect(presets).not.toContainElement(reset);
    expect(presets.compareDocumentPosition(reset) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(screen.getByTestId('movie-reset-help')).toHaveTextContent('the captions, the labels and the view as well as the file');
    expect(screen.getByTestId('movie-reset-help')).toHaveTextContent('the title caption and the file named after it come back');
    // Above the groups: before the first of them in the document.
    const firstGroup = screen.getByText('Trips');
    expect(presets.compareDocumentPosition(firstGroup) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it('change the file and leave who is shown, the view and the captions exactly as they were', async () => {
    await openWith();
    for (const group of PRIVATE_GROUPS) {
      expect(CHOSEN[group], group).not.toEqual(DEFAULT_MOVIE_SETTINGS[group]);
    }

    fireEvent.click(screen.getByTestId('movie-preset-chat'));
    await waitFor(() => expect(stored().format).toBe('gif'));
    expect(stored()).toMatchObject({ size: '480x270', fps: 10, durationS: 15, quality: 'medium' });
    for (const group of PRIVATE_GROUPS) {
      expect(stored()[group], group).toEqual(CHOSEN[group]);
    }
    // Read off the file's name: every radio group of the form shares one name under test, so which
    // button of one group is checked cannot be asked of the document here.
    await waitFor(() => expect(screen.getByTestId('movie-file-name')).toHaveTextContent(/\.gif$/));

    fireEvent.click(screen.getByTestId('movie-preset-hd'));
    await waitFor(() => expect(stored().format).toBe('mp4'));
    expect(stored()).toMatchObject({ size: '1280x720', fps: 30, quality: 'high', durationS: 15 });
    for (const group of PRIVATE_GROUPS) {
      expect(stored()[group], group).toEqual(CHOSEN[group]);
    }
    expect(screen.getByTestId('movie-file-name')).toHaveTextContent(/\.mp4$/);
  });

  it('make the HD video a WebM where this browser writes no MP4', async () => {
    probe.answer = [
      { format: 'gif', supported: true, codec: null },
      { format: 'webm', supported: true, codec: 'vp09.00.10.08' },
      { format: 'mp4', supported: false, codec: null },
    ];
    await openWith(DEFAULT_MOVIE_SETTINGS);
    fireEvent.click(screen.getByTestId('movie-preset-hd'));
    await waitFor(() => expect(stored()?.format).toBe('webm'));
    expect(stored()).toMatchObject({ size: '1280x720', fps: 30, quality: 'high' });
  });

  it('leave everything alone where this browser writes no video, and say so', async () => {
    probe.answer = [
      { format: 'gif', supported: true, codec: null },
      { format: 'webm', supported: false, codec: null },
      { format: 'mp4', supported: false, codec: null },
    ];
    await openWith(DEFAULT_MOVIE_SETTINGS);
    const before = stored();
    fireEvent.click(screen.getByTestId('movie-preset-hd'));

    expect(await screen.findByText('This browser cannot write a 1280 × 720 video, so nothing was changed.')).toBeInTheDocument();
    expect(stored()).toEqual(DEFAULT_MOVIE_SETTINGS);
    expect(screen.getByTestId('movie-file-name')).toHaveTextContent(/\.gif$/);
    // Nothing was written at all: what is remembered is the very object it was, not a copy of it.
    expect(stored()).toBe(before);
    // The same press is not refused where the browser does write video: the refusal is the probe's.
    probe.answer = ALL_FORMATS;
    fireEvent.click(screen.getByTestId('movie-preset-hd'));
    await waitFor(() => expect(screen.getByTestId('movie-file-name')).toHaveTextContent(/\.mp4$/));
    expect(stored()).toMatchObject({ format: 'mp4', size: '1280x720', fps: 30, quality: 'high' });
    expect(stored()).not.toBe(before);
  });

  it('reset puts every setting back, the written title included', async () => {
    await openWith();
    fireEvent.click(screen.getByText('Captions'));
    fireEvent.click(await screen.findByTestId('movie-caption-title'));
    const title = screen.getByTestId('movie-title-text');
    fireEvent.change(title, { target: { value: 'Ours' } });
    expect(title).toHaveValue('Ours');

    fireEvent.click(screen.getByTestId('movie-reset'));

    await waitFor(() => expect(stored()).toEqual(DEFAULT_MOVIE_SETTINGS));
    await waitFor(() => expect(title).toHaveValue(''));
    await waitFor(() => expect(screen.getByTestId('movie-file-name')).toHaveTextContent(/\.gif$/));
    expect(preview.direction).toBe(DEFAULT_MOVIE_SETTINGS.view.direction);
  });

  it('change nothing while an export runs', async () => {
    let finish: (file: Blob) => void = () => {};
    recordMovie.mockImplementation(() => new Promise<Blob>((resolve) => (finish = resolve)));
    await openWith(DEFAULT_MOVIE_SETTINGS);
    const exportButton = screen.getByTestId('movie-export');
    await waitFor(() => expect(exportButton).not.toBeDisabled());
    fireEvent.click(exportButton);
    await screen.findByTestId('movie-cancel');

    for (const id of ['movie-preset-chat', 'movie-preset-hd', 'movie-reset']) {
      expect(screen.getByTestId(id), id).toBeDisabled();
    }
    await act(async () => finish(new Blob(['GIF89a'], { type: 'image/gif' })));
    await waitFor(() => expect(screen.getByTestId('movie-preset-chat')).not.toBeDisabled());
  });
});

describe('the preview’s own keys', () => {
  async function opened() {
    reads.movie = ready(movieTrip('trip-a', 'Alpha'));
    open(['trip-a']);
    const play = await screen.findByTestId('movie-play');
    await waitFor(() => expect(play).not.toBeDisabled());
    return {
      play,
      keys: screen.getByTestId('movie-preview-keys'),
      handle: screen.getByRole('slider', { name: 'Moment in the movie' }),
      frameShown: () => screen.getByTestId('movie-position').dataset.frame,
    };
  }
  /** Whether the key was left to do what it does by itself: type, press, scroll. */
  const leftAlone = (element: Element, init: KeyboardEventInit) => fireEvent.keyDown(element, init);

  let raf: ReturnType<typeof vi.spyOn>;
  beforeEach(() => {
    // Play asks for a frame and is handed none: whether it is playing is all these tests ask.
    raf = vi.spyOn(window, 'requestAnimationFrame').mockImplementation(() => 1);
  });
  afterEach(() => raf.mockRestore());

  it('can be given the focus, says what its keys are, and plays and pauses on Space', async () => {
    const { play, keys } = await opened();
    expect(keys).toHaveAttribute('tabindex', '0');
    expect(keys).toHaveAccessibleName('Preview of the movie');
    expect(keys).toContainElement(screen.getByTestId('movie-preview'));
    expect(screen.getByTestId('movie-keys-hint')).toHaveTextContent('Space plays and pauses');

    expect(leftAlone(keys, { key: ' ' })).toBe(false);
    expect(play).toHaveAccessibleName('Stop the preview');
    expect(leftAlone(keys, { key: ' ' })).toBe(false);
    expect(play).toHaveAccessibleName('Play the preview');
  });

  it('takes the focus when it is pressed, whatever the viewer does with the press', async () => {
    const { play, keys } = await opened();
    play.focus();
    expect(play).toHaveFocus();
    // The viewer cancels every press on its drawing, and a cancelled press moves no focus by
    // itself: the preview takes it in so many words.
    fireEvent.pointerDown(screen.getByTestId('movie-preview'));
    expect(keys).toHaveFocus();
  });

  it('plays and pauses on Space from the moment slider as well', async () => {
    const { play, handle } = await opened();
    expect(leftAlone(handle, { key: ' ' })).toBe(false);
    expect(play).toHaveAccessibleName('Stop the preview');
    expect(leftAlone(handle, { key: ' ' })).toBe(false);
    expect(play).toHaveAccessibleName('Play the preview');
  });

  it('goes to the ends of the movie on Home and End', async () => {
    const { keys, handle, frameShown } = await opened();
    expect(leftAlone(keys, { key: 'End' })).toBe(false);
    expect(frameShown()).toBe('219');
    expect(leftAlone(keys, { key: 'Home' })).toBe(false);
    expect(frameShown()).toBe('0');
    // On the slider's handle the two are the slider's own, and go to the same ends.
    fireEvent.keyDown(handle, { key: 'End', code: 'End', keyCode: 35 });
    expect(frameShown()).toBe('219');
    fireEvent.keyDown(handle, { key: 'Home', code: 'Home', keyCode: 36 });
    expect(frameShown()).toBe('0');
  });

  it('stops play when a key sends the preview to an end', async () => {
    const { play, keys, frameShown } = await opened();
    fireEvent.keyDown(keys, { key: ' ' });
    expect(play).toHaveAccessibleName('Stop the preview');
    fireEvent.keyDown(keys, { key: 'End' });
    expect(play).toHaveAccessibleName('Play the preview');
    expect(frameShown()).toBe('219');
  });

  it('leaves Space to a text box, where it types, and to a button, where it presses', async () => {
    const { play } = await opened();
    fireEvent.click(screen.getByText('Captions'));
    const title = await screen.findByTestId('movie-title-text');
    expect(leftAlone(title, { key: ' ' })).toBe(true);
    fireEvent.change(title, { target: { value: 'Two words' } });
    expect(title).toHaveValue('Two words');
    expect(play).toHaveAccessibleName('Play the preview');

    // On the play button itself the key is the button's: pressed once by the browser, which a
    // second press of it from here would undo.
    expect(leftAlone(play, { key: ' ' })).toBe(true);
    expect(leftAlone(screen.getByTestId('movie-still'), { key: ' ' })).toBe(true);
    expect(leftAlone(play, { key: 'Home' })).toBe(true);
    expect(play).toHaveAccessibleName('Play the preview');
  });

  it('takes no key held with a modifier, and does not start and stop on a key held down', async () => {
    const { play, keys, frameShown } = await opened();
    expect(leftAlone(keys, { key: ' ', ctrlKey: true })).toBe(true);
    expect(leftAlone(keys, { key: 'End', altKey: true })).toBe(true);
    expect(leftAlone(keys, { key: 'End', metaKey: true })).toBe(true);
    expect(play).toHaveAccessibleName('Play the preview');
    expect(frameShown()).toBe('0');

    fireEvent.keyDown(keys, { key: ' ' });
    fireEvent.keyDown(keys, { key: ' ', repeat: true });
    fireEvent.keyDown(keys, { key: ' ', repeat: true });
    expect(play).toHaveAccessibleName('Stop the preview');
  });

  it('does nothing while an export runs', async () => {
    let finish: (file: Blob) => void = () => {};
    recordMovie.mockImplementation(() => new Promise<Blob>((resolve) => (finish = resolve)));
    const { play, keys, frameShown } = await opened();
    const exportButton = screen.getByTestId('movie-export');
    await waitFor(() => expect(exportButton).not.toBeDisabled());
    fireEvent.click(exportButton);
    await screen.findByTestId('movie-cancel');

    expect(leftAlone(keys, { key: ' ' })).toBe(true);
    expect(leftAlone(keys, { key: 'End' })).toBe(true);
    expect(play).toHaveAccessibleName('Play the preview');
    expect(frameShown()).toBe('0');

    await act(async () => finish(new Blob(['GIF89a'], { type: 'image/gif' })));
    await waitFor(() => expect(play).not.toBeDisabled());
    // The same key on the same preview, once the export is over.
    expect(leftAlone(keys, { key: ' ' })).toBe(false);
    expect(play).toHaveAccessibleName('Stop the preview');
  });

  it('keeps its keys from the viewer’s document-wide shortcuts like every other key on the dialog', async () => {
    const { keys, handle } = await opened();
    const seen: string[] = [];
    const listener = (event: KeyboardEvent) => seen.push(event.key);
    document.addEventListener('keydown', listener);
    try {
      fireEvent.keyDown(keys, { key: ' ' });
      fireEvent.keyDown(keys, { key: 'Home' });
      fireEvent.keyDown(handle, { key: ' ' });
      fireEvent.keyDown(keys, { key: 'r' });
      expect(seen).toEqual([]);
      fireEvent.keyDown(keys, { key: 'Escape' });
      expect(seen).toEqual(['Escape']);
    } finally {
      document.removeEventListener('keydown', listener);
    }
  });
});
