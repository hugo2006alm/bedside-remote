import test from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { buildSource } from './sidestore-source.mjs';

test('SideStore source points to the verified release and preserves the IPA build version', () => {
  const version = '1.2.3-alpha.10';
  const artifact = `bedside-remote-ios-v${version}-arm64-sideload.ipa`;
  const url = `https://github.com/hugo2006alm/bedside-remote/releases/download/v${version}/${artifact}`;
  const manifest = { version, iosSideload: { artifact, minimumOS: '17.0' } };
  const ipa = Buffer.from('ipa');
  const release = { publishedAt: '2026-11-24T12:00:00Z', assets: [{ name: artifact, size: ipa.length, url, digest: `sha256:${createHash('sha256').update(ipa).digest('hex')}` }] };
  const source = buildSource(manifest, release, ipa, '123');
  const entry = source.apps[0].versions[0];
  assert.equal(entry.version, '1.2.3');
  assert.equal(entry.buildVersion, '123');
  assert.equal(entry.downloadURL, url);
  assert.equal(entry.minOSVersion, '17.0');
  assert.match(entry.sha256, /^[0-9a-f]{64}$/);
  assert.throws(() => buildSource(manifest, release, Buffer.from('different'), '123'), /differs/);
});
