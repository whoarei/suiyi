import test from 'node:test';
import assert from 'node:assert/strict';
import { changeLanguagePair, formatLanguagePair } from '../languages.js';

test('selecting the other language swaps the pair without changing the saved object', () => {
  const original = { primaryLanguage: 'ja', secondaryLanguage: 'en' };
  assert.deepEqual(changeLanguagePair(original, 'primaryLanguage', 'en'), { primaryLanguage: 'en', secondaryLanguage: 'ja' });
  assert.deepEqual(changeLanguagePair(original, 'secondaryLanguage', 'ja'), { primaryLanguage: 'en', secondaryLanguage: 'ja' });
  assert.deepEqual(changeLanguagePair(original, 'secondaryLanguage', 'fr'), { primaryLanguage: 'ja', secondaryLanguage: 'fr' });
  assert.deepEqual(original, { primaryLanguage: 'ja', secondaryLanguage: 'en' });
});

test('pair labels include both languages and respect interface translations', () => {
  const languages = [{ code: 'ja', name: '日语' }, { code: 'en', name: '英语' }];
  const pair = { primaryLanguage: 'ja', secondaryLanguage: 'en' };
  assert.equal(formatLanguagePair(pair, languages), '日语 ⇄ 英语');
  assert.equal(formatLanguagePair(pair, languages, text => ({ 日语: 'Japanese', 英语: 'English' })[text]), 'Japanese ⇄ English');
});
