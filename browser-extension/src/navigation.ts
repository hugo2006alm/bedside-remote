import { visible } from './adapters/player';
import type { NavigationCommand } from './protocol';

const selector = 'a[href], button, input:not([type="hidden"]), select, textarea, [role="button"], [role="link"], [role="option"], [role="menuitem"], [role="menuitemradio"], [role="radio"], [role="checkbox"], [tabindex]';
const focused = new WeakMap<Document, HTMLElement>();
function candidates(doc: Document): HTMLElement[] {
  const dialogs = Array.from(doc.querySelectorAll<HTMLElement>('[role="dialog"], [aria-modal="true"]')).filter(visible);
  const audioMenus = Array.from(doc.querySelectorAll<HTMLElement>('[data-uia="selector-audio-subtitle"], .track-list-container')).filter(visible);
  const root = audioMenus.at(-1) ?? dialogs.at(-1) ?? doc;
  return Array.from(root.querySelectorAll<HTMLElement>(selector)).filter(element => {
    const rect = element.getBoundingClientRect();
    return visible(element) && !element.closest('[inert]') && rect.width > 0 && rect.height > 0 &&
      rect.right > 0 && rect.left < (doc.defaultView?.innerWidth ?? Infinity);
  }).slice(0, 1000);
}
function select(doc: Document, element: HTMLElement) {
  if (!doc.getElementById('bedside-remote-focus-style')) {
    const style = doc.createElement('style'); style.id = 'bedside-remote-focus-style';
    style.textContent = '[data-bedside-remote-focus="true"]{outline:3px solid #D8B57A!important;outline-offset:4px!important}';
    (doc.head ?? doc.documentElement).append(style);
  }
  focused.get(doc)?.removeAttribute('data-bedside-remote-focus');
  if (!element.matches('a[href],button,input,select,textarea,[tabindex]')) element.tabIndex = -1;
  element.focus({ preventScroll: true });
  if (doc.activeElement !== element) throw new Error('This element could not receive focus.');
  element.setAttribute('data-bedside-remote-focus', 'true'); focused.set(doc, element);
  element.addEventListener('blur', () => element.removeAttribute('data-bedside-remote-focus'), { once: true });
  element.scrollIntoView?.({ block: 'nearest', inline: 'nearest', behavior: 'instant' });
}
export function navigate(doc: Document, command: NavigationCommand): void {
  const items = candidates(doc);
  const current = items.find(element => element === doc.activeElement) ?? items.find(element => element === focused.get(doc));
  if (command === 'navigateBack') {
    const close = items.find(element => element.matches('[data-uia="player-back-to-browse"], [data-uia="previewModal-closebtn"], [data-uia="close-button"]'));
    if (close) { close.click(); return; }
    throw new Error('No visible Back control is available. Use the universal Back key.');
  }
  if (command === 'navigateEnter') {
    if (!current) throw new Error('Select an item with an arrow before pressing OK.');
    current.click(); return;
  }
  if (!current) {
    const height = doc.defaultView?.innerHeight ?? Infinity;
    const first = items.filter(element => { const rect = element.getBoundingClientRect(); return rect.bottom > 0 && rect.top < height; })
      .sort((a, b) => a.getBoundingClientRect().top - b.getBoundingClientRect().top || a.getBoundingClientRect().left - b.getBoundingClientRect().left)[0];
    if (!first) throw new Error('No selectable element is available on this page.');
    select(doc, first); return;
  }
  const origin = current.getBoundingClientRect();
  const horizontal = command === 'navigateLeft' || command === 'navigateRight';
  const sign = command === 'navigateLeft' || command === 'navigateUp' ? -1 : 1;
  const center = (rect: DOMRect, axis: boolean) => axis ? (rect.left + rect.right) / 2 : (rect.top + rect.bottom) / 2;
  const ranked = items.filter(element => element !== current).map(element => {
    const rect = element.getBoundingClientRect();
    const primary = sign * (center(rect, horizontal) - center(origin, horizontal));
    const secondary = Math.abs(center(rect, !horizontal) - center(origin, !horizontal));
    const aligned = horizontal ? rect.bottom > origin.top && rect.top < origin.bottom : rect.right > origin.left && rect.left < origin.right;
    return { element, primary, score: primary + 3 * secondary + (aligned ? 0 : 1000) };
  }).filter(item => item.primary > 2).sort((a, b) => a.score - b.score);
  if (!ranked.length) return;
  select(doc, ranked[0]!.element);
}
