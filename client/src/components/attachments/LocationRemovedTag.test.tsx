// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import i18n from '../../i18n';
import { downloadTitle } from './downloadTitle.ts';
import LocationRemovedTag from './LocationRemovedTag.tsx';

afterEach(cleanup);

describe('the label for a picture whose location was taken out', () => {
  it('says so for the copy a reader is given, and says nothing for the stored file', () => {
    const { rerender } = render(<LocationRemovedTag file={{ mayDownloadOriginal: true, locationRemoved: true }} />);
    expect(screen.getByTestId('location-removed').textContent).toContain('Location removed');

    rerender(<LocationRemovedTag file={{ mayDownloadOriginal: true, locationRemoved: false }} />);
    expect(screen.queryByTestId('location-removed')).toBeNull();
  });

  it('keeps withheld and location-removed apart in what a download control says', () => {
    const t = i18n.t.bind(i18n);
    expect(downloadTitle({ mayDownloadOriginal: false, locationRemoved: false }, t)).toContain('may not download');
    expect(downloadTitle({ mayDownloadOriginal: true, locationRemoved: true }, t)).toContain('location data taken out');
    expect(downloadTitle({ mayDownloadOriginal: true, locationRemoved: false }, t)).toBeUndefined();
  });
});
