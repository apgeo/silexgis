// SPDX-License-Identifier: AGPL-3.0-or-later
//
// The survey compiler is three files that have to agree with each other and with the application:
// the overlay that adds it, its image, and the settings the application reads. Nothing fails loudly
// when they drift — a port named differently on the two sides is every upload ending "compiler not
// available", and a dropped hardening line is a compiler that runs over strangers' files with more
// reach than it was given. Both are silences, so what they depend on is pinned here.
//
// Run with the rest: `node --test "deploy/**/*.test.mjs"`.
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { describe, it } from 'node:test';
import { fileURLToPath } from 'node:url';

const deployDir = dirname(fileURLToPath(import.meta.url));
const repoDir = join(deployDir, '..');
const read = (...parts) => readFileSync(join(repoDir, ...parts), 'utf8');

const overlay = read('deploy', 'docker-compose.therion.yml');
const image = read('deploy', 'therion', 'Dockerfile');
const envExample = read('deploy', '.env.example');
const solution = read('server', 'SilexGis.slnx');

/** The block of a top-level compose service, up to the next service or top-level key. */
function service(compose, name) {
  const start = compose.indexOf(`\n  ${name}:\n`);
  assert.notEqual(start, -1, `service ${name} is defined`);
  const rest = compose.slice(start + 1);
  const end = rest.slice(1).search(/\n(?: {2}[a-z][a-z0-9-]*:\n|[a-z])/);
  return end === -1 ? rest : rest.slice(0, end + 1);
}

describe('the compiler overlay', () => {
  const therion = service(overlay, 'therion');
  const api = service(overlay, 'api');

  it('tells the application where the compiler listens, at the port the image listens on', () => {
    assert.match(api, /SILEXGIS__SurveyCompiler__Url: http:\/\/therion:3100/);
    assert.match(image, /ASPNETCORE_HTTP_PORTS=3100/);
  });

  it('refuses to start beside an application that has not been told to use it', () => {
    assert.match(api, /SILEXGIS__SurveyCompiler__Mode: \$\{SILEXGIS__SurveyCompiler__Mode:\?/);
  });

  it('publishes no port, and joins only a network with no way out', () => {
    // The compiler runs over files somebody uploaded; reachable from outside, it is a Therion and a
    // TeX for strangers to run, and with a route out it is a way for a survey to send what it read.
    assert.doesNotMatch(therion, /\n {4}ports:/);
    assert.match(therion, /\n {4}networks:\n {6}- therion\n/);
    assert.match(overlay, /\nnetworks:\n(?: {2}#[^\n]*\n)* {2}therion:\n {4}internal: true\n/);
  });

  it('keeps the application on its own network as well as the compiler’s', () => {
    // Naming any network for a service takes it off the default one unless that is named too, and
    // the default network is where the database is.
    assert.match(api, /\n {4}networks:\n {6}- default\n {6}- therion\n/);
  });

  it('runs read-only, without capabilities or new privileges, and within limits', () => {
    assert.match(therion, /\n {4}read_only: true\n/);
    assert.match(therion, /\n {4}cap_drop:\n {6}- ALL\n/);
    assert.match(therion, /no-new-privileges:true/);
    assert.match(therion, /\n {4}mem_limit: /);
    assert.match(therion, /\n {4}cpus: /);
    assert.match(therion, /\n {4}pids_limit: \d+\n/);
  });

  it('weakens none of Docker’s own confinement to make room for an inner sandbox', () => {
    // Profiles that would let a sandbox run inside (no seccomp filter, no AppArmor, an unmasked
    // /proc) loosen the container's boundary towards the host; the container is the sandbox here.
    assert.doesNotMatch(overlay, /seccomp[:=]unconfined|apparmor[:=]unconfined|systempaths|privileged:/);
  });

  it('makes its runs on a volume the image prepared for its own user', () => {
    assert.match(therion, /- silexgis-therion-work:\/work\n/);
    assert.match(overlay, /\nvolumes:\n {2}silexgis-therion-work:/);
    assert.match(image, /SILEXGIS__SurveyCompiler__WorkDir=\/work/);
    assert.match(image, /mkdir -p \/work && chown therion:therion \/work/);
    assert.match(image, /\nUSER therion\n/);
  });

  it('builds the image from the server sources, with the Dockerfile kept here', () => {
    assert.match(therion, /context: \.\.\/server\n/);
    assert.match(therion, /dockerfile: \.\.\/deploy\/therion\/Dockerfile\n/);
  });
});

describe('the compiler image', () => {
  it('publishes the service project the solution lists', () => {
    assert.match(image, /dotnet publish src\/SilexGis\.TherionService /);
    assert.match(solution, /src\/SilexGis\.TherionService\/SilexGis\.TherionService\.csproj/);
    assert.match(image, /ENTRYPOINT \["dotnet", "SilexGis\.TherionService\.dll"\]/);
  });

  it('carries Therion and the programs it draws with, from a pinned release', () => {
    assert.match(image, /FROM ubuntu:26\.04 AS compiler/);
    for (const pkg of ['therion', 'texlive-base', 'texlive-metapost', 'util-linux', 'coreutils', 'aspnetcore-runtime-10.0']) {
      assert.match(image, new RegExp(`\\b${pkg.replace(/[.]/g, '\\.')}\\b`), `installs ${pkg}`);
    }
    assert.match(image, /--no-install-recommends/);
  });

  it('runs without bubblewrap only because it says so, and installs none to be found', () => {
    // A bubblewrap present but unable to make namespaces under Docker's default profile would be
    // found by the runner and fail every compile; its absence is explicit, and so is the setting
    // that lets the runner start Therion with only the limits around it.
    assert.match(image, /SILEXGIS__SurveyCompiler__AllowUnsandboxed=true/);
    assert.doesNotMatch(image.split('\n').filter((l) => !l.trimStart().startsWith('#')).join('\n'), /bubblewrap/);
  });
});

describe('the application’s settings for it', () => {
  it('are offered in the sample environment file', () => {
    assert.match(envExample, /# SILEXGIS__SurveyCompiler__Mode=none/);
    assert.match(envExample, /# SILEXGIS__SurveyCompiler__Url=http:\/\/therion:3100/);
    assert.match(envExample, /# SILEXGIS__SurveyCompiler__MaxOutputBytes=\d+/);
  });

  it('include the compiler service’s own limits, which the overlay passes through to it', () => {
    // The service reads these from its own environment; set in .env and not passed through, they
    // would do nothing, and nobody would notice.
    const therion = service(overlay, 'therion');
    for (const name of ['MaxConcurrentCompiles', 'MaxProjectBytes']) {
      assert.match(envExample, new RegExp(`# SILEXGIS__TherionService__${name}=\\d+`), `sample offers ${name}`);
      assert.match(
        therion,
        new RegExp(`SILEXGIS__TherionService__${name}: \\$\\{SILEXGIS__TherionService__${name}:-\\d+\\}`),
        `overlay passes ${name}`);
    }
  });
});
