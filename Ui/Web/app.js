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
const liveBadge = document.querySelector('#live-badge');
const liveText = document.querySelector('#live-text');
const connectionNotice = document.querySelector('#connection-notice');
const connectionNoticeTitle = document.querySelector('#connection-notice-title');
const connectionNoticeMessage = document.querySelector('#connection-notice-message');
const connectionNoticeAction = document.querySelector('#connection-notice-action');
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
const shortcutEditButton = document.querySelector('#shortcut-edit-button');
const mappingUnbindButton = document.querySelector('#mapping-unbind-button');
const shortcutEditPanel = document.querySelector('#shortcut-edit-panel');
const shortcutEditInput = document.querySelector('#shortcut-edit-input');
const shortcutEditHint = document.querySelector('#shortcut-edit-hint');
const shortcutSaveButton = document.querySelector('#shortcut-save-button');
const shortcutCancelButton = document.querySelector('#shortcut-cancel-button');
const autoStartToggle = document.querySelector('#auto-start-toggle');
const controllerModel = document.querySelector('#controller-model');
const footerControllerModel = document.querySelector('#footer-controller-model');
const profileFileName = document.querySelector('#profile-file-name');
const interactionCount = document.querySelector('#interaction-count');

let mappings = {};
let currentKey = null;
let previewOpen = false;
let selectionPinned = false;
let shortcutEditing = false;
let shortcutSaving = false;
let mappingToggleSaving = false;
let editingKey = null;
let controllerFamily = 'stadia';
let lastInputSignature = '';
let settings = { assistantMode: 'typeless', autoStartEnabled: false };
const controllerLayouts = [...document.querySelectorAll('[data-controller-family]')];
const controllerMap = document.querySelector('.controller-map');
const previewControllerFamily = new URLSearchParams(window.location.search).get('controller');

const symbolByKey = {
  North: 'Y', West: 'X', East: 'B', South: 'A', Guide: 'S', Misc1: '↥',
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

const xboxMappingGroups = [
  { title: '主要按键', caption: '提交、取消与任务操作', keys: ['South', 'East', 'West', 'North', 'Guide'] },
  { title: '任务与模式', caption: 'View 分支当前对话；Share 新聊天', keys: ['View', 'Menu', 'Misc1', 'LeftShoulder', 'RightShoulder', 'LeftTrigger', 'RightTrigger'] },
  { title: '十字键导航', caption: 'Codex 面板与工具窗口', keys: ['DpadUp', 'DpadDown', 'DpadLeft', 'DpadRight'] },
  { title: '摇杆操作', caption: '滚动、推理强度与方向键', keys: ['LeftStickUp', 'LeftStickDown', 'LeftStickLeft', 'LeftStickRight', 'LeftStick', 'RightStickUp', 'RightStickDown', 'RightStickLeft', 'RightStickRight', 'RightStick'] }
];

function activeMappingGroups() {
  return controllerFamily === 'xbox' ? xboxMappingGroups : mappingGroups;
}

function symbolForKey(key, item) {
  if (key === 'Guide') return controllerFamily === 'xbox' ? '◉' : 'S';
  if (key === 'Misc1') return '↥';
  return symbolByKey[key] || (item?.display || '?').slice(0, 2);
}

function applyControllerFamily(family) {
  const next = family === 'xbox' ? 'xbox' : 'stadia';
  if (controllerFamily === next && controllerLayouts.some(layout => !layout.hasAttribute('hidden') && layout.dataset.controllerFamily === next)) return;
  controllerFamily = next;
  document.body.dataset.controllerFamily = next;
  controllerLayouts.forEach(layout => layout.toggleAttribute('hidden', layout.dataset.controllerFamily !== next));
  controllerMap.setAttribute('aria-label', next === 'xbox' ? 'Xbox Series 手柄交互映射' : 'Stadia 手柄交互映射');
  selectionPinned = false;
  resetSelection(true);
}

function pulseControllerInput(key, timestamp) {
  if (!key || !timestamp) return;
  const signature = `${key}|${timestamp}`;
  if (signature === lastInputSignature) return;
  lastInputSignature = signature;
  const visibleLayout = controllerLayouts.find(layout => !layout.hasAttribute('hidden'));
  const targets = [...(visibleLayout?.querySelectorAll('.hotspot') || [])]
    .filter(node => node.dataset.key === key);
  targets.forEach(node => {
    node.classList.remove('input-pulse');
    requestAnimationFrame(() => node.classList.add('input-pulse'));
    window.setTimeout(() => node.classList.remove('input-pulse'), 420);
  });
}

function send(command, value) {
  bridge?.postMessage({ type: 'command', command, value });
}

function updateConfigEditor(key) {
  const assistantSelected = key === 'North'
    && mappings[key]?.enabled !== false
    && ['TypelessToggle', 'CodexDictation'].includes(mappings[key]?.action);
  contextConfig.hidden = !assistantSelected;
  assistantConfig.hidden = !assistantSelected;
  detailCard.classList.toggle('config-active', assistantSelected);

  if (assistantSelected) {
    assistantModeButtons.forEach(button => {
      const active = button.dataset.assistantMode === settings.assistantMode;
      button.classList.toggle('active', active);
      button.setAttribute('aria-pressed', String(active));
    });
    assistantModeHint.textContent = settings.assistantMode === 'codex'
      ? '按住 Y 开始 Codex 听写，松开后结束。'
      : '按一下开始，再按一下结束并插入识别结果。';
  }
}

function setShortcutEditing(editing) {
  if (editing && (!currentKey || !mappings[currentKey]?.action)) return;
  shortcutEditing = editing;
  shortcutSaving = false;
  editingKey = editing ? currentKey : null;
  shortcutEditPanel.hidden = !editing;
  shortcutEditButton.hidden = editing || !currentKey || !mappings[currentKey]?.action;
  shortcutSaveButton.disabled = false;
  shortcutCancelButton.disabled = false;
  shortcutEditInput.disabled = false;
  detailCard.classList.toggle('shortcut-editing', editing);
  mappingUnbindButton.hidden = editing;
  if (!editing) {
    if (currentKey) selectKey(currentKey);
    return;
  }

  const item = mappings[currentKey] || {};
  shortcutEditInput.value = item.customShortcut || '';
  shortcutEditInput.placeholder = item.defaultShortcut
    ? `默认：${item.defaultShortcut}`
    : '例如 Ctrl+Shift+P';
  shortcutEditHint.textContent = item.customShortcut
    ? `当前自定义：${item.customShortcut}；留空保存可恢复默认。`
    : '当前使用默认触发操作；自定义快捷键会替代它，留空可恢复默认。';
  requestAnimationFrame(() => {
    shortcutEditInput.focus();
    shortcutEditInput.select();
  });
}

function saveShortcutEdit() {
  if (!shortcutEditing || !editingKey || shortcutSaving) return;
  shortcutSaving = true;
  shortcutEditInput.disabled = true;
  shortcutSaveButton.disabled = true;
  shortcutCancelButton.disabled = true;
  shortcutEditHint.textContent = '正在保存并重新加载桥接服务…';
  send('set-mapping-shortcut', {
    profileKey: editingKey,
    shortcut: shortcutEditInput.value.trim()
  });
}

function selectKey(key) {
  if (shortcutEditing && editingKey !== key) setShortcutEditing(false);
  const item = mappings[key] || {
    display: key,
    function: '正在读取映射配置…',
    shortcut: '—'
  };
  currentKey = key;
  hotspots.forEach(node => node.classList.toggle('active', node.dataset.key === key));
  keyTitle.textContent = item.display || key;
  keySymbol.textContent = symbolForKey(key, item);
  functionText.textContent = item.function || '未映射';
  shortcutText.textContent = item.shortcut || '未设置快捷键';
  const reserved = item.action === 'SystemReserved';
  const enabled = item.enabled !== false;
  shortcutEditButton.hidden = shortcutEditing || !item.action || reserved || !enabled;
  mappingUnbindButton.hidden = shortcutEditing || !item.action || reserved;
  mappingUnbindButton.textContent = enabled ? '撤销绑定' : '绑定';
  mappingUnbindButton.dataset.enabled = String(enabled);
  updateConfigEditor(key);
  keyOrbit.classList.remove('bump');
  requestAnimationFrame(() => keyOrbit.classList.add('bump'));
  window.setTimeout(() => keyOrbit.classList.remove('bump'), 220);
}

function resetSelection(force = false) {
  if (shortcutEditing && !force) return;
  if (shortcutEditing) setShortcutEditing(false);
  if (selectionPinned && !force) return;
  if (document.activeElement?.classList?.contains('hotspot')) return;
  currentKey = null;
  hotspots.forEach(node => node.classList.remove('active'));
  keyTitle.textContent = '选择一个按键';
  keySymbol.textContent = '?';
  functionText.textContent = '将鼠标移到手柄上的按键，查看对应的 Codex 功能。';
  shortcutText.textContent = '等待选择';
  shortcutEditButton.hidden = true;
  mappingUnbindButton.hidden = true;
  updateConfigEditor(null);
}

function renderMappingPreview() {
  mappingPreviewContent.replaceChildren();
  const groups = activeMappingGroups();
  const allKeys = groups.flatMap(group => group.keys);
  mappingPreviewCount.textContent = String(allKeys.length);

  groups.forEach((group, groupIndex) => {
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
      symbol.textContent = symbolForKey(key, item);
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
  mappingToggleSaving = false;
  mappingUnbindButton.disabled = false;
  if (state.settings) settings = { ...settings, ...state.settings };
  if (state.status) statusText.textContent = state.status.replace(/^●\s*/, '');
  statusPill.dataset.tone = state.tone || 'warning';
  const running = Boolean(state.running);
  const controller = state.controller || {};
  applyControllerFamily(controller.family || 'stadia');
  pulseControllerInput(controller.lastInputKey, controller.lastInputAt);
  const connected = Boolean(controller.connected);
  document.body.dataset.running = String(running);
  liveBadge.dataset.active = String(connected);
  liveText.textContent = connected ? 'LIVE' : (running ? 'DETECTING' : 'OFFLINE');
  const model = controller.name || (running ? '正在检测…' : '未检测到');
  controllerModel.textContent = model;
  footerControllerModel.textContent = model;
  profileFileName.textContent = controller.profile || (controllerFamily === 'xbox'
    ? 'controller-padmicro-profile.xbox.json'
    : 'controller-padmicro-profile.stadia.json');
  interactionCount.textContent = String(activeMappingGroups().flatMap(group => group.keys).length);
  startButton.disabled = running;
  stopButton.disabled = !running;
  const notice = state.notice || {};
  connectionNotice.hidden = !notice.visible;
  connectionNotice.dataset.tone = state.tone || 'warning';
  connectionNoticeTitle.textContent = notice.title || '需要检查连接';
  connectionNoticeMessage.textContent = notice.message || '';
  connectionNoticeAction.hidden = !notice.action;
  connectionNoticeAction.dataset.command = notice.action || '';
  autoStartToggle.checked = Boolean(settings.autoStartEnabled);
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
shortcutEditButton.addEventListener('click', () => {
  if (currentKey) setShortcutEditing(true);
});
mappingUnbindButton.addEventListener('click', () => {
  if (!currentKey || mappingToggleSaving) return;
  const item = mappings[currentKey];
  if (!item?.action || item.action === 'SystemReserved') return;
  const enabled = item.enabled !== false;
  if (enabled && !window.confirm(`确认撤销“${item.display || currentKey}”的按键绑定？`)) return;
  mappingToggleSaving = true;
  mappingUnbindButton.disabled = true;
  mappingUnbindButton.textContent = enabled ? '正在撤销…' : '正在绑定…';
  send('set-mapping-enabled', { profileKey: currentKey, enabled: !enabled });
});
shortcutSaveButton.addEventListener('click', saveShortcutEdit);
shortcutCancelButton.addEventListener('click', () => setShortcutEditing(false));
shortcutEditInput.addEventListener('keydown', event => {
  if (event.key === 'Enter') {
    event.preventDefault();
    saveShortcutEdit();
  } else if (event.key === 'Escape') {
    event.preventDefault();
    setShortcutEditing(false);
  }
});
connectionNoticeAction.addEventListener('click', () => {
  const command = connectionNoticeAction.dataset.command;
  if (command) send(command);
});
mappingPreviewButton.addEventListener('click', () => setMappingPreview(!previewOpen));
document.querySelector('#mapping-preview-close').addEventListener('click', () => setMappingPreview(false));
document.querySelector('#mapping-preview-backdrop').addEventListener('click', () => setMappingPreview(false));
document.querySelector('#export-button').addEventListener('click', () => send('export'));
document.querySelector('#tray-button').addEventListener('click', () => send('tray'));
autoStartToggle.addEventListener('change', () => {
  settings.autoStartEnabled = autoStartToggle.checked;
  send('set-auto-start', String(autoStartToggle.checked));
});
assistantModeButtons.forEach(button => {
  button.addEventListener('click', () => {
    const mode = button.dataset.assistantMode;
    settings.assistantMode = mode;
    updateConfigEditor('North');
    send('set-assistant-mode', mode);
  });
});

document.addEventListener('keydown', event => {
  if (event.key !== 'Escape') return;
  if (shortcutEditing) setShortcutEditing(false);
  else if (previewOpen) setMappingPreview(false);
});

bridge?.addEventListener('message', event => {
  const message = event.data;
  if (message?.type === 'state') applyState(message);
  if (message?.type === 'shortcut-result' && message.profileKey === editingKey) {
    shortcutSaving = false;
    shortcutEditInput.disabled = false;
    shortcutSaveButton.disabled = false;
    shortcutCancelButton.disabled = false;
    shortcutEditHint.textContent = message.message || (message.success ? '已保存。' : '保存失败。');
    if (message.success) setShortcutEditing(false);
  }
});

if (previewControllerFamily) applyControllerFamily(previewControllerFamily);
bridge?.postMessage({ type: 'ready' });
