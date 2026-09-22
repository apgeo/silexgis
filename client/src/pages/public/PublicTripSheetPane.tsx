// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { markerLine } from '../../caveview/markerLine.ts';
import type { TrackedCaver } from '../../caveview/trackedCavers.ts';
import CaveViewTrackingOverlay, {
  type TrackedPlace,
} from '../../components/caveview/CaveViewTrackingOverlay.tsx';
import { trackedCaverPalette } from '../../map/markerPalette.ts';
import { coarsePointer } from '../../map/pointer.ts';
import {
  placedSheetCavers,
  stationsWithoutPoint,
  type SheetCaverMarker,
} from '../../rastermap/caverPlacement.ts';
import type { PublishedSheet } from '../../rastermap/publishedSheets.ts';
import RasterMapView, { type SheetCaverDrawnMarker } from '../../rastermap/RasterMapView.tsx';
import { usePinnedModelUrl } from './pinnedModelUrl.ts';

interface Props {
  sheet: PublishedSheet;
  /**
   * The party exactly as the page holds it — the same fold the 3D pane draws, so the two
   * drawings can never disagree about who is where. Live today; whatever replays a past
   * trip through the same `TrackedCaver` shape draws on the sheets unchanged.
   */
  cavers: readonly TrackedCaver[];
  /** Whether this pane's tab is the one on screen; forwarded to the OL map's build gate. */
  active: boolean;
  height?: number | string;
  /** The follow token — the page's identity, which is what the pinned URL resets on. */
  token: string | undefined;
  /**
   * The station a replay is keeping the camera on, when one is following a team or a caver.
   *
   * <b>Yields to the reader's own press, and that order is the whole of it.</b> Somebody who has
   * pressed a row in the list beside this sheet has said where they want to look, and a replay's
   * follow re-aiming over them every time the party moves would take the sheet away from them
   * while they are reading it. A follow is what decides the view until a reader decides otherwise.
   */
  followStation?: string | null;
}

/**
 * One published map sheet: the scan, the party drawn on it, and the same list-with-reasons
 * the 3D pane carries — on a page whose reader holds one link and nothing else.
 *
 * <b>Fed exclusively from the envelope.</b> The signed-in pane next door
 * (`RasterMapTrackingPane`) resolves its document, its file and its pins through routes that
 * take an account; every one of those answers arrives here pre-resolved in the sheet, because
 * the envelope is the whole of what this reader can read. Nothing is fetched that the
 * envelope did not hand over, and nothing the envelope handed over is second-guessed.
 *
 * <b>The image URL is pinned the way the model URL is.</b> The envelope re-signs every URL
 * on every poll, and a changed URL makes the map view reload its picture and re-frame — so
 * the address this pane builds from is held still while it is the same rendering and
 * replaced only when the rendering itself changes (a club uploading a better scan mid-trip).
 * A pane not yet opened holds no pin, so a tab first pressed half an hour in reads the
 * freshest signature the poll has restamped — which is the whole reason the page keeps
 * polling a finished trip that carries sheets.
 *
 * <b>The honesty rule, verbatim.</b> A caver is drawn at a station's point or listed with
 * the reason no dot could be drawn — withheld, another survey, nothing reported, or the
 * sheet's own fourth state, "no point on this map". A withholding is never converted into a
 * sheet absence, and an absence never into a guess.
 */
export default function PublicTripSheetPane({
  sheet,
  cavers,
  active,
  height,
  token,
  followStation = null,
}: Props) {
  const { t, i18n } = useTranslation();
  // The same defaults every drawing opens with: names on, times asked for. Per pane, as
  // every surface's own switches are — the switch takes text off this drawing only.
  const [showLabels, setShowLabels] = useState(true);
  const [showTimes, setShowTimes] = useState(false);
  const [openCaverId, setOpenCaverId] = useState<string | null>(null);
  const [shownPlace, setShownPlace] = useState<TrackedPlace | null>(null);

  const imageUrl = usePinnedModelUrl(sheet.imageUrl, token);

  const coarse = coarsePointer();
  const placed = useMemo(
    () => placedSheetCavers(cavers, sheet.markers, coarse ? 20 : 16),
    [cavers, sheet.markers, coarse],
  );
  const unplaced = useMemo(
    () => stationsWithoutPoint(cavers, sheet.markers),
    [cavers, sheet.markers],
  );

  const today = new Date().toLocaleDateString(i18n.language);
  const drawn = useMemo<SheetCaverDrawnMarker[]>(
    () =>
      placed.map((marker) => ({
        marker,
        // The very line the 3D scene prints for the same person — one spelling, and the
        // same two switches deciding what it says.
        label: showLabels
          ? markerLine(marker.caver, { t, language: i18n.language, showTimes, today })
          : null,
        color: marker.caver.out ? trackedCaverPalette.out : trackedCaverPalette.underground,
      })),
    [placed, showLabels, showTimes, t, i18n.language, today],
  );

  /**
   * Where this sheet is looking: the row a reader pressed, or the station a replay is following.
   *
   * <b>A station with no point on this sheet is nowhere to look, and answers null.</b> That is the
   * sheet's own fourth state — the survey holds the station perfectly well and nobody has defined a
   * point for it on this scan — and a view flown at a guess would be this application claiming a
   * measurement nobody made.
   */
  const focus = useMemo(() => {
    const station = shownPlace?.station ?? followStation;
    if (station === null || station === undefined) {
      return null;
    }
    const pin = sheet.markers.find((marker) => marker.station === station);
    return pin === undefined ? null : { x: pin.x, y: pin.y };
  }, [shownPlace, followStation, sheet.markers]);

  /** A press on a caver's dot opens their card — the same card, from the same overlay. */
  const onCaverPressed = (marker: SheetCaverMarker) => {
    const id = marker.caver.caverId;
    setOpenCaverId(id === openCaverId ? null : id);
  };

  if (imageUrl === null) {
    return null;
  }

  return (
    // height:100% is inert on the followed page (the pane is sized by its px height prop)
    // and load-bearing in the embed, where the frame's whole box cascades down to the sheet.
    <div style={{ position: 'relative', height: '100%' }} data-testid="public-sheet-pane">
      <RasterMapView
        imageUrl={imageUrl}
        alt={sheet.title ?? t('rastermap.untitledMap')}
        markers={sheet.markers}
        cavers={drawn}
        active={active}
        height={height}
        onCaverClick={onCaverPressed}
        focus={focus}
      />
      {cavers.length > 0 && (
        <CaveViewTrackingOverlay
          cavers={cavers}
          unplacedStations={unplaced}
          showTimes={showTimes}
          onShowTimesChange={setShowTimes}
          showLabels={showLabels}
          onShowLabelsChange={setShowLabels}
          openCaverId={openCaverId}
          onOpenCaver={setOpenCaverId}
          shown={shownPlace}
          onShow={setShownPlace}
          drawing="map"
        />
      )}
    </div>
  );
}
