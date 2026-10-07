// SPDX-License-Identifier: AGPL-3.0-or-later
import { Button } from 'antd';
import { useTranslation } from 'react-i18next';
import type { PublicLanguage } from './usePublicLanguage.ts';

interface Props {
  /** The page's language and how to change it — read once by the page, handed down. */
  control: PublicLanguage;
  /** Two letters instead of the language's name, for a frame with one line of room. */
  compact?: boolean;
  size?: 'large' | 'middle' | 'small';
}

/**
 * The way into the other language, for a reader who cannot read the one the page opened in.
 *
 * <b>Everything about it is written in the language it leads to.</b> Somebody who needs this
 * button is by definition not reading the page around it, so its text is the other language's own
 * name for itself, its accessible name is a sentence in that language, and the element says which
 * language those words are in — without that a screen reader pronounces "English" with a Romanian
 * voice, to the one listener who was waiting to hear it.
 *
 * <b>A button, not a link.</b> Nothing on a published trip's page is an anchor: its reader holds
 * one address and every other address in the installation refuses them. This changes what the
 * page says and leaves the reader exactly where they are.
 */
export default function PublicLanguageButton({ control, compact = false, size }: Props) {
  const { t } = useTranslation();
  const { other, choose } = control;
  // Asked for in the target language whatever the page is in: each language's catalogue holds its
  // own invitation, so the two can never be swapped by a mistranslation in the other's file.
  const invitation = t('publicTrip.language.switchTo', { lng: other });

  return (
    <Button
      type={compact ? 'default' : 'text'}
      size={size}
      lang={other}
      aria-label={invitation}
      title={compact ? invitation : undefined}
      className="public-trip-language"
      onClick={() => choose(other)}
      data-testid="public-trip-language"
    >
      {compact
        ? t('publicTrip.language.short', { lng: other })
        : t('publicTrip.language.name', { lng: other })}
    </Button>
  );
}
