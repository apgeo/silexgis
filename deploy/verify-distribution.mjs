// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Build, run and smoke-test the production Docker distribution (deploy/docker-compose.yml).
// Cross-platform (Node 18+, no shell-isms): `node deploy/verify-distribution.mjs`.
//
// Flags:
//   --no-build   skip the image build, just (re)start and verify what is already built
//   --down       stop and remove the stack after a successful verify (default: leave it up)
//   --no-cache   build without the layer cache (a clean-room build)
//
// Exit code 0 = the web front answered, the API health endpoint answered, every module script
// in the build is served as JavaScript, and the proxy in front of the API accepts a body as
// large as the configured upload limit while still refusing one that is genuinely too large;
// non-zero otherwise.

import { execSync } from 'node:child_process';
import { existsSync, readFileSync } from 'node:fs';
import { connect } from 'node:net';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const deployDir = dirname(fileURLToPath(import.meta.url));
const args = new Set(process.argv.slice(2));
const compose = 'docker compose -f docker-compose.yml';

function run(cmd) {
  console.log(`\n$ ${cmd}`);
  execSync(cmd, { cwd: deployDir, stdio: 'inherit' });
}

/** A numeric setting from .env, or the compose file's own default for it. */
function envNumber(name, fallback) {
  const envPath = join(deployDir, '.env');
  if (existsSync(envPath)) {
    const match = readFileSync(envPath, 'utf8').match(new RegExp(`^\\s*${name}\\s*=\\s*(\\d+)`, 'm'));
    if (match) return Number(match[1]);
  }
  return fallback;
}

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

/** Polls a URL until it answers with the expected status, or the timeout elapses. */
async function waitFor(url, { expect = 200, timeoutMs = 180_000, label } = {}) {
  const deadline = Date.now() + timeoutMs;
  process.stdout.write(`Waiting for ${label ?? url} `);
  for (;;) {
    try {
      const res = await fetch(url, { redirect: 'manual' });
      if (res.status === expect) {
        console.log(' ok');
        return;
      }
    } catch {
      // container not accepting connections yet
    }
    if (Date.now() > deadline) {
      console.log(' timeout');
      throw new Error(`${label ?? url} did not become ready within ${timeoutMs / 1000}s`);
    }
    process.stdout.write('.');
    await sleep(2000);
  }
}

/**
 * Sends an upload request that announces a body of `declaredBytes` and returns the first
 * status line that comes back.
 *
 * Only the announced length is sent, not the bytes: a proxy decides whether to accept a body
 * from the declared length, before a byte of it has been read, so transferring half a gigabyte
 * would prove nothing extra and take minutes. Written against a raw socket for the same
 * reason — an HTTP client insists on finishing the body it promised, and here the point is to
 * stop after the headers and read whatever answers.
 */
function announceUpload(port, declaredBytes, timeoutMs = 20_000) {
  return new Promise((resolve, reject) => {
    const socket = connect(port, '127.0.0.1');
    let received = '';
    const finish = (value) => {
      socket.destroy();
      resolve(value);
    };

    socket.setTimeout(timeoutMs, () => finish('no answer'));
    socket.on('error', (err) => {
      socket.destroy();
      reject(err);
    });
    socket.on('connect', () => {
      socket.write(
        `POST /api/v1/files/ HTTP/1.1\r\nHost: localhost:${port}\r\n` +
          `Content-Type: application/octet-stream\r\nContent-Length: ${declaredBytes}\r\n` +
          'Connection: close\r\n\r\n',
      );
      socket.write(Buffer.alloc(1024));
    });
    socket.on('data', (chunk) => {
      received += chunk.toString('latin1');
      if (received.includes('\r\n')) finish(received.split('\r\n')[0]);
    });
    socket.on('close', () => finish(received.split('\r\n')[0] || 'no answer'));
  });
}

/**
 * Checks that the proxy in front of the API accepts a body as large as the configured upload
 * limit, and would visibly refuse one that is genuinely too large.
 *
 * This is the limit that is easiest to get wrong and hardest to diagnose: the application's
 * upload cap means nothing if the proxy refuses the body first, and when that happens the
 * caller gets the proxy's own error with nothing in the application log to explain it. The
 * request needs no credentials — whatever the API answers, it answered, which is the whole
 * claim. The oversized second probe is there so a pass means something: without it, a proxy
 * that accepted everything and a check that could not detect a refusal look identical.
 */
async function checkProxyBodyLimit(port, maxUploadBytes) {
  const megabytes = (bytes) => Math.round(bytes / (1024 * 1024));

  process.stdout.write(`Announcing a ${megabytes(maxUploadBytes)} MB upload through the proxy `);
  const allowed = await announceUpload(port, maxUploadBytes);
  if (allowed.includes(' 413')) {
    console.log('refused');
    throw new Error(
      `the proxy refused a ${megabytes(maxUploadBytes)} MB body — raise client_max_body_size in ` +
        'client/nginx.conf above SILEXGIS__Files__MaxUploadBytes and rebuild the web image',
    );
  }
  console.log(`ok (${allowed})`);

  // Four times the proxy's own 1 GB ceiling: large enough that any sane configuration says no.
  const oversized = 4 * 1024 * 1024 * 1024;
  process.stdout.write(`Announcing a ${megabytes(oversized)} MB upload (must be refused) `);
  const refused = await announceUpload(port, oversized);
  if (!refused.includes(' 413')) {
    console.log('accepted');
    throw new Error(
      `the proxy did not refuse a ${megabytes(oversized)} MB body (${refused}) — the body-size ` +
        'check cannot detect a misconfigured proxy, so the previous result proves nothing',
    );
  }
  console.log('ok (refused)');
}

/**
 * Every module script in the build is served as JavaScript.
 *
 * Strict MIME checking applies to module scripts and to nothing else: a browser refuses one
 * whose declared type is not a JavaScript type, however valid the bytes are. The web server's
 * bundled type map does not cover every extension a bundler emits, so a module can go out as
 * `application/octet-stream` — and the failure appears **only in this image**, because the
 * development server declares types for itself. Nothing shows up in a network log but a 200,
 * and whatever the module was for silently never starts.
 *
 * The file list is read out of the running container rather than guessed, so a bundler that
 * starts emitting a different extension is covered the day it does.
 */
async function checkModuleScriptTypes(port) {
  process.stdout.write('Checking module scripts are served as JavaScript ');
  const listed = execSync(
    `${compose} exec -T web sh -c "find /usr/share/nginx/html -name '*.mjs' -type f"`,
    { cwd: deployDir, encoding: 'utf8' },
  );
  const paths = listed
    .split(/\r?\n/)
    .map((line) => line.trim().replace('/usr/share/nginx/html', ''))
    .filter(Boolean);

  if (paths.length === 0) {
    console.log('ok (the build emits none)');
    return;
  }

  for (const path of paths) {
    const res = await fetch(`http://localhost:${port}${path}`);
    const type = (res.headers.get('content-type') ?? '').split(';')[0].trim().toLowerCase();
    if (!['text/javascript', 'application/javascript'].includes(type)) {
      console.log('failed');
      throw new Error(`${path} is served as "${type || 'nothing'}"; a browser will refuse it as a module`);
    }
  }
  console.log(`ok (${paths.length})`);
}

async function main() {
  if (!existsSync(join(deployDir, '.env'))) {
    console.error('deploy/.env is missing — copy .env.example to .env and set the passwords first.');
    process.exit(2);
  }

  if (!args.has('--no-build')) {
    run(`${compose} build${args.has('--no-cache') ? ' --no-cache' : ''}`);
  }
  run(`${compose} up -d`);

  // The host web port and the upload cap both come from .env; the defaults here are the ones
  // docker-compose.yml applies when .env leaves them out.
  const port = envNumber('SILEXGIS_HTTP_PORT', 8080);
  const maxUploadBytes = envNumber('SILEXGIS_MAX_UPLOAD_BYTES', 512 * 1024 * 1024);

  // The API applies migrations on first boot, so the health endpoint can lag the container.
  await waitFor(`http://localhost:${port}/health/ready`, { label: 'API (/health/ready via web proxy)' });
  await waitFor(`http://localhost:${port}/`, { label: 'web front (SPA index)' });
  await checkModuleScriptTypes(port);
  await checkProxyBodyLimit(port, maxUploadBytes);

  console.log(`\n✅ Distribution is up and healthy at http://localhost:${port}`);

  if (args.has('--down')) {
    run(`${compose} down`);
    console.log('Stack stopped (--down).');
  }
}

main().catch((err) => {
  console.error(`\n❌ ${err.message}`);
  console.error('Inspect logs with:  docker compose -f deploy/docker-compose.yml logs --tail=50');
  process.exit(1);
});
