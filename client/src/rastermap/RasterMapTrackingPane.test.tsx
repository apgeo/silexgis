// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../i18n';
import type { ResLink, ResLinkMember } from '../api/hooks.ts';
import type { TrackedCaver, TrackedCaverPosition } from '../caveview/trackedCavers.ts';
import RasterMapTrackingPane from './RasterMapTrackingPane.tsx';
import type { SheetCaverMarker } from './caverPlacement.ts';
import type { MapStationMarker } from './mapPoints.ts';
import type { RasterMapDeclaration } from './rasterMaps.ts';
import type { SheetCaverDrawnMarker } from './RasterMapView.tsx';

/**
 * The coordinator's map sheet: the party drawn through station pins and no other path,
 * everybody else listed with the sheet's own reason — "no point on this map", a fourth
 * state distinct from the three the 3D list already tells — and the presses: a caver's
 * dot opens the same card the 3D scene opens, a pinned station raises the record-here
 * channel. The drawing is faked down to its contract; the folds and the overlay are real.
 */

vi.mock('../api/hooks.ts', () => ({
  useDocument: () => ({ data: { id: 'doc-1', currentFileId: 'file-1' } }),
  useFile: () => ({
    data: {
      id: 'file-1',
      mimeType: 'image/png',
      contentUrl: 'http://files.local/f1',
      thumbnailUrl: null,
      mayDownloadOriginal: true,
    },
  }),
}));

/** What the pane last handed the drawing, read back by the assertions. */
let view:
  | {
      markers: readonly MapStationMarker[];
      cavers?: readonly SheetCaverDrawnMarker[];
      focus?: { x: number; y: number } | null;
      onCaverClick?: (marker: SheetCaverMarker) => void;
      onMarkerClick?: (marker: MapStationMarker, at: { x: number; y: number } | null) => void;
    }
  | undefined;

vi.mock('./RasterMapView.tsx', () => ({
  default: function FakeRasterMapView(props: NonNullable<typeof view>) {
    view = props;
    return (
      <div data-testid="fake-view">
        {(props.cavers ?? []).map((drawn) => (
          <button
            key={drawn.marker.caver.caverId}
            data-testid={`fake-dot-${drawn.marker.caver.caverId}`}
            onClick={() => props.onCaverClick?.(drawn.marker)}
          />
        ))}
        {props.markers.map((marker) => (
          <button
            key={marker.memberId}
            data-testid={`fake-pin-${marker.station}`}
            onClick={() => props.onMarkerClick?.(marker, { x: marker.x, y: marker.y })}
          />
        ))}
      </div>
    );
  },
}));

const MODEL = 'model-1';
const DOC = 'doc-1';
const FILE = 'file-1';

function member(overrides: Partial<ResLinkMember>): ResLinkMember {
  return {
    id: `m-${Math.random()}`,
    targetType: 'document',
    targetId: DOC,
    isMain: false,
    sortOrder: 0,
    note: null,
    anchorKind: 'whole',
    anchor: null,
    anchorFileId: null,
    anchorState: 'exact',
    display: null,
    ...overrides,
  } as ResLinkMember;
}

let linkCounter = 0;
function pinLink(station: string, x = 0.25, y = 0.75): ResLink {
  linkCounter += 1;
  return {
    id: `link-${linkCounter}`,
    shortCode: 'ABCD1234',
    relationType: { id: 7, code: 'map-station-point', name: 'x', directed: true, inverseName: 'x' },
    description: null,
    createdAt: '2026-09-01T10:00:00Z',
    updatedAt: '2026-09-01T10:00:00Z',
    mayEdit: true,
    members: [
      member({
        id: `point-${linkCounter}`,
        anchorKind: 'imageRegion',
        anchor: { shape: 'point', x, y } as unknown as ResLinkMember['anchor'],
        anchorFileId: FILE,
      }),
      member({
        id: `station-${linkCounter}`,
        targetType: 'surveyModel',
        targetId: MODEL,
        anchorKind: 'modelStation',
        anchor: { station } as unknown as ResLinkMember['anchor'],
      }),
    ],
  } as unknown as ResLink;
}

const declaration: RasterMapDeclaration = {
  linkId: 'decl-1',
  documentId: DOC,
  viewKind: 'plan',
  title: 'Plan sheet',
  createdAt: '2026-09-01T09:00:00Z',
  description: null,
  mayEdit: true,
};

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

const ANA = caver('caver-ana', 'Ana Popescu', { kind: 'station', station: 'p.g.7' });
const BOGDAN = caver('caver-bogdan', 'Bogdan Ilie', { kind: 'station', station: 'p.g.99' });

function show(
  cavers: readonly TrackedCaver[] = [ANA, BOGDAN],
  onPickStation?: (station: string) => void,
) {
  return render(
    <RasterMapTrackingPane
      declaration={declaration}
      links={[pinLink('p.g.7')]}
      surveyModelId={MODEL}
      active
      cavers={cavers}
      onPickStation={onPickStation}
    />,
  );
}

beforeEach(() => {
  view = undefined;
  linkCounter = 0;
});

afterEach(cleanup);

describe('RasterMapTrackingPane', () => {
  it('draws a caver whose station has a point here, and not the one whose station has none', () => {
    show();

    const drawn = view!.cavers ?? [];
    expect(drawn).toHaveLength(1);
    expect(drawn[0].marker.caver.caverId).toBe('caver-ana');
    // At the pin's own stored point — the fold's rule, visible at the contract.
    expect(drawn[0].marker).toMatchObject({ station: 'p.g.7', x: 0.25, y: 0.75 });
  });

  it('says "no point on this map" for the unplaced caver — the sheet’s own words, not the 3D pane’s', () => {
    show();

    // The fourth state, worded as the map's: distinct from "not on the drawing",
    // "not shown to you" and "no position reported".
    expect(screen.getByTestId('caveview-position-not-on-map')).toHaveTextContent(
      'No point on this map',
    );
    expect(screen.queryByTestId('caveview-position-not-on-model')).toBeNull();

    // The placed caver's row names the station plainly — no message, because none is due.
    expect(screen.getByTestId('caveview-caver-caver-ana')).not.toHaveTextContent(
      'No point on this map',
    );
  });

  it('opens the very overlay card from a press on the caver’s dot', () => {
    show();

    expect(screen.queryByTestId('caveview-caver-card')).toBeNull();
    fireEvent.click(screen.getByTestId('fake-dot-caver-ana'));

    // The same card component the 3D scene opens, with the same facts on it.
    expect(screen.getByTestId('caveview-caver-card')).toHaveTextContent('Ana Popescu');

    // Pressed again, the card goes away — the dot toggles like the list row does.
    fireEvent.click(screen.getByTestId('fake-dot-caver-ana'));
    expect(screen.queryByTestId('caveview-caver-card')).toBeNull();
  });

  it('explains the missing marker at length on the unplaced caver’s card', () => {
    show();

    fireEvent.click(screen.getByTestId('caveview-caver-caver-bogdan'));

    expect(screen.getByTestId('caveview-caver-card-not-on-map')).toHaveTextContent(
      'this map has no point defined',
    );
    expect(screen.queryByTestId('caveview-caver-card-not-on-model')).toBeNull();
  });

  it('hands a pinned station press up the record channel, and no channel where none was given', () => {
    const picked: string[] = [];
    show([ANA, BOGDAN], (station) => picked.push(station));

    fireEvent.click(screen.getByTestId('fake-pin-p.g.7'));
    expect(picked).toEqual(['p.g.7']);

    // The absence twin: without the channel, the drawing is not offered a click meaning.
    cleanup();
    show();
    expect(view!.onMarkerClick).toBeUndefined();
  });

  it('labels the dots with the one marker line, and takes the words off with the switch', () => {
    show();

    expect((view!.cavers ?? [])[0].label).toBe('Ana Popescu');

    fireEvent.click(screen.getByTestId('caveview-tracking-labels'));
    expect((view!.cavers ?? [])[0].label).toBeNull();
  });

  it('sends the view to the pin when a placed row is pressed, and nowhere for an unplaced one', () => {
    show();
    expect(view!.focus).toBeNull();

    fireEvent.click(screen.getByTestId('caveview-caver-caver-ana'));
    expect(view!.focus).toEqual({ x: 0.25, y: 0.75 });

    // The unplaced caver's row opens their card but asks the sheet for no place: there
    // is no point to fly to, and the row already says why.
    fireEvent.click(screen.getByTestId('caveview-caver-caver-ana'));
    fireEvent.click(screen.getByTestId('caveview-caver-caver-bogdan'));
    expect(view!.focus).toBeNull();
  });

  it('keeps the three shared states word-for-word: withheld stays withheld on a sheet', () => {
    show([
      caver('caver-w', 'Withheld One', { kind: 'withheld', certain: true }),
      caver('caver-o', 'Elsewhere One', { kind: 'otherModel' }),
    ]);

    // Neither is drawn — nothing names a station — and neither is converted into the
    // sheet's own absence: a withholding must never read as "no point on this map".
    expect(view!.cavers ?? []).toHaveLength(0);
    expect(screen.getByTestId('caveview-position-withheld')).toBeInTheDocument();
    expect(screen.getByTestId('caveview-position-other-model')).toBeInTheDocument();
    expect(screen.queryByTestId('caveview-position-not-on-map')).toBeNull();
  });
});
