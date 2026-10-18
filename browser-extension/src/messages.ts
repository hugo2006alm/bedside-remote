declare const BEDSIDE_EDGE: boolean;
type Handler = (message: unknown, sender: browser.runtime.MessageSender) => Promise<unknown> | undefined;

export function listen(handler: Handler) {
  if (typeof BEDSIDE_EDGE === 'undefined' || !BEDSIDE_EDGE) {
    browser.runtime.onMessage.addListener(handler);
    return;
  }
  // Edge may expose Promise-returning methods while still requiring callback responses.
  browser.runtime.onMessage.addListener((message: unknown, sender, respond) => {
    const pending = handler(message, sender);
    if (pending === undefined) return undefined;
    void pending.then(respond, () => respond({ ok: false, error: 'Extension message failed.' }));
    return true;
  });
}
