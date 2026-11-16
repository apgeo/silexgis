// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import en from '../../i18n/locales/en.json';
import ro from '../../i18n/locales/ro.json';
import { uploadReasonKey } from './uploadReasons.ts';

function lookup(tree: unknown, key: string): unknown {
  return key.split('.').reduce<unknown>((node, part) => (node as Record<string, unknown> | undefined)?.[part], tree);
}

describe('uploadReasonKey', () => {
  it.each([
    ['upload.length_required', 'uploads.reason.lengthRequired'],
    ['upload.session_not_found', 'uploads.reason.sessionEnded'],
    ['upload.chunk_out_of_order', 'uploads.reason.outOfStep'],
    ['upload.size_mismatch', 'uploads.reason.outOfStep'],
    ['upload.incomplete', 'uploads.reason.outOfStep'],
    ['storage.insufficient', 'uploads.reason.quotaExceeded'],
    ['file.quota_exceeded', 'uploads.reason.quotaExceeded'],
  ])('words the resumable refusal %s', (code, key) => {
    expect(uploadReasonKey(code, 'failed')).toBe(key);
  });

  it('tells a dropped connection and a server fault apart from a plain failure', () => {
    expect(uploadReasonKey(undefined, 'failed', 0)).toBe('uploads.reason.connectionLost');
    expect(uploadReasonKey(undefined, 'failed', 500)).toBe('uploads.reason.serverFault');
    expect(uploadReasonKey(undefined, 'failed', 503)).toBe('uploads.reason.serverFault');
    expect(uploadReasonKey(undefined, 'failed', 400)).toBe('uploads.reason.failed');
    expect(uploadReasonKey(undefined, 'failed')).toBe('uploads.reason.failed');
  });

  it.each([
    'archive.unreadable', 'archive.too_many_entries', 'archive.too_large',
    'archive.suspicious_compression', 'import.directory_not_found', 'import.path_not_allowed',
    'import.no_roots_configured',
  ])('words the batch failure %s rather than calling it a plain failure', (code) => {
    expect(uploadReasonKey(code, 'failed')).not.toBe('uploads.reason.failed');
  });

  it('prefers a known code to the status it came with', () => {
    expect(uploadReasonKey('file.too_large', 'failed', 400)).toBe('uploads.reason.tooLarge');
  });

  it('has every key it can answer in both languages', () => {
    const codes = [
      'upload.length_required', 'upload.session_not_found', 'upload.chunk_out_of_order',
      'file.duplicate', 'file.too_large', 'file.type_not_accepted', 'file.quota_exceeded',
      'file.empty', 'upload.path_refused', 'upload.filing_refused', 'upload.unreadable', undefined,
      'archive.unreadable', 'archive.too_many_entries', 'archive.too_large',
      'archive.suspicious_compression', 'import.directory_not_found', 'import.path_not_allowed',
      'import.no_roots_configured',
    ];
    const keys = new Set([
      ...codes.map((code) => uploadReasonKey(code, 'failed')),
      uploadReasonKey(undefined, 'failed', 0),
      uploadReasonKey(undefined, 'failed', 500),
    ]);
    for (const key of keys) {
      expect(typeof lookup(en, key), `${key} in English`).toBe('string');
      expect(typeof lookup(ro, key), `${key} in Romanian`).toBe('string');
    }
  });
});
