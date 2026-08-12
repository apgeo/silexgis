// SPDX-License-Identifier: AGPL-3.0-or-later
import { Typography } from 'antd';

import { useAttachments } from '../../api/hooks.ts';

/**
 * The one picture a trip is known by, drawn at the head of its page.
 *
 * <p>
 * It is the starred attachment and nothing else. That star is already the application-wide way of
 * saying "this is the picture that stands for this object", it is already kept to at most one per
 * object by the database itself, and it already has a control on the attachments section below —
 * so this reads that choice rather than offering a second one. A cover chosen in two places is two
 * answers to one question, and the page would eventually show the loser.
 * </p>
 * <p>
 * Read through the same request the attachments section makes, deliberately: that request is
 * already cut to the attachments this caller may read, so a cover cannot outlive the rule that
 * hides the picture behind it. Asking a second way — a field of its own on the trip, filled by a
 * query written for it — is how the loudest thing on the page comes to be the one thing that
 * escaped the filter. The two callers share one cached response, so this costs no extra request.
 * </p>
 * <p>
 * Nothing is drawn when there is no starred picture, rather than a placeholder: most trips will
 * never have one chosen, and an empty frame at the top of every one of them is worse than no frame.
 * </p>
 */
export default function TripCover({ tripId, tripTitle }: { tripId: string; tripTitle: string }) {
  const { data: attachments } = useAttachments('tripLog', tripId);

  const cover = (attachments ?? []).find((a) => a.isPrimary && a.file.kind === 'image');
  if (!cover) {
    return null;
  }

  // The rendering rather than the stored bytes: a caller who may see the trip is not thereby
  // entitled to the original file, and the thumbnail is what every other surface showing a
  // headline picture uses for the same reason.
  const src = cover.file.thumbnailUrl ?? cover.file.contentUrl;
  if (!src) {
    return null;
  }

  return (
    <figure style={{ margin: '0 0 16px' }} data-testid="trip-cover">
      <img
        src={src}
        alt={cover.caption ?? tripTitle}
        style={{
          width: '100%',
          maxHeight: 320,
          objectFit: 'cover',
          borderRadius: 8,
          display: 'block',
        }}
      />
      {cover.caption && (
        <Typography.Text type="secondary" style={{ display: 'block', marginTop: 4 }}>
          {cover.caption}
        </Typography.Text>
      )}
    </figure>
  );
}
