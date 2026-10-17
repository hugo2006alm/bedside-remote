import { canonicalYoutube } from '../protocol';
import { DomAdapter } from './player';

export class YoutubeAdapter extends DomAdapter {
  platform = 'youtube' as const;
  constructor(doc: Document) {
    super(doc, {
      title: 'h1.ytd-watch-metadata, h1.title, .ytmusic-player-bar .title',
      player: '#movie_player, ytmusic-player',
      controls: { subtitles: '.ytp-subtitles-button' }
    });
  }
  metadata() {
    let videoId: string | undefined;
    try { videoId = new URL(canonicalYoutube(this.doc.location.href)).searchParams.get('v') ?? undefined; } catch { /* Home and search pages have no video ID. */ }
    const title = (this.doc.querySelector(this.selectors.title)?.textContent?.trim() || this.doc.title.replace(/ - YouTube$/, '')).slice(0, 512);
    const channel = this.doc.querySelector('#owner #channel-name, ytd-channel-name a, .ytmusic-player-bar .byline')?.textContent?.trim().slice(0, 512);
    // Canonical public thumbnail; never forward arbitrary image URLs or page tokens.
    return { title, ...(videoId ? { videoId, thumbnail: `https://i.ytimg.com/vi/${videoId}/hqdefault.jpg` } : {}), ...(channel ? { channel } : {}) };
  }
  override async execute(command: Parameters<DomAdapter['execute']>[0], position?: number) {
    if (command === 'subtitles') {
      const button = this.button(command);
      if (!button) throw new Error('Subtitles are unavailable for this video.');
      const before = button.getAttribute('aria-pressed');
      button.click();
      await new Promise(resolve => setTimeout(resolve, 150));
      if (before === null || button.getAttribute('aria-pressed') === before) throw new Error('The player did not confirm subtitle changes.');
      return;
    }
    await super.execute(command, position);
  }
}
