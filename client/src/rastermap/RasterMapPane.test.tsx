// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { App } from 'antd';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../i18n';
import type { ResLink, ResLinkMember } from '../api/hooks.ts';
import type { PaneAuthoring } from './RasterMapPane.tsx';
import RasterMapPane from './RasterMapPane.tsx';
import type { MapStationMarker } from './mapPoints.ts';
import type { RasterMapDeclaration } from './rasterMaps.ts';

/**
 * The pane's authoring surface: what a placement click writes, when the duplicate
 * warning stands in the way, which corrections are offered against which pins — and,
 * throughout, that a control is offered exactly where the server would accept the write
 * it stands for. The drawing itself is faked down to its click contract; the writes are
 * spies; everything asserted here is the pane's own decision.
 */
const createLink = vi.fn();
const deleteLink = vi.fn();

vi.mock('../api/hooks.ts', () => ({
  useCreateResLink: () => ({ mutateAsync: createLink, isPending: false }),
  useDeleteResLink: () => ({ mutateAsync: deleteLink, isPending: false }),
  useResLinkRelationTypes: () => ({
    data: [
      { id: 7, code: 'map-station-point', name: 'x', directed: true, inverseName: 'x' },
      { id: 3, code: 'map-plan-of', name: 'x', directed: true, inverseName: 'x' },
    ],
  }),
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

// The real view is an OpenLayers map; the pane's responsibility toward it is exactly the
// click contract, so the fake is that contract with buttons on it.
vi.mock('./RasterMapView.tsx', () => ({
  default: function FakeRasterMapView({
    markers,
    onMapClick,
    onMarkerClick,
  }: {
    markers: readonly MapStationMarker[];
    onMapClick?: (at: { x: number; y: number }) => void;
    onMarkerClick?: (marker: MapStationMarker, at: { x: number; y: number } | null) => void;
  }) {
    return (
      <div data-testid="fake-view" data-writing={String(onMapClick !== undefined)}>
        <button data-testid="fake-click-sheet" onClick={() => onMapClick?.({ x: 0.5, y: 0.25 })} />
        {markers.map((marker) => (
          <div key={marker.memberId}>
            {/* A press inside the marker's halo: the click's own point rides along… */}
            <button
              data-testid={`fake-marker-${marker.station}`}
              onClick={() => onMarkerClick?.(marker, { x: 0.31, y: 0.62 })}
            />
            {/* …and its twin where the click sat in the halo but outside the picture. */}
            <button
              data-testid={`fake-marker-outside-${marker.station}`}
              onClick={() => onMarkerClick?.(marker, null)}
            />
          </div>
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
    id: 'member-1',
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
function pinLink(
  station: string,
  overrides: Partial<{ x: number; y: number; fileId: string; mayEdit: boolean; createdAt: string }> = {},
): ResLink {
  linkCounter += 1;
  const { x = 0.25, y = 0.75, fileId = FILE, mayEdit = true, createdAt = '2026-09-01T10:00:00Z' } =
    overrides;
  return {
    id: `link-${linkCounter}`,
    shortCode: 'ABCD1234',
    relationType: { id: 7, code: 'map-station-point', name: 'x', directed: true, inverseName: 'x' },
    description: null,
    createdAt,
    updatedAt: createdAt,
    mayEdit,
    members: [
      member({
        id: `point-${linkCounter}`,
        anchorKind: 'imageRegion',
        anchor: { shape: 'point', x, y } as unknown as ResLinkMember['anchor'],
        anchorFileId: fileId,
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

const declaration = (mayEdit = true): RasterMapDeclaration => ({
  linkId: 'map-link',
  documentId: DOC,
  viewKind: 'plan',
  title: 'Plan sheet',
  createdAt: '2026-08-01T10:00:00Z',
  description: null,
  mayEdit,
});

const authoring = (overrides: Partial<PaneAuthoring> = {}): PaneAuthoring => ({
  defining: true,
  onToggleDefining: vi.fn(),
  armed: null,
  onArm: vi.fn(),
  onDisarm: vi.fn(),
  onEditDeclaration: vi.fn(),
  ...overrides,
});

function show(links: ResLink[], lease?: PaneAuthoring, mapMayEdit = true) {
  return render(
    <App>
      <RasterMapPane
        declaration={declaration(mapMayEdit)}
        links={links}
        surveyModelId={MODEL}
        active
        authoring={lease}
      />
    </App>,
  );
}

beforeEach(() => {
  createLink.mockReset().mockResolvedValue({ id: 'made' });
  deleteLink.mockReset().mockResolvedValue(undefined);
});

afterEach(() => {
  cleanup();
});

describe('the authoring controls', () => {
  it('a read-only mount (no lease) shows no authoring controls and takes no clicks', () => {
    show([pinLink('p.g.7')]);
    expect(screen.queryByTestId('rastermap-define')).not.toBeInTheDocument();
    expect(screen.getByTestId('fake-view')).toHaveAttribute('data-writing', 'false');
  });

  it('with the lease, define toggles through the modal and settings follow the curation answer', () => {
    const lease = authoring({ defining: false });
    show([pinLink('p.g.7')], lease);

    fireEvent.click(screen.getByTestId('rastermap-define'));
    expect(lease.onToggleDefining).toHaveBeenCalled();

    // The settings doorway is offered exactly when the server would accept the edit —
    // the declaration link's own mayEdit — never as an offer that earns a refusal.
    fireEvent.click(screen.getByTestId('rastermap-map-settings'));
    expect(lease.onEditDeclaration).toHaveBeenCalled();
  });

  it('hides map settings from a caller the server would refuse', () => {
    show([pinLink('p.g.7')], authoring(), false);
    expect(screen.queryByTestId('rastermap-map-settings')).not.toBeInTheDocument();
    // The define control stays: creating a pin needs only the read floor this pane proves.
    expect(screen.getByTestId('rastermap-define')).toBeInTheDocument();
  });
});

describe('placing', () => {
  it('an armed click on a free station writes the one designed POST and spends the arm', async () => {
    const lease = authoring({ armed: { station: 'p.g.9', replaceLinkId: null } });
    show([pinLink('p.g.7')], lease);

    fireEvent.click(screen.getByTestId('fake-click-sheet'));

    await waitFor(() => expect(createLink).toHaveBeenCalledTimes(1));
    expect(createLink).toHaveBeenCalledWith({
      relationTypeId: 7,
      description: null,
      members: [
        {
          targetType: 'document',
          targetId: DOC,
          isMain: true,
          sortOrder: 0,
          note: null,
          anchorKind: 'imageRegion',
          anchor: { shape: 'point', x: 0.5, y: 0.25 },
          anchorFileId: FILE,
        },
        {
          targetType: 'surveyModel',
          targetId: MODEL,
          isMain: false,
          sortOrder: 1,
          note: null,
          anchorKind: 'modelStation',
          anchor: { station: 'p.g.9' },
          anchorFileId: null,
        },
      ],
    });
    expect(deleteLink).not.toHaveBeenCalled();
    await waitFor(() => expect(lease.onDisarm).toHaveBeenCalled());
  });

  it('without an armed station a sheet click is not a write', () => {
    show([pinLink('p.g.7')], authoring());
    // The fake still exposes the button; the pane hands the view no placement handler.
    expect(screen.getByTestId('fake-view')).toHaveAttribute('data-writing', 'false');
  });

  it('warns instead of writing when the station already has a pin, and move-here rewrites it', async () => {
    const standing = pinLink('p.g.7');
    const lease = authoring({ armed: { station: 'p.g.7', replaceLinkId: null } });
    show([standing], lease);

    fireEvent.click(screen.getByTestId('fake-click-sheet'));

    // Nothing written yet: the duplicate warning stands in front of the write.
    expect(createLink).not.toHaveBeenCalled();
    expect(await screen.findByTestId('rastermap-move-here')).toBeInTheDocument();

    fireEvent.click(screen.getByTestId('rastermap-move-here'));
    await waitFor(() => expect(deleteLink).toHaveBeenCalledWith(standing.id));
    expect(createLink).toHaveBeenCalledTimes(1);
    // Create lands before delete, so a failure between them can only leave a duplicate
    // (which the fold resolves newest-wins), never a station without its pin.
    expect(createLink.mock.invocationCallOrder[0]).toBeLessThan(
      deleteLink.mock.invocationCallOrder[0],
    );
  });

  it('offers no move-here against a pin the server would refuse to edit', async () => {
    const foreign = pinLink('p.g.7', { mayEdit: false });
    show([foreign], authoring({ armed: { station: 'p.g.7', replaceLinkId: null } }));

    fireEvent.click(screen.getByTestId('fake-click-sheet'));

    // The warning still explains; the write the server would 403 is simply not offered.
    expect(await screen.findByText(/You may not correct this point/)).toBeInTheDocument();
    expect(screen.queryByTestId('rastermap-move-here')).not.toBeInTheDocument();
  });

  it('an armed press on ANOTHER station\'s marker places at the click, not the neighbor', async () => {
    // p.g.7's marker sits at its stored (0.25, 0.75); the author, armed with p.g.9,
    // clicks a spot inside that marker's halo. The pin must land where the click did —
    // silently substituting the neighbor's stored point would forge a calibration-grade
    // position nobody measured.
    const lease = authoring({ armed: { station: 'p.g.9', replaceLinkId: null } });
    show([pinLink('p.g.7')], lease);

    fireEvent.click(screen.getByTestId('fake-marker-p.g.7'));

    await waitFor(() => expect(createLink).toHaveBeenCalledTimes(1));
    const body = createLink.mock.calls[0][0] as {
      members: { anchor: unknown; anchorKind: string }[];
    };
    expect(body.members[0].anchor).toEqual({ shape: 'point', x: 0.31, y: 0.62 });
    expect(body.members[0].anchor).not.toEqual({ shape: 'point', x: 0.25, y: 0.75 });
    expect(body.members[1].anchor).toEqual({ station: 'p.g.9' });
    await waitFor(() => expect(lease.onDisarm).toHaveBeenCalled());
  });

  it('an armed press on the armed station\'s OWN marker keeps its exact stored point', async () => {
    // The move flow armed p.g.7 against its own link; pressing its marker means "keep it
    // exactly here" — the stored point, never the click re-measured against the drawing.
    const own = pinLink('p.g.7');
    show([own], authoring({ armed: { station: 'p.g.7', replaceLinkId: own.id } }));

    fireEvent.click(screen.getByTestId('fake-marker-p.g.7'));

    await waitFor(() => expect(deleteLink).toHaveBeenCalledWith(own.id));
    const body = createLink.mock.calls[0][0] as { members: { anchor: unknown }[] };
    expect(body.members[0].anchor).toEqual({ shape: 'point', x: 0.25, y: 0.75 });
  });

  it('an armed press in a halo outside the picture places nothing', async () => {
    // The positive twin is the in-halo test above: the same armed press with a click the
    // picture contains writes. Outside the picture there is no honest point to store.
    show([pinLink('p.g.7')], authoring({ armed: { station: 'p.g.9', replaceLinkId: null } }));

    fireEvent.click(screen.getByTestId('fake-marker-outside-p.g.7'));

    expect(createLink).not.toHaveBeenCalled();
    expect(deleteLink).not.toHaveBeenCalled();
    // Nor does the press fall through to the correction modal — the arm still stands.
    expect(screen.queryByTestId('rastermap-move-pin')).not.toBeInTheDocument();
  });

  it('an arm that names its link (re-place, move) rewrites without questions', async () => {
    const stranded = pinLink('p.g.7', { fileId: 'file-0' });
    show([stranded], authoring({ armed: { station: 'p.g.7', replaceLinkId: stranded.id } }));

    fireEvent.click(screen.getByTestId('fake-click-sheet'));

    await waitFor(() => expect(deleteLink).toHaveBeenCalledWith(stranded.id));
    // The corrected link pins the file on screen — that is what re-placing means.
    const body = createLink.mock.calls[0][0] as {
      members: { anchorFileId: string | null }[];
    };
    expect(body.members[0].anchorFileId).toBe(FILE);
    expect(screen.queryByTestId('rastermap-move-here')).not.toBeInTheDocument();
  });
});

describe('correcting from a pressed marker', () => {
  it('offers move and delete on an editable pin; move re-arms against the same link', async () => {
    const own = pinLink('p.g.7');
    const lease = authoring();
    show([own], lease);

    fireEvent.click(screen.getByTestId('fake-marker-p.g.7'));
    expect(await screen.findByTestId('rastermap-move-pin')).toBeInTheDocument();

    fireEvent.click(screen.getByTestId('rastermap-move-pin'));
    expect(lease.onArm).toHaveBeenCalledWith({ station: 'p.g.7', replaceLinkId: own.id });
  });

  it('delete removes the whole link — the pair is the link', async () => {
    const own = pinLink('p.g.7');
    show([own], authoring());

    fireEvent.click(screen.getByTestId('fake-marker-p.g.7'));
    fireEvent.click(await screen.findByTestId('rastermap-delete-pin'));

    await waitFor(() => expect(deleteLink).toHaveBeenCalledWith(own.id));
    expect(createLink).not.toHaveBeenCalled();
  });

  it('offers neither against a pin the server would refuse, and says why', async () => {
    show([pinLink('p.g.7', { mayEdit: false })], authoring());

    fireEvent.click(screen.getByTestId('fake-marker-p.g.7'));

    expect(await screen.findByText(/You may not correct this point/)).toBeInTheDocument();
    expect(screen.queryByTestId('rastermap-move-pin')).not.toBeInTheDocument();
    expect(screen.queryByTestId('rastermap-delete-pin')).not.toBeInTheDocument();
  });
});

describe('the superseded work list', () => {
  it('in define mode, lists stranded stations with re-place arming against their own link', () => {
    const stranded = pinLink('p.g.8', { fileId: 'file-0' });
    const lease = authoring();
    show([stranded, pinLink('p.g.7')], lease);

    const list = screen.getByTestId('rastermap-superseded-list');
    expect(list).toHaveTextContent('p.g.8');
    // The current-file pin is not on the work list — its twin is the drawn marker.
    expect(list).not.toHaveTextContent('p.g.7');

    fireEvent.click(screen.getByTestId('rastermap-replace'));
    expect(lease.onArm).toHaveBeenCalledWith({ station: 'p.g.8', replaceLinkId: stranded.id });
  });

  it('offers no re-place against a stranded pin the server would refuse to edit', () => {
    show([pinLink('p.g.8', { fileId: 'file-0', mayEdit: false })], authoring());

    expect(screen.getByTestId('rastermap-superseded-list')).toHaveTextContent('p.g.8');
    expect(screen.queryByTestId('rastermap-replace')).not.toBeInTheDocument();
  });

  it('outside define mode the same fact stays the read surface count', () => {
    show([pinLink('p.g.8', { fileId: 'file-0' })], authoring({ defining: false }));

    expect(screen.getByTestId('rastermap-superseded')).toBeInTheDocument();
    expect(screen.queryByTestId('rastermap-superseded-list')).not.toBeInTheDocument();
  });
});
