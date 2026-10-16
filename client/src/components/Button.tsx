// SPDX-License-Identifier: AGPL-3.0-or-later
import { cloneElement, forwardRef, isValidElement, type ReactElement, type ReactNode } from 'react';
import { Button as AntButton, type ButtonProps } from 'antd';

/** Whether a button says what it does without its icon: words of its own, or a label it was given. */
function namesItself(props: Pick<ButtonProps, 'children' | 'aria-label' | 'aria-labelledby'>): boolean {
  const hasWords = (node: ReactNode): boolean =>
    Array.isArray(node) ? node.some(hasWords) : node !== null && node !== undefined && node !== false && node !== '';
  return hasWords(props.children) || Boolean(props['aria-label']) || Boolean(props['aria-labelledby']);
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
 * but a name — until it is given a label of its own, which is the fix for it.
 *
 * Every prop is the library's; a caller changes its import and nothing else.
 */
const Button = forwardRef<HTMLAnchorElement | HTMLButtonElement, ButtonProps>(function Button(props, ref) {
  const { icon } = props;
  const decorative =
    isValidElement(icon) && namesItself(props)
      ? cloneElement(icon as ReactElement<{ 'aria-hidden'?: boolean }>, { 'aria-hidden': true })
      : icon;
  return <AntButton ref={ref} {...props} icon={decorative} />;
});

export default Button;
