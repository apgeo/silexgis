// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Build, run and smoke-test the production Docker distribution (deploy/docker-compose.yml).
// Cross-platform (Node 18+, no shell-isms): `node deploy/verify-distribution.mjs`.
//
// Flags:
//   --no-build   skip the image build, just (re)start and verify what is already built
//   --down       stop and remove the stack after a successful verify (default: leave it up)
//   --no-cache   build without the layer cache (a clean-room build)
//   --reports    also sign in as the stack's first administrator and have the running image
//                write up a throwaway trip and a throwaway camp, removing both afterwards
//
// Exit code 0 = the web front answered, the API health endpoint answered, every module script
// in the build is served as JavaScript, the proxy in front of the API accepts a body as large
// as the configured upload limit while still refusing one that is genuinely too large, and —
// when --reports asked for it — the image produced both documents; non-zero otherwise.
//
// WHY THE WRITE-UPS ARE ASKED FOR HERE
// A trip or a camp is written up as a word-processor document by the API itself, and nothing
// else runs that code inside these images. The test suite runs it on a development machine,
// which has fonts, a full SDK and every system library; the runtime image has none of the
// three. A writer that measured a line of text, or reached for a native library the base image
// does not carry, would pass every test and answer 500 on the first installation. Reading the
// code cannot settle that. Asking the packaged image for the document does.
//
// WHY THAT IS NOT DONE UNLESS ASKED
// It is the one check here that writes: it signs in and creates records, if only for a moment.
// Every installation's .env still names its first administrator, so a check that ran whenever
// those two settings were present would write into every installation this script is pointed
// at — and would fail on each one whose administrator has changed that password since.

import { execSync } from 'node:child_process';
import { createHash, randomBytes } from 'node:crypto';
import { existsSync } from 'node:fs';
import { connect } from 'node:net';
import { dirname, join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const deployDir = dirname(fileURLToPath(import.meta.url));
const args = new Set(process.argv.slice(2));
const compose = 'docker compose -f docker-compose.yml';

/** The web application's own registered client. Public: it holds no secret and must prove a key. */
const WEB_CLIENT_ID = 'silexgis-spa';

/**
 * What the web application asks for, less `offline_access`. That one scope is what produces a
 * refresh token, and a check that is finished in seconds has no use for a credential that
 * stays good for a month after it.
 */
const SIGN_IN_SCOPE = 'openid profile email roles';

/** The media type a write-up is served as. */
const DOCX = 'application/vnd.openxmlformats-officedocument.wordprocessingml.document';

/** The part of a word-processor package that holds the document itself. */
const DOCUMENT_PART = 'word/document.xml';

/**
 * How long one request of the report check may take. Generous — the first document a fresh
 * container writes pays for loading everything it has never run — but finite, because a request
 * that never answers would otherwise hold a build runner until the runner gives up.
 */
const REQUEST_TIMEOUT_MS = 120_000;

/** The upload cap the API applies when nothing sets one. */
const DEFAULT_MAX_UPLOAD_BYTES = 512 * 1024 * 1024;

function run(cmd) {
  console.log(`\n$ ${cmd}`);
  execSync(cmd, { cwd: deployDir, stdio: 'inherit' });
}

/**
 * The configuration compose itself resolved for this stack.
 *
 * Asked of compose rather than read out of .env, because what a setting ends up as is compose's
 * decision and not a simple one: a value in the environment wins over the same name in .env, a
 * name .env leaves out takes the default written into docker-compose.yml, and a value is
 * unquoted and interpolated on the way. A second reading of those rules here would agree with
 * compose until the day it did not — and then this script would probe one port while the stack
 * listened on another, or sign in with a password the stack was never given.
 *
 * The answer holds the database and administrator passwords in the clear, which is why it is
 * captured rather than echoed the way every other command here is.
 */
function resolvedConfig() {
  const json = execSync(`${compose} config --format json`, {
    cwd: deployDir,
    encoding: 'utf8',
    maxBuffer: 16 * 1024 * 1024,
  });
  return JSON.parse(json);
}

/**
 * What the checks below need to know about the stack, out of the configuration compose resolved.
 *
 * Exported for its own test; `main` is what an operator runs.
 */
export function stackSettings(config) {
  const environment = config?.services?.api?.environment ?? {};
  const published = (config?.services?.web?.ports ?? []).find((entry) => Number(entry.target) === 80);
  const port = Number(published?.published);
  if (!Number.isInteger(port) || port <= 0) {
    throw new Error(
      'the web service publishes no host port, so there is nothing on this machine to verify ' +
        '(check SILEXGIS_HTTP_PORT)',
    );
  }

  // The port line may carry an address in front of the port — `127.0.0.1:8080` publishes on
  // this machine only — and then that address is the one place the front answers. `localhost`
  // would be a guess at it: the name resolves to the IPv6 loopback first on most machines, and
  // nothing is listening there when the port was bound to an IPv4 address.
  const address = published.host_ip ?? '';
  const everywhere = address === '' || address === '0.0.0.0' || address === '::';
  const host = everywhere ? 'localhost' : address.includes(':') ? `[${address}]` : address;

  const maxUploadBytes = Number(environment.SILEXGIS__Files__MaxUploadBytes);
  const email = (environment.SILEXGIS__Admin__Email ?? '').trim();
  const password = environment.SILEXGIS__Admin__Password ?? '';

  return {
    origin: `http://${host}:${port}`,
    // The address a raw socket is opened to. Never the name: a socket takes the first address a
    // name resolves to and tries no other.
    socketHost: everywhere ? '127.0.0.1' : address,
    port,
    maxUploadBytes: maxUploadBytes > 0 ? maxUploadBytes : DEFAULT_MAX_UPLOAD_BYTES,
    // Trimmed the way the API trims it before registering the web client's return address.
    publicUrl: (environment.SILEXGIS__PublicUrl || 'http://localhost:8080').replace(/\/+$/, ''),
    admin: email && password ? { email, password } : null,
  };
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
function announceUpload(stack, declaredBytes, timeoutMs = 20_000) {
  return new Promise((resolve, reject) => {
    const socket = connect(stack.port, stack.socketHost);
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
        `POST /api/v1/files/ HTTP/1.1\r\nHost: ${new URL(stack.origin).host}\r\n` +
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
async function checkProxyBodyLimit(stack) {
  const megabytes = (bytes) => Math.round(bytes / (1024 * 1024));

  process.stdout.write(`Announcing a ${megabytes(stack.maxUploadBytes)} MB upload through the proxy `);
  const allowed = await announceUpload(stack, stack.maxUploadBytes);
  if (allowed.includes(' 413')) {
    console.log('refused');
    throw new Error(
      `the proxy refused a ${megabytes(stack.maxUploadBytes)} MB body — raise client_max_body_size in ` +
        'client/nginx.conf above SILEXGIS__Files__MaxUploadBytes and rebuild the web image',
    );
  }
  console.log(`ok (${allowed})`);

  // Four times the proxy's own 1 GB ceiling: large enough that any sane configuration says no.
  const oversized = 4 * 1024 * 1024 * 1024;
  process.stdout.write(`Announcing a ${megabytes(oversized)} MB upload (must be refused) `);
  const refused = await announceUpload(stack, oversized);
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
async function checkModuleScriptTypes(stack) {
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
    const res = await fetch(`${stack.origin}${path}`);
    const type = (res.headers.get('content-type') ?? '').split(';')[0].trim().toLowerCase();
    if (!['text/javascript', 'application/javascript'].includes(type)) {
      console.log('failed');
      throw new Error(`${path} is served as "${type || 'nothing'}"; a browser will refuse it as a module`);
    }
  }
  console.log(`ok (${paths.length})`);
}

/**
 * The names of the files inside a zip archive, read from the directory at its end.
 *
 * A word-processor document is a zip, and this is all of the format the check needs: the
 * archive closes with a fixed-size record saying where its directory starts and how many
 * entries it has, and each entry carries its name behind a fixed-size header. Read backwards
 * from the end because that closing record is the one thing in a zip with a known position —
 * which also makes this a test of the whole body having arrived, since a document cut short
 * has lost exactly that record.
 *
 * Exported for its own test; `main` is what an operator runs.
 */
export function zipEntryNames(bytes) {
  const END_OF_DIRECTORY = 0x06054b50;
  const DIRECTORY_ENTRY = 0x02014b50;

  // The closing record is 22 bytes and may be followed only by a comment of at most 65535.
  let end = bytes.length - 22;
  const earliest = Math.max(0, end - 0xffff);
  while (end >= earliest && bytes.readUInt32LE(end) !== END_OF_DIRECTORY) {
    end -= 1;
  }
  if (end < earliest) {
    throw new Error('it has no end-of-directory record');
  }

  const names = [];
  let at = bytes.readUInt32LE(end + 16);
  for (let entry = bytes.readUInt16LE(end + 10); entry > 0; entry -= 1) {
    if (at + 46 > bytes.length || bytes.readUInt32LE(at) !== DIRECTORY_ENTRY) {
      throw new Error('its directory is damaged');
    }
    const nameLength = bytes.readUInt16LE(at + 28);
    names.push(bytes.toString('utf8', at + 46, at + 46 + nameLength));
    at += 46 + nameLength + bytes.readUInt16LE(at + 30) + bytes.readUInt16LE(at + 32);
  }
  return names;
}

/**
 * What is wrong with an answer that was supposed to be a write-up, or null when nothing is.
 *
 * Kept apart from the request so a test can exercise the judgement without a server: a check
 * that only asked whether something came back would pass on the error page of a writer that
 * had just failed.
 *
 * Exported for its own test; `main` is what an operator runs.
 */
export function writeUpProblem(status, contentType, body) {
  if (status !== 200) {
    return `it answered ${wording(status, body.toString('utf8'))}`;
  }

  const type = (contentType ?? '').split(';')[0].trim().toLowerCase();
  if (type !== DOCX) {
    return `it was served as "${type || 'nothing'}", not as a word-processor document`;
  }

  let names;
  try {
    names = zipEntryNames(body);
  } catch (err) {
    return `it is not a zip archive: ${err.message}`;
  }
  return names.includes(DOCUMENT_PART)
    ? null
    : `the archive holds no ${DOCUMENT_PART} (it holds ${names.join(', ') || 'nothing'})`;
}

/**
 * How a refusal was worded: the status, with the code and the sentence the API put beside it
 * when it gave any. The token endpoint words its own refusals differently from the rest, so
 * both spellings are read.
 */
function wording(status, text) {
  try {
    const said = JSON.parse(text);
    return [status, said.code ?? said.error, said.detail ?? said.error_description]
      .filter(Boolean)
      .join(' ');
  } catch {
    return String(status);
  }
}

/**
 * One request with a deadline on it. A request that gets no answer at all is reported by what
 * it was, because the error underneath names neither the route nor, often, the reason.
 */
async function request(url, options = {}) {
  try {
    return await fetch(url, { ...options, signal: AbortSignal.timeout(REQUEST_TIMEOUT_MS) });
  } catch (err) {
    const route = `${options.method ?? 'GET'} ${new URL(url).pathname}`;
    throw new Error(`${route} got no answer: ${err.cause?.message ?? err.message}`);
  }
}

/**
 * Signs in the way the web application does and returns the access token it ends up holding.
 *
 * Three requests, because that is the only way in. The password buys a session cookie and
 * nothing else; the authorization request spends the cookie on a single-use code, sent to the
 * web client's registered return address; and the code is exchanged, together with proof of the
 * key the request was made with, for the token the API accepts. There is no shorter route and
 * this does not add one: a check that had a door of its own would be testing that door.
 *
 * The return address is never visited. It only has to be, letter for letter, the one the web
 * client is registered with — which the API derives from the installation's public address, so
 * that is where this gets it too.
 */
async function signIn(stack) {
  const { email, password } = stack.admin;

  const login = await request(`${stack.origin}/api/v1/auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email, password }),
  });
  if (login.status !== 200) {
    throw new Error(
      `signing in as ${email} was refused (${wording(login.status, await login.text())}). The check ` +
        'signs in as the administrator the stack was first started with, SILEXGIS_ADMIN_EMAIL and ' +
        'SILEXGIS_ADMIN_PASSWORD; on an installation that has been in use, that password may have ' +
        'been changed since',
    );
  }
  const cookie = (login.headers.getSetCookie?.() ?? []).map((entry) => entry.split(';')[0]).join('; ');
  if (!cookie) {
    throw new Error('the sign-in answered 200 but set no session cookie');
  }

  const verifier = randomBytes(48).toString('base64url');
  const state = randomBytes(8).toString('hex');
  const returnAddress = `${stack.publicUrl}/auth/callback`;
  const query = new URLSearchParams({
    client_id: WEB_CLIENT_ID,
    redirect_uri: returnAddress,
    response_type: 'code',
    scope: SIGN_IN_SCOPE,
    code_challenge: createHash('sha256').update(verifier, 'ascii').digest('base64url'),
    code_challenge_method: 'S256',
    state,
  });
  const authorize = await request(`${stack.origin}/connect/authorize?${query}`, {
    headers: { Cookie: cookie },
    redirect: 'manual',
  });
  const location = authorize.headers.get('location');
  const returned = location ? new URL(location, stack.origin) : null;
  const code = returned?.searchParams.get('code');
  if (authorize.status !== 302 || !code || returned.searchParams.get('state') !== state) {
    // A refusal the server could address comes back on the return address itself, as an error
    // in place of the code; one it could not — an address it does not recognise — comes back
    // as a page, with nowhere to point.
    const refused = returned?.searchParams.get('error_description') ?? returned?.searchParams.get('error');
    const pointed = returned ? `it pointed at ${returned.origin}${returned.pathname}` : null;
    throw new Error(
      `the authorization request answered ${authorize.status} and handed back no code` +
        (refused ?? pointed ? ` (${refused ?? pointed})` : '') +
        `. It named ${returnAddress} as the web client's return address, which has to be the one ` +
        'the installation registered from SILEXGIS_PUBLIC_URL',
    );
  }

  const exchange = await request(`${stack.origin}/connect/token`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
    body: new URLSearchParams({
      grant_type: 'authorization_code',
      code,
      redirect_uri: returnAddress,
      client_id: WEB_CLIENT_ID,
      code_verifier: verifier,
    }),
  });
  const answer = await exchange.text();
  let token = null;
  try {
    token = JSON.parse(answer).access_token ?? null;
  } catch {
    // Not JSON: reported below as whatever status it came with.
  }
  if (exchange.status !== 200 || !token) {
    throw new Error(
      `the code exchange answered ${wording(exchange.status, answer)} and gave no access token`,
    );
  }
  return token;
}

/** One call to the API as the signed-in administrator. Answers whatever came back. */
function call(session, method, path, body) {
  return request(`${session.origin}${path}`, {
    method,
    headers: {
      Authorization: `Bearer ${session.token}`,
      ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
    },
    ...(body === undefined ? {} : { body: JSON.stringify(body) }),
  });
}

/** Runs one named step, finishing its line with what it found or with the word "failed". */
async function step(out, label, work) {
  out.write(`${label} `);
  try {
    const found = await work();
    out.write(found ? `ok (${found})\n` : 'ok\n');
  } catch (err) {
    out.write('failed\n');
    throw err;
  }
}

/** Asks for a write-up and judges what came back; answers a few words about it. */
async function askForWriteUp(session, path) {
  const response = await call(session, 'GET', path);
  const body = Buffer.from(await response.arrayBuffer());
  const problem = writeUpProblem(response.status, response.headers.get('content-type'), body);
  if (problem) {
    throw new Error(`GET ${path}: ${problem}`);
  }
  return `${body.length} bytes in ${zipEntryNames(body).length} parts`;
}

/** Creates a record and answers its id. */
async function create(session, path, body) {
  const created = await call(session, 'POST', path, body);
  const text = await created.text();
  if (created.status !== 201) {
    throw new Error(`POST ${path} answered ${wording(created.status, text)}`);
  }
  return JSON.parse(text).id;
}

/** Deletes a record and confirms that it, and anything else named, can no longer be read. */
async function remove(session, path, alsoGone = []) {
  const deleted = await call(session, 'DELETE', path);
  if (deleted.status !== 204) {
    throw new Error(`DELETE ${path} answered ${wording(deleted.status, await deleted.text())}`);
  }
  for (const gone of [path, ...alsoGone]) {
    const after = await call(session, 'GET', gone);
    if (after.status !== 404) {
      throw new Error(
        `GET ${gone} answered ${after.status} after the delete, so the check has left it behind`,
      );
    }
  }
}

/**
 * Takes away what a failed check had made, so the failure that is reported is the first one.
 *
 * The record is named for what it is, so one that cannot be removed here — the API has stopped
 * answering, say — is still recognisable to whoever finds it.
 */
async function discard(session, out, path) {
  if (!path) {
    return;
  }
  try {
    const deleted = await call(session, 'DELETE', path);
    if (deleted.status !== 204) {
      out.write(`(could not remove what the check made: DELETE ${path} answered ${deleted.status})\n`);
    }
  } catch {
    out.write(`(could not remove what the check made: ${path})\n`);
  }
}

/** True for an identifier that names something: the API answers all zeroes when nothing was made. */
const namesSomething = (id) => typeof id === 'string' && /^[0-9a-f-]{36}$/i.test(id) && /[1-9a-f]/i.test(id);

/**
 * Has the running image write up a trip and a camp, and leaves neither behind.
 *
 * Both are made for the purpose, private to the administrator who made them, and named so that
 * one a killed run left behind says what it is. The trip's write-up is asked for, then filed
 * against the trip, and the trip is deleted — which takes the filed write-up with it, and the
 * check confirms that it did rather than assuming so. What a sign-in and a write always leave —
 * the session's entries in the token store and the lines in the audit trail — stays, as it
 * does for anybody.
 *
 * Neither holds a photograph, so the half of the writer that places pictures is not run here. A
 * picture hung on a trip is a document in its own right and deliberately outlives the trip, and
 * no route removes a document outright — so a check that hung one on its trip would leave a
 * picture behind on every installation it was pointed at, once for every run.
 *
 * Exported for its own test; `main` is what an operator runs.
 */
export async function checkReports(stack, out = process.stdout) {
  const made = new Date().toISOString().replace('T', ' ').slice(0, 16);
  const title =
    `Distribution check ${made} UTC — a throwaway made by verify-distribution.mjs, safe to delete`;
  const day = made.slice(0, 10);

  const session = { origin: stack.origin, token: null };
  await step(out, `Signing in as ${stack.admin.email}`, async () => {
    session.token = await signIn(stack);
  });

  let trip = null;
  let filed = null;
  try {
    await step(out, 'Writing up a throwaway trip', async () => {
      const id = await create(session, '/api/v1/trip-logs', {
        title,
        tripDate: day,
        participants: [],
        // Said rather than left to the default, so that nobody else on the installation is shown
        // the trip during the seconds it exists.
        visibility: 'private',
      });
      trip = `/api/v1/trip-logs/${id}`;
      return askForWriteUp(session, `${trip}/report`);
    });

    await step(out, 'Filing that write-up against the trip', async () => {
      const kept = await call(session, 'POST', `${trip}/report`);
      const text = await kept.text();
      if (kept.status !== 200) {
        throw new Error(`POST ${trip}/report answered ${wording(kept.status, text)}`);
      }
      const { documentId, fileName } = JSON.parse(text);
      if (!namesSomething(documentId)) {
        throw new Error(`POST ${trip}/report answered 200 and named no document`);
      }
      filed = `/api/v1/documents/${documentId}`;
      const read = await call(session, 'GET', filed);
      if (read.status !== 200) {
        throw new Error(
          `GET ${filed} answered ${read.status}: the document the route said it filed is not there`,
        );
      }
      return fileName;
    });
  } catch (err) {
    await discard(session, out, trip);
    throw err;
  }
  await step(out, 'Deleting the trip', async () => {
    await remove(session, trip, [filed]);
    return 'its filed write-up went with it';
  });

  let camp = null;
  try {
    await step(out, 'Writing up a throwaway camp', async () => {
      const id = await create(session, '/api/v1/expeditions', {
        name: title,
        startDate: day,
        visibility: 'private',
      });
      camp = `/api/v1/expeditions/${id}`;
      // Downloaded and not filed, unlike the trip's. Deleting a camp takes the rows that pinned
      // files to it and leaves the files themselves, so a write-up filed here would stay on the
      // installation as a document nothing leads to — and the API has no route that deletes a
      // document. The download is the part an image can get wrong: it runs the same writer over
      // the camp's own layout. Filing would add nothing the trip's step has not already shown.
      return askForWriteUp(session, `${camp}/report`);
    });
  } catch (err) {
    await discard(session, out, camp);
    throw err;
  }
  await step(out, 'Deleting the camp', () => remove(session, camp));
}

async function main() {
  if (!existsSync(join(deployDir, '.env'))) {
    console.error('deploy/.env is missing — copy .env.example to .env and set the passwords first.');
    process.exit(2);
  }

  // Before anything is built, so that a request this run cannot honour is refused in a second
  // rather than after the images have been built to find it out.
  const stack = stackSettings(resolvedConfig());
  if (args.has('--reports') && !stack.admin) {
    console.error(
      'The report check was asked for (--reports) but the stack names no first administrator to ' +
        'sign in as — set SILEXGIS_ADMIN_EMAIL and SILEXGIS_ADMIN_PASSWORD in .env first.',
    );
    process.exit(2);
  }

  if (!args.has('--no-build')) {
    run(`${compose} build${args.has('--no-cache') ? ' --no-cache' : ''}`);
  }
  run(`${compose} up -d`);

  // The API applies migrations on first boot, so the health endpoint can lag the container.
  await waitFor(`${stack.origin}/health/ready`, { label: 'API (/health/ready via web proxy)' });
  await waitFor(`${stack.origin}/`, { label: 'web front (SPA index)' });
  await checkModuleScriptTypes(stack);
  await checkProxyBodyLimit(stack);

  if (args.has('--reports')) {
    await checkReports(stack);
  } else {
    console.log(
      'Report check skipped: it signs in and makes a throwaway trip and camp, so it runs only ' +
        'when asked for (--reports).',
    );
  }

  console.log(`\n✅ Distribution is up and healthy at ${stack.origin}`);

  if (args.has('--down')) {
    run(`${compose} down`);
    console.log('Stack stopped (--down).');
  }
}

// Only when run, not when imported, so the test file can exercise the helpers above.
if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  main().catch((err) => {
    console.error(`\n❌ ${err.message}`);
    console.error('Inspect logs with:  docker compose -f deploy/docker-compose.yml logs --tail=50');
    process.exit(1);
  });
}
