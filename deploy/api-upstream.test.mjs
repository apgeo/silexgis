// The web image's proxy finds the API by a configurable name. On a Docker network shared by several
// Compose projects every service's bare name is an address, so another project's `api` can answer
// to the name this stack's proxy uses; an installation in that position names its API by its unique
// container name instead. These checks keep the setting reachable end to end.
import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const read = (...parts) => fs.readFileSync(path.join(root, ...parts), 'utf8');

describe('the web proxy\'s API address', () => {
  it('comes from the setting, not a fixed name', () => {
    const conf = read('client', 'nginx.conf');
    const block = conf.match(/upstream silexgis_api \{[\s\S]*?\n\}/);
    assert.ok(block, 'no silexgis_api upstream block');
    assert.match(block[0], /^\s*server \$\{SILEXGIS__Web__ApiUpstream\} resolve;$/m);
    assert.doesNotMatch(block[0], /server api:8080/);
  });

  it('defaults to the Compose service name in the image and in the compose file', () => {
    assert.match(read('client', 'Dockerfile'), /^ENV SILEXGIS__Web__ApiUpstream="api:8080"$/m);
    assert.match(
      read('deploy', 'docker-compose.yml'),
      /^\s*SILEXGIS__Web__ApiUpstream:\s*\$\{SILEXGIS_API_UPSTREAM:-api:8080\}\s*$/m,
    );
    // Only variables with this prefix are expanded; any other would be left in the file verbatim.
    assert.match(read('client', 'Dockerfile'), /^ENV NGINX_ENVSUBST_FILTER="\^SILEXGIS__Web__"/m);
  });

  it('is documented where an operator looks', () => {
    assert.match(read('deploy', '.env.example'), /^# SILEXGIS_API_UPSTREAM=api:8080$/m);
    assert.match(read('docs', 'INSTALL.md'), /`SILEXGIS_API_UPSTREAM`/);
    assert.match(read('docs', 'DEPLOY-SERVER.md'), /SILEXGIS_API_UPSTREAM=<project>-api-1:8080/);
  });
});
