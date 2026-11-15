// Request generations also guard UI state; cancellation alone cannot stop late replies.
export class TranslationSession {
  constructor(invoke, changed, delay = 450) {
    this.invoke = invoke; this.changed = changed; this.delay = delay;
    this.source = ''; this.result = ''; this.phase = 'empty'; this.message = '';
    this.serial = 0; this.composing = false; this.suspended = true; this.timer = null;
  }
  get canOutput() { return ['ready', 'edited'].includes(this.phase) && !!this.source.trim() && !!this.result.trim() && !this.composing; }
  emit() { this.changed(this); }
  invalidate() { clearTimeout(this.timer); this.serial++; this.invoke('cancel_translation', { invalidate: true }).catch(() => {}); }
  update(source) {
    this.source = source; this.result = ''; this.message = ''; this.invalidate();
    this.phase = !source.trim() ? 'empty' : this.composing ? 'composing' : 'waiting';
    this.emit(); if (!this.suspended && !this.composing && source.trim()) this.schedule();
  }
  schedule(immediate = false) {
    clearTimeout(this.timer); const serial = this.serial;
    this.timer = setTimeout(async () => {
      if (serial !== this.serial || this.composing || this.suspended) return;
      this.phase = 'translating'; this.emit();
      try {
        const result = await this.invoke('translate', { text: this.source, serial });
        if (serial !== this.serial || this.suspended) return;
        this.result = result; this.phase = 'ready'; this.message = '';
      } catch (error) {
        if (serial !== this.serial || this.suspended) return;
        this.result = ''; this.phase = 'error'; this.message = String(error);
      }
      this.emit();
    }, immediate ? 0 : this.delay);
  }
  setComposing(value) { this.composing = value; this.update(this.source); }
  edit(result) { if (['ready', 'edited'].includes(this.phase)) { this.result = result; this.phase = 'edited'; this.emit(); } }
  resume() { this.suspended = false; if (!this.canOutput && this.source.trim()) { this.update(this.source); } else this.emit(); }
  suspend() {
    this.suspended = true; clearTimeout(this.timer);
    if (['waiting', 'translating', 'composing'].includes(this.phase)) { this.serial++; this.phase = 'waiting'; }
    this.composing = false; this.invoke('cancel_translation', { invalidate: false }).catch(() => {});
  }
  retry() { this.update(this.source); this.schedule(true); }
  clear() { this.update(''); }
}

export function hotkeyFromEvent(event) {
  let virtualKey;
  if (/^Key[A-Z]$/.test(event.code)) virtualKey = event.code.charCodeAt(3);
  else if (/^Digit[0-9]$/.test(event.code)) virtualKey = event.code.charCodeAt(5);
  else if (/^F([1-9]|1[0-2])$/.test(event.key)) virtualKey = 0x6f + Number(event.key.slice(1));
  else if (/^[a-z0-9]$/i.test(event.key || '')) virtualKey = event.key.toUpperCase().charCodeAt(0);
  const modifiers = (event.altKey ? 1 : 0) | (event.ctrlKey ? 2 : 0) | (event.shiftKey ? 4 : 0);
  return virtualKey && (modifiers & 3) && !event.metaKey ? { modifiers, virtualKey } : null;
}
export function displayHotkey(key) {
  return `${key.modifiers & 2 ? 'Ctrl+' : ''}${key.modifiers & 1 ? 'Alt+' : ''}${key.modifiers & 4 ? 'Shift+' : ''}${key.virtualKey >= 0x70 ? `F${key.virtualKey - 0x6f}` : String.fromCharCode(key.virtualKey)}`;
}
