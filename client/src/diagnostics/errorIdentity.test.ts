// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import {
  collapseIds,
  fingerprintOf,
  normalizeMessage,
  stripUrls,
  topAppFrame,
} from './errorIdentity.ts';

describe('normalising a message', () => {
  it('reduces absolute addresses to their path and drops the cache-busting query', () => {
    expect(stripUrls('failed at http://localhost:5173/src/map/layers.tsx?t=1712 while drawing')).toBe(
      'failed at /src/map/layers.tsx while drawing',
    );
  });

  it('keeps a message that names no address untouched apart from its spacing', () => {
    expect(normalizeMessage('  Cannot read   properties of undefined ')).toBe(
      'Cannot read properties of undefined',
    );
  });

  it('replaces generated record identifiers, so one defect does not look new every time', () => {
    const first = normalizeMessage('GET /api/v1/caves/019fdbbd-fef9-7443-ac8c-9c736f5a0a98 failed');
    const second = normalizeMessage('GET /api/v1/caves/019fdbc7-d76c-79df-8f45-f2e1d381a1e4 failed');
    expect(first).toBe(second);
    expect(first).toContain('{id}');
  });

  it('replaces a path segment that is nothing but digits, which is an identifier too', () => {
    expect(collapseIds('/api/v1/relation-types/11')).toBe(collapseIds('/api/v1/relation-types/12'));
  });

  it('leaves short numbers alone, so two errors do not merge just by both mentioning one', () => {
    expect(collapseIds('expected 2 columns, got 3')).toBe('expected 2 columns, got 3');
  });
});

describe('choosing the frame to blame', () => {
  const stack = [
    'TypeError: nope',
    '    at getStyle (http://localhost:5173/node_modules/.vite/deps/ol.js?v=abc:900:11)',
    '    at drawLayer (http://localhost:5173/src/map/layers.tsx:120:8)',
    '    at renderFrame (http://localhost:5173/src/map/mapContext.ts:44:3)',
  ].join('\n');

  // Line and column are kept: this string is meant to be opened by a person. It is `fingerprintOf`
  // that drops them, so that an edit moving the code does not invent a new defect.
  it('prefers the first frame in our own code over the library above it', () => {
    expect(topAppFrame(stack)).toBe('/src/map/layers.tsx:120:8');
  });

  it('falls back to the topmost frame when nothing in the stack is ours', () => {
    const libraryOnly = ['Error: x', '    at f (http://localhost:5173/node_modules/.vite/deps/ol.js:1:1)'].join(
      '\n',
    );
    expect(topAppFrame(libraryOnly)).toBe('/node_modules/.vite/deps/ol.js:1:1');
  });

  it('skips frames it is told to ignore, so the reporting code never blames itself', () => {
    const throughReporter = [
      'Error: x',
      '    at report (http://localhost:5173/src/diagnostics/reporter.ts:30:5)',
      '    at save (http://localhost:5173/src/pages/caves/CaveFormPage.tsx:88:9)',
    ].join('\n');
    expect(topAppFrame(throughReporter, /\/src\/diagnostics\//)).toBe(
      '/src/pages/caves/CaveFormPage.tsx:88:9',
    );
  });
});

describe('the identity of a defect', () => {
  const thrown = (frame: string, message = 'Cannot read properties of undefined') => ({
    kind: 'uncaught' as const,
    name: 'TypeError',
    normalized: message,
    frame,
  });

  it('is the same for a throw seen twice at different lines of the same file', () => {
    expect(fingerprintOf(thrown('/src/map/layers.tsx:120:8'))).toBe(
      fingerprintOf(thrown('/src/map/layers.tsx:126:8')),
    );
  });

  it('differs when the same message is thrown from a different file', () => {
    expect(fingerprintOf(thrown('/src/map/layers.tsx:120:8'))).not.toBe(
      fingerprintOf(thrown('/src/pages/caves/CaveDetailPage.tsx:120:8')),
    );
  });

  it('is eight hexadecimal characters, so it can be used as a key in prose', () => {
    expect(fingerprintOf(thrown('/src/map/layers.tsx:1:1'))).toMatch(/^[0-9a-f]{8}$/);
  });

  // The reason this module exists: the two observers of a console error know different amounts about
  // it, and the identity must not depend on the part they disagree about. The application knows the
  // line of its own code that called `console.error`; the suite watching from outside is told the
  // development server's wrapper did.
  it('is the same for a console error whichever observer saw it', () => {
    const fromInsideTheApp = {
      kind: 'console' as const,
      name: '',
      normalized: 'Warning: something is deprecated',
      frame: '/src/components/AppLayout.tsx:42:7',
    };
    const fromTheSuiteOutside = { ...fromInsideTheApp, frame: '/@vite/client:524:3' };
    expect(fingerprintOf(fromInsideTheApp)).toBe(fingerprintOf(fromTheSuiteOutside));
  });

  it('still tells two failed requests apart, because the address is all they have', () => {
    const failed = (address: string) => ({
      kind: 'console' as const,
      name: '',
      normalized: 'Failed to load resource: the server responded with a status of 404 (Not Found)',
      frame: address,
    });
    expect(fingerprintOf(failed('/api/v1/caves/{id}'))).not.toBe(
      fingerprintOf(failed('/api/v1/caves/{id}/summary')),
    );
  });

  it('groups the same failed request asked for two different records', () => {
    const failed = (address: string) => ({
      kind: 'console' as const,
      name: '',
      normalized: 'Failed to load resource: the server responded with a status of 404 (Not Found)',
      frame: address,
    });
    expect(fingerprintOf(failed('/api/v1/caves/019fdbbd-fef9-7443-ac8c-9c736f5a0a98'))).toBe(
      fingerprintOf(failed('/api/v1/caves/019fdbc7-d76c-79df-8f45-f2e1d381a1e4')),
    );
  });

  it('does not confuse a throw with a console error carrying the same words', () => {
    expect(fingerprintOf(thrown('/src/a.ts:1:1', 'boom'))).not.toBe(
      fingerprintOf({ kind: 'console', name: '', normalized: 'boom', frame: '/src/a.ts:1:1' }),
    );
  });
});
