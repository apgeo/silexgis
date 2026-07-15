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
// Exit code 0 = the web front and the API health endpoint both answered; non-zero otherwise.

import { execSync } from 'node:child_process';
import { existsSync, readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const deployDir = dirname(fileURLToPath(import.meta.url));
const args = new Set(process.argv.slice(2));
const compose = 'docker compose -f docker-compose.yml';

function run(cmd) {
  console.log(`\n$ ${cmd}`);
  execSync(cmd, { cwd: deployDir, stdio: 'inherit' });
}

function envPort() {
  // The host web port comes from .env (SILEXGIS_HTTP_PORT), default 8080.
  const envPath = join(deployDir, '.env');
  if (existsSync(envPath)) {
    const match = readFileSync(envPath, 'utf8').match(/^\s*SILEXGIS_HTTP_PORT\s*=\s*(\d+)/m);
    if (match) return Number(match[1]);
  }
  return 8080;
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

async function main() {
  if (!existsSync(join(deployDir, '.env'))) {
    console.error('deploy/.env is missing — copy .env.example to .env and set the passwords first.');
    process.exit(2);
  }

  if (!args.has('--no-build')) {
    run(`${compose} build${args.has('--no-cache') ? ' --no-cache' : ''}`);
  }
  run(`${compose} up -d`);

  const port = envPort();
  // The API applies migrations on first boot, so the health endpoint can lag the container.
  await waitFor(`http://localhost:${port}/health/ready`, { label: 'API (/health/ready via web proxy)' });
  await waitFor(`http://localhost:${port}/`, { label: 'web front (SPA index)' });

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
