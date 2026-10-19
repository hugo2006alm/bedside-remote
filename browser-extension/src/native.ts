import { HOST_NAME, object, onlyKeys, parseNativeCommand } from './protocol';

export const reconnectDelay = (attempt: number): number => Math.min(30_000, 1000 * 2 ** Math.min(5, Math.max(0, attempt)));
function failureMessage(cause: unknown, fallback: string): string {
  const detail = typeof cause === 'object' && cause !== null && 'message' in cause ? cause.message : undefined;
  return typeof detail === 'string' && detail.trim()
    ? `${fallback} ${detail.replace(/[\u0000-\u001f\u007f]/g, ' ').trim().slice(0, 300)}`
    : fallback;
}
export class NativeLink {
  private port: browser.runtime.Port | null = null;
  private retry: ReturnType<typeof setTimeout> | undefined;
  private attempts = 0;
  private confirmed = false;
  private awaiting = false;
  private handshake: ReturnType<typeof setTimeout> | undefined;
  lastError = '';
  constructor(private receive: (message: unknown) => void, private changed: () => void) {}
  get connected() { return this.port !== null && this.confirmed; }
  get connecting() { return this.port !== null && this.awaiting; }
  private clearHandshake() { if (this.handshake) { clearTimeout(this.handshake); this.handshake = undefined; } }
  reconnect() {
    const previous = this.port;
    this.port = null;
    this.confirmed = false;
    this.awaiting = false;
    this.clearHandshake();
    previous?.disconnect();
    this.connect();
  }
  connect() {
    if (this.port) return;
    if (this.retry) { clearTimeout(this.retry); this.retry = undefined; }
    try {
      const port = browser.runtime.connectNative(HOST_NAME);
      this.port = port;
      this.confirmed = false;
      this.awaiting = true;
      port.onMessage.addListener(message => {
        if (this.port !== port) return;
        const ready = object(message) && onlyKeys(message, ['version', 'type']) && message.version === 1 && message.type === 'native.ready';
        // A valid command also confirms older hosts that predate the optional ready notice.
        const legacyCommand = !ready && (() => { try { parseNativeCommand(message); return true; } catch { return false; } })();
        if ((ready || legacyCommand) && !this.confirmed) {
          this.clearHandshake(); this.confirmed = true; this.awaiting = false; this.attempts = 0; this.lastError = '';
          this.changed();
        }
        if (!ready) this.receive(message);
      });
      port.onDisconnect.addListener(() => {
        if (this.port !== port) return;
        this.port = null;
        this.clearHandshake(); this.confirmed = false; this.awaiting = false;
        this.lastError = failureMessage(port.error ?? browser.runtime.lastError, 'Windows agent disconnected or Native Messaging host is not registered.');
        this.changed();
        this.retry = setTimeout(() => this.connect(), reconnectDelay(this.attempts++));
      });
      this.handshake = setTimeout(() => {
        if (this.port !== port || this.confirmed) return;
        this.handshake = undefined; this.awaiting = false;
        this.lastError = 'Windows has not confirmed the connection. Start or update Bedside Remote, then retry.';
        this.changed();
      }, 5000);
      this.changed();
    } catch (cause) {
      this.port = null;
      this.clearHandshake(); this.confirmed = false; this.awaiting = false;
      this.lastError = failureMessage(cause, 'Cannot connect to the Windows Native Messaging host.');
      this.changed();
      this.retry = setTimeout(() => this.connect(), reconnectDelay(this.attempts++));
    }
  }
  send(message: unknown) {
    if (!this.port) return;
    // Browser owns framing. Keep UTF-8 payload within the agent's stricter 64 KiB cap.
    if (new TextEncoder().encode(JSON.stringify(message)).length > 65_536) throw new Error('Native message exceeds 64 KiB.');
    try { this.port.postMessage(message); }
    catch { this.port.disconnect(); }
  }
}
