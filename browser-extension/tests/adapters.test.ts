import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { test } from 'node:test';
import { JSDOM } from 'jsdom';
import { YoutubeAdapter } from '../src/adapters/youtube';
import { NetflixAdapter } from '../src/adapters/netflix';

async function fixture(name: 'youtube' | 'netflix') {
  const html = await readFile(new URL(`fixtures/${name}.html`, import.meta.url), 'utf8');
  const dom = new JSDOM(html, { url: name === 'youtube' ? 'https://www.youtube.com/watch?v=dQw4w9WgXcQ' : 'https://www.netflix.com/watch/12345' });
  // JSDOM has no layout/media engine: provide only the explicitly tested fixture state.
  for (const element of dom.window.document.querySelectorAll('*')) Object.defineProperty(element, 'getClientRects', { value: () => [{ width: 10, height: 10 }] });
  const video = dom.window.document.querySelector('video')!;
  let paused = true;
  Object.defineProperties(video, {
    duration: { value: 120, configurable: true },
    readyState: { value: 4, configurable: true },
    paused: { get: () => paused },
    seekable: { value: { length: 1, start: () => 0, end: () => 120 } },
    play: { value: async () => { paused = false; }, configurable: true },
    pause: { value: () => { paused = true; } }
  });
  const adapter = name === 'youtube' ? new YoutubeAdapter(dom.window.document) : new NetflixAdapter(dom.window.document);
  return { dom, video, adapter };
}
test('Netflix selects the episode video instead of a retained Home preview', async () => {
  const { dom, video, adapter } = await fixture('netflix');
  const preview = dom.window.document.createElement('video');
  preview.hidden = true; dom.window.document.body.prepend(preview);
  assert.equal(adapter.video(), video);
  assert.equal(adapter.state(false).title, 'Evening episode');
  assert.equal(adapter.state(false).duration, 120);
  dom.window.close();
});

test('YouTube reports public metadata, finite progress and actual available controls', async () => {
  const { dom, video, adapter } = await fixture('youtube');
  video.currentTime = 24;
  const state = adapter.state(false);
  assert.equal(state.videoId, 'dQw4w9WgXcQ');
  assert.equal(state.title, 'A quiet room');
  assert.equal(state.channel, 'Bedside channel');
  assert.equal(state.position, 24);
  assert.deepEqual(state.capabilities, ['playPause', 'seek', 'subtitles']);
  assert.equal(state.ended, false);
  assert.equal(adapter.state(true).ended, true);
  dom.window.close();
});
test('play/pause and seek verify media state; unavailable and gesture-only actions fail', async () => {
  const { dom, video, adapter } = await fixture('youtube');
  await adapter.execute('playPause');
  assert.equal(video.paused, false);
  await adapter.execute('playPause');
  assert.equal(video.paused, true);
  await adapter.execute('seek', 75);
  assert.equal(video.currentTime, 75);
  await assert.rejects(adapter.execute('seek', 121), /exceeds/);
  await assert.rejects(adapter.execute('seek', -1));
  await assert.rejects(adapter.execute('skipIntro'), /unavailable/);
  await assert.rejects(adapter.execute('fullscreen'), /requires a click/);
  dom.window.close();
});

test('YouTube channel metadata excludes hidden duplicate labels and collapses layout whitespace', async () => {
  const { dom, adapter } = await fixture('youtube');
  dom.window.document.querySelector('#channel-name')!.innerHTML = '<a>Bedside\n  channel</a><span hidden>Bedside channel</span>';
  assert.equal(adapter.state(false).channel, 'Bedside channel');
  dom.window.close();
});
test('autoplay denial is explicit and never reported as successful playback', async () => {
  const { dom, video, adapter } = await fixture('youtube');
  Object.defineProperty(video, 'play', { value: async () => { throw new Error('NotAllowedError'); } });
  await assert.rejects(adapter.execute('playPause'), /blocked.*autoplay/);
  assert.equal(adapter.state(false).paused, true);
  dom.window.close();
});
test('live/unknown duration never emits nonfinite values or advertises seeking', async () => {
  const { dom, video, adapter } = await fixture('youtube');
  Object.defineProperty(video, 'duration', { value: Infinity });
  assert.equal(adapter.state(false).duration, 0);
  assert.equal(adapter.state(false).capabilities.includes('seek'), false);
  dom.window.close();
});
test('YouTube subtitles require observable confirmation', async () => {
  const { dom, adapter } = await fixture('youtube');
  const button = dom.window.document.querySelector('button')!;
  await assert.rejects(adapter.execute('subtitles'), /did not confirm/);
  button.addEventListener('click', () => button.setAttribute('aria-pressed', 'true'));
  await adapter.execute('subtitles');
  dom.window.close();
});
test('Netflix contextual actions are isolated, visible and confirm actual effect', async () => {
  const { dom, video, adapter } = await fixture('netflix');
  assert.equal(adapter.state(false).title, 'Evening episode');
  assert.deepEqual(adapter.state(false).capabilities, ['playPause', 'seek', 'skipIntro', 'nextEpisode', 'subtitles', 'audio']);
  await assert.rejects(adapter.execute('skipRecap'), /unavailable/);
  await assert.rejects(adapter.execute('skipIntro'), /did not confirm/);
  dom.window.document.querySelector('[data-uia="player-skip-intro"]')!.addEventListener('click', () => { video.currentTime = 45; });
  await adapter.execute('skipIntro');
  const menu = dom.window.document.querySelector('[data-uia="control-audio-subtitle"]')!;
  menu.addEventListener('click', () => menu.setAttribute('aria-expanded', 'true'));
  await adapter.execute('audio');
  dom.window.close();
});
test('missing/failed player exposes no playback controls and reports media errors', async () => {
  const { dom, video, adapter } = await fixture('netflix');
  Object.defineProperty(video, 'error', { value: { code: 3 } });
  assert.equal(adapter.state(false).capabilities.includes('playPause'), false);
  assert.match(adapter.state(false).error!, /Media playback error/);
  video.remove();
  assert.equal(adapter.state(true).ended, false);
  assert.equal(adapter.state(false).duration, 0);
  dom.window.close();
});

test('Netflix separates series, episode number and episode title text nodes', async () => {
  const { dom, adapter } = await fixture('netflix');
  const title = dom.window.document.querySelector('[data-uia="video-title"]')!;
  const spans = ['Evening series', 'E03', 'A quiet episode'].map(text => {
    const span = dom.window.document.createElement('span'); span.textContent = text; return span;
  });
  title.replaceChildren(...spans);
  assert.equal(adapter.metadata().title, 'Evening series E03 A quiet episode');
  dom.window.close();
});

test('Netflix audio and subtitles share a menu that confirms both opening and closing without aria-expanded', async () => {
  const { dom, adapter } = await fixture('netflix');
  const button = dom.window.document.querySelector<HTMLElement>('[data-uia="control-audio-subtitle"]')!;
  button.removeAttribute('aria-expanded');
  const menu = dom.window.document.createElement('div');
  menu.dataset.uia = 'selector-audio-subtitle'; menu.hidden = true;
  Object.defineProperty(menu, 'getClientRects', { value: () => [{ width: 200, height: 200 }] });
  dom.window.document.body.append(menu);
  button.addEventListener('click', () => { menu.hidden = !menu.hidden; });
  await adapter.execute('audio'); assert.equal(menu.hidden, false);
  await adapter.execute('subtitles'); assert.equal(menu.hidden, true);
  dom.window.close();
});

test('Netflix closes a hover-only audio menu through controller leave events', async () => {
  const { dom, adapter } = await fixture('netflix');
  const button = dom.window.document.querySelector<HTMLElement>('[data-uia="control-audio-subtitle"]')!;
  button.removeAttribute('aria-expanded');
  const menu = dom.window.document.createElement('div'); menu.className = 'track-list-container'; menu.hidden = true;
  Object.defineProperty(menu, 'getClientRects', { value: () => [{ width: 200, height: 200 }] });
  dom.window.document.body.append(menu);
  button.addEventListener('click', () => { menu.hidden = false; });
  button.addEventListener('mouseout', () => { menu.hidden = true; });
  await adapter.execute('audio'); assert.equal(menu.hidden, false);
  await adapter.execute('audio'); assert.equal(menu.hidden, true);
  dom.window.close();
});

test('an unrelated open dialog cannot falsely confirm the Netflix audio menu', async () => {
  const { dom, adapter } = await fixture('netflix');
  const dialog = dom.window.document.createElement('div'); dialog.setAttribute('role', 'dialog');
  Object.defineProperty(dialog, 'getClientRects', { value: () => [{ width: 200, height: 200 }] });
  dom.window.document.body.append(dialog);
  await assert.rejects(adapter.execute('audio'), /did not confirm/);
  dom.window.document.querySelector('[data-uia="control-audio-subtitle"]')!.setAttribute('aria-expanded', 'true');
  await assert.rejects(adapter.execute('audio'), /did not confirm/, 'an unchanged expanded flag cannot falsely confirm closing');
  dom.window.close();
});
