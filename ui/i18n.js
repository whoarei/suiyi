// Interface language follows Windows. Translation target language is independent.
export function createTranslator(locale, messages = {}) {
  return (source, values = {}) => {
    const template = locale === 'en' ? messages[source] ?? source : source;
    return template.replace(/\{(\w+)\}/g, (match, key) => Object.hasOwn(values, key) ? String(values[key]) : match);
  };
}

export function localizeElement(root, translate) {
  for (const element of [root, ...root.querySelectorAll('*')]) {
    for (const attribute of ['title', 'placeholder', 'aria-label']) {
      if (element.hasAttribute(attribute)) element.setAttribute(attribute, translate(element.getAttribute(attribute)));
    }
    for (const node of element.childNodes) {
      if (node.nodeType === 3 && node.textContent.trim()) {
        const content = node.textContent;
        node.textContent = content.replace(content.trim(), translate(content.trim()));
      }
    }
  }
}
