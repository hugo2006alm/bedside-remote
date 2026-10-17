import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { test } from 'node:test';
import { canonicalYoutube, isPlayerState, parseNativeCommand, platformForUrl } from '../src/protocol';
import { reconnectDelay } from '../src/native';

test('canonical YouTube forms and spoof rejection', () => {
  const expected = 'https://www.youtube.com/watch?v=dQw4w9WgXcQ';
  for (const input of [expected, 'https://youtu.be/dQw4w9WgXcQ?t=5', 'https://m.youtube.com/shorts/dQw4w9WgXcQ', 'https://youtube.com/live/dQw4w9WgXcQ', 'https://music.youtube.com/embed/dQw4w9WgXcQ']) assert.equal(canonicalYoutube(input), expected);
  for (const input of ['https://youtube.com.evil.test/watch?v=dQw4w9WgXcQ', 'https://evil.test@youtube.com/watch?v=dQw4w9WgXcQ', 'https://youtube.com:444/watch?v=dQw4w9WgXcQ', 'http://youtube.com/watch?v=dQw4w9WgXcQ', 'https://youtu.be/dQw4w9WgXcQ/extra', 'https://youtube.com/playlist?list=abc', 'https://youtube.com/watch?v=dQw4w9WgXcQ&v=xxxxxxxxxxx', 'https://youtube.com/watch?v=short', ' https://youtube.com/watch?v=dQw4w9WgXcQ', 'https://you\ntube.com/watch?v=dQw4w9WgXcQ']) assert.throws(() => canonicalYoutube(input), input);
});
test('native commands reject unknown keys, versions and mismatched payloads', () => {
  const command = { version: 1, type: 'browser.command', id: 'test', command: 'seek', position: 10 };
  assert.equal(parseNativeCommand(command).position, 10);
  for (const change of [{ version: 2 }, { type: 'command' }, { command: 'executeShell' }, { extra: true }, { position: NaN }, { position: -1 }, { tabId: -1 }, { command: 'playPause' }]) assert.throws(() => parseNativeCommand({ ...command, ...change }));
  assert.throws(() => parseNativeCommand({ version: 1, type: 'browser.command', id: 'x', command: 'openYoutube', url: 'https://youtu.be/dQw4w9WgXcQ' }));
  assert.equal(parseNativeCommand({ version: 1, type: 'browser.command', id: 'x', command: 'openYoutube', url: 'https://www.youtube.com/watch?v=dQw4w9WgXcQ' }).command, 'openYoutube');
  const select = { version: 1, type: 'browser.command', id: 'select', command: 'selectTab', tabId: 2 };
  assert.equal(parseNativeCommand(select).command, 'selectTab');
  for (const change of [{ tabId: undefined }, { position: 0 }, { url: 'https://www.youtube.com/watch?v=dQw4w9WgXcQ' }]) assert.throws(() => parseNativeCommand({ ...select, ...change }));
});
test('state validation excludes malformed and unbounded browser data', () => {
  const state = { platform: 'youtube', title: 'Title', paused: false, position: 1, duration: 2, ended: false, capabilities: ['playPause'] };
  assert.equal(isPlayerState(state), true);
  for (const change of [{ duration: Infinity }, { position: -1 }, { cookie: 'no' }, { capabilities: ['executeShell'] }, { title: 'x'.repeat(513) }]) assert.equal(isPlayerState({ ...state, ...change }), false);
  assert.equal(platformForUrl('https://www.netflix.com/watch/1'), 'netflix');
  assert.equal(platformForUrl('https://www.netflix.com.evil.test/watch/1'), undefined);
});
test('reconnection backs off within a fixed 30-second upper bound', () => {
  assert.deepEqual([0, 1, 2, 3, 4, 5, 20].map(reconnectDelay), [1000, 2000, 4000, 8000, 16000, 30000, 30000]);
});
test('shared cross-component YouTube URL corpus', async () => {
  const corpus = JSON.parse(await readFile(new URL('../../contracts/youtube-url-cases.json', import.meta.url), 'utf8'));
  for (const item of corpus) {
    if (item.videoId === null) assert.throws(() => canonicalYoutube(item.url));
    else assert.equal(canonicalYoutube(item.url), `https://www.youtube.com/watch?v=${item.videoId}`);
  }
});
