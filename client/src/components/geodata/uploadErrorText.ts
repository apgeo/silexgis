// SPDX-License-Identifier: AGPL-3.0-or-later
import type { TFunction } from 'i18next';
import { ApiError } from '../../api/client.ts';

/** The refusals an upload of a vector file or a raster map can meet, each with a sentence of its own. */
const BY_CODE: Record<string, string> = {
  'geofile.format_unsupported': 'geodata.upload.errors.formatUnsupported',
  'georeferenced_map.format_unsupported': 'geodata.upload.errors.formatUnsupported',
  'geofile.file_empty': 'geodata.upload.errors.empty',
  'georeferenced_map.size_invalid': 'geodata.upload.errors.sizeInvalid',
  'geofile.file_too_large': 'geodata.upload.errors.tooLarge',
  'geofile.file_name_too_long': 'geodata.upload.errors.nameTooLong',
  'georeferenced_map.file_name_too_long': 'geodata.upload.errors.nameTooLong',
  'geofile.unreadable': 'geodata.upload.errors.unreadable',
  'geofile.not_delimited': 'geodata.upload.errors.notDelimited',
};

/**
 * Why an upload did not go through, in a sentence for the person who dropped the file.
 *
 * A refusal the server names gets this client's own wording; one it does not know is shown in the
 * server's words when it wrote any, since those say what was wrong with this file; a request that
 * never got an answer, or got one from something in front of the server (a proxy's size limit),
 * is told apart from a refusal, because the remedy differs.
 */
export function uploadErrorText(error: unknown, t: TFunction): string {
  if (error instanceof DOMException && error.name === 'AbortError') {
    return t('geodata.upload.errors.stopped');
  }
  if (!(error instanceof ApiError)) {
    return t('geodata.upload.errors.generic');
  }
  const known = error.code ? BY_CODE[error.code] : undefined;
  if (known) {
    return t(known);
  }
  switch (error.status) {
    case 0:
      return t('geodata.upload.errors.connection');
    case 401:
      return t('geodata.upload.errors.signedOut');
    case 403:
      return t('geodata.upload.errors.forbidden');
    case 413:
      return t('geodata.upload.errors.tooLarge');
    case 429:
      return t('geodata.upload.errors.tooMany');
  }
  if (error.status >= 500) {
    return t('geodata.upload.errors.server', { status: error.status });
  }
  return error.detail ?? t('geodata.upload.errors.refused', { status: error.status });
}
