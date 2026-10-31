// SPDX-License-Identifier: AGPL-3.0-or-later
import { cloneElement, forwardRef, isValidElement, type ReactElement, type ReactNode } from 'react';
// The one place the library's button is used directly: everything else imports this wrapper.
// oxlint-disable-next-line no-restricted-imports
import { Button as AntButton, type ButtonProps } from 'antd';

/** Whether a button says what it does without its icon: words of its own, or a label it was given. */
function namesItself(props: Pick<ButtonProps, 'children' | 'aria-label' | 'aria-labelledby'>): boolean {
  const hasWords = (node: ReactNode): boolean =>
    Array.isArray(node) ? node.some(hasWords) : node !== null && node !== undefined && node !== false && node !== '';
  return hasWords(props.children) || Boolean(props['aria-label']) || Boolean(props['aria-labelledby']);
}

/**
 * What the development-time notice for an icon-only button with no label of its own starts with.
 * The unit-test setup recognises it by this text and fails the test that rendered the button.
 */
export const ICON_ONLY_UNLABELLED = 'icon-only Button has no accessible label';

const reported = new Set<string>();

function reportUnlabelled(icon: ReactNode): void {
  if (!import.meta.env.DEV) return;
  const glyph = isValidElement(icon)
    ? ((icon.type as { displayName?: string }).displayName ?? 'unknown icon')
    : 'unknown icon';
  if (reported.has(glyph)) return;
  reported.add(glyph);
  console.error(`${ICON_ONLY_UNLABELLED} (${glyph}): give it aria-label={t('…')}; the icon's own name is not a label`);
}

/**
 * The component library's button, with its icon kept out of the button's name when the button
 * already has one.
 *
 * The icon set draws every icon as an image named after its glyph — `share-alt`, `aim`, `delete`,
 * in English whatever the interface language — and a button is named by everything inside it, so
 * a button that reads "Share" is announced as "share-alt Share", and in Romanian as "share-alt
 * Distribuie". The library has no setting for this, so it is decided here, once: where the button
 * says what it does with words or with its own label, the icon is decoration and is hidden from
 * assistive technology. An icon-only button with no label keeps the glyph's name — a poor name,
 * but a name — so that it is never nameless, and says so in development (see
 * `ICON_ONLY_UNLABELLED`): the unit tests fail on it and the browser suite records it, so a
 * button added without a label of its own is found when it is written, not by a screen-reader user.
 *
 * Every prop is the library's; a caller changes its import and nothing else.
 */
const Button = forwardRef<HTMLAnchorElement | HTMLButtonElement, ButtonProps>(function Button(props, ref) {
  const { icon } = props;
  if (icon && !namesItself(props)) reportUnlabelled(icon);
  const decorative =
    isValidElement(icon) && namesItself(props)
      ? cloneElement(icon as ReactElement<{ 'aria-hidden'?: boolean }>, { 'aria-hidden': true })
      : icon;
  return <AntButton ref={ref} {...props} icon={decorative} />;
});

export default Button;
