import { build } from 'esbuild';
import { cp, mkdir, readFile, writeFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { zipSync } from 'fflate';

const edge = process.argv.includes('--edge');
const output = edge ? 'dist-edge' : 'dist';

await mkdir(output, { recursive: true });
await cp('static', output, { recursive: true });
await cp('../LICENSE', `${output}/LICENSE.txt`);
await mkdir(`${output}/fonts`, { recursive: true });
await mkdir(`${output}/licenses`, { recursive: true });
for (const name of ['dm_sans', 'manrope']) await cp(`../android/app/src/main/res/font/${name}.ttf`, `${output}/fonts/${name}.ttf`);
for (const name of ['DM-Sans', 'Manrope']) await cp(`../android/app/src/main/assets/licenses/${name}-OFL.txt`, `${output}/licenses/${name}-OFL.txt`);
const manifest = JSON.parse(await readFile('static/manifest.json', 'utf8'));
const version = process.env.APP_VERSION ?? manifest.version;
const match = /^(\d+\.\d+\.\d+)(?:-[0-9A-Za-z.-]+)?$/.exec(version);
if (!match) throw new Error('APP_VERSION must be major.minor.patch with an optional SemVer prerelease suffix.');
manifest.version = match[1];
if (edge) {
  const identity = JSON.parse(await readFile('config/edge-identity.json', 'utf8'));
  const id = [...createHash('sha256').update(Buffer.from(identity.key, 'base64')).digest().subarray(0, 16)]
    .map(value => String.fromCharCode(97 + (value >> 4), 97 + (value & 15))).join('');
  if (id !== identity.id || identity.origin !== `chrome-extension://${id}/`) throw new Error('Edge public key and extension identity differ.');
  delete manifest.browser_specific_settings;
  manifest.key = identity.key;
  manifest.minimum_chrome_version = '148';
  manifest.background = { service_worker: 'background.js' };
}
await writeFile(`${output}/manifest.json`, JSON.stringify(manifest, null, 2) + '\n');
await build({
  entryPoints: ['src/background.ts', 'src/content.ts', 'src/popup.ts'],
  outdir: output, bundle: true, format: 'iife', target: edge ? 'chrome148' : 'firefox140',
  define: edge ? { browser: 'chrome', BEDSIDE_EDGE: 'true' } : { BEDSIDE_EDGE: 'false' },
  legalComments: 'eof', sourcemap: false, minify: false
});
if (edge) {
  const files = {};
  for (const file of ['manifest.json', 'background.js', 'content.js', 'popup.js', 'popup.html', 'popup.css', 'LICENSE.txt',
    'icons/icon-16.png', 'icons/icon-32.png', 'icons/icon-48.png', 'icons/icon-128.png', 'icons/mark.svg',
    'fonts/dm_sans.ttf', 'fonts/manrope.ttf', 'licenses/DM-Sans-OFL.txt', 'licenses/Manrope-OFL.txt']) {
    files[file] = new Uint8Array(await readFile(`${output}/${file}`));
  }
  await mkdir('artifacts', { recursive: true });
  await writeFile('artifacts/bedside-remote-edge-development.zip', zipSync(files));
}
