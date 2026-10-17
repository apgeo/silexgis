// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { batchCsv, csvColumns, detectSeparator, readBatch, readOne, splitCsvLine, type BatchCsvLabels } from './batch.ts';

const labels: BatchCsvLabels = {
  line: 'line',
  input: 'input',
  latitude: 'lat',
  longitude: 'lon',
  error: 'error',
  format: (id) => id,
  errorText: (error) => `E:${error}`,
};

describe('reading many positions', () => {
  it('reads one position per line, in mixed formats, with an error per line it cannot read', () => {
    const { rows, leftOut } = readBatch(
      ['45°35′58″N 24°44′10″E', '', '45.59944, 24.73617', 'not a position', '35T 401234 5049876'].join('\n'),
      { mode: 'lines' },
    );
    expect(leftOut).toBe(0);
    expect(rows.map((row) => row.line)).toEqual([1, 3, 4, 5]);
    expect(rows.map((row) => row.outcome.kind)).toEqual(['ok', 'ok', 'error', 'ok']);
    const first = rows[0].outcome;
    expect(first.kind === 'ok' && first.lat).toBeCloseTo(45.59944, 4);
    expect(rows[2].outcome).toEqual({ kind: 'error', error: 'unrecognised' });
  });

  it('reads a table with a header, the position split over two columns', () => {
    const text = ['name;lat;lon', 'one;45,5;24,75', 'two;46.1;"25.2"', 'three;46.1'].join('\n');
    expect(csvColumns(text, 'auto')).toEqual(['name', 'lat', 'lon']);
    const { rows } = readBatch(text, { mode: 'csv', separator: 'auto', header: true, columns: [1, 2] });
    expect(rows).toHaveLength(3);
    expect(rows[0].outcome.kind === 'ok' && [rows[0].outcome.lat, rows[0].outcome.lon]).toEqual([45.5, 24.75]);
    expect(rows[1].input).toBe('46.1 25.2');
    expect(rows[2].outcome).toEqual({ kind: 'error', error: 'missingColumn' });
  });

  it('refuses a text that names two different positions, and accepts one read twice the same', () => {
    // A UTM reference whose letter is both a band and a hemisphere is two positions far apart.
    expect(readOne('35S 500000 3800000')).toEqual({ kind: 'error', error: 'ambiguous' });
    expect(readOne('45.5 24.7').kind).toBe('ok');
  });

  it('splits quoted fields and picks the separator a line uses', () => {
    expect(splitCsvLine('"a, b",c,"say ""hi"""', ',')).toEqual(['a, b', 'c', 'say "hi"']);
    expect(detectSeparator('a\tb;c,d')).toBe('\t');
    expect(detectSeparator('a;45,5;24,7')).toBe(';');
    expect(detectSeparator('a,b')).toBe(',');
  });
});

describe('writing the CSV', () => {
  it('writes every asked format per row, an empty cell where a format has no reference, and the error', () => {
    const { rows } = readBatch('45.5, 24.75\nnowhere', { mode: 'lines' });
    const csv = batchCsv(rows, ['dd', 'lv95'], {}, labels);
    const lines = csv.trimEnd().split('\r\n');
    expect(lines[0]).toBe('line,input,lat,lon,dd,lv95,error');
    expect(lines[1]).toMatch(/^1,"45\.5, 24\.75",45\.5000000,24\.7500000,[^,]+,,$/);
    expect(lines[2]).toBe('2,nowhere,,,,,E:unrecognised');
  });
});
