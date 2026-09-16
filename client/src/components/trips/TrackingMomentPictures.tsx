// SPDX-License-Identifier: AGPL-3.0-or-later
import { DeleteOutlined, PictureOutlined } from '@ant-design/icons';
import { Alert, App, Button, Card, Empty, Flex, Popconfirm, Skeleton, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { useDetachTrackingPicture } from '../../api/hooks.ts';
import type { ReplayPicture } from '../../caveview/trackingReplay.ts';
import './TrackingMomentPictures.css';

export interface TrackingMomentPicturesProps {
  tripLogId: string;
  /** Every photograph hung on a moment of this trip, oldest first. */
  pictures: readonly ReplayPicture[];
  /** True while the trip's links are still being read. */
  loading: boolean;
  /** Set when they could not be read at all. */
  failed: boolean;
  /** Whether this reader may write to the log, which is what attaching and detaching need. */
  canEdit: boolean;
  /** What the trip's roster calls a caver — the links carry ids and nothing on them knows names. */
  nameOf(caverId: string): string;
  /** Offers to hang photographs on the trip, at the moment this panel opens the dialog with. */
  onAttach(): void;
}

/**
 * The photographs hung on this trip's moments, as a list of moments.
 *
 * <b>This panel exists because the replay is the wrong place for the act that actually happens.</b>
 * Nobody uploads from underground: these arrive days later, when somebody empties a memory card and
 * turns a finished log into a trip report. Offered only inside the survey panel, that act needed a
 * survey model that still exists, that is finished importing, and that this reader may open — then
 * expanding it, opening the replay, and scrubbing to the instant. Three conditions and three acts,
 * none of which has anything to do with attaching a photograph to a time.
 *
 * <b>And the conditions are not hypothetical, which is the part worth stating.</b> A survey can be
 * deleted after the trip — the tab already carries a notice for exactly that — and the whole claim
 * of this design is that a photograph outlives the survey the party was placed in. A reader who may
 * read the trip but may not place the cave never sees a model at all, and the picture is the same
 * class of statement as the note beside it on the log, which does reach them. In both cases the
 * pictures were on the trip and nothing on screen said so.
 *
 * So this is the surface of record for them: it needs no model, it shows every photograph whatever
 * its camera's clock claims — including the one whose clock is out by years, which the replay's
 * scrubber deliberately will not stretch itself to reach — and it says, for each, the moment it is
 * filed at and who it is about. The model panel keeps its own strip, which answers the other
 * question: where a photograph was taken.
 */
export default function TrackingMomentPictures({
  tripLogId,
  pictures,
  loading,
  failed,
  canEdit,
  nameOf,
  onAttach,
}: TrackingMomentPicturesProps) {
  const { t, i18n } = useTranslation();
  const { message } = App.useApp();
  const detach = useDetachTrackingPicture();

  const clock = (at: number) =>
    new Date(at).toLocaleString(i18n.language, { dateStyle: 'short', timeStyle: 'short' });

  const onDetach = async (memberId: string) => {
    try {
      await detach.mutateAsync({ tripLogId, memberId });
      message.success(t('trips.tracking.pictures.detached'));
    } catch {
      message.error(t('trips.tracking.pictures.detachFailed'));
    }
  };

  return (
    <Card
      size="small"
      title={t('trips.tracking.pictures.panelTitle')}
      data-testid="trip-tracking-moment-pictures"
      extra={
        canEdit && (
          <Button
            size="small"
            icon={<PictureOutlined />}
            onClick={onAttach}
            data-testid="trip-tracking-pictures-add"
          >
            {t('trips.tracking.pictures.panelAdd')}
          </Button>
        )
      }
    >
      <Typography.Paragraph type="secondary">
        {t('trips.tracking.pictures.panelHint')}
      </Typography.Paragraph>

      {/* A failed read is said rather than drawn as an empty trip: "this trip has no photographs"
          is a claim about the world, and a request that did not come back is not evidence for it. */}
      {failed ? (
        <Alert
          type="error"
          showIcon
          message={t('trips.tracking.pictures.panelUnavailable')}
          data-testid="trip-tracking-pictures-unavailable"
        />
      ) : loading ? (
        <Skeleton active paragraph={{ rows: 1 }} />
      ) : pictures.length === 0 ? (
        <Empty
          image={Empty.PRESENTED_IMAGE_SIMPLE}
          description={t('trips.tracking.pictures.panelNone')}
        />
      ) : (
        <Flex gap="middle" wrap data-testid="trip-tracking-pictures-list-panel">
          {pictures.map((picture) => (
            <figure
              className="tracking-moment-picture"
              key={picture.memberId}
              data-testid={`trip-tracking-moment-picture-${picture.memberId}`}
            >
              {/* Opened in a tab of its own rather than in a viewer of this panel's making: the
                  URL is a rendering the server minted for this reader, and following it is the
                  whole of what this surface is entitled to do with it. */}
              <a href={picture.entry.url} target="_blank" rel="noreferrer">
                <img
                  src={picture.entry.thumbnailUrl ?? picture.entry.url}
                  alt={picture.entry.caption ?? t('trips.tracking.pictures.thumbnailAlt')}
                  className="tracking-moment-picture-image"
                />
              </a>
              <figcaption>
                <Typography.Text strong>{clock(picture.at)}</Typography.Text>
                <br />
                <Typography.Text type="secondary">
                  {picture.caverId === null
                    ? t('trips.tracking.pictures.aboutParty')
                    : t('trips.tracking.pictures.aboutCaver', { name: nameOf(picture.caverId) })}
                </Typography.Text>
                {canEdit && (
                  <Popconfirm
                    title={t('trips.tracking.pictures.detachConfirm')}
                    onConfirm={() => void onDetach(picture.memberId)}
                  >
                    <Button
                      type="text"
                      size="small"
                      icon={<DeleteOutlined />}
                      aria-label={t('trips.tracking.pictures.detach')}
                      data-testid={`trip-tracking-picture-detach-${picture.memberId}`}
                    />
                  </Popconfirm>
                )}
              </figcaption>
            </figure>
          ))}
        </Flex>
      )}
    </Card>
  );
}
