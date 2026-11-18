import { TranslationSession, hotkeyFromEvent, displayHotkey } from './controller.js';
import { createTranslator, localizeElement } from './i18n.js';
import { changeLanguagePair, formatLanguagePair } from './languages.js';
let t = (source) => source;
const { invoke } = window.__TAURI__.core;
const { listen } = window.__TAURI__.event;
const currentWindow = window.__TAURI__.window.getCurrentWindow();
const view = new URLSearchParams(location.search).get('view') || 'main';
const root = document.querySelector('#app');
const $ = (id) => document.getElementById(id);
const quickModeHint = '直接替换可编辑选区，只读选区打开翻译框。关闭后始终打开翻译框。';
const pairHint = '主要语言译为常用外语，其他语言译为主要语言。';
function languagePairInputs(primaryId, secondaryId) {
  let pair;
  const fields = { primaryLanguage: $(primaryId), secondaryLanguage: $(secondaryId) };
  function set(config) {
    pair = { primaryLanguage: config.primaryLanguage, secondaryLanguage: config.secondaryLanguage };
    for (const [field, select] of Object.entries(fields)) select.value = pair[field];
  }
  for (const [field, select] of Object.entries(fields)) {
    select.replaceChildren(...bootData.languages.map(({ code, name }) => {
      const option = document.createElement('option'); option.value = code; option.textContent = t(name); return option;
    }));
    select.onchange = () => set(changeLanguagePair(pair, field, select.value));
  }
  set(bootData.config);
  return { set, value: () => ({ ...pair }) };
}
const paths = {
  language: '<path d="M3 5h12M9 3v2M5 8c1 5 4 8 8 10M13 5c-1 6-5 11-10 13M14 21l4-10 4 10m-6-4h4"/>', back: '<path d="M19 12H5m7-7-7 7 7 7"/>', chevron: '<path d="m9 5 7 7-7 7"/>',
  close: '<path d="m5 5 14 14M19 5 5 19"/>', copy: '<rect x="8" y="8" width="12" height="12" rx="2"/><path d="M16 8V4H4v12h4"/>',
  check: '<path d="m5 12 4 4L19 6"/>', down: '<path d="m6 9 6 6 6-6"/>',
  settings: '<path d="m9 3-1 3-3 1 1 3-2 2 2 3-1 2 3 1 1 3h6l1-3 3-1-1-2 2-3-2-2 1-3-3-1-1-3Z"/><circle cx="12" cy="12" r="3"/>',
  eye: '<path d="M2 12s3-7 10-7 10 7 10 7-3 7-10 7S2 12 2 12Z"/><circle cx="12" cy="12" r="3"/>',
  download: '<path d="M12 3v12m-5-5 5 5 5-5M4 16v5h16v-5"/>', reset: '<path d="M4 10a8 8 0 1 1 1 8M4 4v6h6"/>',
  open: '<path d="M10 4H4v16h16v-6M13 3h8v8m0-8L10 14"/>', update: '<path d="M4 10a8 8 0 0 1 14-5l3 3M21 3v5h-5M20 14a8 8 0 0 1-14 5l-3-3m0 5v-5h5"/>',
  theme: '<circle cx="12" cy="12" r="9"/><path d="M12 3a9 9 0 0 1 0 18Z"/>', quick: '<path d="m13 2-9 12h7l-1 8 10-13h-7Z"/>', startup: '<path d="M7 4a9 9 0 1 0 10 0M12 2v10"/>', exit: '<path d="M9 4H4v16h5m5-13 5 5-5 5m-6-5h11"/>',
};
function icon(name, title = '', id = '') { const tag = title ? 'button' : 'span'; return `<${tag} ${title ? 'type="button"' : ''} class="icon" ${id ? `id="${id}"` : ''} title="${title}" aria-label="${title}"><svg viewBox="0 0 24 24" aria-hidden="true">${paths[name] || paths.open}</svg></${tag}>`; }
function header(title) { return `<header class="header"><strong>${title}</strong>${icon('close', '关闭', 'close')}</header>`; }
function status(text, error = false) { if (!$('status')) return; $('status').textContent = t(text); $('status').classList.toggle('error', error); }
function colors(values) { for (const [name, color] of Object.entries(values)) document.documentElement.style.setProperty(`--${name}`, color); }
let bootData;
async function boot() { bootData = await invoke('boot'); t = createTranslator(bootData.locale, bootData.messages); document.documentElement.lang = bootData.locale; document.title = t('随译'); colors(bootData.colors); return bootData; }
await listen('theme', (event) => colors(event.payload));
async function hide() { await invoke('hide_window'); }
function wireFrame() {
  $('close')?.addEventListener('click', hide);
  document.querySelector('.header')?.addEventListener('mousedown', (event) => { if (event.button === 0 && !event.target.closest('button')) invoke('drag'); });
  document.addEventListener('keydown', (event) => { if (event.key === 'Escape' && !event.isComposing) { event.preventDefault(); hide(); } });
}
// Observe content instead of assigning a fixed height so error messages never clip.
function fit() {
  const panel = document.querySelector('.panel');
  if (!panel) return;
  const bounds = panel.getBoundingClientRect();
  const width = view === 'main' ? bounds.width : { settings: 420, menu: 244, notice: 360, update: 420 }[view];
  const height = Math.ceil(bounds.height);
  if (width !== window.innerWidth || height !== window.innerHeight) {
    currentWindow.setSize(new window.__TAURI__.dpi.LogicalSize(width, height)).catch(() => {});
  }
}
new ResizeObserver(fit).observe(root);
await boot();

if (view === 'main') {
  root.innerHTML = `
    <section id="popup" class="panel popup" tabindex="-1" aria-label="翻译浮窗">
      <header class="header">
        <span id="translation-languages" class="direction"></span>
        ${icon('close', '收起', 'close')}
      </header>
      <div class="body">
        <div class="popup-content">
          <div id="source-section" class="popup-source">
            <span id="source-languages" class="direction" hidden></span>
            <textarea id="source" class="popup-text" rows="1" dir="auto" maxlength="2000" placeholder="输入文字…" aria-label="原文"></textarea>
          </div>
          <div id="result-section" class="popup-translation" hidden>
            <textarea id="result" class="popup-text" rows="1" dir="auto" maxlength="8000" placeholder="译文" aria-label="译文" readonly></textarea>
          </div>
        </div>
        <div id="toolbar" class="toolbar" hidden>
          <button id="source-toggle" type="button" aria-controls="source-section" aria-expanded="false" hidden>
            <span id="source-toggle-label">查看原文</span><svg viewBox="0 0 24 24" aria-hidden="true">${paths.down}</svg>
          </button>
          <span id="target" class="target" hidden></span>
          <div class="popup-actions">
            <button id="copy" type="button" class="icon" title="复制译文" aria-label="复制译文" disabled>
              <svg viewBox="0 0 24 24" aria-hidden="true"><g class="copy-mark">${paths.copy}</g><g class="copy-check">${paths.check}</g></svg>
            </button>
            <button id="primary" type="button" class="primary" title="Ctrl+Enter" aria-keyshortcuts="Control+Enter" hidden disabled>填入</button>
          </div>
        </div>
        <div id="popup-feedback" class="popup-feedback" hidden>
          <p id="status" class="status"></p><button id="retry" type="button" class="secondary" hidden>重试</button>
        </div>
        <span id="copy-announcement" class="popup-announcement" role="status"></span>
      </div>
    </section>`;
  const popup = $('popup'), source = $('source'), result = $('result');
  const primary = $('primary'), copyButton = $('copy');
  let context = bootData.context || {};
  let selectionDraft = false, preview = false, sourceExpanded = false;
  let busy = false, resultComposing = false, copyTimer, focusTimer;
  const session = new TranslationSession(invoke, (s) => {
    const hasSource = Boolean(s.source.trim());
    const error = s.phase === 'error';
    popup.toggleAttribute('data-preview', preview);
    popup.toggleAttribute('data-copy-only', !context.hasTarget);
    $('translation-languages').hidden = preview;
    $('source-languages').hidden = !preview;
    $('source-section').hidden = preview && !sourceExpanded && hasSource;
    $('source-toggle').hidden = !preview;
    $('source-toggle').setAttribute('aria-expanded', String(sourceExpanded));
    $('source-toggle-label').textContent = t(sourceExpanded ? '收起原文' : '查看原文');
    $('result-section').hidden = !hasSource;
    $('toolbar').hidden = !hasSource;
    if (!resultComposing && result.value !== s.result) result.value = s.result;
    result.readOnly = !['ready', 'edited'].includes(s.phase) || busy;
    result.placeholder = t(({ waiting: '等待翻译…', translating: '翻译中…', composing: '选词中…', error: '翻译未完成' })[s.phase] || '译文');
    primary.disabled = copyButton.disabled = !s.canOutput || resultComposing || busy;
    primary.hidden = !context.hasTarget;
    primary.textContent = t(busy ? '…' : selectionDraft ? '替换' : '填入');
    if (context.hasTarget) copyButton.removeAttribute('aria-keyshortcuts');
    else copyButton.setAttribute('aria-keyshortcuts', 'Control+Enter');
    copyButton.title = t('复制译文') + (context.hasTarget ? '' : ' · Ctrl+Enter');
    $('target').hidden = !context.hasTarget || !context.targetName;
    $('target').textContent = context.hasTarget ? t(context.targetName || '') : '';
    $('target').title = $('target').textContent;
    status(error ? s.message : context.message || '', error || (context.error ?? !!context.message));
    $('retry').hidden = !error;
    $('popup-feedback').hidden = !error && !context.message;
    fit();
  });

  function clearCopyFeedback() {
    clearTimeout(copyTimer);
    copyButton.removeAttribute('data-copied');
    $('copy-announcement').textContent = '';
  }
  function resetSelection() {
    preview = selectionDraft = sourceExpanded = false;
    source.value = '';
    session.clear();
  }
  function open(next) {
    context = next || {};
    sourceExpanded = false;
    clearCopyFeedback();
    if (typeof context.selected === 'string') {
      selectionDraft = true;
      preview = !context.hasTarget;
      source.value = context.selected;
      session.update(context.selected);
    } else if (selectionDraft) {
      resetSelection();
    }
    session.resume();
    clearTimeout(focusTimer);
    focusTimer = setTimeout(() => (preview ? popup : source).focus({ preventScroll: true }), 30);
  }
  $('source-toggle').onclick = () => {
    sourceExpanded = !sourceExpanded;
    session.emit();
  };
  source.addEventListener('input', () => {
    clearCopyFeedback();
    context.message = '';
    session.update(source.value);
  });
  source.addEventListener('compositionstart', () => {
    clearCopyFeedback();
    session.setComposing(true);
  });
  source.addEventListener('compositionend', () => { session.source = source.value; session.setComposing(false); });
  result.addEventListener('input', () => { clearCopyFeedback(); session.edit(result.value); });
  result.addEventListener('compositionstart', () => { clearCopyFeedback(); resultComposing = true; session.emit(); });
  result.addEventListener('compositionend', () => { resultComposing = false; session.edit(result.value); });
  async function output(copy = !context.hasTarget) {
    if (!session.canOutput || busy || resultComposing) return;
    busy = true; session.emit();
    try {
      const receipt = await invoke('output', { text: session.result, serial: session.serial, copy });
      if (receipt.outcome === 'copied') {
        clearCopyFeedback();
        copyButton.setAttribute('data-copied', '');
        $('copy-announcement').textContent = t(receipt.message);
        copyTimer = setTimeout(clearCopyFeedback, 1500);
        context.message = '';
        context.error = false;
      } else {
        context.hasTarget = false;
        context.message = receipt.message;
        context.error = ['rejected', 'uncertain'].includes(receipt.outcome);
        if (receipt.outcome === 'submitted') resetSelection();
      }
    } catch (error) { context.message = String(error); context.error = true; }
    finally { busy = false; session.emit(); }
  }
  primary.onclick = () => output(); copyButton.onclick = () => output(true);
  $('retry').onclick = () => session.retry();
  document.addEventListener('keydown', (event) => { if (event.ctrlKey && event.key === 'Enter' && !event.isComposing) { event.preventDefault(); output(); } });
  await listen('open', (event) => open(event.payload));
  await listen('clear', () => { clearCopyFeedback(); resetSelection(); });
  function renderLanguages(config) {
    const pair = formatLanguagePair(config, bootData.languages, t);
    for (const id of ['translation-languages', 'source-languages']) {
      $(id).textContent = pair;
      $(id).title = t(pairHint);
    }
  }
  renderLanguages(bootData.config);
  await listen('config-changed', ({ payload }) => { clearCopyFeedback(); renderLanguages(payload); session.update(session.source); });
  window.addEventListener('blur', () => { if (!busy) { session.suspend(); context.hasTarget = false; } });
  window.addEventListener('focus', () => { if (!session.suspended) return; session.resume(); });
  wireFrame(); open(context);
} else if (view === 'settings') {
  root.innerHTML = `<section class="panel settings">${header('设置')}<div class="body"><div class="row"><label for="address">API</label><div class="field input-group"><input id="address" placeholder="https://example.com/v1" autocomplete="off" spellcheck="false"></div></div><div class="row"><label for="key">Key</label><div class="field input-group"><input id="key" type="password" placeholder="API Key" autocomplete="off" spellcheck="false">${icon('eye', '显示密钥', 'eye')}</div></div><div class="row"><label for="model">模型</label><div class="field input-group"><input id="model" placeholder="模型名称" autocomplete="off" spellcheck="false">${icon('download', '获取模型', 'models')}<div class="model-options hidden" id="model-options"></div></div></div><div class="row"><label for="primary-language">主要语言</label><div class="field input-group"><select id="primary-language" aria-label="主要语言"></select></div></div><div class="row"><label for="secondary-language">常用外语</label><div class="field input-group"><select id="secondary-language" aria-label="常用外语"></select></div></div><div class="row"><label for="shortcut">快捷键</label><div class="field input-group"><button id="shortcut" class="hotkey" title="点击后按下新组合键"></button>${icon('reset', '恢复默认快捷键', 'reset')}</div></div><div class="settings-bottom"><label class="switch-label"><input id="quick" class="switch" type="checkbox">快速模式</label><button class="secondary" id="test">测试</button><button class="primary" id="save">保存</button></div><p class="status" id="status"></p></div></section>`;
  $('quick').closest('label').title = t(quickModeHint);
  const languageEditor = languagePairInputs('primary-language', 'secondary-language');
  $('primary-language').title = $('secondary-language').title = t(pairHint);
  let hotkey, origin = null, waiting = false, recording = false;
  function masked() { $('key').type = 'password'; $('eye').title = t('显示密钥'); $('eye').setAttribute('aria-label', t('显示密钥')); }
  async function load() {
    const data = await boot(); $('address').value = data.config.address; $('model').value = data.config.model;
    $('key').value = ''; $('key').placeholder = t(data.config.hasKey ? '已保存，留空保留' : 'API Key');
    languageEditor.set(data.config);
    hotkey = data.preferences.hotkey; $('shortcut').textContent = displayHotkey(hotkey); $('quick').checked = data.preferences.quickMode;
    origin = null; masked(); status(''); $('model-options').classList.add('hidden');
  }
  function input() { return { address: $('address').value.trim(), model: $('model').value.trim(), key: $('key').value, hotkey, quickMode: $('quick').checked, ...languageEditor.value() }; }
  async function request(task) {
    if (waiting) return; waiting = true;
    document.querySelectorAll('.row input,.row button,.row select,#test,#save,#quick').forEach((el) => el.disabled = true); status('正在连接…');
    try { await task(); } catch (error) { status(String(error), true); }
    finally { waiting = false; document.querySelectorAll('.row input,.row button,.row select,#test,#save,#quick').forEach((el) => el.disabled = false); }
  }
  $('address').addEventListener('input', () => { if (origin !== null) { $('key').value = ''; origin = null; } masked(); $('model-options').classList.add('hidden'); status(''); });
  $('eye').onclick = async () => {
    if ($('key').type === 'text') return masked();
    try { if (!$('key').value) { $('key').value = await invoke('reveal_key', { address: $('address').value.trim() }); if ($('key').value) origin = $('address').value; } $('key').type = 'text'; $('eye').title = t('隐藏密钥'); $('eye').setAttribute('aria-label', t('隐藏密钥')); } catch (error) { status(String(error), true); }
  };
  $('shortcut').addEventListener('focus', async () => { recording = true; $('shortcut').textContent = t('按下组合键…'); await invoke('record_shortcut', { recording: true }); });
  $('shortcut').addEventListener('keydown', (event) => { if (event.key === 'Tab') return; event.preventDefault(); event.stopPropagation(); const next = hotkeyFromEvent(event); if (next) { hotkey = next; $('shortcut').textContent = displayHotkey(next); } });
  $('shortcut').addEventListener('blur', async () => { recording = false; $('shortcut').textContent = displayHotkey(hotkey); const restored = await invoke('record_shortcut', { recording: false }); if (!restored) status('快捷键暂时被占用，请更换后保存。', true); });
  $('reset').onclick = () => { hotkey = { modifiers: 3, virtualKey: 0x54 }; $('shortcut').textContent = displayHotkey(hotkey); };
  $('models').onclick = () => request(async () => { const { address, key } = input(); const models = await invoke('models', { address, key }); const list = $('model-options'); list.replaceChildren(); for (const model of models) { const button = document.createElement('button'); button.textContent = model; button.onclick = () => { $('model').value = model; list.classList.add('hidden'); }; list.append(button); } list.classList.toggle('hidden', !models.length); status(models.length ? t('已获取 {count} 个模型', { count: models.length }) : '未返回模型，可手动填写。'); });
  $('test').onclick = () => request(async () => { const { address, model, key, primaryLanguage, secondaryLanguage } = input(); await invoke('test_connection', { address, model, key, primaryLanguage, secondaryLanguage }); status('测试通过'); });
  $('save').onclick = async () => { if (waiting || recording) return; try { await invoke('save_settings', { input: input() }); $('key').value = ''; masked(); await hide(); } catch (error) { status(String(error), true); } };
  window.addEventListener('blur', masked);
  await listen('settings-open', load);
  await listen('config-changed', ({ payload }) => languageEditor.set(payload));
  await listen('settings-closed', () => { $('key').value = ''; origin = null; masked(); });
  wireFrame(); $('close').addEventListener('click', () => { $('key').value = ''; origin = null; masked(); });
  document.addEventListener('keydown', (event) => { if (event.key === 'Escape') { $('key').value = ''; masked(); } });
  await load();
} else if (view === 'menu') {
  root.innerHTML = `<section class="panel menu"><div id="menu-home"><button class="item" data-action="open">${icon('open')}打开随译</button><button class="item" data-action="settings">${icon('settings')}设置</button><button class="item" data-action="update">${icon('update')}检查更新</button><button class="item" id="languages-toggle" aria-controls="language-page" aria-expanded="false">${icon('language')}<span class="language-summary"><span class="language-caption">语言组合</span><span id="language-name"></span></span><span class="trailing">${icon('chevron')}</span></button><button class="item" id="themes-toggle">${icon('theme')}主题<span class="trailing" id="theme-name"></span></button><div class="themes" id="themes" hidden><div class="theme-grid" id="theme-grid"></div></div><hr><button class="item" data-action="quick">${icon('quick')}快速模式<input class="switch trailing" id="quick-state" type="checkbox" tabindex="-1" aria-label="快速模式状态"></button><button class="item" data-action="startup">${icon('startup')}开机启动<input class="switch trailing" id="startup-state" type="checkbox" tabindex="-1" aria-label="开机启动状态"></button><hr><button class="item" data-action="exit">${icon('exit')}退出</button></div><div id="language-page" hidden><div class="language-header">${icon('back', '返回', 'language-back')}<strong>语言组合</strong></div><div class="language-pair-fields"><label for="menu-primary">主要语言</label><div class="field input-group"><select id="menu-primary" aria-label="主要语言"></select></div><label for="menu-secondary">常用外语</label><div class="field input-group"><select id="menu-secondary" aria-label="常用外语"></select></div><p class="status">主要语言译为常用外语，其他语言译为主要语言。</p><button id="language-save" class="primary">保存</button></div><p id="language-status" class="status error" role="status"></p></div></section>`;
  document.querySelector('[data-action="quick"]').title = t(quickModeHint);
  const languageEditor = languagePairInputs('menu-primary', 'menu-secondary');
  let languagePending = false;
  function showHome(focus = true) {
    $('language-page').hidden = true; $('menu-home').hidden = false;
    $('languages-toggle').setAttribute('aria-expanded', 'false');
    if (focus) $('languages-toggle').focus(); fit();
  }
  async function saveLanguages() {
    if (languagePending) return;
    languagePending = true; $('language-status').textContent = '';
    document.querySelectorAll('#menu-primary,#menu-secondary,#language-save').forEach(button => button.disabled = true);
    try { await invoke('set_language_pair', languageEditor.value()); }
    catch (error) { $('language-status').textContent = t(String(error)); }
    finally { languagePending = false; document.querySelectorAll('#menu-primary,#menu-secondary,#language-save').forEach(button => button.disabled = false); fit(); }
  }
  async function render() {
    const data = await boot();
    $('quick-state').checked = data.preferences.quickMode; $('startup-state').checked = data.preferences.autoStart;
    $('theme-name').textContent = t(data.palettes.find(p => p[0] === data.preferences.theme)?.[1] || '橙色');
    const grid = $('theme-grid'); grid.replaceChildren();
    for (const [id, name, color] of data.palettes) {
      const button = document.createElement('button'); button.className = `swatch${id === data.preferences.theme ? ' selected' : ''}`;
      button.style.setProperty('--swatch', color); button.title = t(name); button.setAttribute('aria-label', t(name));
      button.onclick = () => invoke('action', { name: 'theme', value: id }); grid.append(button);
    }
    const summary = formatLanguagePair(data.config, data.languages, t);
    $('language-name').textContent = summary; $('languages-toggle').title = summary;
    languageEditor.set(data.config);
    fit();
  }
  document.querySelectorAll('[data-action]').forEach(button => button.onclick = () => invoke('action', { name: button.dataset.action }).catch(() => console.warn('操作未完成')));
  $('themes-toggle').onclick = () => { $('themes').hidden = !$('themes').hidden; fit(); };
  $('languages-toggle').onclick = async () => {
    $('themes').hidden = true; $('language-status').textContent = '';
    $('menu-home').hidden = true; $('language-page').hidden = false; $('languages-toggle').setAttribute('aria-expanded', 'true');
    await render(); $('menu-primary').focus(); fit();
  };
  $('language-back').onclick = () => showHome();
  $('language-save').onclick = saveLanguages;
  await listen('menu-open', () => { showHome(false); $('themes').hidden = true; $('language-status').textContent = ''; render(); });
  await listen('preferences-changed', render); await listen('config-changed', render); await render();
  document.addEventListener('keydown', event => { if (event.key === 'Escape') { event.preventDefault(); if (!$('language-page').hidden) showHome(); else hide(); } });
} else if (view === 'notice') {
  root.innerHTML = '<section class="panel notice"><span class="dot"></span><p id="notice-text"></p></section>';
  await listen('notice', (event) => { $('notice-text').textContent = t(event.payload); fit(); });
} else if (view === 'update') {
  root.innerHTML = `<section class="panel update">${header('检查更新')}<div class="body"><h2 id="update-title">正在检查…</h2><p id="update-detail">正在获取最新正式版本。</p><progress id="progress"></progress><footer><span class="version">${t('当前 v{version}', { version: bootData.version })}</span><button id="update-action" class="primary hidden">重试</button></footer></div></section>`;
  let result = null, downloading = false;
  async function check() {
    if (downloading) return;
    result = null;
    $('progress').classList.remove('hidden');
    $('progress').removeAttribute('value');
    $('update-action').classList.add('hidden');
    $('update-title').textContent = t('正在检查…');
    try {
      result = await invoke('check_update');
      $('update-title').textContent = result.available ? t('发现新版本 v{version}', { version: result.latest }) : t(result.ahead ? '当前版本较新' : '已是最新版本');
      $('update-detail').textContent = result.available ? t(result.installer ? '安装完成后自动重启，配置与密钥保留。' : '请前往发布页面下载安装。') : result.ahead ? t('最新正式版为 v{version}。', { version: result.latest }) : t('可以继续使用随译。');
      $('update-action').textContent = t(result.installer ? '下载并安装' : '前往下载');
      $('update-action').classList.toggle('hidden', !result.available);
    } catch (error) {
      $('update-title').textContent = t('未能检查更新');
      $('update-detail').textContent = t(String(error));
      $('update-action').textContent = t('重试');
      $('update-action').classList.remove('hidden');
    } finally { $('progress').classList.add('hidden'); }
  }
  $('update-action').onclick = async () => { if (downloading) return; if (!result) return check(); if (!result.installer) return invoke('release_page'); downloading = true; $('update-action').classList.add('hidden'); $('progress').classList.remove('hidden'); $('progress').removeAttribute('value'); $('update-title').textContent = t('正在下载安装包…'); try { const state = await invoke('install_update'); if (state === 'ready') { $('update-title').textContent = t('下载完成'); $('update-detail').textContent = t('请完成当前操作并关闭设置，再点击安装更新。'); $('update-action').textContent = t('安装更新'); } } catch (error) { $('update-title').textContent = t('未能完成更新'); $('update-detail').textContent = t(String(error)); $('update-action').textContent = t('重试'); } finally { downloading = false; $('progress').classList.add('hidden'); $('update-action').classList.remove('hidden'); } };
  await listen('download-progress', ({ payload: p }) => { $('progress').max = p.total; $('progress').value = p.received; $('update-detail').textContent = t('{received} / {total} MB · 关闭可取消', { received: (p.received / 1048576).toFixed(1), total: (p.total / 1048576).toFixed(1) }); });
  await listen('check-update', check); wireFrame(); if (await currentWindow.isVisible()) await check();
}
localizeElement(root, t);
fit();
