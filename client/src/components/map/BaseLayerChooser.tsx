// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useRef, type ComponentProps } from 'react';
import BackgroundLayerChooser from '@terrestris/react-geo/dist/BackgroundLayerChooser/BackgroundLayerChooser';
import { useTranslation } from 'react-i18next';

/**
 * The mapping library's base-layer chooser, with a name on the button that opens it.
 *
 * The library gives that button a tooltip and nothing else — no way to pass it a label — so it
 * reached assistive technology with no name at all. Rather than carry a changed copy of the
 * library, the label is set on the button after it is drawn. The library never sets that
 * attribute itself, so nothing it renders later takes it away; it is set again on every render of
 * this host so it follows the reader's language.
 */
export default function BaseLayerChooser(props: ComponentProps<typeof BackgroundLayerChooser>) {
  const { t } = useTranslation();
  const host = useRef<HTMLDivElement>(null);
  const label = t('a11y.baseLayerChooser');

  useEffect(() => {
    host.current?.querySelector('.change-bg-btn')?.setAttribute('aria-label', label);
  });

  return (
    <div ref={host} className="base-layer-chooser-host">
      <BackgroundLayerChooser {...props} />
    </div>
  );
}
