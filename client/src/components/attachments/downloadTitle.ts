// SPDX-License-Identifier: AGPL-3.0-or-later
import type { TFunction } from 'i18next';

/** The two facts about a picture's delivery that a label needs; a file and a photograph both carry them. */
export interface Delivery {
  mayDownloadOriginal?: boolean;
  locationRemoved?: boolean;
}

/**
 * What to tell somebody pointing at a download control: why it is switched off, or that what it
 * hands over is a copy without the picture's location. Nothing when it is the stored file.
 */
export function downloadTitle(file: Delivery, t: TFunction): string | undefined {
  if (!file.mayDownloadOriginal) {
    return t('attachments.originalWithheld');
  }
  return file.locationRemoved ? t('attachments.locationRemovedDownload') : undefined;
}
