// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import type { TFunction } from 'i18next';
import { useTranslation } from 'react-i18next';
import {
  DownloadError,
  TRIP_REPORT_MAP_PART,
  downloadFile,
  downloadFileForm,
  tripReportDownloadUrl,
  tripReportUrl,
} from '../../api/download.ts';
import { useTripReportMapSources, type TripLogInfo } from '../../api/hooks.ts';
import { documentBasemap, tripReportMapContent } from './tripReportMap.ts';
import {
  makeTripReportMapPicture,
  type ReportMapWords,
  type TripReportMapPicture,
} from './tripReportMapPicture.ts';

/**
 * How a download of a trip's write-up went, for the page to tell its reader about.
 *
 * The first two need no telling: the file arriving is the news. The other three are each a way
 * the document differs from what the page showed, and a reader who is not told finds out only by
 * opening the file and wondering.
 */
export type ReportDownloadOutcome =
  /** The trip places nothing, so there was no map to draw. */
  | 'plain'
  /** The document carries the map, on its background. */
  | 'with-map'
  /** The document carries the map on a plain ground, and says on it why. */
  | 'with-map-no-background'
  /** The map could not be drawn here; the document was downloaded without it. */
  | 'map-not-made'
  /** The server would not take the picture; the document was downloaded without it. */
  | 'map-refused';

/** The words drawn onto the picture, in the language this reader is using. */
function mapWords(t: TFunction): ReportMapWords {
  return {
    legend: {
      sketch: t('trips.report.map.legendSketch'),
      meeting: t('trips.report.map.legendMeeting'),
      cave: t('trips.report.map.legendCave'),
    },
    background: (attribution) => t('trips.report.map.background', { attribution }),
    noBackground: {
      'none-offered': t('trips.report.map.noBackgroundNoneOffered'),
      'not-delivered': t('trips.report.map.noBackgroundNotDelivered'),
      'timed-out': t('trips.report.map.noBackgroundTimedOut'),
      'not-copyable': t('trips.report.map.noBackgroundNotCopyable'),
    },
  };
}

/**
 * Whether a refused download was refused over the picture it carried, rather than over the
 * document.
 *
 * Told apart because the remedy differs. A document that cannot be had is a failure to report; a
 * picture that was not taken leaves a perfectly good document to fetch without it. The picture's
 * own refusals share one prefix; a request too large for the server to read at all, a body it
 * would not read as a form, and a form it could not read are the same thing seen earlier, since
 * the picture is the only reason this request has a body.
 */
function refusedOverThePicture(error: unknown): boolean {
  if (!(error instanceof DownloadError)) {
    return false;
  }
  return (
    error.status === 413 ||
    error.status === 415 ||
    (error.status === 400 &&
      (error.code?.startsWith('trip_report.map_') === true || error.code === 'request.binding_failed'))
  );
}

/**
 * Downloading a trip's write-up, with a map of the trip in it when one can be drawn.
 *
 * The map is drawn here, by this reader's browser, from what the page already holds: the trip as
 * the page read it, and the caves it names as the page read each of them. So it shows what this
 * reader may see and nothing more, and it goes into this reader's own download — it is never
 * sent with a request to file the write-up against the trip, where every later reader would
 * open it.
 *
 * Whatever happens to the map, the document is downloaded. A map that cannot be drawn, a
 * background that will not load and a picture the server will not take each end in a file, and
 * the outcome says which, so the page can tell its reader what the file does not hold.
 */
export function useTripReportDownload() {
  const { t } = useTranslation();
  const sources = useTripReportMapSources();
  const [downloading, setDownloading] = useState(false);

  const download = async (
    trip: TripLogInfo,
    templateId?: string,
  ): Promise<ReportDownloadOutcome> => {
    setDownloading(true);
    try {
      let picture: TripReportMapPicture | null = null;
      let notMade = false;
      try {
        const content = tripReportMapContent(trip, await sources.caves(trip.caveIds));
        if (content) {
          picture = await makeTripReportMapPicture(
            content,
            documentBasemap(await sources.catalog()),
            mapWords(t),
          );
        }
      } catch {
        // Whatever went wrong with the drawing, the document is still owed.
        notMade = true;
      }

      if (picture) {
        const form = new FormData();
        form.append(TRIP_REPORT_MAP_PART, picture.blob, 'map.png');
        try {
          await downloadFileForm(tripReportDownloadUrl(trip.id, templateId), form);
          return picture.background.drawn ? 'with-map' : 'with-map-no-background';
        } catch (error) {
          if (!refusedOverThePicture(error)) {
            throw error;
          }
          await downloadFile(tripReportUrl(trip.id, templateId));
          return 'map-refused';
        }
      }

      await downloadFile(tripReportUrl(trip.id, templateId));
      return notMade ? 'map-not-made' : 'plain';
    } finally {
      setDownloading(false);
    }
  };

  return { download, downloading };
}
