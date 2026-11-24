import { createHash } from 'node:crypto';
import { readFileSync, writeFileSync } from 'node:fs';
import { pathToFileURL } from 'node:url';
import { parseVersion } from './version.mjs';

export function buildSource(manifest, release, ipa, buildVersion) {
  const sideload = manifest.iosSideload;
  const parsed = parseVersion(manifest.version);
  if (!sideload || !parsed || parsed.channel === 'stable' || !/^[1-9]\d*$/.test(buildVersion)) {
    throw new Error('Expected a versioned prerelease IPA');
  }
  const base = 'https://github.com/hugo2006alm/bedside-remote';
  const downloadURL = `${base}/releases/download/v${manifest.version}/${sideload.artifact}`;
  const asset = release.assets.find(item => item.name === sideload.artifact);
  const sha256 = createHash('sha256').update(ipa).digest('hex');
  if (!release.publishedAt || asset?.url !== downloadURL || ipa.length === 0 || ipa.length !== asset.size ||
      asset.digest !== `sha256:${sha256}`) {
    throw new Error('Published iOS asset differs from the checked IPA');
  }
  const iconURL = 'https://raw.githubusercontent.com/hugo2006alm/bedside-remote/sidestore/assets/brand/icon-512.png';
  return {
    name: 'Bedside Remote',
    identifier: 'com.bedside.remote.source',
    subtitle: 'Control your Windows PC from iPhone',
    description: 'Development builds for SideStore. Pair with the Bedside Remote Windows agent.',
    website: base,
    iconURL,
    apps: [{
      name: 'Bedside Remote',
      bundleIdentifier: 'com.bedside.remote',
      developerName: 'hugo2006alm',
      subtitle: 'Windows touchpad and media remote',
      localizedDescription: 'Control Windows input and compatible YouTube and Netflix browser sessions over your local network.',
      iconURL,
      versions: [{
        version: manifest.version.split('-')[0],
        buildVersion,
        date: release.publishedAt,
        localizedDescription: `Bedside Remote ${manifest.version}. Release notes: ${base}/releases/tag/v${manifest.version}`,
        downloadURL,
        size: ipa.length,
        sha256,
        minOSVersion: sideload.minimumOS,
      }],
    }],
    news: [],
  };
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const manifest = JSON.parse(readFileSync('artifacts/release-manifest.json', 'utf8'));
  const ipa = readFileSync(`artifacts/${manifest.iosSideload.artifact}`);
  const release = JSON.parse(readFileSync(0, 'utf8'));
  const source = buildSource(manifest, release, ipa, process.env.GITHUB_RUN_NUMBER);
  writeFileSync('sidestore-source.json', JSON.stringify(source, null, 2) + '\n');
}
