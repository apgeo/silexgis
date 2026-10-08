// SPDX-License-Identifier: AGPL-3.0-or-later
import { act, renderHook } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { TripLogInfo } from '../../api/hooks.ts';

const CAVE = '33333333-4444-5555-6666-777777777777';

const { downloadFile, downloadFileForm, caves, catalog, makePicture, DownloadError } = vi.hoisted(() => {
  class DownloadError extends Error {
    readonly status: number;
    readonly code?: string;
    constructor(status: number, problem?: Record<string, unknown>) {
      super(`Download failed (${status})`);
      this.status = status;
      this.code = typeof problem?.code === 'string' ? problem.code : undefined;
    }
  }
  return {
    downloadFile: vi.fn(),
    downloadFileForm: vi.fn(),
    caves: vi.fn(),
    catalog: vi.fn(),
    makePicture: vi.fn(),
    DownloadError,
  };
});

// The transport is stubbed: what is under test is which request is made, with what in it, and
// what the page is told afterwards — not what a server says.
vi.mock('../../api/download.ts', () => ({
  DownloadError,
  TRIP_REPORT_MAP_PART: 'map',
  downloadFile: (...args: unknown[]) => downloadFile(...args),
  downloadFileForm: (...args: unknown[]) => downloadFileForm(...args),
  tripReportUrl: (id: string, templateId?: string, format?: string) =>
    `/api/v1/trip-logs/${id}/report?${query(templateId, format)}`,
  tripReportDownloadUrl: (id: string, templateId?: string, format?: string) =>
    `/api/v1/trip-logs/${id}/report/download?${query(templateId, format)}`,
}));

/** The query the real builders write: a layout when one was chosen, a format only when it is a PDF. */
function query(templateId?: string, format?: string): string {
  return [templateId ? `templateId=${templateId}` : '', format === 'pdf' ? 'format=pdf' : '']
    .filter(Boolean)
    .join('&');
}

vi.mock('../../api/hooks.ts', () => ({
  useTripReportMapSources: () => ({ caves, catalog }),
}));

// The drawing wants a real browser. Which shapes it is asked to draw, and over which background,
// is the part decided here.
vi.mock('./tripReportMapPicture.ts', () => ({
  makeTripReportMapPicture: (...args: unknown[]) => makePicture(...args),
}));

const { useTripReportDownload } = await import('./useTripReportDownload.ts');

const sketch = { type: 'Point', coordinates: [25.4, 45.5] };

function trip(overrides: Partial<TripLogInfo> = {}): TripLogInfo {
  return { id: 'trip-1', geom: sketch, meetingGeom: null, caveIds: [CAVE], ...overrides } as TripLogInfo;
}

const openMap = {
  id: 1,
  name: 'Open map',
  layerKind: 'xyz',
  urlTemplate: 'https://tiles.example.invalid/{z}/{x}/{y}.png',
  attribution: '© Open Mappers',
  isBase: true,
  isDefault: true,
  sortOrder: 10,
  inDocuments: true,
};
const restricted = { ...openMap, id: 2, name: 'Restricted', isDefault: false, sortOrder: 5, inDocuments: false };

const picture = new Blob(['picture'], { type: 'image/png' });

beforeEach(() => {
  downloadFile.mockReset().mockResolvedValue(undefined);
  downloadFileForm.mockReset().mockResolvedValue(undefined);
  caves.mockReset().mockResolvedValue(
    new Map([[CAVE, { name: 'Peștera Mare', geom: { type: 'Point', coordinates: [25.41, 45.51] }, approximateLocation: false }]]),
  );
  catalog.mockReset().mockResolvedValue([restricted, openMap]);
  makePicture.mockReset().mockResolvedValue({ blob: picture, background: { drawn: true, attribution: '© Open Mappers' } });
});

async function download(subject: TripLogInfo, templateId?: string, format?: 'docx' | 'pdf') {
  const { result } = renderHook(() => useTripReportDownload());
  let outcome: string | undefined;
  await act(async () => {
    outcome = await result.current.download(subject, templateId, format);
  });
  return { outcome, result };
}

describe('downloading a trip’s write-up', () => {
  it('draws the map from what the page holds and sends it with the request for the document', async () => {
    const { outcome } = await download(trip(), 'layout-7');

    // The caves asked about are the ones the trip's own answer names, and no others.
    expect(caves).toHaveBeenCalledWith([CAVE]);

    // What is drawn: the trip's sketch and the cave it names, each as its answer carried it.
    const [content, basemap] = makePicture.mock.calls[0];
    expect(content.shapes).toEqual([
      { kind: 'sketch', geometry: sketch },
      { kind: 'cave', geometry: { type: 'Point', coordinates: [25.41, 45.51] }, label: 'Peștera Mare' },
    ]);
    // Over the one background the catalogue lets a document copy — not the first in the list.
    expect(basemap).toMatchObject({ id: 1 });

    expect(downloadFileForm).toHaveBeenCalledTimes(1);
    const [url, form] = downloadFileForm.mock.calls[0] as [string, FormData];
    expect(url).toBe('/api/v1/trip-logs/trip-1/report/download?templateId=layout-7');
    expect(form.get('map')).toBeInstanceOf(Blob);
    expect((form.get('map') as Blob).size).toBe(picture.size);
    // The picture and nothing else: nothing about the trip travels with the request, because
    // the server writes the document from its own reading of it.
    const parts: string[] = [];
    form.forEach((_value, name) => parts.push(name));
    expect(parts).toEqual(['map']);

    expect(downloadFile).not.toHaveBeenCalled();
    expect(outcome).toBe('with-map');
  });

  it('writes the picture’s words in the reader’s language', async () => {
    await download(trip());

    const words = makePicture.mock.calls[0][2];
    expect(words.legend).toEqual({
      sketch: 'Where the trip worked (its sketch)',
      meeting: 'Meeting point',
      cave: 'Cave the trip names',
    });
    expect(words.background('© Open Mappers')).toBe('Background map: © Open Mappers');
    expect(words.noBackground['not-delivered']).toContain('did not deliver');
  });

  it('says so when the map went out on a plain ground', async () => {
    makePicture.mockResolvedValue({ blob: picture, background: { drawn: false, reason: 'not-delivered' } });

    const { outcome } = await download(trip());

    expect(downloadFileForm).toHaveBeenCalledTimes(1);
    expect(outcome).toBe('with-map-no-background');
  });

  it('asks for the plain document when the trip places nothing', async () => {
    const { outcome } = await download(trip({ geom: null, caveIds: [] }));

    expect(makePicture).not.toHaveBeenCalled();
    expect(downloadFileForm).not.toHaveBeenCalled();
    expect(downloadFile).toHaveBeenCalledWith('/api/v1/trip-logs/trip-1/report?');
    expect(outcome).toBe('plain');
  });

  /**
   * The document is owed whatever becomes of its picture. A browser that cannot draw the map is
   * not a reason to hand its reader nothing.
   */
  it('still downloads the document, without a map, when the map cannot be drawn', async () => {
    makePicture.mockRejectedValue(new Error('This browser cannot draw onto a 2D canvas.'));

    const { outcome } = await download(trip(), 'layout-7');

    expect(downloadFileForm).not.toHaveBeenCalled();
    expect(downloadFile).toHaveBeenCalledWith('/api/v1/trip-logs/trip-1/report?templateId=layout-7');
    expect(outcome).toBe('map-not-made');
  });

  it.each([
    ['the picture is refused by name', new DownloadError(400, { code: 'trip_report.map_too_large' })],
    ['the request is too large to be read', new DownloadError(413)],
    ['the form cannot be read', new DownloadError(400, { code: 'request.binding_failed' })],
  ])('downloads the document without the map when %s', async (_why, refusal) => {
    downloadFileForm.mockRejectedValue(refusal);

    const { outcome } = await download(trip());

    expect(downloadFile).toHaveBeenCalledWith('/api/v1/trip-logs/trip-1/report?');
    expect(outcome).toBe('map-refused');
  });

  /**
   * A refusal of the document itself is not answered by asking again without the picture: the
   * second request would be refused the same way, and the failure would be reported as a
   * missing map.
   */
  it('reports a refusal of the document as a failure, and does not ask a second time', async () => {
    downloadFileForm.mockRejectedValue(new DownloadError(404, { code: 'trip_log.not_found' }));
    const { result } = renderHook(() => useTripReportDownload());

    let failure: unknown;
    await act(async () => {
      failure = await result.current.download(trip()).catch((error: unknown) => error);
    });

    expect(failure).toBeInstanceOf(DownloadError);
    expect(downloadFile).not.toHaveBeenCalled();
    expect(result.current.downloading).toBe(false);
  });

  /**
   * A write-up asked for as a PDF is the same request with one more word on it: the map is drawn
   * and sent exactly as for the document, and goes in before the document is laid out.
   */
  it('asks for a PDF with the map in it, by the same route and with the same picture', async () => {
    const { outcome } = await download(trip(), 'layout-7', 'pdf');

    expect(downloadFileForm).toHaveBeenCalledTimes(1);
    const [url, form] = downloadFileForm.mock.calls[0] as [string, FormData];
    expect(url).toBe('/api/v1/trip-logs/trip-1/report/download?templateId=layout-7&format=pdf');
    expect(form.get('map')).toBeInstanceOf(Blob);
    expect(downloadFile).not.toHaveBeenCalled();
    expect(outcome).toBe('with-map');
  });

  it('keeps asking for a PDF when the picture is refused and the document is fetched without it', async () => {
    downloadFileForm.mockRejectedValue(new DownloadError(400, { code: 'trip_report.map_too_large' }));

    const { outcome } = await download(trip(), undefined, 'pdf');

    expect(downloadFile).toHaveBeenCalledWith('/api/v1/trip-logs/trip-1/report?format=pdf');
    expect(outcome).toBe('map-refused');
  });

  /**
   * A refusal that is about the PDF is not about the picture. Fetching the plain document in its
   * place would be refused the same way, and the page would report a missing map for what is a
   * missing converter — so it is thrown, with its code, for the page to word.
   */
  it('hands a refusal of the PDF back as that, and does not ask again without the picture', async () => {
    downloadFileForm.mockRejectedValue(new DownloadError(503, { code: 'report.pdf_no_answer' }));
    const { result } = renderHook(() => useTripReportDownload());

    let failure: unknown;
    await act(async () => {
      failure = await result.current.download(trip(), undefined, 'pdf').catch((error: unknown) => error);
    });

    expect(failure).toBeInstanceOf(DownloadError);
    expect((failure as { code?: string }).code).toBe('report.pdf_no_answer');
    expect(downloadFile).not.toHaveBeenCalled();
    expect(result.current.downloading).toBe(false);
    expect(result.current.downloadingFormat).toBeNull();
  });

  it('is busy for as long as the download takes, and no longer', async () => {
    let finish: () => void = () => {};
    downloadFileForm.mockReturnValue(new Promise<void>((resolve) => { finish = resolve; }));
    const { result } = renderHook(() => useTripReportDownload());

    let pending: Promise<unknown> = Promise.resolve();
    act(() => {
      pending = result.current.download(trip());
    });
    expect(result.current.downloading).toBe(true);

    await act(async () => {
      // The request is only made once the picture has been drawn, which takes a turn or two.
      await vi.waitFor(() => expect(downloadFileForm).toHaveBeenCalled());
      finish();
      await pending;
    });
    expect(result.current.downloading).toBe(false);
  });
});
