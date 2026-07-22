const bridge = window.chrome?.webview;
const hotspots = [...document.querySelectorAll('.hotspot')];
const controllerCard = document.querySelector('#controller-card');
const keyTitle = document.querySelector('#key-title');
const keySymbol = document.querySelector('#key-symbol');
const functionText = document.querySelector('#function-text');
const shortcutText = document.querySelector('#shortcut-text');
const keyOrbit = document.querySelector('.key-orbit');
const statusPill = document.querySelector('#status-pill');
const statusText = document.querySelector('#status-text');
const startButton = document.querySelector('#start-button');
const stopButton = document.querySelector('#stop-button');
const mappingPreview = document.querySelector('#mapping-preview');
const mappingPreviewButton = document.querySelector('#mapping-preview-button');
const mappingPreviewLabel = document.querySelector('#mapping-preview-label');
const mappingPreviewContent = document.querySelector('#mapping-preview-content');
const mappingPreviewCount = document.querySelector('#mapping-preview-count');
const detailCard = document.querySelector('.detail-card');
const contextConfig = document.querySelector('#context-config');
const assistantConfig = document.querySelector('#assistant-config');
const assistantModeButtons = [...document.querySelectorAll('[data-assistant-mode]')];
const assistantModeHint = document.querySelector('#assistant-mode-hint');
const planConfig = document.querySelector('#plan-config');
const planShortcutInput = document.querySelector('#plan-shortcut-input');
const planShortcutHint = document.querySelector('#plan-shortcut-hint');

let mappings = {};
let currentKey = null;
let previewOpen = false;
let selectionPinned = false;
let settings = { assistantMode: 'typeless', planShortcut: '' };

const symbolByKey = {
  North: 'Y', West: 'X', East: 'B', South: 'A', Guide: 'S',
  View: '•••', Menu: '≡', StadiaAssistant: '••••', StadiaCapture: '⌗',
  LeftTrigger: 'L2', RightTrigger: 'R2', LeftShoulder: 'L1', RightShoulder: 'R1',
  LeftStick: 'L3', RightStick: 'R3',
  DpadUp: '↑', DpadDown: '↓', DpadLeft: '←', DpadRight: '→',
  LeftStickUp: '↑', LeftStickDown: '↓', LeftStickLeft: '←', LeftStickRight: '→',
  RightStickUp: '↑', RightStickDown: '↓', RightStickLeft: '←', RightStickRight: '→'
};

const mappingGroups = [
  { title: '主要按键', caption: '提交、取消与任务操作', keys: ['South', 'East', 'West', 'North', 'Guide'] },
  { title: '任务与语音', caption: '任务切换、项目与听写', keys: ['View', 'Menu', 'LeftShoulder', 'RightShoulder', 'StadiaCapture', 'StadiaAssistant', 'LeftTrigger', 'RightTrigger'] },
  { title: '十字键导航', caption: 'Codex 面板与工具窗口', keys: ['DpadUp', 'DpadDown', 'DpadLeft', 'DpadRight'] },
  { title: '摇杆操作', caption: '滚动、推理强度与方向键', keys: ['LeftStickUp', 'LeftStickDown', 'LeftStickLeft', 'LeftStickRight', 'LeftStick', 'RightStickUp', 'RightStickDown', 'RightStickLeft', 'RightStickRight', 'RightStick'] }
];

function send(command, value) {
  bridge?.postMessage({ type: 'command', command, value });
}

function updateConfigEditor(key) {
  const assistantSelected = key === 'StadiaAssistant';
  const planSelected = key === 'RightTrigger';
  const visible = assistantSelected || planSelected;
  contextConfig.hidden = !visible;
  assistantConfig.hidden = !assistantSelected;
  planConfig.hidden = !planSelected;
  detailCard.classList.toggle('config-active', visible);

  if (assistantSelected) {
    assistantModeButtons.forEach(button => {
      const active = button.dataset.assistantMode === settings.assistantMode;
      button.classList.toggle('active', active);
      button.setAttribute('aria-pressed', String(active));
    });
    assistantModeHint.textContent = settings.assistantMode === 'codex'
      ? '按住 Assistant 开始 Codex 听写，松开后结束。'
      : '按一下开始，再按一下结束并插入识别结果。';
  }
  if (planSelected && document.activeElement !== planShortcutInput) {
    planShortcutInput.value = settings.planShortcut || '';
    planShortcutHint.textContent = settings.planShortcut
      ? `当前快捷键：${settings.planShortcut}`
      : '尚未配置；留空保存可禁用 R2 输出。';
  }
}

function selectKey(key) {
  const item = mappings[key] || {
    display: key,
    function: '正在读取映射配置…',
    shortcut: '—'
  };
  currentKey = key;
  hotspots.forEach(node => node.classList.toggle('active', node.dataset.key === key));
  keyTitle.textContent = item.display || key;
  keySymbol.textContent = symbolByKey[key] || (item.display || '?').slice(0, 2);
  functionText.textContent = item.function || '未映射';
  shortcutText.textContent = item.shortcut || '未设置快捷键';
  updateConfigEditor(key);
  keyOrbit.classList.remove('bump');
  requestAnimationFrame(() => keyOrbit.classList.add('bump'));
  window.setTimeout(() => keyOrbit.classList.remove('bump'), 220);
}

function resetSelection(force = false) {
  if (selectionPinned && !force) return;
  if (document.activeElement?.classList?.contains('hotspot')) return;
  currentKey = null;
  hotspots.forEach(node => node.classList.remove('active'));
  keyTitle.textContent = '选择一个按键';
  keySymbol.textContent = '?';
  functionText.textContent = '将鼠标移到手柄上的按键，查看对应的 Codex 功能。';
  shortcutText.textContent = '等待选择';
  updateConfigEditor(null);
}

function renderMappingPreview() {
  mappingPreviewContent.replaceChildren();
  const allKeys = mappingGroups.flatMap(group => group.keys);
  mappingPreviewCount.textContent = String(allKeys.length);

  mappingGroups.forEach((group, groupIndex) => {
    const section = document.createElement('section');
    section.className = 'mapping-group';
    section.style.setProperty('--group-index', String(groupIndex));

    const heading = document.createElement('div');
    heading.className = 'mapping-group-heading';
    const titleWrap = document.createElement('div');
    const title = document.createElement('h3');
    title.textContent = group.title;
    const caption = document.createElement('p');
    caption.textContent = group.caption;
    const count = document.createElement('span');
    count.textContent = `${group.keys.length} 项`;
    titleWrap.append(title, caption);
    heading.append(titleWrap, count);

    const list = document.createElement('div');
    list.className = 'mapping-preview-list';
    group.keys.forEach(key => {
      const item = mappings[key] || { display: key, function: '未映射', shortcut: '—' };
      const row = document.createElement('div');
      row.className = 'mapping-preview-item';
      if (!mappings[key] || item.function === '未映射') row.classList.add('unmapped');

      const symbol = document.createElement('span');
      symbol.className = 'mapping-preview-symbol';
      symbol.textContent = symbolByKey[key] || (item.display || '?').slice(0, 2);
      const copy = document.createElement('div');
      const name = document.createElement('strong');
      name.textContent = item.display || key;
      const action = document.createElement('span');
      action.textContent = item.function || '未映射';
      copy.append(name, action);
      const shortcut = document.createElement('code');
      shortcut.textContent = item.shortcut || '—';
      row.append(symbol, copy, shortcut);
      list.append(row);
    });

    section.append(heading, list);
    mappingPreviewContent.append(section);
  });
}

function setMappingPreview(open) {
  previewOpen = open;
  mappingPreview.classList.toggle('open', open);
  mappingPreview.setAttribute('aria-hidden', String(!open));
  mappingPreviewButton.setAttribute('aria-expanded', String(open));
  mappingPreviewButton.classList.toggle('active', open);
  mappingPreviewLabel.textContent = open ? '关闭预览' : '全部映射';
  document.body.classList.toggle('preview-open', open);
  if (open) renderMappingPreview();
}

function applyState(state) {
  if (state.mappings) mappings = state.mappings;
  if (state.settings) settings = { ...settings, ...state.settings };
  if (state.status) statusText.textContent = state.status.replace(/^●\s*/, '');
  statusPill.dataset.tone = state.tone || 'warning';
  const running = Boolean(state.running);
  document.body.dataset.running = String(running);
  startButton.disabled = running;
  stopButton.disabled = !running;
  if (currentKey) selectKey(currentKey);
  if (previewOpen) renderMappingPreview();
}

hotspots.forEach(node => {
  node.addEventListener('pointerenter', () => {
    if (!selectionPinned) selectKey(node.dataset.key);
  });
  node.addEventListener('focus', () => selectKey(node.dataset.key));
  node.addEventListener('click', event => {
    event.stopPropagation();
    selectionPinned = true;
    selectKey(node.dataset.key);
  });
});

const controllerMap = document.querySelector('.controller-map');
controllerMap.addEventListener('pointerleave', () => resetSelection());
controllerMap.addEventListener('click', event => {
  if (event.target.closest?.('.hotspot')) return;
  selectionPinned = false;
  resetSelection(true);
});
controllerMap.addEventListener('focusout', event => {
  if (!event.relatedTarget?.classList?.contains('hotspot')) resetSelection();
});

controllerCard.addEventListener('pointermove', event => {
  const bounds = controllerCard.getBoundingClientRect();
  controllerCard.style.setProperty('--pointer-x', `${event.clientX - bounds.left}px`);
  controllerCard.style.setProperty('--pointer-y', `${event.clientY - bounds.top}px`);
});

startButton.addEventListener('click', () => send('start'));
stopButton.addEventListener('click', () => send('stop'));
mappingPreviewButton.addEventListener('click', () => setMappingPreview(!previewOpen));
document.querySelector('#mapping-preview-close').addEventListener('click', () => setMappingPreview(false));
document.querySelector('#mapping-preview-backdrop').addEventListener('click', () => setMappingPreview(false));
document.querySelector('#export-button').addEventListener('click', () => send('export'));
document.querySelector('#tray-button').addEventListener('click', () => send('tray'));
assistantModeButtons.forEach(button => {
  button.addEventListener('click', () => {
    const mode = button.dataset.assistantMode;
    settings.assistantMode = mode;
    updateConfigEditor('StadiaAssistant');
    send('set-assistant-mode', mode);
  });
});

function savePlanShortcut() {
  const shortcut = planShortcutInput.value.trim();
  settings.planShortcut = shortcut;
  planShortcutHint.textContent = shortcut ? '正在保存并重载桥接服务…' : '正在清除快捷键…';
  send('set-plan-shortcut', shortcut);
}

document.querySelector('#save-plan-shortcut').addEventListener('click', savePlanShortcut);
planShortcutInput.addEventListener('keydown', event => {
  if (event.key !== 'Enter') return;
  event.preventDefault();
  savePlanShortcut();
});
document.addEventListener('keydown', event => {
  if (event.key === 'Escape' && previewOpen) setMappingPreview(false);
});

bridge?.addEventListener('message', event => {
  const message = event.data;
  if (message?.type === 'state') applyState(message);
});

bridge?.postMessage({ type: 'ready' });
