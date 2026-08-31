// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import '../../i18n';
import QrCodeSquare from './QrCodeSquare.tsx';
import { isPrintableCode, printedCode } from './printedCode.ts';

afterEach(cleanup);

function show(code: string) {
  render(
    <App>
      <QrCodeSquare code={code} />
    </App>,
  );
}

describe('printedCode', () => {
  it('reads the code the caving app stores on a place', () => {
    expect(printedCode({ speleolocQcri: 'a1b2c3d4' })).toBe('a1b2c3d4');
    expect(printedCode({ speleolocPci: 'RO-BH-0001-014' })).toBe('RO-BH-0001-014');
  });

  it('prefers the derived code over the raw place code, which is what a label carries', () => {
    expect(printedCode({ speleolocPci: 'RO-BH-0001-014', speleolocQcri: 'a1b2c3d4' }))
      .toBe('a1b2c3d4');
  });

  it('finds no code in a document that carries none, or none worth printing', () => {
    expect(printedCode({ speleolocDepthInCave: 12 })).toBeNull();
    expect(printedCode({ speleolocQcri: '   ' })).toBeNull();
    expect(printedCode({ speleolocQcri: 42 })).toBeNull();
    expect(printedCode(null)).toBeNull();
    expect(printedCode('a1b2c3d4')).toBeNull();
  });
});

describe('isPrintableCode', () => {
  it('accepts what the derivation produces', () => {
    expect(isPrintableCode('a1b2c3d4')).toBe(true);
    expect(isPrintableCode('RO-BH-0001-014')).toBe(true);
  });

  it('rejects what the phone scanner cannot read back off a label', () => {
    // Truncated at the last delimiter rather than escaped: the scanner splits on these.
    expect(isPrintableCode('a/b')).toBe(false);
    expect(isPrintableCode('a=b')).toBe(false);
    // Escaped into the address, and nothing unescapes it on the way back.
    expect(isPrintableCode('Peștera 01')).toBe(false);
  });
});

describe('QrCodeSquare', () => {
  it('carries the landing address for the installation being looked at', () => {
    show('a1b2c3d4');

    expect(screen.getByTestId('qr-code-square')).toBeTruthy();
    expect(screen.getByDisplayValue(`${window.location.origin}/q/a1b2c3d4`)).toBeTruthy();
    // The characters are readable, because a camera fails where a person can still transcribe.
    expect(screen.getByText('a1b2c3d4')).toBeTruthy();
    expect(screen.queryByTestId('qr-code-unscannable')).toBeNull();
  });

  it('says so when the code cannot survive the trip onto a label and back', () => {
    // A browser opens this address perfectly well, which is precisely why the warning exists.
    show('a/b');

    expect(screen.getByTestId('qr-code-square')).toBeTruthy();
    expect(screen.getByTestId('qr-code-unscannable')).toBeTruthy();
  });
});
