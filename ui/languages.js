// Selecting the other member swaps the pair, so both selectors stay usable.
export function changeLanguagePair(pair, field, language) {
  if (!['primaryLanguage', 'secondaryLanguage'].includes(field)) throw new Error('Unknown language field');
  const other = field === 'primaryLanguage' ? 'secondaryLanguage' : 'primaryLanguage';
  return { ...pair, [field]: language, [other]: language === pair[other] ? pair[field] : pair[other] };
}

export function formatLanguagePair(pair, languages, translate = text => text) {
  const name = code => translate(languages.find(language => language.code === code)?.name || code);
  return `${name(pair.primaryLanguage)} ⇄ ${name(pair.secondaryLanguage)}`;
}
