import { readdir, readFile } from 'node:fs/promises';
async function check(directory) {
  for (const entry of await readdir(directory, { withFileTypes: true })) {
    const path = `${directory}/${entry.name}`;
    if (entry.isDirectory()) await check(path);
    else if (/\.(ts|mjs|html|css|json)$/.test(entry.name)) {
      const source = await readFile(path, 'utf8');
      if (/\b(?:eval|new Function)\s*\(|\.innerHTML\s*=|\b(?:fetch|WebSocket)\s*\(/.test(source)) {
        throw new Error(`Unsafe or unexpected execution/network primitive: ${path}`);
      }
      if (/[\t ]+$/m.test(source.replaceAll('\r\n', '\n').trimEnd())) throw new Error(`Trailing whitespace: ${path}`);
    }
  }
}
await check('src');
await check('static');
console.log('Source safety/style checks passed.');
