// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState, useSyncExternalStore } from 'react';
import { EyeInvisibleOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import Button from '../Button.tsx';
import { getCenterlineLoadState, subscribeCenterlineLoadState } from '../../map/centerlineLayer.ts';

interface Props {
  /** Whether the survey-line overlay is switched on. */
  centerlinesVisible: boolean;
  /** Whether the layer panel is closed — the docked pane collapsed, or the phone drawer shut. */
  panelHidden: boolean;
  /** The map's chrome is hidden: a clean map was asked for, so this says nothing either. */
  chromeHidden: boolean;
  /** Opens the layer panel at its survey-line block. */
  onOpenPanel: () => void;
}

/**
 * The survey lines the map is holding back at this zoom, said on the map itself.
 *
 * The layer panel already says it, but only to someone who has the panel open; with the panel
 * closed the overlay just looks thinner than it should, with nothing to say why. So while the
 * panel is closed and lines are withheld, a small notice sits in a corner of the map, and pressing
 * it opens the panel where the same alert and the line-budget settings are.
 *
 * It is announced politely, and once per appearance: the count is republished after every pan, and
 * usually changes, so a live region that followed it would speak on every move of the map. The
 * announcement is therefore taken when the notice appears and cleared when it goes; the button
 * itself always shows the current count to anyone who looks or moves to it.
 */
export default function CenterlineWithheldNotice({
  centerlinesVisible,
  panelHidden,
  chromeHidden,
  onOpenPanel,
}: Props) {
  const { t } = useTranslation();
  const { withheldCount } = useSyncExternalStore(subscribeCenterlineLoadState, getCenterlineLoadState);
  const shown = centerlinesVisible && panelHidden && !chromeHidden && withheldCount > 0;
  const message = t('map.centerlinesWithheldNotice', { count: withheldCount });

  // Adjusted while rendering rather than in an effect, so the announcement and the notice reach
  // the page in the same commit.
  const [wasShown, setWasShown] = useState(false);
  const [announcement, setAnnouncement] = useState('');
  if (shown !== wasShown) {
    setWasShown(shown);
    setAnnouncement(shown ? message : '');
  }

  return (
    <>
      {/* Mounted for as long as the map is, empty while there is nothing to say: a live region
          that arrives together with its text is not reliably read out. */}
      <div className="visually-hidden" role="status" aria-live="polite" data-testid="map-centerline-notice-live">
        {announcement}
      </div>
      {shown && (
        <Button
          className="map-centerline-notice"
          size="small"
          icon={<EyeInvisibleOutlined />}
          onClick={onOpenPanel}
          data-testid="map-centerline-notice"
        >
          {message}
        </Button>
      )}
    </>
  );
}
