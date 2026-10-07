// SPDX-License-Identifier: AGPL-3.0-or-later
import { Alert, Button } from 'antd';
import { useTranslation } from 'react-i18next';

export interface PublicWatchBarProps {
  /**
   * The party on screen, or null while the list it comes from has not been read — said as
   * "reading", never by naming the link's own trip in its place.
   */
  party: { title: string; dates: string } | null;
  /** Back to the trip this link was published for. */
  onBack(): void;
}

/**
 * The statement that the party on screen is not the one this link was published for.
 *
 * <b>First thing under the title, for as long as another party is on screen.</b> A link is handed
 * to a family as "this is where they are", and every figure below this strip — who is out, who is
 * still underground, how long since anybody was heard — is read as being about that party. Once a
 * reader has asked for another party of the cave those figures are about somebody else, and the
 * page has to say so where it cannot be scrolled past without being seen, with the way back
 * beside it: somebody who pressed a row by accident must not need to find the list again to undo
 * it.
 *
 * <b>It also says on whose drawing.</b> The party is placed on the survey this link was published
 * with and no other, so a place it reported against a different survey is listed and not drawn —
 * which is explained under the drawing, as it is for the link's own party.
 */
export default function PublicWatchBar({ party, onBack }: PublicWatchBarProps) {
  const { t } = useTranslation();

  return (
    <Alert
      type="warning"
      showIcon
      className="public-watch-banner"
      title={t('publicTrip.live.watchBannerTitle')}
      description={
        <div className="public-watch-banner-body">
          <div data-testid="public-watch-banner-what">
            {party === null
              ? t('publicTrip.live.watchBannerLoading')
              : t('publicTrip.live.watchBannerWhat', { title: party.title, dates: party.dates })}
          </div>
          <div className="public-watch-banner-action">
            <Button className="public-live-watch" onClick={onBack} data-testid="public-watch-back">
              {t('publicTrip.live.backToOwn')}
            </Button>
          </div>
        </div>
      }
      data-testid="public-watch-banner"
    />
  );
}
