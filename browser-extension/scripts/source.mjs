import { readdir, readFile, mkdir, writeFile } from 'node:fs/promises';
import { zipSync } from 'fflate';
const files = {};
async function include(directory) {
  for (const item of await readdir(directory, { withFileTypes: true })) {
    const path = `${directory}/${item.name}`;
    if (item.isDirectory()) await include(path);
    else files[`browser-extension/${path}`] = new Uint8Array(await readFile(path));
  }
}
for (const directory of ['src', 'static', 'scripts', 'tests']) await include(directory);
for (const path of ['package.json', 'package-lock.json', 'tsconfig.json', 'README.md']) files[`browser-extension/${path}`] = new Uint8Array(await readFile(path));
// Keep the shared test corpus in its original relative location in the source archive.
files['contracts/youtube-url-cases.json'] = new Uint8Array(await readFile('../contracts/youtube-url-cases.json'));
await mkdir('artifacts', { recursive: true });
await writeFile('artifacts/bedside-remote-source.zip', zipSync(files));
console.log('AMO reviewer source: artifacts/bedside-remote-source.zip');
