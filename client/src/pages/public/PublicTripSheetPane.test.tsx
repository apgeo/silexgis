// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { TrackedCaver, TrackedCaverPosition } from '../../caveview/trackedCavers.ts';
import type { SheetCaverMarker } from '../../rastermap/caverPlacement.ts';
import type { MapStationMarker } from '../../rastermap/mapPoints.ts';
import { sheetsFromEnvelope, type PublishedSheet } from '../../rastermap/publishedSheets.ts';
import type { SheetCaverDrawnMarker } from '../../rastermap/RasterMapView.tsx';

/**
 * A published map sheet: the party drawn through the envelope's points and no other path,
 * everybody else listed with the reason — including the sheet's own fourth state — and the
 * image address pinned across the envelope's own re-signing. The drawing is faked down to
 * its contract, exactly as the signed-in pane's tests fake it; the placement folds and the
 * overlay are real, because their wording is half of what this surface promises.
 */

/** What the pane last handed the drawing, read back by the assertions. */
let view:
  | {
      imageUrl: string;
      markers: readonly MapStationMarker[];
      cavers?: readonly SheetCaverDrawnMarker[];
      focus?: { x: number; y: number } | null;
      onCaverClick?: (marker: SheetCaverMarker) => void;
      onMarkerClick?: unknown;
      onMapClick?: unknown;
    }
  | undefined;

vi.mock('../../rastermap/RasterMapView.tsx', () => ({
  default: function FakeRasterMapView(props: NonNullable<typeof view>) {
    view = props;
    return (
      <div data-testid="fake-sheet">
        {(props.cavers ?? []).map((drawn) => (
          <button
            key={drawn.marker.caver.caverId}
            data-testid={`fake-dot-${drawn.marker.caver.caverId}`}
            onClick={() => props.onCaverClick?.(drawn.marker)}
          />
        ))}
      </div>
    );
  },
}));

const { default: PublicTripSheetPane } = await import('./PublicTripSheetPane.tsx');

function sheet(imageUrl = '/api/v1/files/f1/thumbnail?size=1200&token=first'): PublishedSheet {
  return sheetsFromEnvelope([
    {
      title: 'Plan sheet',
      viewKind: 'plan',
      imageUrl,
      points: [{ station: 'p.g.7', x: 0.25, y: 0.75 }],
    },
  ])[0];
}

function caver(id: string, name: string, position: TrackedCaverPosition): TrackedCaver {
  return {
    caverId: id,
    name,
    teamId: null,
    teamTitle: null,
    position,
    lastRecordedAt: '2026-09-20T10:00:00Z',
    positionAt: '2026-09-20T10:00:00Z',
    enteredAt: null,
    out: false,
  };
}

const ANA = caver('1', 'Ana', { kind: 'station', station: 'p.g.7' });
const BOGDAN = caver('2', 'Caver 2', { kind: 'station', station: 'p.g.99' });

function show(cavers: readonly TrackedCaver[] = [ANA, BOGDAN], given: PublishedSheet = sheet()) {
  return render(
    <PublicTripSheetPane sheet={given} cavers={cavers} active height={300} token="follow-token" />,
  );
}

beforeEach(() => {
  view = undefined;
});

afterEach(cleanup);

describe('PublicTripSheetPane', () => {
  it('draws a caver at the envelope point and lists the one the sheet has no point for', () => {
    show();

    const drawn = view!.cavers ?? [];
    expect(drawn).toHaveLength(1);
    expect(drawn[0].marker.caver.caverId).toBe('1');
    expect(drawn[0].marker).toMatchObject({ station: 'p.g.7', x: 0.25, y: 0.75 });

    // The fourth state, in the map's own words — distinct from the renamed-survey wording.
    expect(screen.getByTestId('caveview-position-not-on-map')).toHaveTextContent(
      'No point on this map',
    );
    expect(screen.queryByTestId('caveview-position-not-on-model')).toBeNull();
  });

  it('keeps the shared states word-for-word: a withholding never reads as a sheet absence', () => {
    show([
      caver('3', 'Withheld One', { kind: 'withheld', certain: false }),
      caver('4', 'Elsewhere One', { kind: 'otherModel' }),
      caver('5', 'Quiet One', { kind: 'unreported' }),
    ]);

    expect(view!.cavers ?? []).toHaveLength(0);
    // The weaker withheld claim is the only one a public fold can make — the envelope
    // carries no report kind — and it keeps exactly its own wording on a sheet.
    expect(screen.getByTestId('caveview-position-maybe-withheld')).toBeInTheDocument();
    expect(screen.getByTestId('caveview-position-other-model')).toBeInTheDocument();
    expect(screen.queryByTestId('caveview-position-not-on-map')).toBeNull();
  });

  it('opens the shared overlay card from a press on the dot', () => {
    show();

    expect(screen.queryByTestId('caveview-caver-card')).toBeNull();
    fireEvent.click(screen.getByTestId('fake-dot-1'));
    expect(screen.getByTestId('caveview-caver-card')).toHaveTextContent('Ana');
  });

  it('offers the drawing no write-shaped click: a public sheet is a picture with people on it', () => {
    show();

    expect(view!.onMarkerClick).toBeUndefined();
    expect(view!.onMapClick).toBeUndefined();
  });

  /**
   * The pinned address, from the pane's side. Every envelope poll re-signs the sheet's URL and
   * the restamp writes it into the very object this pane holds — so without the pin, each poll
   * would hand the drawing a new address, and the drawing reloads its picture and throws away
   * the reader's pan whenever its address changes. Held while it is the same rendering,
   * released the moment it is not, which is the model URL's rule reused.
   */
  it('pins the image address across re-signing and releases it for a new rendering', () => {
    const held = sheet();
    const shown = show([ANA], held);
    expect(view!.imageUrl).toBe('/api/v1/files/f1/thumbnail?size=1200&token=first');

    // The restamp mutates the held sheet, as the derivation does on a poll.
    held.imageUrl = '/api/v1/files/f1/thumbnail?size=1200&token=second';
    shown.rerender(
      <PublicTripSheetPane sheet={held} cavers={[ANA]} active height={300} token="follow-token" />,
    );
    expect(view!.imageUrl).toBe('/api/v1/files/f1/thumbnail?size=1200&token=first');

    // A different file is a different picture, and the pin must not outlive it.
    const rescanned = sheet('/api/v1/files/f2/thumbnail?size=1200&token=third');
    shown.rerender(
      <PublicTripSheetPane
        sheet={rescanned}
        cavers={[ANA]}
        active
        height={300}
        token="follow-token"
      />,
    );
    expect(view!.imageUrl).toBe('/api/v1/files/f2/thumbnail?size=1200&token=third');
  });
});
