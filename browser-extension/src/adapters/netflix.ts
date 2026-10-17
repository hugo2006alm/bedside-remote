import { DomAdapter } from './player';

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
  metadata() {
    return { title: (this.doc.querySelector(this.selectors.title)?.textContent?.trim() || this.doc.title.replace(/\s*[-|]\s*Netflix$/, '')).slice(0, 512) };
  }
}
