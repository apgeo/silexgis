// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { decodeSheet, sniffDelimiter } from './trackingCsvSheet.ts';

const bytes = (...values: number[]) => Uint8Array.from(values).buffer;

/**
 * The two readings made in the browser before the server sees the sheet.
 *
 * <b>The defect these pin.</b> The dialog read the file as UTF-8 and nothing else, and sent a
 * comma to the server whatever the header said. A plain "CSV" save from a Romanian Excel is
 * Windows-1250 and semicolon-separated, so the one sheet a Romanian coordinator is most likely to
 * hand over lost every diacritic and previewed as a file with no columns.
 */
describe('decodeSheet', () => {
  it('reads valid UTF-8 as UTF-8, byte-order mark and all', () => {
    const { text, encoding } = decodeSheet(bytes(0xef, 0xbb, 0xbf, 0x69, 0x65, 0xc8, 0x99), 'auto');
    expect(encoding).toBe('utf-8');
    expect(text).toBe('ieș');
  });

  it('falls back to Windows-1250 for bytes that are not UTF-8', () => {
    // "ieşire" as a Romanian Excel writes it: 0xBA is ş in the Central European code page and
    // an impossible continuation byte in UTF-8.
    const { text, encoding } = decodeSheet(bytes(0x69, 0x65, 0xba, 0x69, 0x72, 0x65), 'auto');
    expect(encoding).toBe('windows-1250');
    expect(text).toBe('ieşire');
    expect(text).not.toContain('�');
  });

  it('reads under the encoding the reviewer chose, even where the guess would differ', () => {
    // Every byte here is valid UTF-8 and also valid Latin-2; the choice, not the bytes, decides.
    const { text, encoding } = decodeSheet(bytes(0x69, 0x65, 0xc8, 0x99), 'iso-8859-2');
    expect(encoding).toBe('iso-8859-2');
    expect(text).not.toBe('ieș');
  });
});

describe('sniffDelimiter', () => {
  it('takes the separator the header line holds most of', () => {
    expect(sniffDelimiter('Data si ora;Adancime;Speologi;Stare\r\n12.09.2026 08:15;0;Maria Pop;intrare')).toBe(';');
    expect(sniffDelimiter('Data si ora,Adancime,Speologi\n1,2,3')).toBe(',');
    expect(sniffDelimiter('Data si ora\tAdancime\tSpeologi\n')).toBe('\t');
  });

  it('decides on the header alone, however many semicolons a note further down carries', () => {
    expect(sniffDelimiter('When,Depth,Note\n08:15,0,"a; b; c; d; e"')).toBe(',');
  });

  it('keeps the comma for a header that divides on nothing', () => {
    expect(sniffDelimiter('x')).toBe(',');
    expect(sniffDelimiter('')).toBe(',');
  });
});
