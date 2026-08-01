// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, test, type Locator, type Page } from '@playwright/test';
import { login } from './helpers.ts';

/** Opens an antd select and picks the option with this label (options render in a portal). */
async function pickOption(page: Page, select: Locator, label: string) {
  await select.click();
  await page.locator('.ant-select-dropdown:visible .ant-select-item-option').filter({ hasText: label }).first().click();
}

// The permission-group editor round-trip, including the model's sharpest edge: a deny
// may not be saved blind — the editor demands a look at the effective-access preview,
// and the preview explains each verdict by naming the rule that decided it.
test('permission-group editor round-trips a deny behind the preview gate', async ({ page }) => {
  const stamp = Date.now();
  const cavingGroupName = `E2E Preview Club ${stamp}`;
  const permissionGroupName = `E2E Deny Ruleset ${stamp}`;
  await login(page);

  // A caving group to preview as: previewing as the admin would only ever show full
  // administration, which no rule (not even the deny) can reach.
  await page.goto('/caving-groups');
  await page.getByRole('button', { name: 'New caving group' }).click();
  await page.getByLabel('Name', { exact: true }).fill(cavingGroupName);
  await page.getByRole('button', { name: 'OK' }).click();
  await expect(page.getByText('Saved.')).toBeVisible({ timeout: 15_000 });

  // Create the permission group; the page drops straight into its editor.
  await page.goto('/admin/permission-groups');
  await page.getByRole('button', { name: 'New permission group' }).click();
  await page.getByLabel('Name', { exact: true }).fill(permissionGroupName);
  await page.getByRole('button', { name: 'OK' }).click();
  const drawer = page.locator('.ant-drawer-section');
  await expect(drawer.getByText(permissionGroupName).first()).toBeVisible({ timeout: 15_000 });
  // Tab panes stay mounted once visited, so everything below addresses the visible one.
  const pane = drawer.locator('[role="tabpanel"]:visible');

  // Trustee: the caving group — its account-holding members inherit the ruleset.
  await drawer.getByRole('tab', { name: 'Members' }).click();
  await pickOption(page, pane.locator('.ant-select').first(), 'Caving group');
  await pickOption(page, pane.locator('.ant-select').nth(1), cavingGroupName);
  // The icon joins the accessible name ("plus Add"), so this matches by substring; the
  // visible pane scoping keeps it away from the hidden rules pane's "Add rule".
  await pane.getByRole('button', { name: 'Add' }).click();
  await expect(pane.locator('.ant-list-item').filter({ hasText: cavingGroupName })).toBeVisible();

  // Rules: one allow (features · read · everything) and one deny (trip logs · read).
  await drawer.getByRole('tab', { name: 'Rules' }).click();
  await pane.getByRole('button', { name: 'Add rule' }).click();
  await expect(pane.locator('.ant-table-row').first().getByText('Allow')).toBeVisible();

  await pane.getByRole('button', { name: 'Add rule' }).click();
  const denyRule = pane.locator('.ant-table-row').nth(1);
  await pickOption(page, denyRule.locator('.ant-select').first(), 'Deny');
  await pickOption(page, denyRule.locator('.ant-select').nth(1), 'Trip logs');

  // The foot-gun guard: with a deny staged, saving is locked until the preview is seen.
  await expect(pane.getByTestId('save-rules')).toBeDisabled();
  await expect(pane.getByText(/contains a deny/)).toBeVisible();
  await pane.getByRole('button', { name: 'Open the preview' }).click();

  // Preview as the caving group; looking at the answer is what unlocks the save.
  await pickOption(page, pane.locator('.ant-select').first(), 'Caving group');
  await pickOption(page, pane.locator('.ant-select').nth(1), cavingGroupName);
  await pane.getByTestId('run-preview').click();
  await expect(
    pane.locator('.ant-table-row').filter({ hasText: 'Trip logs' }).first(),
  ).toBeVisible({ timeout: 15_000 });

  await drawer.getByRole('tab', { name: 'Rules' }).click();
  await expect(pane.getByTestId('save-rules')).toBeEnabled();
  await pane.getByTestId('save-rules').click();
  await expect(page.getByText('Saved.')).toBeVisible({ timeout: 15_000 });

  // Now the deny is stored: the preview must show trip-log reads decided by this very
  // ruleset — "which rule won and why", served by the server and rendered verbatim.
  await drawer.getByRole('tab', { name: 'Preview' }).click();
  await pane.getByTestId('run-preview').click();
  await expect(pane.getByText('Expand a row to see which rule decided each action.')).toBeVisible();
  const tripLogsRow = pane.locator('.ant-table-row').filter({ hasText: 'Trip logs' }).first();
  await tripLogsRow.locator('.ant-table-row-expand-icon').click();
  await expect(
    pane.getByText(`Decided at the global level by "${permissionGroupName}".`).first(),
  ).toBeVisible({ timeout: 15_000 });

  // Round-trip: a fresh load serves the saved ruleset back, deny row loud and first-class.
  // A full navigation re-runs the OIDC round-trip, so wait for the page to settle first.
  await page.goto('/admin/permission-groups');
  await expect(page.getByRole('button', { name: 'New permission group' })).toBeVisible({ timeout: 20_000 });
  await page
    .getByRole('row', { name: new RegExp(permissionGroupName) })
    .getByRole('button', { name: 'Manage' })
    .click();
  await expect(drawer.getByText(permissionGroupName).first()).toBeVisible({ timeout: 15_000 });
  const reloadedDeny = pane.locator('.ant-table-row').filter({ hasText: 'Trip logs' }).first();
  await expect(reloadedDeny.getByText('Deny')).toBeVisible({ timeout: 15_000 });

  // Cleanup: the ruleset goes; the throwaway caving group stays (there is no UI delete).
  await page.locator('.ant-drawer-close').click();
  await page
    .getByRole('row', { name: new RegExp(permissionGroupName) })
    .getByRole('button', { name: 'delete' })
    .click();
  await page.getByRole('button', { name: 'OK' }).click();
  await expect(page.getByText('Deleted.')).toBeVisible({ timeout: 15_000 });
});
