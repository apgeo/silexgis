// SPDX-License-Identifier: AGPL-3.0-or-later
import { useId, useState } from 'react';
import { Button, Typography } from 'antd';
import { useTranslation } from 'react-i18next';

/**
 * What a place on a followed page is, said to somebody who has never seen one.
 *
 * <b>The reader is not a caver.</b> They were sent an address by somebody they know, and a page of
 * names beside stations with times on them reads, to anybody who carries a phone, as a tracker: a
 * dot that is where a person is, and a dot that has stopped moving as a person who has. Neither is
 * what this page is told. A place is word passed out of a cave and typed in by a person, true of
 * the moment it was reported; and a long gap is the ordinary condition of being underground. One
 * sentence says the first standing on the page, and the rest is one press away for whoever wants
 * it — which is the reader who has started to worry, and it is written for them: plainly, and
 * without a word that is not about how the page works.
 *
 * <b>No link, here as everywhere on this page.</b> The fuller text opens in place: this reader
 * holds one address and every other one in the installation refuses them.
 */
export default function PublicTripAbout() {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const bodyId = useId();

  return (
    <section className="public-trip-about" data-testid="public-trip-about">
      <Typography.Text type="secondary" className="public-trip-about-line">
        {t('publicTrip.about.line')}
      </Typography.Text>
      <Button
        type="link"
        size="small"
        className="public-trip-about-more"
        aria-expanded={open}
        aria-controls={bodyId}
        onClick={() => setOpen((shown) => !shown)}
        data-testid="public-trip-about-more"
      >
        {t('publicTrip.about.title')}
      </Button>
      {/* Kept in the tree while shut, so the button always controls something that exists. */}
      <Typography.Paragraph
        id={bodyId}
        hidden={!open}
        className="public-trip-about-body"
        data-testid="public-trip-about-body"
      >
        {t('publicTrip.about.body')}
      </Typography.Paragraph>
    </section>
  );
}
