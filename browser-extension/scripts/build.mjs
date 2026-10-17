import { build } from 'esbuild';
import { cp, mkdir, readFile, writeFile } from 'node:fs/promises';

await mkdir('dist', { recursive: true });
await cp('static', 'dist', { recursive: true });
const manifest = JSON.parse(await readFile('static/manifest.json', 'utf8'));
const version = process.env.APP_VERSION ?? manifest.version;
const match = /^(\d+\.\d+\.\d+)(?:-[0-9A-Za-z.-]+)?$/.exec(version);
if (!match) throw new Error('APP_VERSION must be major.minor.patch with an optional SemVer prerelease suffix.');
manifest.version = match[1];
await writeFile('dist/manifest.json', JSON.stringify(manifest, null, 2) + '\n');
await build({
  entryPoints: ['src/background.ts', 'src/content.ts', 'src/popup.ts'],
  outdir: 'dist', bundle: true, format: 'iife', target: 'firefox140',
  legalComments: 'eof', sourcemap: false, minify: false
});
