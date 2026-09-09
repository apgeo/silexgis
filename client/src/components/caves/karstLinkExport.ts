// SPDX-License-Identifier: AGPL-3.0-or-later
import { downloadFilePost } from '../../api/download.ts';
import type { KarstLinkExportRequest } from '../../api/hooks.ts';
import type { ProtectedPositionTreatment } from '../../stores/uiPrefsStore.ts';

/** The one route that produces an interchange file, named once. */
const url = '/api/v1/export/caves/karstlink';

/**
 * Takes the file with an answer the exporter has just given, applied to every cave in the
 * set that needs one.
 *
 * The per-cave map the route also accepts is deliberately not sent: a decision made once for
 * the whole export is what was actually made, and writing it out per cave would record a
 * precision of intent nobody had.
 */
export function exportKarstLink(
  request: KarstLinkExportRequest,
  treatment: ProtectedPositionTreatment,
): Promise<void> {
  return downloadFilePost(url, { ...request, treatmentForAll: treatment });
}

/**
 * Takes the file using the answer this account settled on earlier, without asking again.
 *
 * Sent as the request's default rather than as the answer for all, which is the difference
 * between "this is what I decided this time" and "this is what I decided once and have not
 * changed": the server consults it last, so a treatment named for a particular cave still
 * wins over it, and a stored answer the server no longer understands comes back as a refusal
 * this caller can recover from by being asked again rather than as a coordinate somebody
 * guessed at.
 */
export function exportKarstLinkWithStoredAnswer(
  request: KarstLinkExportRequest,
  treatment: ProtectedPositionTreatment,
): Promise<void> {
  return downloadFilePost(url, { ...request, defaultTreatment: treatment });
}
