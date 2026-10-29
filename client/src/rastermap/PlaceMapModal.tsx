// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo, useState } from 'react';
import { App, Flex, Modal, Skeleton, Typography } from 'antd';
import { DeleteOutlined, DragOutlined, PlusOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { useDeleteResLink, useDocument, useFile } from '../api/hooks.ts';
import { useCreatePlacePoint, useMovePlacePoint, usePlacePointLinks } from '../api/hooks/placePoints.ts';
import Button from '../components/Button.tsx';
import List from '../components/List.tsx';
import { renditionImageUrl } from '../components/documents/derivativeUrl.ts';
import { resLinkProblemMessage } from '../components/reslinks/problems.ts';
import ResLinkTargetPicker from '../components/reslinks/ResLinkTargetPicker.tsx';
import {
  earlierPicturePins,
  offPictureCount,
  placeMarkers,
  placePinsFromLinks,
  type PlaceMarker,
  type PlacePin,
} from './placePoints.ts';
import RasterMapView from './RasterMapView.tsx';

interface Props {
  open: boolean;
  onClose: () => void;
  /** The map's picture document. */
  documentId: string;
  /** What the map is called on the cave — its attachment's caption. */
  title: string | null;
  /**
   * Whether this reader may change the cave the map is of, as the cave's page already knows. It
   * offers adding a point; moving and removing one follow each pin's own answer.
   */
  canEdit: boolean;
}

/** What the next click on the map does. */
type Placing = { kind: 'add' } | { kind: 'move'; linkId: string; place: string } | null;

/**
 * A phone's map of a cave, opened as a map: every place pinned on it is a marker labelled with the
 * place, and an editor moves, removes and adds them. The phones receive each change under the pin's
 * own identifier.
 *
 * The picture is always the server's rendition, drawn upright, because that is the frame the points
 * are kept in — the stored bytes carry no recorded turn. Points measured on an earlier picture, and
 * points whose pixel does not fall on this one, are counted rather than drawn at a guessed spot; an
 * editor places an earlier one again by moving it.
 */
export default function PlaceMapModal({ open, onClose, documentId, title, canEdit }: Props) {
  const { t } = useTranslation();
  const { message, modal } = App.useApp();
  const { data: document } = useDocument(documentId, open);
  const { data: file } = useFile(document?.currentFileId ?? undefined);
  const { data: links } = usePlacePointLinks(documentId, open);
  const createPoint = useCreatePlacePoint();
  const movePoint = useMovePlacePoint();
  const deletePoint = useDeleteResLink();

  const [placing, setPlacing] = useState<Placing>(null);
  const [pressed, setPressed] = useState<PlaceMarker | null>(null);
  const [adding, setAdding] = useState<{ x: number; y: number } | null>(null);
  const [place, setPlace] = useState<string | null>(null);

  const unnamed = t('placeMap.unnamedPlace');
  const pins = useMemo(() => placePinsFromLinks(links ?? [], documentId), [links, documentId]);
  const markers = useMemo(
    () => (file === undefined ? [] : placeMarkers(pins, file.id, unnamed)),
    [pins, file, unnamed],
  );
  const earlier = file === undefined ? [] : earlierPicturePins(pins, file.id);
  const offPicture = file === undefined ? 0 : offPictureCount(pins, file.id);

  const close = () => {
    setPlacing(null);
    setPressed(null);
    setAdding(null);
    onClose();
  };

  const placeAt = async (at: { x: number; y: number }) => {
    if (placing === null || file === undefined) {
      return;
    }
    if (placing.kind === 'add') {
      setPlace(null);
      setAdding(at);
      return;
    }
    try {
      await movePoint.mutateAsync({ id: placing.linkId, body: { fileId: file.id, x: at.x, y: at.y } });
      message.success(t('placeMap.saved'));
      setPlacing(null);
    } catch (error) {
      message.error(resLinkProblemMessage(error, t));
    }
  };

  const saveNew = async () => {
    if (adding === null || place === null || file === undefined) {
      return;
    }
    try {
      await createPoint.mutateAsync({ documentId, placeId: place, fileId: file.id, x: adding.x, y: adding.y });
      message.success(t('placeMap.saved'));
      setAdding(null);
      setPlacing(null);
    } catch (error) {
      message.error(resLinkProblemMessage(error, t));
    }
  };

  const remove = (marker: { linkId: string }) => {
    modal.confirm({
      title: t('placeMap.deleteConfirm'),
      okButtonProps: { danger: true },
      okText: t('placeMap.delete'),
      onOk: async () => {
        try {
          await deletePoint.mutateAsync(marker.linkId);
          message.success(t('placeMap.deleted'));
          setPressed(null);
        } catch (error) {
          message.error(resLinkProblemMessage(error, t));
        }
      },
    });
  };

  const moveArmed = (pin: { linkId: string; label?: string; placeName?: string | null }) => {
    setPressed(null);
    setPlacing({ kind: 'move', linkId: pin.linkId, place: pin.label ?? pin.placeName ?? unnamed });
  };

  const imageUrl = file === undefined ? null : renditionImageUrl(file);

  return (
    <Modal
      open={open}
      onCancel={close}
      footer={null}
      width="90vw"
      destroyOnHidden
      title={t('placeMap.title', { title: title ?? t('placeMap.untitled') })}
      data-testid="placemap-modal"
    >
      {document === undefined || file === undefined || links === undefined ? (
        <Skeleton active data-testid="placemap-loading" />
      ) : imageUrl === null ? (
        <Typography.Text type="secondary">{t('rastermap.imageMissing')}</Typography.Text>
      ) : (
        <Flex vertical gap={8}>
          <Flex wrap gap={8} align="center">
            {canEdit && placing === null && (
              <Button data-testid="placemap-add" icon={<PlusOutlined />} onClick={() => setPlacing({ kind: 'add' })}>
                {t('placeMap.addPoint')}
              </Button>
            )}
            {placing !== null && (
              <>
                <Typography.Text data-testid="placemap-hint">
                  {placing.kind === 'add' ? t('placeMap.addHint') : t('placeMap.moveHint', { place: placing.place })}
                </Typography.Text>
                <Button data-testid="placemap-stop" onClick={() => setPlacing(null)}>
                  {t('placeMap.stopPlacing')}
                </Button>
              </>
            )}
            {earlier.length > 0 && (
              <Typography.Text type="secondary" data-testid="placemap-earlier">
                {t('placeMap.earlierPicture', { count: earlier.length })}
              </Typography.Text>
            )}
            {offPicture > 0 && (
              <Typography.Text type="secondary" data-testid="placemap-offpicture">
                {t('placeMap.notOnPicture', { count: offPicture })}
              </Typography.Text>
            )}
          </Flex>

          <RasterMapView
            imageUrl={imageUrl}
            alt={title ?? t('placeMap.untitled')}
            markers={markers}
            active={open}
            testId="placemap"
            onMapClick={placing !== null ? (at) => void placeAt(at) : undefined}
            onMarkerClick={(marker, at) => {
              if (placing !== null) {
                // A press near another point while placing means the spot pressed, never that
                // point's stored one; the point being moved is no target for itself.
                if (at !== null && !(placing.kind === 'move' && placing.linkId === marker.linkId)) {
                  void placeAt(at);
                }
                return;
              }
              setPressed(marker);
            }}
          />

          {earlier.length > 0 && placing === null && earlier.some((pin) => pin.mayEdit) && (
            <List
              size="small"
              dataSource={earlier}
              renderItem={(pin: PlacePin) => (
                <List.Item
                  actions={
                    pin.mayEdit
                      ? [
                          <Button key="move" size="small" onClick={() => moveArmed(pin)}>
                            {t('rastermap.replacePin')}
                          </Button>,
                        ]
                      : undefined
                  }
                >
                  {pin.placeName ?? unnamed}
                </List.Item>
              )}
            />
          )}
        </Flex>
      )}

      <Modal
        open={pressed !== null}
        onCancel={() => setPressed(null)}
        footer={null}
        title={pressed?.label}
        data-testid="placemap-marker"
      >
        {pressed !== null &&
          (pressed.mayEdit ? (
            <Flex gap={8}>
              <Button data-testid="placemap-move" icon={<DragOutlined />} onClick={() => moveArmed(pressed)}>
                {t('placeMap.move')}
              </Button>
              <Button data-testid="placemap-delete" danger icon={<DeleteOutlined />} onClick={() => remove(pressed)}>
                {t('placeMap.delete')}
              </Button>
            </Flex>
          ) : (
            <Typography.Text type="secondary">{t('placeMap.cannotEdit')}</Typography.Text>
          ))}
      </Modal>

      <Modal
        open={adding !== null}
        onCancel={() => setAdding(null)}
        title={t('placeMap.pickPlaceTitle')}
        data-testid="placemap-pick"
        destroyOnHidden
        footer={
          <Button
            type="primary"
            data-testid="placemap-save"
            disabled={place === null}
            loading={createPoint.isPending}
            onClick={() => void saveNew()}
          >
            {t('common.save')}
          </Button>
        }
      >
        <Typography.Text>{t('placeMap.pickPlace')}</Typography.Text>
        <ResLinkTargetPicker targetType="feature" value={place} onChange={(id) => setPlace(id)} />
      </Modal>
    </Modal>
  );
}
