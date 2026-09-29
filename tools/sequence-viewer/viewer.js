'use strict';
const $ = id => document.getElementById(id);
const { sortFiles, activeIndices, step, advance, zip } = FrameCore;
let frames = [], current = -1, active = [], playing = false, history = [], cards = [], elapsed = 0, last = 0, importing = false, exporting = false;
const fps = () => Math.max(1, Math.min(60, Number($('fps').value) || 12));
const message = text => { $('message').textContent = text; };
function setPlaying(value) {
  playing = value && active.length > 0; elapsed = 0; last = performance.now();
  $('play').textContent = playing ? '暂停' : '播放';
  $('playState').textContent = playing ? '正在播放' : frames.length ? '已暂停' : '等待导入';
}
function show(index) {
  if (index < 0 || !frames[index]) return;
  if (cards[current]) cards[current].classList.remove('current');
  current = index;
  const f = frames[index];
  $('picture').src = f.url; $('picture').hidden = false; $('empty').hidden = true;
  $('frameName').textContent = `${index + 1} / ${frames.length} · ${f.name}${f.keep ? '' : '（已排除）'}`;
  $('dimensions').textContent = `${f.width} × ${f.height}`;
  $('scrub').value = index; $('toggle').textContent = f.keep ? '排除此帧' : '恢复此帧';
  cards[index]?.classList.add('current');
}
function update() {
  active = activeIndices(frames);
  $('counts').textContent = `保留 ${active.length} / ${frames.length} · 排除 ${frames.length - active.length}`;
  $('duration').textContent = active.length ? `${(active.length / fps()).toFixed(2)} 秒 / 轮` : '';
  for (const id of ['play','prev','next']) $(id).disabled = !active.length || importing;
  $('export').disabled = !active.length || importing || exporting;
  for (const id of ['all','none','invert','save','excludeRange','keepRange','scrub','toggle']) $(id).disabled = !frames.length || importing;
  $('undo').disabled = !history.length || importing;
  cards.forEach((card, i) => { card.classList.toggle('excluded', !frames[i].keep); card.querySelector('input').checked = frames[i].keep; });
  if (!active.length) setPlaying(false);
  if (playing && !frames[current]?.keep) show(step(active, current));
  else if (current >= 0) show(current);
}
function changeSelection(change) {
  if (importing || !frames.length) return;
  const before = frames.map(f => f.keep); change();
  if (frames.some((f, i) => f.keep !== before[i])) { history.push(before); if (history.length > 100) history.shift(); }
  elapsed = 0; update();
}
function drawCards() {
  $('grid').replaceChildren(); cards = [];
  const fragment = document.createDocumentFragment();
  frames.forEach((f, i) => {
    const card = document.createElement('div'); card.className = 'frame';
    const thumb = document.createElement('button'); thumb.className = 'thumb checker'; thumb.title = `查看第 ${i + 1} 帧：${f.name}`;
    const img = document.createElement('img'); img.src = f.url; img.alt = f.name; img.loading = 'lazy'; thumb.append(img);
    thumb.onclick = () => { setPlaying(false); show(i); };
    const number = document.createElement('span'); number.className = 'number'; number.textContent = String(i + 1).padStart(3, '0');
    const name = document.createElement('div'); name.className = 'name'; name.textContent = f.name; name.title = f.path;
    const label = document.createElement('label'), check = document.createElement('input'); check.type = 'checkbox'; check.checked = f.keep; check.setAttribute('aria-label', `保留第 ${i + 1} 帧`);
    check.onchange = () => changeSelection(() => { f.keep = check.checked; });
    label.append(check, document.createTextNode('保留')); card.append(thumb, number, name, label); fragment.append(card); cards.push(card);
  });
  $('grid').append(fragment);
}
async function decode(file) {
  const url = URL.createObjectURL(file), img = new Image(); img.src = url;
  try { await img.decode(); return { file, url, name: file.name, path: file.webkitRelativePath || file.name, width: img.naturalWidth, height: img.naturalHeight, keep: true }; }
  catch { URL.revokeObjectURL(url); return null; }
}
async function importFiles(list) {
  if (importing) return;
  const files = sortFiles([...list].filter(f => /\.png$/i.test(f.name)));
  if (!files.length) { message('没有找到 PNG 图片，请选择 PNG 序列文件。'); return; }
  importing = true; setPlaying(false); update(); message(`正在读取 ${files.length} 帧…`);
  const loaded = [];
  try {
    // Bound decode concurrency to avoid launching thousands of decoders together.
    for (let i = 0; i < files.length; i += 12) loaded.push(...await Promise.all(files.slice(i, i + 12).map(decode)));
    const valid = loaded.filter(Boolean);
    if (!valid.length) { message('图片读取失败，原序列已保留。请检查文件是否为有效 PNG。'); return; }
    const old = frames; frames = valid; current = -1; history = [];
    drawCards(); $('scrub').max = frames.length - 1; $('rangeEnd').value = frames.length;
    $('rangeStart').value = 1; $('rangeStart').max = $('rangeEnd').max = frames.length;
    show(0); old.forEach(f => URL.revokeObjectURL(f.url));
    const sizes = new Set(frames.map(f => `${f.width}x${f.height}`));
    message(`已导入 ${frames.length} 帧，按文件名自然排序。${files.length !== valid.length ? `跳过 ${files.length - valid.length} 张无法读取的图片。` : ''}${sizes.size > 1 ? ' 注意：画布尺寸不一致，播放时可能跳动，建议导出时统一画布。' : ''}`);
  } finally { importing = false; update(); }
}
$('files').onchange = e => { importFiles(e.target.files); e.target.value = ''; };
$('folder').onchange = e => { importFiles(e.target.files); e.target.value = ''; };
$('play').onclick = () => {
  if (!playing && (!frames[current]?.keep || (!$('loop').checked && current === active.at(-1)))) show(active[0]);
  setPlaying(!playing);
};
function navigate(direction) { setPlaying(false); show(step(active, current, direction)); }
$('prev').onclick = () => navigate(-1); $('next').onclick = () => navigate(1);
$('scrub').oninput = e => { setPlaying(false); show(+e.target.value); };
$('toggle').onclick = () => changeSelection(() => { if (frames[current]) frames[current].keep = !frames[current].keep; });
$('all').onclick = () => changeSelection(() => frames.forEach(f => f.keep = true));
$('none').onclick = () => changeSelection(() => frames.forEach(f => f.keep = false));
$('invert').onclick = () => changeSelection(() => frames.forEach(f => f.keep = !f.keep));
$('undo').onclick = () => { const state = history.pop(); if (state) frames.forEach((f,i) => f.keep = state[i]); update(); };
function range(keep) {
  const a = Number($('rangeStart').value), b = Number($('rangeEnd').value);
  if (!Number.isInteger(a) || !Number.isInteger(b) || a < 1 || b < a || b > frames.length) { message(`请输入 1–${frames.length} 内的有效区间，开始序号不能大于结束序号。`); return; }
  changeSelection(() => { for (let i = a - 1; i < b; i++) frames[i].keep = keep; });
}
$('excludeRange').onclick = () => range(false); $('keepRange').onclick = () => range(true);
$('fps').oninput = () => { elapsed = 0; update(); }; $('fps').onchange = () => { $('fps').value = fps(); update(); };
$('background').onchange = e => $('stage').className = `stage ${e.target.value}`;
$('size').onchange = e => { const size = e.target.value === 'fit' ? '100%' : `${e.target.value}px`; $('picture').style.width = size; $('picture').style.height = size; };
function tick(now) {
  if (playing && active.length) {
    elapsed += Math.min(now - last, 250);
    const count = Math.floor(elapsed / (1000 / fps()));
    if (count) {
      elapsed %= 1000 / fps();
      const next = advance(active, current, count, $('loop').checked);
      show(next.index);
      if (next.ended) setPlaying(false);
    }
  }
  last = now; requestAnimationFrame(tick);
}
requestAnimationFrame(tick);
document.addEventListener('visibilitychange', () => { last = performance.now(); elapsed = 0; });
document.addEventListener('keydown', e => {
  if (e.ctrlKey || e.metaKey || e.altKey || /INPUT|SELECT|TEXTAREA|BUTTON/.test(e.target.tagName)) return;
  if (e.code === 'Space') { e.preventDefault(); $('play').click(); }
  if (e.code === 'ArrowLeft') { e.preventDefault(); $('prev').click(); }
  if (e.code === 'ArrowRight') { e.preventDefault(); $('next').click(); }
  if (e.code === 'KeyX' || e.code === 'Delete') { e.preventDefault(); $('toggle').click(); }
});
let dragDepth = 0;
document.addEventListener('dragenter', e => { if ([...e.dataTransfer.types].includes('Files')) { e.preventDefault(); dragDepth++; document.body.classList.add('dragging'); } });
document.addEventListener('dragover', e => e.preventDefault());
document.addEventListener('dragleave', () => { if (--dragDepth <= 0) { dragDepth = 0; document.body.classList.remove('dragging'); } });
document.addEventListener('drop', e => { e.preventDefault(); dragDepth = 0; document.body.classList.remove('dragging'); importFiles(e.dataTransfer.files); });
function download(blob, name) {
  const url = URL.createObjectURL(blob), a = document.createElement('a'); a.href = url; a.download = name; document.body.append(a); a.click(); a.remove(); setTimeout(() => URL.revokeObjectURL(url), 60000);
}
function plan() { return { format: 'whale-frame-selection', version: 1, fps: fps(), loop: $('loop').checked, frames: frames.map(f => ({ path: f.path, name: f.name, size: f.file.size, keep: f.keep })) }; }
$('save').onclick = () => download(new Blob([JSON.stringify(plan(), null, 2)], { type: 'application/json' }), 'frame-selection.json');
$('load').onchange = async e => {
  const file = e.target.files[0]; e.target.value = ''; if (!file) return;
  if (importing || !frames.length) { message('请先导入对应的 PNG 序列，再载入筛选方案。'); return; }
  try {
    const data = JSON.parse(await file.text());
    if (data.format !== 'whale-frame-selection' || data.version !== 1 || !Array.isArray(data.frames)) throw new Error('不是有效的筛选方案。');
    const available = [...data.frames];
    const matched = frames.map(f => {
      let at = available.findIndex(p => p.path === f.path && p.size === f.file.size);
      if (at < 0) { const candidates = available.flatMap((p,i) => p.name === f.name && p.size === f.file.size ? [i] : []); if (candidates.length === 1) at = candidates[0]; }
      if (at < 0) throw new Error('方案与当前图片不匹配，请导入保存方案时使用的同一组文件。');
      const p = available.splice(at,1)[0]; if (typeof p.keep !== 'boolean') throw new Error('方案中的帧状态无效。'); return p;
    });
    if (available.length || !Number.isFinite(data.fps) || data.fps < 1 || data.fps > 60 || typeof data.loop !== 'boolean') throw new Error('方案与当前序列不匹配，或播放设置无效。');
    setPlaying(false); changeSelection(() => frames.forEach((f,i) => f.keep = matched[i].keep));
    $('fps').value = data.fps; $('loop').checked = data.loop; update(); message('已恢复筛选方案和播放设置。');
  } catch (error) { message(error.message); }
};
$('export').onclick = async () => {
  if (exporting || !active.length) return;
  exporting = true; update(); message('正在打包保留的原始 PNG…');
  const kept = frames.filter(f => f.keep), rate = fps(), loop = $('loop').checked;
  try {
    const manifest = { fps: rate, loop, frameDurationMs: 1000 / rate, frames: kept.map((f,i) => ({ file: `frames/frame_${String(i + 1).padStart(4,'0')}.png`, source: f.path, width: f.width, height: f.height })) };
    const entries = kept.map((f,i) => ({ name: manifest.frames[i].file, blob: f.file }));
    entries.push({ name: 'animation.json', blob: new Blob([JSON.stringify(manifest, null, 2)]) });
    download(await zip(entries), 'selected-frames.zip'); message(`已导出 ${kept.length} 个保留帧，按播放顺序重新编号，帧率 ${rate} FPS。`);
  } catch (error) { message(`导出失败：${error.message}`); }
  finally { exporting = false; update(); }
};
