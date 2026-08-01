// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, test, type Page } from '@playwright/test';
import {
  centreOnDemoCave, deleteFeature, login, longPressMap, overlayTreeNode, tapMap,
} from './helpers.ts';

// Runs in the `mobile-android` project only (Pixel 7, 412x915, touch). These cover the
// phone layout — the docks-as-drawers swap and the chrome that has to step aside at this
// width — and full editing by finger, which is the point of the phone support rather than
// a nice-to-have: every tool a cursor has must be reachable without one.

/** Arms drawing for a feature type through the palette, the way a thumb reaches it. */
async function armType(page: Page, typeName: string) {
  await page.locator('.map-edit-overlay').getByTestId('feature-palette-trigger').click();
  await page.getByRole('button', { name: typeName }).click();
}

// PARKED — the drawer half of this test still passes; the cluster half no longer has a way
// to set up. It used to centre the map by picking a search result, and search results now
// carry no coordinates by design, so picking one opens the record instead of moving the map.
// Centring from the feature list's "show on map" works on desktop (the same flow passes in
// smoke.spec.ts) but does not reach a clustering zoom here within any reasonable retry
// budget, so what this needs is a phone-layout way to place the view, not a longer timeout.
test.fixme('docks become drawers, and the details drawer opens when something is picked', async ({ page }) => {
  await login(page);

  // Neither dock is mounted at this width — the map gets the whole canvas.
  await expect(page.getByText('Base layers')).toHaveCount(0);
  await expect(page.locator('.map-workspace-panel')).toHaveCount(0);

  // The same dock toggle that collapses the pane on desktop opens the drawer here.
  await page.getByTestId('map-dock-toggle-left').click();
  await expect(page.getByText('Base layers')).toBeVisible();

  // The layer composer is fully usable from inside the drawer.
  const entrances = overlayTreeNode(page, 'Cave entrances');
  await expect(entrances.locator('.ant-tree-checkbox-checked')).toBeVisible();
  await entrances.locator('.ant-tree-checkbox').click();
  await expect(entrances.locator('.ant-tree-checkbox-checked')).toHaveCount(0);
  await entrances.locator('.ant-tree-checkbox').click();
  await expect(entrances.locator('.ant-tree-checkbox-checked')).toBeVisible();

  // Close it by tapping the map beside the drawer, the way a thumb would. The mask spans
  // the viewport but the 320px panel sits on top of most of it, so aim at the strip of
  // mask the panel leaves exposed rather than at its centre.
  await page.locator('.ant-drawer-mask').click({ position: { x: 370, y: 400 } });
  await expect(page.getByText('Base layers')).toBeHidden();

  // Centre on the demo cave, then zoom out until its entrances aggregate at the view
  // centre. Search cannot centre the map any more — its results carry no coordinates.
  await centreOnDemoCave(page);

  // Picking on the map opens the details drawer by itself: with no dock on screen, a
  // selection would otherwise appear to do nothing at all. Stepping the zoom inside the
  // retry keeps this independent of the zoom the fit landed on, and absorbs the zoom
  // animation and the debounced bbox loader, which settle at their own pace.
  const canvas = page.locator('.map-canvas');
  await expect(async () => {
    await page.locator('.ol-zoom-out').click();
    await canvas.click();
    await expect(page.getByText(/entrances in this area/)).toBeVisible({ timeout: 2_000 });
  }).toPass({ timeout: 120_000 });

  // ...and it is a real drawer over the map, not the desktop pane.
  await expect(page.locator('.ant-drawer-right')).toBeVisible();
  await expect(page.locator('.map-workspace-panel')).toHaveCount(0);

  // The cluster's members are reachable and select their entrance from in there.
  await page.getByRole('button', { name: /entrance|Peșter/i }).first().click();
  await expect(page.getByRole('heading', { name: /Peștera Demo Mare/ })).toBeVisible({ timeout: 15_000 });
});

test('desktop-only chrome steps aside and the save cluster outlives the tool strip', async ({ page }) => {
  await login(page);

  // Pop-out windows and the jump-to-scale combo are meaningless/clutter at this width.
  await expect(page.getByTestId('map-popout-registry')).toHaveCount(0);
  await expect(page.getByTestId('map-popout-viewer3d')).toHaveCount(0);
  await expect(page.locator('.map-scale-overlay')).toHaveCount(0);
  // Locate-me stays: it is more useful on a phone than anywhere else.
  await expect(page.locator('.map-popout-overlay')).toBeVisible();

  // Search takes the width the pop-outs gave up.
  const search = page.getByPlaceholder('Search features, trips or places…');
  await expect(search).toBeVisible();
  const searchWidth = (await search.boundingBox())!.width;
  expect(searchWidth).toBeGreaterThan(200);

  // The edit bar splits in two: a strip that scrolls, and a save cluster that does not.
  const strip = page.getByTestId('edit-tool-strip');
  const saveCluster = page.getByTestId('edit-save-cluster');
  await expect(strip).toBeVisible();
  await expect(saveCluster).toBeVisible();

  // The strip really does overflow at this width — otherwise the rest of this proves
  // nothing about scrolling.
  expect(await strip.evaluate((el) => el.scrollWidth > el.clientWidth)).toBe(true);

  // Scrolled to its far end, every tool has been reachable and Save is still on screen.
  await strip.evaluate((el) => el.scrollTo({ left: el.scrollWidth }));
  await expect(strip.getByTestId('tool-add-entrance')).toBeInViewport();
  await expect(saveCluster.getByRole('button', { name: /Save/ })).toBeInViewport();
});

test('the app declares itself installable, with icons that exist', async ({ page }) => {
  await login(page);

  // Every part of this is silent when broken: a bad path or a missing size does not fail the
  // page, it just quietly costs the install prompt or leaves a blank home-screen icon.
  await expect(page.locator('link[rel="manifest"]')).toHaveAttribute('href', '/manifest.webmanifest');
  await expect(page.locator('meta[name="theme-color"]')).toHaveAttribute('content', '#146262');

  const response = await page.request.get('/manifest.webmanifest');
  expect(response.status()).toBe(200);
  // The MIME type is part of the contract: `.webmanifest` is absent from common server MIME
  // tables (nginx's among them), and a manifest served as application/octet-stream is
  // ignored by some browsers without any visible error.
  expect(response.headers()['content-type']).toContain('application/manifest+json');
  const manifest = JSON.parse(await response.text()) as {
    name: string;
    start_url: string;
    display: string;
    icons: { src: string; sizes: string; purpose: string }[];
  };
  expect(manifest.name).toBe('SilexGIS');
  expect(manifest.start_url).toBe('/');
  expect(manifest.display).toBe('standalone');

  // Chrome needs a 192 and a 512 to offer installation at all, and a maskable set or Android
  // pads the icon into a white blob.
  const sizes = manifest.icons.map((icon) => `${icon.sizes} ${icon.purpose}`);
  expect(sizes).toEqual(
    expect.arrayContaining(['192x192 any', '512x512 any', '192x192 maskable', '512x512 maskable']),
  );

  for (const icon of manifest.icons) {
    const file = await page.request.get(icon.src);
    expect(file.status(), `${icon.src} is listed in the manifest`).toBe(200);
    expect(file.headers()['content-type']).toContain('image/png');
  }
});

test('a point feature is placed by long-pressing the map', async ({ page }) => {
  const featureName = `E2E Touch Point ${Date.now()}`;
  await login(page);

  // Long-press is the phone's right-click, and the only way to place a point exactly:
  // the feature lands where the finger pressed, not where a fat tap was guessed to be.
  await longPressMap(page, 200, 300);
  await expect(page.getByText('Copy coordinates')).toBeVisible();
  await page.getByText('Add feature').click();
  await page.getByRole('menuitem', { name: 'Sinkhole / Doline' }).click();

  const modal = page.getByRole('dialog');
  await expect(modal.getByText('New feature')).toBeVisible();
  await modal.getByLabel('Name').fill(featureName);
  await modal.getByRole('button', { name: 'OK' }).click();

  // ...and it saves through the same batched pipeline a mouse uses.
  const reloaded = page.waitForResponse((r) => r.url().includes('/api/v1/map/features') && r.ok());
  await page.getByTestId('edit-save-cluster').getByRole('button', { name: /Save/ }).click();
  await expect(page.getByText('Saved.')).toBeVisible({ timeout: 15_000 });
  await reloaded;

  await deleteFeature(page, featureName);
});

test('a line is drawn, corrected and finished entirely by finger', async ({ page }) => {
  const featureName = `E2E Touch Line ${Date.now()}`;
  await login(page);

  await armType(page, 'Fracture line / Fault');
  const sketchBar = page.getByTestId('map-sketch-bar');
  await expect(sketchBar).toBeHidden();

  // Three taps, three vertices. The bar appears as soon as a shape is under way, because
  // from here there is no gesture that ends it: hitting the last vertex needs a cursor's
  // precision, and a double-tap is the map's zoom.
  await tapMap(page, 120, 300);
  await expect(sketchBar).toBeVisible();
  await tapMap(page, 200, 380);
  await tapMap(page, 280, 300);

  // Retract the mis-tapped last vertex, then place it again — the shape survives.
  await sketchBar.getByTestId('sketch-remove-point').click();
  await tapMap(page, 300, 420);
  await sketchBar.getByTestId('sketch-finish').click();
  await expect(sketchBar).toBeHidden();

  const modal = page.getByRole('dialog');
  await expect(modal.getByText('New feature')).toBeVisible();
  await modal.getByLabel('Name').fill(featureName);
  await modal.getByRole('button', { name: 'OK' }).click();

  const saveCluster = page.getByTestId('edit-save-cluster');
  await expect(saveCluster.locator('.ant-badge-count')).toHaveText('1'); // the dirty badge counts it
  const reloaded = page.waitForResponse((r) => r.url().includes('/api/v1/map/features') && r.ok());
  await saveCluster.getByRole('button', { name: /Save/ }).click();
  await expect(page.getByText('Saved.')).toBeVisible({ timeout: 15_000 });
  await reloaded;

  // The saved line is then reshaped by finger: touch a vertex and delete it from the bar.
  // Desktop deletes vertices with alt-click, which needs a keyboard the phone has not got.
  await page.getByTestId('edit-tool-strip').getByRole('button', { name: 'aim' }).click();
  await tapMap(page, 200, 380);
  await page.getByTestId('map-sketch-bar').getByTestId('sketch-delete-vertex').click();

  // The deletion is an ordinary pending geometry edit, so Save comes back to life for it.
  // Asserted on the button rather than on the dirty badge: the badge from the save above
  // animates away over a few hundred ms, and its lingering "1" answers a text assertion
  // truthfully enough to hide a Delete vertex that did nothing at all.
  await expect(saveCluster.getByRole('button', { name: /Save/ })).toBeEnabled({ timeout: 10_000 });
  const resaved = page.waitForResponse((r) => r.url().includes('/api/v1/map/features') && r.ok());
  await saveCluster.getByRole('button', { name: /Save/ }).click();
  await expect(page.getByText('Saved.').first()).toBeVisible({ timeout: 15_000 });
  await resaved;

  await deleteFeature(page, featureName);
});

test('cancelling a sketch abandons the shape but keeps the tool armed', async ({ page }) => {
  await login(page);

  await armType(page, 'Fracture line / Fault');
  const sketchBar = page.getByTestId('map-sketch-bar');
  await tapMap(page, 150, 300);
  await tapMap(page, 250, 380);
  await expect(sketchBar).toBeVisible();

  await sketchBar.getByTestId('sketch-cancel').click();

  // Nothing was drawn and nothing is pending — but the next tap starts a new line
  // without re-arming, so a mistake costs one button, not the whole setup.
  await expect(page.getByRole('dialog')).toBeHidden();
  await expect(page.getByTestId('edit-save-cluster').getByRole('button', { name: /Save/ })).toBeDisabled();
  await tapMap(page, 150, 300);
  await expect(sketchBar).toBeVisible();
});

test('a measurement is finished from the same sketch bar', async ({ page }) => {
  await login(page);

  // Measuring is a different interaction owned by a different component, but a finger
  // cannot end it either — so the one Finish button has to reach it too.
  const strip = page.getByTestId('edit-tool-strip');
  await strip.getByRole('button', { name: 'column-width' }).click();
  const sketchBar = page.getByTestId('map-sketch-bar');
  await expect(sketchBar).toBeVisible();

  await tapMap(page, 120, 300);
  await tapMap(page, 260, 400);
  await sketchBar.getByTestId('sketch-finish').click();

  // A finished measurement parks its total in a static tooltip; an unfinished one only
  // ever has the dynamic one that trails the pointer.
  const total = page.locator('.react-geo-measure-tooltip-static');
  await expect(total.first()).toBeVisible({ timeout: 10_000 });
  await expect(total.first()).toContainText(/\d/);

  // Toggling the tool off clears the drawing and retires the bar with it.
  await strip.getByRole('button', { name: 'column-width' }).click();
  await expect(sketchBar).toBeHidden();
  await expect(total).toHaveCount(0);
});
