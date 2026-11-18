// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Locator, type Page } from '@playwright/test';
import { asPerson } from './arrange.ts';
import { test } from './consoleGuard.ts';
import { chooseOption, confirmOk, gotoRoute, login } from './helpers.ts';

// A cave-potential model written in the form, run over an area, and its map switched on.
//
// What this file shows that no server test can. The server's tests prove the arithmetic — the
// weights a set of comparisons comes to, how a ramp scores a distance, which cells a run scores —
// and who may read what came of it. None of them can show that a person can get from nothing to a
// map: that the form can be filled in at all, that the weights and the consistency of the
// comparisons appear beside it as it is typed, that a run asked for here is the one that turns up
// in the list, and that its map arrives on the map when it is asked for.
//
// Why it is run for real. The model here weighs only what has been recorded — the distance to a
// sinkhole and the density of cave entrances — so the run needs no prepared elevation, which a
// plain installation has none of. Everything it reads is invented by this file, in a corner of the
// map no other spec uses, and removed again at the end.

interface FeatureType {
  id: number;
  code: string;
}

/** Far from the demonstration data and from every other spec's features. */
const HERE = { lon: 27.31, lat: 46.71 };

/**
 * Where the model that learns from entrances is run: its own corner, further from the first test's
 * than any layer reaches, so the entrances invented here are in no other run's evidence.
 */
const THERE = { lon: 27.61, lat: 46.71 };

/** An InputNumber by the name it was given; the name is on the field itself. */
function numberField(scope: Page | Locator, name: string) {
  return scope.getByRole('spinbutton', { name, exact: true });
}

/** Finds an area by typing its name into the run form's choice, and takes it. */
async function chooseArea(page: Page, select: Locator, name: string) {
  await select.click();
  await page.keyboard.type(name);
  const option = page
    .locator('.ant-select-dropdown:visible .ant-select-item-option')
    .filter({ hasText: name });
  await expect(option).toBeVisible({ timeout: 30_000 });
  for (let step = 0; step < 10; step++) {
    if (await option.evaluate((el) => el.classList.contains('ant-select-item-option-active'))) {
      break;
    }
    await page.keyboard.press('ArrowDown');
  }
  await expect(option).toHaveClass(/ant-select-item-option-active/);
  await page.keyboard.press('Enter');
  await expect(select).toContainText(name);
}

test('a model is written with its weights shown, run over an area, and its map switched on', async ({
  page,
}) => {
  // A real run is queued behind whatever else the installation is computing.
  test.setTimeout(300_000);
  await login(page);

  const types = await asPerson<FeatureType[]>(page, 'GET', '/api/v1/feature-types');
  const karstArea = types.find((t) => t.code === 'karst_area');
  const sinkhole = types.find((t) => t.code === 'sinkhole');
  expect(karstArea, 'the karst area kind is seeded').toBeTruthy();
  expect(sinkhole, 'the sinkhole kind is seeded').toBeTruthy();

  const stamp = Date.now();
  const areaName = `E2E potential area ${stamp}`;
  const modelName = `E2E potential model ${stamp}`;
  const { lon: w, lat: s } = HERE;
  const feature = (name: string, featureTypeId: number, geometry: object) =>
    asPerson<{ id: string }>(page, 'POST', '/api/v1/features', {
      kind: 'generic',
      name,
      featureTypeId,
      geometry,
      description: null,
      properties: null,
      parents: null,
      locationProtected: false,
      cavingGroupId: null,
      visibility: 'authenticated',
    });
  const area = await feature(areaName, karstArea!.id, {
    type: 'Polygon',
    coordinates: [[[w, s], [w + 0.01, s], [w + 0.01, s + 0.01], [w, s + 0.01], [w, s]]],
  });
  const hollow = await feature(`E2E potential sinkhole ${stamp}`, sinkhole!.id, {
    type: 'Point',
    coordinates: [w + 0.005, s + 0.005],
  });

  let modelId: string | undefined;
  try {
    await gotoRoute(page, '/cave-potential');
    await page.getByTestId('cave-potential-new-model').click();
    const editor = page.getByTestId('cave-potential-editor');
    await expect(editor).toBeVisible();
    await editor.getByTestId('cave-potential-name').fill(modelName);

    // Nothing to weigh yet, so no weights and nothing to save.
    await expect(editor.getByTestId('cave-potential-unfinished')).toBeVisible();
    await expect(page.getByTestId('cave-potential-save')).toBeDisabled();

    await chooseOption(page, editor.getByTestId('cave-potential-add-layer'), 'Distance to sinkholes (m)');
    await editor.getByTestId('cave-potential-add-layer-confirm').click();
    // Ground on a sinkhole scores one, ground half a kilometre from any scores nought.
    await numberField(editor, 'Value of Distance to sinkholes (m) that scores 0').fill('500');
    await numberField(editor, 'Value of Distance to sinkholes (m) that scores 1').fill('0');

    await chooseOption(page, editor.getByTestId('cave-potential-add-layer'), 'Cave-entrance density (per km²)');
    await editor.getByTestId('cave-potential-add-layer-confirm').click();
    await numberField(editor, 'Value of Cave-entrance density (per km²) that scores 0').fill('0');
    await numberField(editor, 'Value of Cave-entrance density (per km²) that scores 1').fill('5');

    // Both layers are made from recorded positions, and the form says what follows from that.
    await expect(editor.getByTestId('cave-potential-positions-notice')).toBeVisible();

    // Weights given outright first: two equal layers are half each.
    await expect(editor.getByTestId('cave-potential-weight-sinkhole_distance')).toHaveText(
      'Distance to sinkholes: 50.0 %',
      { timeout: 30_000 },
    );

    // Then by comparison: the sinkholes three times as important as the density. The weights
    // follow, and the consistency of the comparisons is shown beside them.
    await editor.getByTestId('cave-potential-method').getByText('Pairwise comparisons').click();
    await chooseOption(
      page,
      editor.getByTestId('cave-potential-judgment-sinkhole_distance-entrance_density'),
      '3 × more important',
    );
    await expect(editor.getByTestId('cave-potential-weight-sinkhole_distance')).toHaveText(
      'Distance to sinkholes: 75.0 %',
      { timeout: 30_000 },
    );
    await expect(editor.getByTestId('cave-potential-consistency')).toContainText('Consistency ratio 0.000');

    await page.getByTestId('cave-potential-save').click();
    await expect(editor).toHaveCount(0, { timeout: 30_000 });

    const modelRow = page.getByTestId('cave-potential-models').locator('tr').filter({ hasText: modelName });
    await expect(modelRow).toBeVisible();
    await expect(modelRow).toContainText('Distance to sinkholes 75 %');
    const models = await asPerson<{ id: string; name: string }[]>(page, 'GET', '/api/v1/cave-potential/models');
    modelId = models.find((m) => m.name === modelName)?.id;

    await modelRow.getByTestId('cave-potential-run').click();
    const runForm = page.getByTestId('cave-potential-run-form');
    await expect(runForm).toBeVisible();
    await chooseArea(page, runForm.getByTestId('cave-potential-run-area'), areaName);
    await numberField(runForm, 'Cell size (m)').fill('50');
    await page.getByTestId('cave-potential-run-start').click();
    await expect(runForm).toHaveCount(0, { timeout: 30_000 });

    // The run asked for is the one listed, and it comes to a map.
    const resultRow = page.getByTestId('cave-potential-results').locator('tr').filter({ hasText: modelName });
    await expect(resultRow).toBeVisible({ timeout: 30_000 });
    await expect(resultRow).toContainText(areaName);
    // Waited on by the row's own word, and for either ending: a run that failed says so at once,
    // with its reason, instead of being waited on for the whole allowance.
    await expect(resultRow.getByTestId('cave-potential-result-status')).toHaveText(/^(Ready|Failed)$/, {
      timeout: 240_000,
    });
    expect(await resultRow.innerText(), 'the run came to a map').toContain('Ready');

    await resultRow.getByTestId('cave-potential-show').click();
    await expect(page.locator('.ol-viewport')).toBeVisible({ timeout: 30_000 });

    // Switched on in the list of layers, with its key and what the map is not.
    const listed = page.getByTestId('cave-potential-list').locator('div').filter({ hasText: modelName }).first();
    await expect(listed.getByRole('checkbox')).toBeChecked({ timeout: 30_000 });
    await expect(page.getByTestId('cave-potential-legend')).toBeVisible();
    await expect(page.getByTestId('cave-potential-caveat')).toContainText('Do not pass it on');

    // And on the map itself: the composer tree is built from the map's own layers, so a row
    // there is the layer existing.
    const treeRow = page.locator('.layer-composer .ant-tree-treenode').filter({ hasText: modelName }).first();
    await expect(treeRow).toBeVisible({ timeout: 30_000 });

    // Taken off again, the map lets go of it.
    await listed.getByRole('checkbox').uncheck();
    await expect(treeRow).toHaveCount(0, { timeout: 15_000 });

    // Deleting the map takes it out of the list it was asked for from.
    await gotoRoute(page, '/cave-potential');
    const again = page.getByTestId('cave-potential-results').locator('tr').filter({ hasText: modelName });
    await again.getByTestId('cave-potential-result-delete').click();
    await confirmOk(page, { scope: page.locator('.ant-popconfirm'), exact: true });
    await expect(again).toHaveCount(0, { timeout: 30_000 });
  } finally {
    const results = await asPerson<{ id: string; modelName: string }[]>(page, 'GET', '/api/v1/cave-potential/results')
      .catch(() => []);
    for (const result of results.filter((r) => r.modelName === modelName)) {
      await asPerson(page, 'DELETE', `/api/v1/cave-potential/results/${result.id}`).catch(() => undefined);
    }
    if (modelId) {
      await asPerson(page, 'DELETE', `/api/v1/cave-potential/models/${modelId}`).catch(() => undefined);
    }
    await asPerson(page, 'DELETE', `/api/v1/features/${hollow.id}`).catch(() => undefined);
    await asPerson(page, 'DELETE', `/api/v1/features/${area.id}`).catch(() => undefined);
  }
});

// A model that learns its weights from known entrances, run for real, and what it learnt read.
//
// The server's tests prove the counting — the ratio and the weights of each class, the seeded
// parting, the two curves — and that nobody reads a result who may not place every entrance in it.
// What they cannot show is that a person can ask for such a model at all: that choosing the method
// takes the scores and weights out of the form and puts the parting in, that the run turns up with
// its figures, and that the tables and the curves open under it.
//
// Fourteen invented entrances and one invented sinkhole, read by the account that made them. Ten
// of the entrances lie within two hundred metres of the sinkhole and four well beyond, so the
// near class must come out commoner in entrances than the ground as a whole.
test('a model learns its weights from known entrances, and its tables and curves are read', async ({ page }) => {
  test.setTimeout(300_000);
  await login(page);

  const types = await asPerson<FeatureType[]>(page, 'GET', '/api/v1/feature-types');
  const karstArea = types.find((t) => t.code === 'karst_area');
  const sinkhole = types.find((t) => t.code === 'sinkhole');
  expect(karstArea, 'the karst area kind is seeded').toBeTruthy();
  expect(sinkhole, 'the sinkhole kind is seeded').toBeTruthy();
  const caveTypes = await asPerson<{ id: number }[]>(page, 'GET', '/api/v1/cave-types');
  const entranceTypes = await asPerson<{ id: number }[]>(page, 'GET', '/api/v1/entrance-types');

  const stamp = Date.now();
  const areaName = `E2E learning area ${stamp}`;
  const modelName = `E2E learning model ${stamp}`;
  const { lon: w, lat: s } = THERE;
  const feature = (name: string, featureTypeId: number, geometry: object) =>
    asPerson<{ id: string }>(page, 'POST', '/api/v1/features', {
      kind: 'generic',
      name,
      featureTypeId,
      geometry,
      description: null,
      properties: null,
      parents: null,
      locationProtected: false,
      cavingGroupId: null,
      visibility: 'authenticated',
    });
  const area = await feature(areaName, karstArea!.id, {
    type: 'Polygon',
    coordinates: [[[w, s], [w + 0.01, s], [w + 0.01, s + 0.01], [w, s + 0.01], [w, s]]],
  });
  // Low in the area, so that its upper part lies beyond two hundred metres of it.
  const hollow = await feature(`E2E learning sinkhole ${stamp}`, sinkhole!.id, {
    type: 'Point',
    coordinates: [w + 0.005, s + 0.002],
  });

  // A thousandth of a degree is some seventy-six metres east and a hundred and eleven north here,
  // so at a fifty-metre cell no two of these share a cell.
  const near: [number, number][] = [
    [0.004, 0.001], [0.005, 0.001], [0.006, 0.001],
    [0.004, 0.002], [0.006, 0.002],
    [0.004, 0.003], [0.005, 0.003], [0.006, 0.003],
    [0.003, 0.002], [0.007, 0.002],
  ];
  const far: [number, number][] = [[0.002, 0.008], [0.005, 0.009], [0.008, 0.007], [0.001, 0.006]];
  const entrances = [...near, ...far];
  const caveIds: string[] = [];
  let modelId: string | undefined;
  try {
    for (const [i, [east, north]] of entrances.entries()) {
      const cave = await asPerson<{ id: string }>(page, 'POST', '/api/v1/caves', {
        name: `E2E learning cave ${stamp} ${i}`,
        caveTypeId: caveTypes[0]!.id,
        visibility: 'authenticated',
        locationProtected: false,
        explorationStatus: 'Unknown',
        isShowCave: false,
      });
      caveIds.push(cave.id);
      await asPerson(page, 'POST', `/api/v1/caves/${cave.id}/entrances`, {
        entranceTypeId: entranceTypes[0]!.id,
        isMain: true,
        geom: { type: 'Point', coordinates: [w + east, s + north] },
        positionQuality: 'Gps',
      });
    }

    await gotoRoute(page, '/cave-potential');
    await page.getByTestId('cave-potential-new-model').click();
    const editor = page.getByTestId('cave-potential-editor');
    await expect(editor).toBeVisible();
    await editor.getByTestId('cave-potential-name').fill(modelName);

    // Choosing the method puts the parting of the entrances into the form.
    await expect(editor.getByTestId('cave-potential-training')).toHaveCount(0);
    await editor.getByTestId('cave-potential-method').getByText('Weights of evidence').click();
    await expect(editor.getByTestId('cave-potential-training')).toBeVisible();
    await expect(numberField(editor, 'Entrances kept back for testing (%)')).toHaveValue('30');
    await numberField(editor, 'Seed of the draw').fill('7');
    await expect(editor.getByTestId('cave-potential-learning-notice')).toBeVisible();

    // The layer is cut into classes and asked for no score and no weight: within two hundred
    // metres of a sinkhole, and beyond.
    await chooseOption(page, editor.getByTestId('cave-potential-add-layer'), 'Distance to sinkholes (m)');
    await editor.getByTestId('cave-potential-add-layer-confirm').click();
    await expect(page.getByTestId('cave-potential-save')).toBeDisabled();
    await numberField(editor, 'Upper bound of class 1 of Distance to sinkholes (m)').fill('200');
    await expect(editor.getByRole('spinbutton', { name: /^Score of/ })).toHaveCount(0);
    await expect(editor.getByTestId('cave-potential-layer-weight')).toHaveCount(0);
    await expect(editor.getByTestId('cave-potential-weights-counted')).toBeVisible();

    await page.getByTestId('cave-potential-save').click();
    await expect(editor).toHaveCount(0, { timeout: 30_000 });

    const modelRow = page.getByTestId('cave-potential-models').locator('tr').filter({ hasText: modelName });
    await expect(modelRow).toBeVisible();
    await expect(modelRow).toContainText('Weights of evidence');
    await expect(modelRow.getByTestId('cave-potential-learns')).toBeVisible();
    const models = await asPerson<{ id: string; name: string }[]>(page, 'GET', '/api/v1/cave-potential/models');
    modelId = models.find((m) => m.name === modelName)?.id;

    await modelRow.getByTestId('cave-potential-run').click();
    const runForm = page.getByTestId('cave-potential-run-form');
    await expect(runForm).toBeVisible();
    await chooseArea(page, runForm.getByTestId('cave-potential-run-area'), areaName);
    await numberField(runForm, 'Cell size (m)').fill('50');
    await page.getByTestId('cave-potential-run-start').click();
    await expect(runForm).toHaveCount(0, { timeout: 30_000 });

    const resultRow = page
      .getByTestId('cave-potential-results')
      .locator('tr.ant-table-row')
      .filter({ hasText: modelName });
    await expect(resultRow).toBeVisible({ timeout: 30_000 });
    await expect(resultRow.getByTestId('cave-potential-result-status')).toHaveText(/^(Ready|Failed)$/, {
      timeout: 240_000,
    });
    expect(await resultRow.innerText(), 'the run came to a map').toContain('Ready');

    // What the run learnt, in a line under its state: every invented entrance is in one part or
    // the other, and both areas under the curve are figures between nought and one.
    const line = await resultRow.getByTestId('cave-potential-result-training').innerText();
    const told = /learnt from (\d+) entrances, tested on (\d+) · AUC (\d\.\d{3}) \/ (\d\.\d{3})/.exec(line);
    expect(told, `the line says what was learnt: ${line}`).not.toBeNull();
    expect(Number(told![1]) + Number(told![2])).toBe(entrances.length);
    expect(Number(told![2])).toBeGreaterThan(0);
    // Ten of fourteen entrances lie on the small near class, so the map finds the entrances it
    // learnt from far better than chance.
    expect(Number(told![3])).toBeGreaterThan(0.5);

    await resultRow.getByTestId('cave-potential-training-toggle').click();
    const view = page.getByTestId('cave-potential-training-view');
    await expect(view).toBeVisible();
    await expect(view.getByTestId('cave-potential-training-split')).toContainText('30 % kept back, seed 7');
    await expect(view.getByTestId('cave-potential-auc-success')).toContainText(`AUC ${told![3]}`);
    await expect(view.getByTestId('cave-potential-auc-prediction')).toContainText(`AUC ${told![4]}`);
    // Nothing invented here is protected, and the view says nothing of protected entrances.
    await expect(view.getByTestId('cave-potential-training-protected')).toHaveCount(0);

    // The weight table: a row for each class under the bounds the model was run with, and the
    // near class commoner in entrances than the ground as a whole.
    const table = view.getByTestId('cave-potential-weights-sinkhole_distance');
    const nearRow = table.locator('tr').filter({ hasText: 'below 200' });
    const farRow = table.locator('tr').filter({ hasText: '200 and above' });
    await expect(nearRow).toBeVisible();
    await expect(farRow).toBeVisible();
    const figures = async (row: Locator) => (await row.locator('td').allInnerTexts()).map((text) => text.trim());
    const nearFigures = await figures(nearRow);
    const farFigures = await figures(farRow);
    // Class, cells, cells with an entrance, frequency ratio, W+, W−, contrast, its deviation, studentised.
    expect(nearFigures).toHaveLength(9);
    expect(Number(nearFigures[2]) + Number(farFigures[2])).toBe(Number(told![1]));
    expect(Number(nearFigures[3])).toBeGreaterThan(1);
    expect(Number(farFigures[3])).toBeLessThan(1);
    expect(Number(nearFigures[4])).toBeGreaterThan(0);
    expect(Number(nearFigures[6])).toBeGreaterThan(0);

    // Both curves are drawn, with the line of chance to read them against.
    const curves = view.getByTestId('cave-potential-rate-curves');
    await expect(curves.locator('svg')).toBeVisible();
    await expect(curves).toContainText('Success rate');
    await expect(curves).toContainText('Prediction rate');
    await expect(curves).toContainText('Chance');

    // On the map its key says the colours rank this map's ground only, and that it must not be passed on.
    await resultRow.getByTestId('cave-potential-show').click();
    await expect(page.locator('.ol-viewport')).toBeVisible({ timeout: 30_000 });
    await expect(page.getByTestId('cave-potential-legend')).toBeVisible({ timeout: 30_000 });
    await expect(page.getByTestId('cave-potential-caveat')).toContainText('they rank the ground');
    await expect(page.getByTestId('cave-potential-caveat')).toContainText('Do not pass it on');
  } finally {
    const results = await asPerson<{ id: string; modelName: string }[]>(page, 'GET', '/api/v1/cave-potential/results')
      .catch(() => []);
    for (const result of results.filter((r) => r.modelName === modelName)) {
      await asPerson(page, 'DELETE', `/api/v1/cave-potential/results/${result.id}`).catch(() => undefined);
    }
    if (modelId) {
      await asPerson(page, 'DELETE', `/api/v1/cave-potential/models/${modelId}`).catch(() => undefined);
    }
    for (const id of caveIds) {
      await asPerson(page, 'DELETE', `/api/v1/caves/${id}`).catch(() => undefined);
    }
    await asPerson(page, 'DELETE', `/api/v1/features/${hollow.id}`).catch(() => undefined);
    await asPerson(page, 'DELETE', `/api/v1/features/${area.id}`).catch(() => undefined);
  }
});
