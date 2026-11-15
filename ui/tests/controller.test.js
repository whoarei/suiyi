import test from 'node:test';
import assert from 'node:assert/strict';
import { TranslationSession, hotkeyFromEvent, displayHotkey } from '../controller.js';
const tick = () => new Promise((resolve) => setTimeout(resolve, 8));
test('late response cannot replace newer source', async () => {
  const pending = [];
  const s = new TranslationSession((command) => command === 'translate' ? new Promise((resolve) => pending.push(resolve)) : Promise.resolve(), () => {}, 0);
  s.resume(); s.update('旧'); await tick(); s.update('新'); await tick();
  pending[1]('New'); await tick(); pending[0]('Old'); await tick();
  assert.equal(s.result, 'New'); assert.equal(s.source, '新'); assert.ok(s.canOutput);
});
test('source/config invalidation disables stale output immediately', async () => {
  const s = new TranslationSession(async (command) => command === 'translate' ? 'Hello' : undefined, () => {}, 0);
  s.resume(); s.update('你好'); await tick(); assert.ok(s.canOutput); s.suspend(); s.update('新原文'); assert.equal(s.result, ''); assert.equal(s.canOutput, false);
});
test('IME composition sends no translation until committed', async () => {
  let requests = 0; const s = new TranslationSession(async (cmd) => { if (cmd === 'translate') { requests++; return 'Hello'; } }, () => {}, 0);
  s.resume(); s.setComposing(true); s.update('ni'); await tick(); assert.equal(requests, 0); s.setComposing(false); await tick(); assert.equal(requests, 1);
});
test('edited result and successful clearing', async () => {
  const s = new TranslationSession(async (cmd) => cmd === 'translate' ? 'Hello' : undefined, () => {}, 0);
  s.resume(); s.update('你好'); await tick(); s.edit('Hi'); assert.equal(s.result, 'Hi'); assert.ok(s.canOutput); s.clear(); assert.equal(s.source, ''); assert.equal(s.result, ''); assert.equal(s.canOutput, false);
});
test('hide cancels pending work; ready draft can resume', async () => {
  let resolve; const s = new TranslationSession((cmd) => cmd === 'translate' ? new Promise((r) => resolve = r) : Promise.resolve(), () => {}, 0);
  s.resume(); s.update('原文'); await tick(); s.suspend(); resolve('late'); await tick(); assert.equal(s.result, ''); assert.equal(s.canOutput, false);
});
test('custom shortcuts reject plain/Win keys and preserve valid combinations', () => {
  assert.equal(hotkeyFromEvent({ code: 'KeyT', key: 't' }), null);
  assert.equal(hotkeyFromEvent({ code: 'KeyT', key: 't', ctrlKey: true, metaKey: true }), null);
  const key = hotkeyFromEvent({ code: 'KeyY', key: 'y', ctrlKey: true, altKey: true }); assert.equal(displayHotkey(key), 'Ctrl+Alt+Y');
  assert.equal(displayHotkey(hotkeyFromEvent({ code: 'F12', key: 'F12', ctrlKey: true })), 'Ctrl+F12');
});
test('remote keyboards can report key names without physical codes', () => {
  assert.equal(displayHotkey(hotkeyFromEvent({ key: 'u', ctrlKey: true, altKey: true })), 'Ctrl+Alt+U');
  assert.equal(displayHotkey(hotkeyFromEvent({ key: '8', ctrlKey: true })), 'Ctrl+8');
  assert.equal(hotkeyFromEvent({ key: 'Unidentified', ctrlKey: true }), null);
});
