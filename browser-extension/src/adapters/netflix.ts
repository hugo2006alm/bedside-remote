import { DomAdapter, visible } from './player';
import type { Command } from '../protocol';

function titleText(element: Element | null): string {
  if (!element) return '';
  const walker = element.ownerDocument.createTreeWalker(element, 4 /* SHOW_TEXT */);
  const fragments: string[] = [];
  for (let node = walker.nextNode(); node; node = walker.nextNode()) {
    const text = node.textContent?.replace(/\s+/g, ' ').trim();
    if (text) fragments.push(text);
  }
  return fragments.join(' ');
}

export class NetflixAdapter extends DomAdapter {
  platform = 'netflix' as const;
  constructor(doc: Document) {
    super(doc, {
      title: '[data-uia="video-title"], [data-uia="player-title"], .video-title',
      player: '[data-uia="watch-video"], .watch-video',
      controls: {
        skipIntro: '[data-uia="player-skip-intro"], [data-uia="skip-intro"]',
        skipRecap: '[data-uia="player-skip-recap"], [data-uia="skip-recap"]',
        nextEpisode: '[data-uia="next-episode-seamless-button"], [data-uia="control-next"]',
        subtitles: '[data-uia="control-audio-subtitle"]',
        audio: '[data-uia="control-audio-subtitle"]'
      }
    });
  }
  override video(): HTMLVideoElement | null {
    const episode = Array.from(this.doc.querySelectorAll(this.selectors.player)).flatMap(player => Array.from(player.querySelectorAll('video')));
    return episode.find(video => visible(video) && video.readyState > 0) ?? episode[0] ??
      Array.from(this.doc.querySelectorAll<HTMLVideoElement>('video')).find(visible) ?? super.video();
  }
  metadata() {
    const episodeTitle = Array.from(this.doc.querySelectorAll(this.selectors.player)).map(player => titleText(player.querySelector(this.selectors.title))).find(Boolean);
    return { title: (episodeTitle || titleText(this.doc.querySelector(this.selectors.title)) || this.doc.title.replace(/\s*[-|]\s*Netflix$/, '').replace(/\s+/g, ' ').trim()).slice(0, 512) };
  }
  private audioMenu(button: HTMLElement): HTMLElement | undefined {
    const controlled = button.getAttribute('aria-controls');
    const explicit = controlled ? this.doc.getElementById(controlled) : null;
    if (explicit && visible(explicit)) return explicit;
    return Array.from(this.doc.querySelectorAll<HTMLElement>('[data-uia="selector-audio-subtitle"], .track-list-container, [data-uia="audio-subtitle-controller"] [role="menu"], .audio-subtitle-controller [role="menu"]')).find(visible);
  }
  private async waitForMenu(button: HTMLElement, wasOpen: boolean, hadMenu: boolean, expanded: string | null, timeout: number): Promise<boolean> {
    const deadline = Date.now() + timeout;
    do {
      const current = this.button('audio') ?? button;
      const value = current.getAttribute('aria-expanded');
      const menu = this.audioMenu(current);
      if ((wasOpen ? hadMenu && !menu : !!menu) || expanded !== null && value === String(!wasOpen) && value !== expanded) return true;
      await new Promise(resolve => setTimeout(resolve, 50));
    } while (Date.now() < deadline);
    return false;
  }
  override async execute(command: Command, position?: number): Promise<void> {
    if (command !== 'audio' && command !== 'subtitles') return super.execute(command, position);
    const button = this.button('audio');
    if (!button) throw new Error('Audio and subtitles are unavailable for this player.');
    const menu = this.audioMenu(button);
    const expanded = button.getAttribute('aria-expanded');
    const wasOpen = !!menu || expanded === 'true';
    button.click();
    if (await this.waitForMenu(button, wasOpen, !!menu, expanded, wasOpen ? 450 : 1200)) return;
    if (wasOpen) {
      // Netflix also uses hover controllers whose button only opens the menu.
      // Leave that controller through normal DOM events; never hide it ourselves.
      const current = this.button('audio') ?? button;
      const Mouse = this.doc.defaultView!.MouseEvent;
      const destination = this.video() ?? this.doc.body;
      current.blur();
      current.dispatchEvent(new Mouse('mouseout', { bubbles: true, relatedTarget: destination }));
      menu?.dispatchEvent(new Mouse('mouseout', { bubbles: true, relatedTarget: destination }));
      current.closest('.audio-subtitle-controller, [data-uia="audio-subtitle-controller"]')?.dispatchEvent(new Mouse('mouseleave', { relatedTarget: destination }));
      if (await this.waitForMenu(current, wasOpen, !!menu, expanded, 1000)) return;
    }
    const markers = Array.from(this.doc.querySelectorAll<HTMLElement>('[data-uia*="audio"], [data-uia*="subtitle"], [role="menu"], [role="listbox"]'))
      .filter(visible)
      .map(element => element.getAttribute('data-uia') || element.getAttribute('role') || '')
      .filter(marker => /^[a-zA-Z0-9_-]{1,64}$/.test(marker));
    const diagnostic = [...new Set(markers)].slice(0, 6).join(', ').slice(0, 155);
    // Only public control identifiers: never titles, chosen languages or page content.
    throw new Error(`Audio/subtitles menu change was not confirmed. Visible controls: ${diagnostic || 'none'}.`);
  }
}
