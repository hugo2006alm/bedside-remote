import assert from 'node:assert/strict';
import { test } from 'node:test';
import { JSDOM } from 'jsdom';
import { navigate } from '../src/navigation';

test('arrows move visible focus spatially and OK activates only the selected item', () => {
  const dom = new JSDOM('<button id="first">First</button><button id="hidden" hidden>Hidden</button><button id="right">Right</button><button id="below">Below</button><button disabled id="disabled">Disabled</button>', { url: 'https://www.netflix.com/browse' });
  const doc = dom.window.document;
  const positions: Record<string, [number, number]> = { first: [20, 20], hidden: [80, 20], right: [140, 20], below: [140, 120], disabled: [140, 70] };
  for (const element of doc.querySelectorAll<HTMLElement>('button')) {
    const [left, top] = positions[element.id]!;
    const rect = { left, top, right: left + 80, bottom: top + 40, width: 80, height: 40 };
    Object.defineProperties(element, { getClientRects: { value: () => [rect] }, getBoundingClientRect: { value: () => rect } });
  }
  let clicks = 0;
  doc.getElementById('below')!.addEventListener('click', () => clicks++);
  assert.throws(() => navigate(doc, 'navigateEnter'), /Select an item/);
  navigate(doc, 'navigateRight'); assert.equal(doc.activeElement?.id, 'first');
  navigate(doc, 'navigateRight'); assert.equal(doc.activeElement?.id, 'right');
  navigate(doc, 'navigateDown'); assert.equal(doc.activeElement?.id, 'below');
  assert.equal(doc.getElementById('below')!.getAttribute('data-bedside-remote-focus'), 'true');
  assert.equal(doc.getElementById('right')!.hasAttribute('data-bedside-remote-focus'), false);
  navigate(doc, 'navigateEnter'); assert.equal(clicks, 1);
  navigate(doc, 'navigateUp'); assert.equal(doc.activeElement?.id, 'right');
  navigate(doc, 'navigateRight'); assert.equal(doc.activeElement?.id, 'right', 'direction boundaries retain focus without an error');
  dom.window.close();
});

test('modal navigation stays inside the visible dialog', () => {
  const dom = new JSDOM('<button id="outside">Outside</button><div role="dialog"><button id="inside">Inside</button></div>');
  for (const element of dom.window.document.querySelectorAll<HTMLElement>('*')) {
    const rect = { left: 10, top: 10, right: 90, bottom: 50, width: 80, height: 40 };
    Object.defineProperties(element, { getClientRects: { value: () => [rect] }, getBoundingClientRect: { value: () => rect } });
  }
  navigate(dom.window.document, 'navigateDown');
  assert.equal(dom.window.document.activeElement?.id, 'inside');
  dom.window.close();
});

test('Netflix audio menu navigation includes language options and stays inside the shared menu', () => {
  const dom = new JSDOM('<button id="outside">Outside</button><div data-uia="selector-audio-subtitle"><div role="option" id="audio">English audio</div><div role="option" id="subtitles">Subtitles</div></div>');
  const doc = dom.window.document;
  for (const element of doc.querySelectorAll<HTMLElement>('*')) {
    const left = element.id === 'subtitles' ? 240 : 20;
    const rect = { left, top: 20, right: left + 150, bottom: 60, width: 150, height: 40 };
    Object.defineProperties(element, { getClientRects: { value: () => [rect] }, getBoundingClientRect: { value: () => rect } });
  }
  let activated = false;
  doc.getElementById('subtitles')!.addEventListener('click', () => { activated = true; });
  navigate(doc, 'navigateDown'); assert.equal(doc.activeElement?.id, 'audio');
  navigate(doc, 'navigateRight'); assert.equal(doc.activeElement?.id, 'subtitles');
  navigate(doc, 'navigateEnter'); assert.equal(activated, true);
  dom.window.close();
});
