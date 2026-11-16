// SPDX-License-Identifier: AGPL-3.0-or-later
import { App, ConfigProvider } from 'antd';
import { cleanup, render, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';

/**
 * The point picker builds a map of its own each time it opens. Each one has to be let go when the
 * dialog closes — a map left attached keeps its listeners and its tile requests — and opening the
 * dialog again must not leave the first one behind. The map is a stand-in that records this.
 */

const { built } = vi.hoisted(() => ({
  built: [] as { target: unknown; disposed: boolean }[],
}));

vi.mock('ol/Map', () => ({
  default: class {
    record: { target: unknown; disposed: boolean };
    constructor(options: { target: unknown }) {
      this.record = { target: options.target, disposed: false };
      built.push(this.record);
    }
    on() {}
    getView() {
      return { setCenter: () => {} };
    }
    setTarget(target: unknown) {
      this.record.target = target;
    }
    dispose() {
      this.record.target = undefined;
      this.record.disposed = true;
    }
  },
}));

vi.mock('../coordinates/PositionTextInput.tsx', () => ({ default: () => null }));

const { default: PointPickerModal } = await import('./PointPickerModal.tsx');

function picker(open: boolean) {
  return (
    // Without motion, so opening and closing finish at once and the dialog says so.
    <ConfigProvider theme={{ token: { motion: false } }}>
      <App>
        <PointPickerModal open={open} value={[25.6, 45.65]} onCancel={() => {}} onPick={() => {}} />
      </App>
    </ConfigProvider>
  );
}

beforeEach(() => {
  built.length = 0;
});
afterEach(cleanup);

describe('PointPickerModal', () => {
  it('builds one map when it opens and lets it go when it closes, every time', async () => {
    const { rerender } = render(picker(true));
    await waitFor(() => expect(built).toHaveLength(1));

    rerender(picker(false));
    await waitFor(() => expect(built[0].disposed).toBe(true));

    rerender(picker(true));
    await waitFor(() => expect(built).toHaveLength(2));
    expect(built[1].disposed).toBe(false);

    rerender(picker(false));
    await waitFor(() => expect(built.every((map) => map.disposed)).toBe(true));
  });

  it('lets its map go when the page leaves with the dialog still open', async () => {
    const { unmount } = render(picker(true));
    await waitFor(() => expect(built).toHaveLength(1));

    unmount();
    expect(built[0].disposed).toBe(true);
  });
});
