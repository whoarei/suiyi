import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import { createTranslator } from '../i18n.js';

const messages = JSON.parse(fs.readFileSync(new URL('../../src-tauri/locales/en.json', import.meta.url), 'utf8'));
test('interface language is independent of translation languages and covers errors', () => {
  const en = createTranslator('en', messages), zh = createTranslator('zh-CN', messages);
  for (const [source, english] of Object.entries(messages)) {
    assert.equal(en(source), english);
    assert.equal(zh(source), source);
    assert.equal(/[\p{Script=Han}]/u.test(english), false, source);
  }
  assert.equal(en('日语'), 'Japanese');
  assert.equal(en('认证失败，请检查 API 地址和 Key。'), 'Authentication failed. Check the API endpoint and key.');
  assert.equal(en('namespace/model'), 'namespace/model');
});
test('localized dynamic messages preserve values without interpreting them', () => {
  const en = createTranslator('en', messages), zh = createTranslator('zh-CN', messages);
  assert.equal(en('已获取 {count} 个模型', { count: 12 }), '12 models found');
  assert.equal(zh('已获取 {count} 个模型', { count: 12 }), '已获取 12 个模型');
  assert.equal(en('发现新版本 v{version}', { version: '0.4.0' }), 'Update available: v0.4.0');
  assert.equal(en('当前 v{version}', { version: '$& {count}' }), 'Current v$& {count}');
});
test('every fixed backend diagnostic has an English translation', () => {
  const folder = new URL('../../src-tauri/src/', import.meta.url);
  const examples = new Set(['你好，世界。', 'こんにちは、世界。', '第一行。\\n\\n第二行。']);
  for (const file of fs.readdirSync(folder).filter(f => f.endsWith('.rs') && !f.endsWith('_tests.rs'))) {
    const source = fs.readFileSync(new URL(file, folder), 'utf8').split('#[cfg(test)]')[0];
    for (const match of source.matchAll(/"([^"\n]*[\p{Script=Han}][^"\n]*。)"/gu)) {
      if (!examples.has(match[1])) assert.ok(messages[match[1]], `${file}: ${match[1]}`);
    }
  }
});
