// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo, useState } from 'react';
import { App, Button, Flex, Modal, Skeleton, Typography } from 'antd';
import { AimOutlined, SettingOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import {
  useCreateResLink,
  useDeleteResLink,
  useDocument,
  useFile,
  useResLinkRelationTypes,
  type ResLink,
} from '../api/hooks.ts';
import { shortNameOf } from '../caveview/modelParts.ts';
import List from '../components/List.tsx';
import { displayableImageUrl } from '../components/documents/derivativeUrl.ts';
import { resLinkProblemMessage } from '../components/reslinks/problems.ts';
import { coarsePointer } from '../map/pointer.ts';
import {
  pinCreateBody,
  placementPlan,
  rewritePin,
  type ArmedStation,
  type FractionPoint,
} from './authoring.ts';
import {
  mapPinsFromLinks,
  stationMarkers,
  supersededPointCount,
  supersededPointPins,
  type MapPin,
  type MapStationMarker,
} from './mapPoints.ts';
import type { RasterMapDeclaration } from './rasterMaps.ts';
import RasterMapView from './RasterMapView.tsx';
import { MAP_STATION_POINT_CODE } from './vocabulary.ts';

/**
 * What the viewer modal lends one pane so its map can be authored on.
 *
 * The armed station lives above the panes on purpose: arming happens in the 3D pane or
 * in the typeahead beside the tab strip, both outside this component, and the click that
 * spends it happens here — so the state is the modal's and this is its lease on it.
 */
export interface PaneAuthoring {
  /** Whether this pane is the one in define-points mode. */
  defining: boolean;
  onToggleDefining: () => void;
  armed: ArmedStation | null;
  onArm: (armed: ArmedStation) => void;
  onDisarm: () => void;
  /** Opens the declaration's settings (view kind, removal). Offered only with `mayEdit`. */
  onEditDeclaration: () => void;
}

interface Props {
  declaration: RasterMapDeclaration;
  /** The model's incident links — the same one answer the tab strip was folded from. */
  links: readonly ResLink[];
  surveyModelId: string;
  /** Whether this pane is the one on screen; forwarded to the OL map's build gate. */
  active: boolean;
  height?: number | string;
  /**
   * The authoring lease, absent on read-only surfaces (the tracking tab, the public
   * page). Creating a pin needs only what being here proves — a signed-in caller who can
   * read the model and this document — so the define control itself is not permission
   * gated; the correcting controls are, per pin, by the server's own `mayEdit` answer.
   */
  authoring?: PaneAuthoring;
}

/**
 * One declared map, resolved to something drawable: the document's current file decides
 * both which rendering is shown and which pins may be drawn on it.
 *
 * <b>The file is part of the pin filter, not a detail.</b> A pin's fractions were measured
 * against one specific file, and a document goes on having new scans uploaded — so only
 * pins measured against the file on screen become markers, and the ones measured against
 * another scan surface as a count. A marker drawn from another file's fractions would sit
 * in a right-looking place it was never measured at, which is precisely the silent
 * wrongness a calibration-grade claim must not acquire.
 *
 * <b>Authoring writes through the two calls the API has.</b> A new pin is one POST; a
 * correction (move, re-place) is a create of the corrected link and a delete of the old —
 * the member PATCH cannot carry a new anchor — in that order, so a failure part-way never
 * costs the station its pin (see {@link rewritePin}). Deleting a pin deletes its link:
 * the pair is the link, and a one-member husk would mean nothing.
 */
export default function RasterMapPane({
  declaration,
  links,
  surveyModelId,
  active,
  height,
  authoring,
}: Props) {
  const { t } = useTranslation();
  const { message } = App.useApp();

  // The map image is the document's current file, delivered through the same entitlement
  // machinery as every other picture: original bytes for full-reach readers, a bounded
  // rendering otherwise. The link display cannot serve here — it names no file id, and
  // the file id is what the pin filter runs on.
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
  const supersededPins = useMemo(
    () => (file === undefined ? [] : supersededPointPins(pins, file.id)),
    [pins, file],
  );

  const { data: relationTypes } = useResLinkRelationTypes(authoring !== undefined);
  const pinRelationId =
    relationTypes?.find((row) => row.code === MAP_STATION_POINT_CODE)?.id ?? null;
  const createLink = useCreateResLink();
  const deleteLink = useDeleteResLink();

  /** A placement waiting on the duplicate warning's answer. */
  const [occupied, setOccupied] = useState<{ pin: MapPin; at: FractionPoint } | null>(null);
  /** A pressed marker waiting for move/delete/nothing. */
  const [pressed, setPressed] = useState<MapStationMarker | null>(null);

  const defining = authoring?.defining === true;
  // Finger-driven authoring gets finger-sized controls, the same axis the markers and
  // the hit tolerance already read.
  const controlSize = coarsePointer() ? 'large' : 'middle';

  const writePin = async (at: FractionPoint, station: string, replaceLinkId: string | null) => {
    if (pinRelationId === null || file === undefined || authoring === undefined) {
      return;
    }
    const body = pinCreateBody(
      pinRelationId,
      declaration.documentId,
      file.id,
      at,
      surveyModelId,
      station,
    );
    try {
      if (replaceLinkId === null) {
        await createLink.mutateAsync(body);
      } else {
        await rewritePin(
          (toCreate) => createLink.mutateAsync(toCreate),
          (id) => deleteLink.mutateAsync(id),
          body,
          replaceLinkId,
        );
      }
      message.success(t('rastermap.pinSaved', { station: shortNameOf(station) }));
      // Armed-empty for the next station: the mode survives, the arm is spent.
      authoring.onDisarm();
    } catch (error) {
      message.error(resLinkProblemMessage(error, t));
    }
  };

  const placeAt = (at: FractionPoint) => {
    if (authoring?.armed == null) {
      return;
    }
    const plan = placementPlan(pins, authoring.armed);
    if (plan.kind === 'occupied') {
      setOccupied({ pin: plan.pin, at });
      return;
    }
    void writePin(at, authoring.armed.station, plan.kind === 'replace' ? plan.oldLinkId : null);
  };

  const markerPressed = (marker: MapStationMarker, at: FractionPoint | null) => {
    if (authoring?.armed != null) {
      if (authoring.armed.station === marker.station) {
        // Clicking the armed station's OWN marker means "put it exactly here" — the
        // marker's stored point, never the click re-measured against the drawing.
        placeAt({ x: marker.x, y: marker.y });
      } else if (at !== null) {
        // Another station's marker does not capture an armed click: the author aimed at
        // the sheet — dense clusters make landing inside a neighbor's halo routine — and
        // the pin goes where the click landed, never silently at the neighbor's stored
        // point. A click inside the halo but outside the picture (at === null) places
        // nothing, the same refusal a bare sheet click gets.
        placeAt(at);
      }
      return;
    }
    setPressed(marker);
  };

  const deletePin = async (linkId: string) => {
    try {
      await deleteLink.mutateAsync(linkId);
      message.success(t('rastermap.pinDeleted'));
      setPressed(null);
    } catch (error) {
      message.error(resLinkProblemMessage(error, t));
    }
  };

  if (document === undefined || file === undefined) {
    return <Skeleton active data-testid="rastermap-pane-loading" />;
  }

  const imageUrl = displayableImageUrl(file);
  if (imageUrl === null) {
    // A file that offers no rendering to this reader is a real state, not an error: the
    // declaration is visible, the picture is not, and saying so beats a broken image.
    return <Typography.Text type="secondary">{t('rastermap.imageMissing')}</Typography.Text>;
  }

  return (
    <Flex vertical gap={8}>
      {authoring !== undefined && (
        <Flex wrap gap={8} align="center">
          <Button
            data-testid="rastermap-define"
            size={controlSize}
            type={defining ? 'primary' : 'default'}
            icon={<AimOutlined />}
            onClick={authoring.onToggleDefining}
          >
            {defining ? t('rastermap.doneDefining') : t('rastermap.definePoints')}
          </Button>
          {declaration.mayEdit && (
            <Button
              data-testid="rastermap-map-settings"
              size={controlSize}
              icon={<SettingOutlined />}
              aria-label={t('rastermap.mapSettings')}
              onClick={authoring.onEditDeclaration}
            />
          )}
        </Flex>
      )}

      {superseded > 0 && !defining && (
        <Typography.Text type="secondary" data-testid="rastermap-superseded">
          {t('rastermap.supersededPoints', { count: superseded })}
        </Typography.Text>
      )}

      {defining && supersededPins.length > 0 && (
        // The authoring reading of the same count: which stations are stranded on another
        // scan, each with the re-place the design promises — arm the station, and the
        // next click on this sheet rewrites the stranded link against the file on screen.
        // Stations only; which file each was measured against stays version history.
        <Flex vertical gap={4} data-testid="rastermap-superseded-list">
          <Typography.Text type="secondary">
            {t('rastermap.supersededPoints', { count: supersededPins.length })}
          </Typography.Text>
          <List
            size="small"
            dataSource={supersededPins}
            renderItem={(pin) => (
              <List.Item
                actions={
                  pin.mayEdit
                    ? [
                        <Button
                          key="replace"
                          size={controlSize}
                          data-testid="rastermap-replace"
                          onClick={() =>
                            authoring?.onArm({ station: pin.station, replaceLinkId: pin.linkId })
                          }
                        >
                          {t('rastermap.replacePin')}
                        </Button>,
                      ]
                    : undefined
                }
              >
                {pin.station}
              </List.Item>
            )}
          />
        </Flex>
      )}

      <RasterMapView
        imageUrl={imageUrl}
        alt={declaration.title ?? t('rastermap.untitledMap')}
        markers={markers}
        active={active}
        height={height}
        onMapClick={defining && authoring?.armed != null ? placeAt : undefined}
        onMarkerClick={defining ? markerPressed : undefined}
      />

      {/* The duplicate warning: the station already has a pin on this map (possibly on an
          older scan), and the honest correction is rewriting that pin, not doubling it.
          The rewrite is offered only when the server would accept it. */}
      <Modal
        open={occupied !== null}
        title={t('rastermap.alreadyPinned', {
          station: occupied === null ? '' : shortNameOf(occupied.pin.station),
        })}
        onCancel={() => setOccupied(null)}
        footer={
          <Flex justify="end" gap={8} wrap>
            <Button size={controlSize} onClick={() => setOccupied(null)}>
              {t('common.cancel')}
            </Button>
            {occupied?.pin.mayEdit === true && (
              <Button
                size={controlSize}
                type="primary"
                data-testid="rastermap-move-here"
                loading={createLink.isPending || deleteLink.isPending}
                onClick={() => {
                  const settled = occupied;
                  setOccupied(null);
                  void writePin(settled.at, settled.pin.station, settled.pin.linkId);
                }}
              >
                {t('rastermap.moveHere')}
              </Button>
            )}
          </Flex>
        }
        data-testid="rastermap-duplicate"
      >
        {occupied?.pin.mayEdit === false && (
          <Typography.Text type="secondary">{t('rastermap.cannotEditPin')}</Typography.Text>
        )}
      </Modal>

      {/* A pressed marker: move re-arms its station against its own link, so the next
          click rewrites rather than duplicates; delete removes the whole link, because
          the pair is the link. Neither is offered against a link the server would refuse. */}
      <Modal
        open={pressed !== null}
        title={t('rastermap.pinTitle', {
          station: pressed === null ? '' : shortNameOf(pressed.station),
        })}
        onCancel={() => setPressed(null)}
        footer={
          <Flex justify="end" gap={8} wrap>
            <Button size={controlSize} onClick={() => setPressed(null)}>
              {t('common.cancel')}
            </Button>
            {pressed?.mayEdit === true && (
              <>
                <Button
                  size={controlSize}
                  danger
                  data-testid="rastermap-delete-pin"
                  loading={deleteLink.isPending}
                  onClick={() => void deletePin(pressed.linkId)}
                >
                  {t('rastermap.deletePin')}
                </Button>
                <Button
                  size={controlSize}
                  type="primary"
                  data-testid="rastermap-move-pin"
                  onClick={() => {
                    authoring?.onArm({ station: pressed.station, replaceLinkId: pressed.linkId });
                    setPressed(null);
                  }}
                >
                  {t('rastermap.movePin')}
                </Button>
              </>
            )}
          </Flex>
        }
        data-testid="rastermap-pin-actions"
      >
        {pressed?.mayEdit === false && (
          <Typography.Text type="secondary">{t('rastermap.cannotEditPin')}</Typography.Text>
        )}
      </Modal>
    </Flex>
  );
}
