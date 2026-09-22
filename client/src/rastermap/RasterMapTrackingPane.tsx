// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo, useState } from 'react';
import { Skeleton, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { useDocument, useFile, type ResLink } from '../api/hooks.ts';
import { markerLine } from '../caveview/markerLine.ts';
import { trackedCaverPalette } from '../map/markerPalette.ts';
import { coarsePointer } from '../map/pointer.ts';
import type { TrackedCaver } from '../caveview/trackedCavers.ts';
import CaveViewTrackingOverlay, {
  type TrackedPlace,
} from '../components/caveview/CaveViewTrackingOverlay.tsx';
import { displayableImageUrl } from '../components/documents/derivativeUrl.ts';
import {
  placedSheetCavers,
  stationsWithoutPoint,
  type SheetCaverMarker,
} from './caverPlacement.ts';
import { mapPinsFromLinks, stationMarkers, supersededPointCount } from './mapPoints.ts';
import type { RasterMapDeclaration } from './rasterMaps.ts';
import RasterMapView, { type SheetCaverDrawnMarker } from './RasterMapView.tsx';

interface Props {
  declaration: RasterMapDeclaration;
  /** The model's incident links — the same one answer the tab strip was folded from. */
  links: readonly ResLink[];
  surveyModelId: string;
  /** Whether this pane is the one on screen; forwarded to the OL map's build gate. */
  active: boolean;
  height?: number | string;
  /**
   * The party as the panel above holds it — live, or the replay's fold of a past moment.
   * Consumed unchanged: who is where has exactly one source of truth, and this pane must
   * show the same people, in the same states, as the 3D pane beside it and the table
   * above them both.
   */
  cavers: readonly TrackedCaver[];
  /**
   * A press on a pinned station, handed up as the station's path so the owner can raise
   * the very record-here offer a 3D station press raises. Absent where recording is not
   * offered at all — then a pin press means nothing and is not pretended to.
   */
  onPickStation?: (station: string) => void;
}

/**
 * One declared map inside the coordinator's tracking panel: the sheet, the party drawn
 * on it, and the same list-with-reasons the 3D pane carries.
 *
 * <b>The honesty rule, per sheet.</b> A caver is drawn at a station's pin iff their
 * reported station has a point defined on the rendering on screen — the fold owns that
 * rule — and everybody else is listed with the reason a marker is missing: withheld,
 * another survey, nothing reported, or — the state only a sheet has — the station has no
 * point on this map. That last one is worded as its own thing ("no point on this map"),
 * because beside a sheet it is the ordinary, remediable state of a partially-pinned map,
 * not the renamed-survey failure the 3D wording describes.
 *
 * <b>Read-only by design.</b> Pins are authored in the survey viewer; here they place
 * people. The one act a pin offers is the same act a 3D station press offers — record a
 * report here — and it is the owner's alert that carries it, outside the tab strip, so
 * one offer governs whichever pane is showing.
 */
export default function RasterMapTrackingPane({
  declaration,
  links,
  surveyModelId,
  active,
  height,
  cavers,
  onPickStation,
}: Props) {
  const { t, i18n } = useTranslation();
  // The same defaults the 3D pane opens with: names on, times asked for. Per pane rather
  // than shared, exactly as each surface's own switches are — the switch takes text off
  // this drawing, and the reader crowded by labels is looking at one drawing at a time.
  const [showLabels, setShowLabels] = useState(true);
  const [showTimes, setShowTimes] = useState(false);
  const [openCaverId, setOpenCaverId] = useState<string | null>(null);
  const [shownPlace, setShownPlace] = useState<TrackedPlace | null>(null);

  // The map image is the document's current file, resolved exactly as the authoring pane
  // resolves it: the file id is what the pin filter runs on, so the party can only ever
  // be placed against points measured on the very rendering being looked at.
  const { data: document } = useDocument(declaration.documentId);
  const { data: file } = useFile(document?.currentFileId);

  const pins = useMemo(
    () => mapPinsFromLinks(links, surveyModelId, declaration.documentId),
    [links, surveyModelId, declaration.documentId],
  );
  const markers = useMemo(
    () => (file === undefined ? [] : stationMarkers(pins, file.id)),
    [pins, file],
  );
  const superseded = file === undefined ? 0 : supersededPointCount(pins, file.id);

  const coarse = coarsePointer();
  const placed = useMemo(
    // The fan has to clear the pin's own dot and leave finger-sized targets apart; both
    // grow with the pointer for the same reason the dots and tolerances do.
    () => placedSheetCavers(cavers, markers, coarse ? 20 : 16),
    [cavers, markers, coarse],
  );
  const unplaced = useMemo(() => stationsWithoutPoint(cavers, markers), [cavers, markers]);

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

  /** Where the overlay's pressed row can be shown on this sheet, or null when nowhere. */
  const focus = useMemo(() => {
    if (shownPlace === null) {
      return null;
    }
    const pin = markers.find((marker) => marker.station === shownPlace.station);
    return pin === undefined ? null : { x: pin.x, y: pin.y };
  }, [shownPlace, markers]);

  /** A press on a caver's dot opens their card — the same card, from the same overlay. */
  const onCaverPressed = (marker: SheetCaverMarker) => {
    const id = marker.caver.caverId;
    setOpenCaverId(id === openCaverId ? null : id);
  };

  if (document === undefined || file === undefined) {
    return <Skeleton active data-testid="rastermap-tracking-loading" />;
  }

  const imageUrl = displayableImageUrl(file);
  if (imageUrl === null) {
    return <Typography.Text type="secondary">{t('rastermap.imageMissing')}</Typography.Text>;
  }

  return (
    <div>
      {superseded > 0 && (
        <Typography.Text type="secondary" data-testid="rastermap-superseded">
          {t('rastermap.supersededPoints', { count: superseded })}
        </Typography.Text>
      )}
      {/* The overlay anchors to this box, which is exactly the drawn sheet — the same
          arrangement as the list over the 3D scene, in the same corner, so a reader
          switching tabs finds who-is-where where they left it. */}
      <div style={{ position: 'relative' }} data-testid="rastermap-tracking-pane">
        <RasterMapView
          imageUrl={imageUrl}
          alt={declaration.title ?? t('rastermap.untitledMap')}
          markers={markers}
          cavers={drawn}
          active={active}
          height={height}
          onCaverClick={onCaverPressed}
          onMarkerClick={
            onPickStation === undefined
              ? undefined
              : (marker) => onPickStation(marker.station)
          }
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
    </div>
  );
}
