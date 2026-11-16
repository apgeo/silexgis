// SPDX-License-Identifier: AGPL-3.0-or-later
//
// The analysis service is three files that have to agree with each other and with the application:
// the overlay that adds it, its image, and the settings the application reads. Nothing fails loudly
// when they drift — a port named differently on the two sides is every analysis ending "not
// available", a dropped hardening line is a third-party program running with more reach than it was
// given, and an unpinned download is whatever the address serves that day. All three are
// silences, so what they depend on is pinned here.
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

const overlay = read('deploy', 'docker-compose.whitebox.yml');
const image = read('deploy', 'whitebox', 'Dockerfile');
const envExample = read('deploy', '.env.example');
const solution = read('server', 'SilexGis.slnx');
const imageCommands = image.split('\n').filter((l) => !l.trimStart().startsWith('#')).join('\n');

/** The block of a top-level compose service, up to the next service or top-level key. */
function service(compose, name) {
  const start = compose.indexOf(`\n  ${name}:\n`);
  assert.notEqual(start, -1, `service ${name} is defined`);
  const rest = compose.slice(start + 1);
  const end = rest.slice(1).search(/\n(?: {2}[a-z][a-z0-9-]*:\n|[a-z])/);
  return end === -1 ? rest : rest.slice(0, end + 1);
}

describe('the analysis overlay', () => {
  const whitebox = service(overlay, 'whitebox');
  const api = service(overlay, 'api');

  it('tells the application where the service listens, at the port the image listens on', () => {
    assert.match(api, /SILEXGIS__Analysis__Whitebox__Url: http:\/\/whitebox:3200/);
    assert.match(image, /ASPNETCORE_HTTP_PORTS=3200/);
  });

  it('refuses to start beside an application that has not been told to use it', () => {
    assert.match(api, /SILEXGIS__Analysis__Whitebox__Mode: \$\{SILEXGIS__Analysis__Whitebox__Mode:\?/);
  });

  it('publishes no port, and joins only a network with no way out', () => {
    assert.doesNotMatch(whitebox, /\n {4}ports:/);
    assert.match(whitebox, /\n {4}networks:\n {6}- whitebox\n/);
    assert.match(overlay, /\nnetworks:\n(?: {2}#[^\n]*\n)* {2}whitebox:\n {4}internal: true\n/);
  });

  it('keeps the application on its own network as well as the service’s', () => {
    assert.match(api, /\n {4}networks:\n {6}- default\n {6}- whitebox\n/);
  });

  it('gives the service none of the application’s settings, and so no database password', () => {
    assert.doesNotMatch(whitebox, /SILEXGIS__Db__|ConnectionStrings|SILEXGIS_DB_PASSWORD|env_file/);
    assert.doesNotMatch(whitebox, /\n {6}- default\n/);
  });

  it('runs read-only, without capabilities or new privileges, and within limits', () => {
    assert.match(whitebox, /\n {4}read_only: true\n/);
    assert.match(whitebox, /\n {4}cap_drop:\n {6}- ALL\n/);
    assert.match(whitebox, /no-new-privileges:true/);
    assert.match(whitebox, /\n {4}mem_limit: /);
    assert.match(whitebox, /\n {4}cpus: /);
    assert.match(whitebox, /\n {4}pids_limit: \d+\n/);
  });

  it('weakens none of Docker’s own confinement to make room for an inner sandbox', () => {
    assert.doesNotMatch(overlay, /seccomp[:=]unconfined|apparmor[:=]unconfined|systempaths|privileged:/);
  });

  it('makes its runs on a volume the image prepared for its own user', () => {
    assert.match(whitebox, /- silexgis-whitebox-work:\/work\n/);
    assert.match(overlay, /\nvolumes:\n {2}silexgis-whitebox-work:/);
    assert.match(image, /SILEXGIS__WhiteboxService__WorkDir=\/work/);
    assert.match(image, /mkdir -p \/work && chown "\$APP_UID" \/work/);
    assert.match(image, /\nUSER \$APP_UID\n/);
  });

  it('builds the image from the server sources, with the Dockerfile kept here', () => {
    assert.match(whitebox, /context: \.\.\/server\n/);
    assert.match(whitebox, /dockerfile: \.\.\/deploy\/whitebox\/Dockerfile\n/);
  });
});

describe('the analysis image', () => {
  it('publishes the service project the solution lists', () => {
    assert.match(image, /dotnet publish src\/SilexGis\.WhiteboxService /);
    assert.match(solution, /src\/SilexGis\.WhiteboxService\/SilexGis\.WhiteboxService\.csproj/);
    assert.match(image, /ENTRYPOINT \["dotnet", "SilexGis\.WhiteboxService\.dll"\]/);
  });

  it('builds WhiteboxTools from its tagged source, pinned by the checksum of the source archive', () => {
    // The vendor's ready-made programs sit at an unversioned address: a pin on one of them stops
    // matching, for good, the day a newer release replaces it, and every update that rebuilds the
    // image stops with it. A tag's source archive and the registry's packages do not change.
    assert.match(imageCommands, /^ARG WHITEBOX_SOURCE_SHA256=[0-9a-f]{64}$/m);
    assert.match(imageCommands, /https:\/\/github\.com\/jblindsay\/whitebox-tools\/archive\/refs\/tags\/v\$\{WHITEBOX_VERSION\}\.tar\.gz/);
    assert.match(imageCommands, /echo "\$\{WHITEBOX_SOURCE_SHA256\}  \/tmp\/whitebox\.tar\.gz" \| sha256sum -c -/);
    assert.match(imageCommands, /cargo build --release --package whitebox_tools --bin whitebox_tools/);
    // Built only from packages the release's lock file pins, by name, version and checksum.
    assert.match(imageCommands, /cp Cargo\.lock \/tmp\/Cargo\.lock\.pinned/);
    assert.match(imageCommands, /comm -13 \/tmp\/pinned\.txt \/tmp\/used\.txt/);
    assert.match(imageCommands, /^FROM rust:\d+\.\d+-\w+ AS whitebox$/m, 'a compiler of a stated version');
    assert.doesNotMatch(imageCommands, /whiteboxgeo\.com/);
  });

  it('keeps the program alone: no plugins, and settings of its own', () => {
    // The vendor's plugins directory holds programs that download and install software, and its
    // settings name a working directory on its author's machine.
    assert.doesNotMatch(imageCommands, /plugins/);
    assert.match(imageCommands, /"working_directory": ""/);
    assert.match(image, /SILEXGIS__Analysis__Whitebox__WhiteboxPath=\/opt\/whitebox\/whitebox_tools/);
  });

  it('runs without bubblewrap only because it says so, and installs none to be found', () => {
    assert.match(image, /SILEXGIS__Analysis__Whitebox__AllowUnsandboxed=true/);
    assert.doesNotMatch(imageCommands, /bubblewrap|bwrap/);
  });
});

describe('the application’s settings for it', () => {
  it('are offered in the sample environment file, off by default', () => {
    assert.match(envExample, /# SILEXGIS__Analysis__Whitebox__Mode=none/);
    assert.match(envExample, /# SILEXGIS__Analysis__Whitebox__Url=http:\/\/whitebox:3200/);
  });

  it('include the service’s own limits, which the overlay passes through to it', () => {
    const whitebox = service(overlay, 'whitebox');
    for (const name of ['MaxConcurrentRuns', 'MaxInputBytes']) {
      assert.match(envExample, new RegExp(`# SILEXGIS__WhiteboxService__${name}=\\d+`), `sample offers ${name}`);
      assert.match(
        whitebox,
        new RegExp(`SILEXGIS__WhiteboxService__${name}: \\$\\{SILEXGIS__WhiteboxService__${name}:-\\d+\\}`),
        `overlay passes ${name}`);
    }
  });
});
