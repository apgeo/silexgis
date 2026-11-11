// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Plays a presentation scenario in the real application and, unless asked not to, makes a movie of
// it. Run with Node 24 from `client/`:
//
//   node presenter/record.ts presenter/scenarios/grind-3d-tour.ts --target silexgistest
//
// Modes:
//   --mode step   (default) one picture per movie frame, each taken once the 3D scene has finished
//                 drawing. Smooth on any machine, however slowly it draws; slow to make.
//   --mode live   the scenario plays in real time and the browser's own frames are kept. Needs a
//                 machine that draws the scene at the movie's frame rate (a real graphics card).
//   --mode play   the scenario plays in real time in a visible window and nothing is kept: record
//                 the screen with your own tool.
//
// Other switches: --draft (1280x720 at 8 frames a second, to check timing),
// --fps <n>, --width/--height <px>, --from <s> (start capturing at this time), --to <s>,
// --out <dir>, --headed, --app <dist> (serve a locally built client in place of the site's own,
// for trying a change before it is deployed), --keep-frames, --probe (set up, report what the
// scene holds and save one picture at --from, then stop).

import { spawnSync } from 'node:child_process';
import { mkdirSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { chromium, type BrowserContext, type CDPSession, type Page } from 'playwright';
import { build } from 'vite';
import { scenarioLength, scenarioProblems, type Scenario } from '../src/presenter/scenario.ts';

const here = path.dirname(fileURLToPath(import.meta.url));

interface Target {
  url: string;
  email: string;
  password: string;
}

/**
 * Where scenarios are played. silexgistest's accounts are its published test logins; anything
 * else takes its account from the environment.
 */
function targetFor(name: string): Target {
  if (name === 'silexgistest') {
    return { url: 'https://silexgistest.speotopo.ro', email: 'admin@test.local', password: 'test-login-pass-1' };
  }
  const url = name === 'local' ? 'http://localhost:8080' : name;
  const email = process.env.PRESENTER_EMAIL ?? process.env.SILEXGIS_ADMIN_EMAIL;
  const password = process.env.PRESENTER_PASSWORD ?? process.env.SILEXGIS_ADMIN_PASSWORD;
  if (!email || !password) {
    throw new Error('set PRESENTER_EMAIL and PRESENTER_PASSWORD for this target');
  }
  return { url, email, password };
}

interface Options {
  scenarioPath: string;
  target: string;
  mode: 'step' | 'live' | 'play';
  draft: boolean;
  fps: number;
  width: number;
  height: number;
  from: number;
  to?: number;
  out?: string;
  headed: boolean;
  app?: string;
  keepFrames: boolean;
  probe: boolean;
}

function parseArgs(argv: string[]): Options {
  const options: Options = {
    scenarioPath: '',
    target: 'silexgistest',
    mode: 'step',
    draft: false,
    fps: 30,
    width: 1920,
    height: 1080,
    from: 0,
    headed: false,
    keepFrames: false,
    probe: false,
  };
  let fpsGiven = false;
  let sizeGiven = false;
  for (let i = 0; i < argv.length; i += 1) {
    const arg = argv[i];
    const value = () => {
      const next = argv[i + 1];
      if (next === undefined) {
        throw new Error(`${arg} needs a value`);
      }
      i += 1;
      return next;
    };
    switch (arg) {
      case '--target': options.target = value(); break;
      case '--mode': options.mode = value() as Options['mode']; break;
      case '--draft': options.draft = true; break;
      case '--fps': options.fps = Number(value()); fpsGiven = true; break;
      case '--width': options.width = Number(value()); sizeGiven = true; break;
      case '--height': options.height = Number(value()); sizeGiven = true; break;
      case '--from': options.from = Number(value()); break;
      case '--to': options.to = Number(value()); break;
      case '--out': options.out = value(); break;
      case '--headed': options.headed = true; break;
      case '--app': options.app = path.resolve(value()); break;
      case '--keep-frames': options.keepFrames = true; break;
      case '--probe': options.probe = true; break;
      default:
        if (arg.startsWith('--') || options.scenarioPath) {
          throw new Error(`unknown argument ${arg}`);
        }
        options.scenarioPath = path.resolve(arg);
    }
  }
  if (!options.scenarioPath) {
    throw new Error('name a scenario file');
  }
  if (!['step', 'live', 'play'].includes(options.mode)) {
    throw new Error('--mode is step, live or play');
  }
  if (options.draft) {
    // Fewer frames and fewer pixels to draw each: a quarter of the time or less, same layout.
    if (!fpsGiven) {
      options.fps = 8;
    }
    if (!sizeGiven) {
      options.width = 1280;
      options.height = 720;
    }
  }
  if (options.mode === 'play') {
    options.headed = true;
  }
  return options;
}

async function loadScenario(file: string): Promise<Scenario> {
  if (file.endsWith('.json')) {
    return JSON.parse(readFileSync(file, 'utf8')) as Scenario;
  }
  const module = (await import(pathToFileURL(file).href)) as { default: Scenario };
  return module.default;
}

/** The page-side runner, bundled into one script that is put into the page. */
async function runnerScript(): Promise<string> {
  const result = await build({
    configFile: false,
    logLevel: 'warn',
    build: {
      write: false,
      minify: false,
      lib: { entry: path.join(here, '..', 'src', 'presenter', 'entry.ts'), formats: ['iife'], name: 'silexgisPresenter' },
    },
  });
  const outputs = Array.isArray(result) ? result : [result];
  for (const output of outputs) {
    if ('output' in output) {
      const chunk = output.output.find((item) => item.type === 'chunk');
      if (chunk && chunk.type === 'chunk') {
        return chunk.code;
      }
    }
  }
  throw new Error('the presentation runner could not be bundled');
}

const CHOICE_KEY = 'silexgis.languageChosen';

/**
 * Serves a locally built client in place of the site's own scripts, leaving everything else —
 * pages, API, sign-in, files — to the site. Only for trying a client change that is not deployed.
 *
 * Swapped at the level of scripts and styles rather than pages because signing in arrives through
 * a redirect, and a page reached by a redirect is not offered for interception; the scripts it
 * asks for are. The site's page names its own entry script and style sheet, so those two names are
 * answered with the local build's, and the local build's other files are served by their names.
 */
async function serveLocalApp(context: BrowserContext, base: string, dist: string): Promise<void> {
  const assets = path.join(dist, 'assets');
  const local = readdirSync(assets);
  const entryScript = local.find((name) => /^index-.*\.js$/.test(name));
  const entryStyle = local.find((name) => /^index-.*\.css$/.test(name));
  await context.route(`${base}/assets/**`, async (route) => {
    const name = decodeURIComponent(new URL(route.request().url()).pathname.replace(/^\/assets\//, ''));
    let file = path.join(assets, name);
    if (!local.includes(name)) {
      if (/^index-.*\.js$/.test(name) && entryScript) {
        file = path.join(assets, entryScript);
      } else if (/^index-.*\.css$/.test(name) && entryStyle) {
        file = path.join(assets, entryStyle);
      } else {
        return route.continue();
      }
    }
    return route.fulfill({ path: file });
  });
}

async function signIn(page: Page, target: Target): Promise<void> {
  await page.goto(`${target.url}/`);
  await page.waitForURL(/\/login\?returnUrl=/, { timeout: 90_000 });
  await page.locator('input[type="email"], input#email, input[name="email"]').first().fill(target.email);
  await page.locator('input[type="password"]').first().fill(target.password);
  await page.locator('button[type="submit"]').first().click();
  await page.locator('.ol-viewport').first().waitFor({ timeout: 90_000 });
}

/** The bearer token the application itself sends, picked up from its own requests. */
function watchToken(page: Page): () => string | undefined {
  let token: string | undefined;
  page.on('request', (request) => {
    const header = request.headers().authorization;
    if (header && request.url().includes('/api/')) {
      token = header;
    }
  });
  return () => token;
}

async function setUp(page: Page, target: Target, scenario: Scenario, token: () => string | undefined) {
  const setup = scenario.setup ?? {};
  if (setup.cave) {
    const auth = token();
    if (!auth) {
      throw new Error('signed in, but no API request carried a token to look the cave up with');
    }
    const response = await page.request.get(
      `${target.url}/api/v1/caves?search=${encodeURIComponent(setup.cave)}&pageSize=20`,
      { headers: { authorization: auth } },
    );
    const body = (await response.json()) as { items?: { id: string; name: string }[] };
    const caves = body.items ?? [];
    // Several caves can share a word of a name; the one meant is the one with walls to show, and
    // among those an exact name beats a partial one.
    const withWalls: { id: string; name: string; exact: boolean }[] = [];
    for (const candidate of caves) {
      const models = await page.request.get(`${target.url}/api/v1/caves/${candidate.id}/survey-models`, {
        headers: { authorization: auth },
      });
      const list = (await models.json()) as { meshUrl?: string | null }[] | { items?: { meshUrl?: string | null }[] };
      const rows = Array.isArray(list) ? list : (list.items ?? []);
      const walls = rows.some((row) => Boolean(row.meshUrl));
      console.log(`setup: cave "${candidate.name}" ${walls ? 'has' : 'has no'} walls`);
      if (walls) {
        withWalls.push({ ...candidate, exact: candidate.name.toLowerCase() === setup.cave.toLowerCase() });
      }
    }
    const cave = withWalls.find((c) => c.exact) ?? withWalls[0];
    if (!cave) {
      throw new Error(`no cave matching "${setup.cave}" has walls to show (${caves.length} matched by name)`);
    }
    await page.goto(`${target.url}/caves/${cave.id}`);
    // The 3D button sits on the cave page's header with the cube icon; found by the icon, so the
    // interface language does not matter.
    const open3d = page.locator('button:has(.anticon-code-sandbox)').first();
    await open3d.waitFor({ timeout: 90_000 });
    await open3d.click();
    await page.waitForURL(/\/map3d/, { timeout: 60_000 });
  } else {
    await page.goto(`${target.url}/map3d`);
  }
  await page.getByTestId('scene3d-container').locator('canvas').first().waitFor({ state: 'attached', timeout: 90_000 });
  // A large model is offered before it is fetched; the recording always takes it.
  const ask = page.getByTestId('scene3d-mesh-ask-download');
  const deadline = Date.now() + 120_000;
  for (;;) {
    if (await ask.isVisible().catch(() => false)) {
      await ask.click();
    }
    const drawn = await page.evaluate(() => Boolean(window.__silexgisPresenter?.scene3d?.modelSphere()));
    if (drawn) {
      break;
    }
    if (Date.now() > deadline) {
      const has = await page.evaluate(() => Boolean(window.__silexgisPresenter?.scene3d));
      throw new Error(
        has
          ? 'the cave model was not drawn within two minutes (is a wall model uploaded and converted?)'
          : 'this build of the site has no recording handle; deploy it, or pass --app with a local build',
      );
    }
    await page.waitForTimeout(500);
  }
  if (setup.hide?.length) {
    await page.addStyleTag({ content: `${setup.hide.join(', ')} { display: none !important; }` });
  }
  if (setup.basemap || setup.layersOff?.length) {
    const trigger = page.getByTestId('scene3d-layers-trigger');
    const panel = page.getByTestId('scene3d-layer-panel');
    await trigger.click();
    await panel.waitFor();
    for (const label of setup.layersOff ?? []) {
      const row = panel.locator('.ant-checkbox-wrapper', { hasText: label }).first();
      if (await row.evaluate((node) => node.classList.contains('ant-checkbox-wrapper-checked'))) {
        await row.click();
      }
    }
    await trigger.click();
    await panel.waitFor({ state: 'hidden' });
  }
  if (setup.basemap) {
    const trigger = page.getByTestId('scene3d-layers-trigger');
    await trigger.click();
    const panel = page.getByTestId('scene3d-layer-panel');
    await panel.waitFor();
    const radio = panel.locator('.ant-radio-wrapper', { hasText: setup.basemap.name }).first();
    if (!(await radio.isVisible().catch(() => false)) && setup.basemap.group) {
      await panel.locator('.layer-group-collapse .ant-collapse-header', { hasText: `${setup.basemap.group} (` }).click();
    }
    await radio.click();
    await trigger.click();
    await panel.waitFor({ state: 'hidden' });
  }
}

function hasCommand(command: string): boolean {
  return spawnSync(command, ['-version'], { stdio: 'ignore' }).status === 0;
}

/** ffmpeg from the PATH, or from a container where none is installed. */
function ffmpeg(args: string[], workDir: string): void {
  const local = hasCommand('ffmpeg');
  const command = local ? 'ffmpeg' : 'docker';
  const full = local
    ? args
    : [
        'run', '--rm',
        ...(process.getuid ? ['--user', `${process.getuid()}:${process.getgid!()}`] : []),
        '-v', `${workDir}:/w`, '-w', '/w',
        // Straight to ffmpeg: the image's own start-up script re-creates a user and fails when the
        // container is not run as root.
        '--entrypoint', 'ffmpeg',
        'linuxserver/ffmpeg', ...args,
      ];
  const result = spawnSync(command, full, { cwd: workDir, stdio: ['ignore', 'inherit', 'inherit'] });
  if (result.status !== 0) {
    throw new Error(`ffmpeg failed (${result.status})`);
  }
}

function encodeFrames(runDir: string, fps: number): string {
  ffmpeg(
    ['-hide_banner', '-loglevel', 'error', '-y', '-framerate', String(fps), '-i', 'frames/%06d.jpg',
      '-c:v', 'libx264', '-preset', 'slow', '-crf', '18', '-pix_fmt', 'yuv420p', '-movflags', '+faststart',
      'movie.mp4'],
    runDir,
  );
  return path.join(runDir, 'movie.mp4');
}

function encodeTimedFrames(runDir: string, frames: { file: string; time: number }[], fps: number): string {
  const lines: string[] = [];
  frames.forEach((frame, index) => {
    const next = frames[index + 1]?.time ?? frame.time + 1 / fps;
    lines.push(`file '${frame.file}'`, `duration ${Math.max(0.001, next - frame.time).toFixed(4)}`);
  });
  lines.push(`file '${frames[frames.length - 1].file}'`);
  writeFileSync(path.join(runDir, 'frames.txt'), `${lines.join('\n')}\n`);
  ffmpeg(
    ['-hide_banner', '-loglevel', 'error', '-y', '-f', 'concat', '-safe', '0', '-i', 'frames.txt',
      '-vf', `fps=${fps},scale=trunc(iw/2)*2:trunc(ih/2)*2`, '-c:v', 'libx264', '-preset', 'slow', '-crf', '18',
      '-pix_fmt', 'yuv420p', '-movflags', '+faststart', 'movie.mp4'],
    runDir,
  );
  return path.join(runDir, 'movie.mp4');
}

function stamp(): string {
  return new Date().toISOString().replace(/[:.]/g, '-').slice(0, 19);
}

function clock(seconds: number): string {
  const s = Math.round(seconds);
  return `${Math.floor(s / 3600)}h${String(Math.floor((s % 3600) / 60)).padStart(2, '0')}m${String(s % 60).padStart(2, '0')}s`;
}

async function main() {
  const options = parseArgs(process.argv.slice(2));
  const scenario = await loadScenario(options.scenarioPath);
  const problems = scenarioProblems(scenario);
  if (problems.length > 0) {
    throw new Error(`the scenario cannot be played:\n- ${problems.join('\n- ')}`);
  }
  const length = scenarioLength(scenario);
  const end = Math.min(length, options.to ?? length);
  const target = targetFor(options.target);
  const runDir = path.resolve(
    options.out ?? path.join(process.env.PRESENTER_OUT ?? path.join(process.cwd(), 'presenter-output'), scenario.name, stamp()),
  );
  if (options.mode !== 'play') {
    mkdirSync(path.join(runDir, 'frames'), { recursive: true });
  }

  const script = await runnerScript();
  const browser = await chromium.launch({
    headless: !options.headed,
    // Software drawing where there is no graphics card; ignored where there is one.
    args: ['--use-gl=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist', '--hide-scrollbars'],
  });
  const context = await browser.newContext({
    viewport: { width: options.width, height: options.height },
    locale: scenario.language === 'ro' ? 'ro-RO' : 'en-US',
  });
  await context.addInitScript(
    ([key, language]) => {
      try {
        window.localStorage.setItem(key, language);
      } catch {
        // Blank and sandboxed documents have no storage.
      }
    },
    [CHOICE_KEY, scenario.language ?? 'en'] as const,
  );
  if (options.app) {
    await serveLocalApp(context, target.url, options.app);
  }
  const page = await context.newPage();
  page.on('pageerror', (error) => console.log(`page error: ${error.message}`));
  const token = watchToken(page);

  const started = Date.now();
  await signIn(page, target);
  await setUp(page, target, scenario, token);
  await page.addScriptTag({ content: script });
  await page.evaluate(
    async ([data, mode]) => {
      const api = window.__silexgisPresenterRunner!;
      const runner = api.createRunner(data, { mode });
      (window as unknown as { __presenterRun: unknown }).__presenterRun = runner;
      await runner.prepare();
    },
    [scenario, options.mode === 'step' ? 'step' : 'live'] as const,
  );
  console.log(`ready after ${clock((Date.now() - started) / 1000)}; scenario ${length.toFixed(1)} s, mode ${options.mode}`);

  if (options.probe) {
    mkdirSync(runDir, { recursive: true });
    const facts = await page.evaluate(async (time) => {
      const bridge = window.__silexgisPresenter!.scene3d!;
      const sphere = bridge.modelSphere()!;
      const run = (window as unknown as { __presenterRun: { seek(t: number): void; settle(ms: number): Promise<boolean> } }).__presenterRun;
      for (let t = 0; t <= time; t += 0.25) {
        run.seek(t);
        await new Promise((resolve) => requestAnimationFrame(resolve));
        await new Promise((resolve) => requestAnimationFrame(resolve));
      }
      const settled = await run.settle(60_000);
      const popovers = Array.from(document.querySelectorAll('.ant-popover')).map((node) => `${node.className} ${JSON.stringify(node.getBoundingClientRect())}`);
      if (!document.querySelector('[data-testid="scene3d-layer-panel"]')) {
        document.querySelector<HTMLElement>('[data-testid="scene3d-layers-trigger"]')?.click();
        await new Promise((resolve) => setTimeout(resolve, 1500));
      }
      const panel = document.querySelector('[data-testid="scene3d-layer-panel"]');
      return {
        sphereRadius: Math.round(sphere.radius),
        sphereCentreBelowGround: Math.round(bridge.groundHeight(sphere.longitude, sphere.latitude) - sphere.height),
        cameraAboveGround: Math.round(bridge.getCamera().height - bridge.groundHeight(bridge.getCamera().longitude, bridge.getCamera().latitude)),
        settled,
        popovers,
        panelOpen: Boolean(panel),
        layerRows: panel ? Array.from(panel.querySelectorAll('.ant-checkbox-wrapper')).map((row) => row.textContent?.trim()) : [],
        meshStatus: document.querySelector('[data-testid="scene3d-mesh-status"]')?.textContent ?? null,
      };
    }, options.from);
    console.log(JSON.stringify(facts, null, 2));
    await page.screenshot({ path: path.join(runDir, 'probe.jpg'), quality: 85, type: 'jpeg' });
    console.log(`picture: ${path.join(runDir, 'probe.jpg')}`);
    await browser.close();
    return;
  }

  const report: Record<string, unknown> = {
    scenario: scenario.name,
    target: target.url,
    mode: options.mode,
    fps: options.fps,
    size: `${options.width}x${options.height}${options.draft ? ' (draft)' : ''}`,
    from: options.from,
    to: end,
  };

  if (options.mode === 'step') {
    const cdp: CDPSession = await context.newCDPSession(page);
    const first = Math.round(options.from * options.fps);
    const last = Math.floor(end * options.fps);
    // Up to the first captured frame the scenario is still played — clicks have to happen — but
    // nothing waits for the scene to draw.
    for (let t = 0; t < options.from; t += 0.25) {
      await page.evaluate(async (time) => {
        (window as unknown as { __presenterRun: { seek(t: number): void } }).__presenterRun.seek(time);
        // Two drawn frames, so a popover or a group of rows opened by this step gets laid out.
        for (let i = 0; i < 2; i += 1) {
          await new Promise((resolve) => requestAnimationFrame(resolve));
        }
      }, t);
    }
    let unsettled = 0;
    const loopStart = Date.now();
    for (let frame = first, n = 1; frame <= last; frame += 1, n += 1) {
      const t = frame / options.fps;
      const before = Date.now();
      const settled = await page.evaluate(async (time) => {
        const run = (window as unknown as { __presenterRun: { seek(t: number): void; settle(ms: number): Promise<boolean> } }).__presenterRun;
        run.seek(time);
        return run.settle(60_000);
      }, t);
      const settledAfter = Date.now() - before;
      if (!settled) {
        unsettled += 1;
      }
      const shot = await cdp.send('Page.captureScreenshot', { format: 'jpeg', quality: options.draft ? 80 : 94 });
      writeFileSync(path.join(runDir, 'frames', `${String(n).padStart(6, '0')}.jpg`), Buffer.from(shot.data, 'base64'));
      if (process.env.PRESENTER_DEBUG) {
        console.log(`frame ${n}: settle ${settledAfter} ms, picture ${Date.now() - before - settledAfter} ms`);
      }
      if (n % 30 === 0 || frame === last) {
        const spent = (Date.now() - loopStart) / 1000;
        const total = last - first + 1;
        console.log(
          `frame ${n}/${total} (t=${t.toFixed(2)} s) ${clock(spent)} spent, ~${clock((spent / n) * (total - n))} left, ${unsettled} unsettled`,
        );
      }
    }
    report.frames = last - first + 1;
    report.unsettledFrames = unsettled;
    report.captureSeconds = Math.round((Date.now() - loopStart) / 1000);
    const movie = encodeFrames(runDir, options.fps);
    report.movie = movie;
    console.log(`movie: ${movie}`);
  } else if (options.mode === 'live') {
    const cdp = await context.newCDPSession(page);
    const frames: { file: string; time: number }[] = [];
    cdp.on('Page.screencastFrame', (event) => {
      const file = `frames/${String(frames.length + 1).padStart(6, '0')}.jpg`;
      writeFileSync(path.join(runDir, file), Buffer.from(event.data, 'base64'));
      frames.push({ file, time: event.metadata.timestamp ?? Date.now() / 1000 });
      void cdp.send('Page.screencastFrameAck', { sessionId: event.sessionId }).catch(() => undefined);
    });
    await cdp.send('Page.startScreencast', { format: 'jpeg', quality: 92, everyNthFrame: 1 });
    await page.evaluate(() => (window as unknown as { __presenterRun: { play(): Promise<void> } }).__presenterRun.play());
    await cdp.send('Page.stopScreencast');
    report.frames = frames.length;
    report.measuredFps = frames.length / Math.max(0.001, frames[frames.length - 1].time - frames[0].time);
    console.log(`kept ${frames.length} frames (${(report.measuredFps as number).toFixed(1)} a second)`);
    report.movie = encodeTimedFrames(runDir, frames, options.fps);
    console.log(`movie: ${report.movie}`);
  } else {
    console.log('playing in the window now; record the screen with your own tool');
    await page.evaluate(() => (window as unknown as { __presenterRun: { play(): Promise<void> } }).__presenterRun.play());
  }

  report.totalSeconds = Math.round((Date.now() - started) / 1000);
  if (options.mode !== 'play') {
    writeFileSync(path.join(runDir, 'run.json'), `${JSON.stringify(report, null, 2)}\n`);
    if (!options.keepFrames) {
      rmSync(path.join(runDir, 'frames'), { recursive: true, force: true });
    }
  }
  await browser.close();
}

main().catch((error: unknown) => {
  console.error(error instanceof Error ? error.message : error);
  process.exit(1);
});
