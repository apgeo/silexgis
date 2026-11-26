// SPDX-License-Identifier: AGPL-3.0-or-later
import { createHash } from 'node:crypto';
import { readFile } from 'node:fs/promises';
import { expect, type APIResponse, type Page } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { entranceToPick, gotoRoute, login, openScene3dAt } from './helpers.ts';

// A flyby rendered on the server, through the pages a person uses: an administrator issues a render
// node, the node calls, the movie panel offers "Render on the server", and the render is followed to
// its film.
//
// The node is this test, speaking the render worker's protocol over HTTP as the worker does — the
// claim, the bundle, a heartbeat, the film and the completion — so no Blender is needed and the film
// is a few invented bytes. What is checked is the page and the server between them: that the bundle
// the node is handed is the one the panel asked for, that the panel follows what the node says, and
// that the film it downloads is the one the node sent.

const sha256 = (bytes: Buffer) => createHash('sha256').update(bytes).digest('hex');

function nodeCalls(page: Page, token: string) {
  const headers = { Authorization: `Bearer ${token}` };
  const url = (path: string) => `/api/v1/render-nodes/${path}`;
  return {
    claim: () =>
      page.request.post(url('claim'), {
        headers,
        data: {
          protocol: 1,
          node: 'e2e-node',
          worker: '0.0.0.0',
          blenderVersions: ['4.5.0'],
          device: 'CPU',
          containers: ['mp4', 'mkv', 'webm'],
          keptJobs: [],
          waitSeconds: 0,
        },
      }),
    get: (path: string) => page.request.get(url(path), { headers }),
    post: (path: string, data: unknown) => page.request.post(url(path), { headers, data }),
    put: (path: string, body: Buffer) =>
      page.request.put(url(path), {
        headers: { ...headers, 'Content-Type': 'application/octet-stream', 'X-Content-SHA256': sha256(body) },
        data: body,
      }),
  };
}

async function json<T>(response: APIResponse): Promise<T> {
  expect(response.ok(), `${response.url()} answered ${response.status()}: ${await response.text()}`).toBeTruthy();
  return (await response.json()) as T;
}

interface Job {
  jobId: string;
  attempt: number;
  name?: string;
  plan: string;
  container?: string;
  bundle: { url: string; size: number; sha256: string };
}

test('a flyby is rendered on the server by a render node, followed in the panel, and its film downloaded', async ({ page }) => {
  test.setTimeout(600_000);
  await login(page);

  // ---- an administrator issues a node, and its token is shown once ----
  await gotoRoute(page, '/admin/render-nodes');
  const nodeName = `e2e-node-${Date.now()}`;
  await page.getByTestId('render-node-name').fill(nodeName);
  await page.getByTestId('render-node-issue').click();
  const token = ((await page.getByTestId('render-node-token').textContent()) ?? '').trim();
  expect(token).toMatch(/^sgn_/);
  const node = nodeCalls(page, token);

  // The node calls with nothing to do: from now on the panel offers rendering on the server.
  expect((await node.claim()).status()).toBe(204);
  await page.reload();
  await expect(page.getByTestId('render-nodes-table')).toContainText('Waiting for work');

  // ---- a flyby round a cave, asked to be rendered on the server ----
  const entrance = await entranceToPick(page);
  await openScene3dAt(page, entrance.lat, entrance.lon);
  await page.getByTestId('scene3d-flyby').click();
  await expect(page.getByTestId('flyby-panel')).toBeVisible();
  await page.getByTestId('flyby-source-inView').check();
  await page.getByTestId('flyby-turns').fill('0.25');
  await page.getByTestId('flyby-turns').blur();
  await page.getByTestId('flyby-seconds-per-turn').fill('4');
  await page.getByTestId('flyby-seconds-per-turn').blur();
  await page.getByTestId('flyby-load-route').uncheck();
  await page.getByTestId('flyby-name').fill('Demo render');

  await page.getByTestId('flyby-movie').click();
  const panel = page.getByTestId('flyby-movie-dialog');
  await expect(panel).toBeVisible({ timeout: 30_000 });
  await panel.locator('label').filter({ has: page.getByTestId('flyby-movie-format-blender') }).click();
  const server = panel.getByTestId('flyby-render-server');
  await expect(server).toContainText('Render on the server');

  const asked = page.waitForRequest((request) => request.url().endsWith('/api/v1/flybys/blender-renders') && request.method() === 'POST', {
    timeout: 420_000,
  });
  await server.getByTestId('flyby-render-start').click();
  const body = (await asked).postDataJSON() as { bundle: { frames: number[][]; name: string }; plan: string; container: string };
  expect(body.plan).toBe('draft');
  expect(body.container).toBe('mp4');
  expect(body.bundle.name).toBe('Demo render');
  const state = server.getByTestId('flyby-render-state');
  await expect(state).toHaveAttribute('data-status', 'queued', { timeout: 120_000 });
  await expect(state).toContainText('is waiting for a render node');

  // ---- the node takes it ----
  let job: Job | null = null;
  for (let tries = 0; tries < 5 && job === null; tries += 1) {
    const claimed = await node.claim();
    expect(claimed.status(), await claimed.text()).toBe(200);
    const offered = (await claimed.json()) as Job;
    if (offered.name === 'Demo render') {
      job = offered;
    } else {
      // Somebody else's render left in the shared queue: not this test's to render.
      await json(await node.post(`jobs/${offered.jobId}/fail`, { attempt: offered.attempt, reason: 'refused', message: 'not this test’s', retryable: false }));
    }
  }
  expect(job, 'the render the panel queued was handed to the node').not.toBeNull();
  expect(job!.plan).toBe('draft');
  expect(job!.container).toBe('mp4');

  const bundle = await node.get(job!.bundle.url);
  expect(bundle.status()).toBe(200);
  const zip = await bundle.body();
  expect(zip.length).toBe(job!.bundle.size);
  expect(sha256(zip)).toBe(job!.bundle.sha256);
  // The bundle the panel asked for: its plan and its draft, under the flyby's own folder.
  expect(zip.includes(Buffer.from('demo-render-blender/scene-plan.draft.json'))).toBe(true);

  const frames = body.bundle.frames.length;
  const beat = await json<{ action: string }>(
    await node.post(`jobs/${job!.jobId}/heartbeat`, {
      attempt: job!.attempt,
      phase: 'rendering',
      message: `Rendering frame 5/${frames}`,
      frame: 5,
      frameCount: frames,
      secondsLeft: 30,
    }),
  );
  expect(beat.action).toBe('continue');
  await expect(state).toHaveAttribute('data-status', 'running', { timeout: 30_000 });
  await expect(state).toContainText(`Rendering · frame 5 of ${frames}`);

  // ---- the film comes back, and the panel offers it ----
  const film = Buffer.from(Array.from({ length: 4096 }, (_, i) => i % 251));
  const log = Buffer.from('an invented log\n');
  expect((await node.put(`jobs/${job!.jobId}/files/${job!.jobId}.mp4?attempt=${job!.attempt}`, film)).status()).toBe(200);
  expect((await node.put(`jobs/${job!.jobId}/files/${job!.jobId}.log?attempt=${job!.attempt}`, log)).status()).toBe(200);
  expect(
    (
      await node.post(`jobs/${job!.jobId}/complete`, {
        attempt: job!.attempt,
        files: [
          { name: `${job!.jobId}.mp4`, role: 'movie', size: film.length, sha256: sha256(film) },
          { name: `${job!.jobId}.log`, role: 'log', size: log.length, sha256: sha256(log) },
        ],
        device: 'CPU',
        seconds: 12,
        frameCount: frames,
        notices: [],
      })
    ).status(),
  ).toBe(200);

  await expect(state).toHaveAttribute('data-status', 'succeeded', { timeout: 30_000 });
  await expect(state).toContainText('“Demo render” is rendered.');
  const download = page.waitForEvent('download');
  await state.getByTestId('flyby-render-file-movie').click();
  const saved = await download;
  expect(saved.suggestedFilename()).toBe(`${job!.jobId}.mp4`);
  expect((await readFile(await saved.path())).equals(film)).toBe(true);
  await expect(panel.getByTestId('flyby-movie-failed')).toHaveCount(0);

  // ---- withdrawn, the node is refused ----
  await gotoRoute(page, '/admin/render-nodes');
  await page.getByTestId(`render-node-revoke-${nodeName}`).click();
  await page.locator('.ant-popconfirm').getByRole('button', { name: 'Withdraw' }).click();
  await expect(page.getByTestId('render-nodes-table')).toContainText('Withdrawn');
  expect((await node.claim()).status()).toBe(401);
});
