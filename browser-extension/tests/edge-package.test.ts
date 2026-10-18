import assert from 'node:assert/strict';
import { test } from 'node:test';
import { readFileSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { unzipSync } from 'fflate';

test('Edge package keeps a fixed identity, MV3 worker and exact player host permissions', () => {
  execFileSync(process.execPath, ['scripts/build.mjs', '--edge']);
  const manifest = JSON.parse(readFileSync('dist-edge/manifest.json', 'utf8'));
  const mozilla = JSON.parse(readFileSync('static/manifest.json', 'utf8'));
  const identity = JSON.parse(readFileSync('config/edge-identity.json', 'utf8'));
  const id = [...createHash('sha256').update(Buffer.from(manifest.key, 'base64')).digest().subarray(0, 16)]
    .map(value => String.fromCharCode(97 + (value >> 4), 97 + (value & 15))).join('');
  assert.equal(id, identity.id);
  assert.equal(identity.origin, `chrome-extension://${id}/`);
  assert.deepEqual(manifest.background, { service_worker: 'background.js' });
  assert.equal(manifest.browser_specific_settings, undefined);
  assert.equal(manifest.minimum_chrome_version, '148');
  assert.deepEqual(manifest.permissions, mozilla.permissions);
  assert.deepEqual(manifest.host_permissions, mozilla.host_permissions);
  const archive = unzipSync(readFileSync('artifacts/bedside-remote-edge-development.zip'));
  assert.ok(archive['manifest.json'] && archive['background.js'] && archive['content.js']);
  assert.equal(Buffer.from(archive['LICENSE.txt']!).toString(), readFileSync('../LICENSE', 'utf8'));
  for (const path of ['../windows/BedsideRemote.Agent/NativeBridge.cs', '../windows/installer/bedside-remote.iss', '../scripts/repair-browser-integration.ps1']) {
    assert.ok(readFileSync(path, 'utf8').includes(identity.origin), `${path} must authorize the same exact Edge origin`);
  }
});
