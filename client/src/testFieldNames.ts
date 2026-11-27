// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect } from 'vitest';

/**
 * Every control a screen reader can land on in `root` has a spoken name.
 *
 * A visible caption is not enough on its own: antd's `Form.Item` ties its label to the control
 * only when the item has a `name` (or an explicit `htmlFor`), a Select shows its placeholder as a
 * separate element rather than as the input's placeholder, and a Switch or a Slider handle has no
 * text of its own at all. Each of those reads as "edit text" or "slider" with nothing after it.
 *
 * A text field's own `placeholder` attribute is accepted as its name, as browsers and the axe
 * checks accept it (the accessible-name library under jsdom does not, so it is checked here).
 */
export function expectEveryFieldNamed(root: ParentNode): void {
  const fields = root.querySelectorAll<HTMLElement>(
    'input:not([type="hidden"]), textarea, [role="combobox"], [role="spinbutton"], [role="slider"], [role="switch"]',
  );
  expect(fields.length).toBeGreaterThan(0);
  for (const field of Array.from(fields)) {
    const what = `${field.tagName.toLowerCase()}${field.getAttribute('role') ? `[role=${field.getAttribute('role')}]` : ''}`
      + (field.dataset.testid ? `[data-testid=${field.dataset.testid}]` : '')
      + ` class="${field.className}" type="${field.getAttribute('type') ?? ''}"`
      + ` in ${field.closest('[data-testid]')?.getAttribute('data-testid') ?? 'no test id'}`;
    // Not drawn, so not reachable: antd's upload keeps its file input under display:none.
    if (getComputedStyle(field).display === 'none') continue;
    if (field.getAttribute('placeholder')?.trim()) continue;
    expect(field, `${what} has no accessible name`).toHaveAccessibleName();
  }
}

/**
 * Every column header in `root` has words a screen reader can say — for a column of buttons or an
 * expand toggle those words may be hidden, but an empty header makes every cell under it read as
 * belonging to nothing.
 */
export function expectEveryColumnHeaderNamed(root: ParentNode): void {
  const headers = root.querySelectorAll<HTMLElement>('th');
  expect(headers.length).toBeGreaterThan(0);
  headers.forEach((header, index) => {
    expect(header.textContent?.trim(), `column header ${index + 1} (${header.className}) is empty`).toBeTruthy();
  });
}
