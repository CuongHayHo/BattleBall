#!/usr/bin/env python3
"""Generate SourceData/skin_browser.html: a SELF-CONTAINED page (all images embedded) that renders the costumes in the IDLE pose so the
owner can review and edit kind / rarity / gem price / stats, then export JSON or CSV.

Inputs : SourceData/tables/seed/skin_pricing.json       (kinds, rarity, price, stats; run build_skin_pricing.py first)
         SourceData/tables/seed/skin_stat_catalog.json  (stat types a skin can carry)
         SourceData/tables/json/part.json               (part frames: [iconId, dx, dy])
         Assets/NroMannequin/Data/CostumeCatalog.json   (head/body/leg part ids)
         SourceData/data/icon/x2/<iconId>.png           (embedded as base64)
IDLE   : NRO pose cf=0 (Stand_1): head sub 0, leg sub 1, body sub 1; offsets from NroPoseTable (same maths as NroMannequin.PlaceSlot).
"""
import base64
import json
import os

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.normpath(os.path.join(HERE, ".."))
SEED = os.path.join(SRC, "tables", "seed", "skin_pricing.json")
STATS = os.path.join(SRC, "tables", "seed", "skin_stat_catalog.json")
PART = os.path.join(SRC, "tables", "json", "part.json")
CATALOG = os.path.normpath(os.path.join(SRC, "..", "Assets", "NroMannequin", "Data", "CostumeCatalog.json"))
OUT = os.path.join(SRC, "skin_browser.html")
DEFAULT_BODY, DEFAULT_LEG = 57, 58

TEMPLATE = r'''<!doctype html>
<html lang="vi">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Phân loại skin BattleBall</title>
<style>
:root{--bg:#f4f5f7;--fg:#1d2330;--card:#fff;--line:#d9dde5;--muted:#667085;--acc:#2a5bd7;
--c:#7a8aa6;--r:#2a8a4a;--v:#b8860b;--bad:#b42318}
@media (prefers-color-scheme:dark){:root{--bg:#14171d;--fg:#e7eaf0;--card:#1e232c;--line:#323a47;--muted:#9aa4b5;--acc:#7aa2ff;
--c:#8fa0bd;--r:#5ccf85;--v:#e7b94a;--bad:#ff8a80}}
*{box-sizing:border-box}
[hidden]{display:none !important}
body{margin:0;background:var(--bg);color:var(--fg);font:14px/1.4 system-ui,Segoe UI,Arial,sans-serif}
header{position:sticky;top:0;z-index:5;background:var(--card);border-bottom:1px solid var(--line);padding:10px 16px}
@media (max-width:900px),(max-height:700px){header{position:static}}
h1{font-size:16px;margin:0 0 8px}
.row{display:flex;flex-wrap:wrap;gap:8px;align-items:center;margin:4px 0}
input,select,button{font:inherit;color:inherit;background:var(--bg);border:1px solid var(--line);border-radius:6px;padding:4px 8px}
button{cursor:pointer}button:hover{border-color:var(--acc)}
button.primary{background:var(--acc);color:#fff;border-color:var(--acc)}
button.mini{padding:0 6px;font-size:12px}
.stats{color:var(--muted);font-size:13px}
main{padding:12px 16px;display:grid;grid-template-columns:repeat(auto-fill,minmax(var(--w,230px),1fr));gap:10px}
.card{background:var(--card);border:1px solid var(--line);border-radius:8px;padding:8px;display:flex;flex-direction:column;gap:6px}
.card.edited{border-color:var(--acc)}
.card.k-FUSION_FORM{border-left:4px solid var(--v)}
.card.k-COMPONENT,.card.k-SKILL_FORM,.card.k-HIDDEN,.card.k-BOSS_MOB,.card.k-NPC{opacity:.85}
.stage{display:flex;justify-content:center;background:repeating-conic-gradient(rgba(127,127,127,.10) 0 25%,transparent 0 50%) 0 0/16px 16px;border-radius:6px;min-height:190px}
canvas{image-rendering:pixelated;max-width:100%}
.name{font-weight:600;font-size:13px;word-break:break-word}
.meta{color:var(--muted);font-size:12px}
.badge{display:inline-block;padding:0 6px;border-radius:9px;font-size:11px;border:1px solid currentColor}
.b-COMMON{color:var(--c)}.b-RARE{color:var(--r)}.b-VERY_RARE{color:var(--v)}
.ctl{display:grid;grid-template-columns:1fr 1fr;gap:4px}
.ctl label{font-size:11px;color:var(--muted);display:flex;flex-direction:column;gap:2px}
.ctl select,.ctl input{width:100%}
.sts{border-top:1px dashed var(--line);padding-top:4px;display:flex;flex-direction:column;gap:3px}
.sts .r{display:grid;grid-template-columns:1fr 64px 22px;gap:3px}
.sts select,.sts input{min-width:0;padding:2px 4px;font-size:12px}
.sts .t{font-size:11px;color:var(--muted)}
.nro{font-size:11px;color:var(--muted);border-top:1px dashed var(--line);padding-top:4px}
.warn{font-size:11px;color:var(--bad)}
.hint{font-size:12px;color:var(--muted)}
</style>
</head>
<body>
<header>
<h1>Phân loại skin BattleBall (trạng thái IDLE, __COUNT__ costume)</h1>
<div class="row">
 <input id="q" placeholder="Tìm theo tên hoặc id" size="22">
 <select id="fGroup"><option value="">Nhóm: tất cả</option></select>
 <select id="fKind"><option value="">Loại: tất cả</option></select>
 <select id="fRar"><option value="">Độ hiếm: tất cả</option></select>
 <select id="fGender"><option value="">Chủng tộc: tất cả</option></select>
 <select id="fSrc"><option value="">Nguồn NRO: tất cả</option><option value="shop">Bán trong shop NRO</option><option value="event">Vật phẩm sự kiện</option><option value="edited">Đã sửa tay</option></select>
 <label>Cỡ <input id="size" type="range" min="190" max="360" value="230"></label>
 <button id="btnStats">Danh sách chỉ số</button>
</div>
<div class="row"><span class="stats" id="stats"></span></div>
<div class="row">
 <b>Áp dụng cho các skin đang lọc:</b>
 <select id="bulkRar"><option value="">Độ hiếm...</option></select><button id="bulkRarGo">Đặt độ hiếm</button>
 <select id="bulkKind"><option value="">Loại...</option></select><button id="bulkKindGo">Đặt loại</button>
 <span style="flex:1"></span>
 <button id="btnExport" class="primary">Xuất JSON</button><button id="btnCsv">Xuất CSV</button>
 <button id="btnImport">Nhập JSON</button><button id="btnReset">Đặt lại về đề xuất</button>
 <input id="file" type="file" accept=".json,application/json" hidden>
</div>
<div class="hint">Loại SHOP và FUSION_FORM có giá và chỉ số. <b>FUSION_FORM</b>: nếu mua, chỉ nhân vật chủ khi dùng Bông tai Potara mới mặc được; AI và nhân vật phụ không mặc được skin hợp thể. <b>BOSS_MOB</b>: dùng cho boss/quái nhỏ, không bán. <b>NPC</b>: dùng cho nhân vật NPC (cửa hàng, nhiệm vụ...), không bán, người chơi không mặc. Thay đổi tự lưu trong trình duyệt; bấm "Xuất JSON" để gửi lại cho trợ lý.</div>
</header>
<div id="expBox" hidden style="position:fixed;inset:0;z-index:20;background:rgba(0,0,0,.55);display:flex;align-items:center;justify-content:center;padding:16px">
 <div style="background:var(--card);border:1px solid var(--line);border-radius:10px;padding:14px;width:min(900px,100%);max-height:90vh;display:flex;flex-direction:column;gap:8px">
  <b id="expTitle"></b>
  <div class="hint" id="expMsg"></div>
  <textarea id="expText" readonly style="width:100%;height:50vh;font:12px/1.4 Consolas,monospace;background:var(--bg);color:var(--fg);border:1px solid var(--line);border-radius:6px"></textarea>
  <div class="row"><button id="expCopy" class="primary">Sao chép</button><button id="expSave">Tải file</button><span style="flex:1"></span><button id="expClose">Đóng</button></div>
 </div>
</div>
<div id="statsPanel" hidden style="padding:8px 16px;border-bottom:1px solid var(--line);background:var(--card);max-height:45vh;overflow:auto"></div>
<main id="grid"></main>
<script id="data" type="application/json">__DATA__</script>
<script id="imgs" type="application/json">__IMGS__</script>
<script>
const DATA = JSON.parse(document.getElementById('data').textContent);
const IMGS = JSON.parse(document.getElementById('imgs').textContent);   // icon id -> base64 PNG (page is self-contained)
const KINDS = ['SHOP','FUSION_FORM','COMPONENT','SKILL_FORM','BOSS_MOB','NPC','HIDDEN'];
const SELLABLE = ['SHOP','FUSION_FORM'];
const RARS = ['COMMON','RARE','VERY_RARE'];
const KIND_VI = {SHOP:'Bán (SHOP)',FUSION_FORM:'Hợp thể Potara',COMPONENT:'Avatar/đầu',SKILL_FORM:'Dạng theo skill',BOSS_MOB:'Boss / quái nhỏ',NPC:'NPC',HIDDEN:'Ẩn (không dùng)'};
const RAR_VI = {COMMON:'Phổ thông',RARE:'Hiếm',VERY_RARE:'Rất hiếm'};
const LS_KEY = 'skinBrowser.v3';
const POSE = {head:[-13,34], leg:[-8,10], body:[-9,16]};   // NRO CharInfo[0]: dx, dy (dy up)
const STAT = Object.fromEntries(DATA.statCatalog.map(s => [s.key, s]));
const PICK = DATA.statCatalog.filter(s => s.applies !== 'NO');
let over = {};
try { over = JSON.parse(localStorage.getItem(LS_KEY) || '{}'); } catch(e) { over = {}; }
const save = () => { try { localStorage.setItem(LS_KEY, JSON.stringify(over)); } catch(e) {} };
const el = id => document.getElementById(id);
const byId = new Map(DATA.costumes.map(c => [c.id, c]));
const sellable = k => SELLABLE.includes(k);

function eff(c) {                       // effective values = default overridden by edits
  const o = over[c.id] || {};
  const kind = o.kind || c.kind;
  const sell = sellable(kind);
  const rarity = sell ? (o.rarity || c.rarity || 'COMMON') : null;
  const auto = Math.round((DATA.cfg.basePrice[c.grp] || 100) * DATA.cfg.mult[rarity || 'COMMON'] / 10) * 10;
  const price = !sell ? null : (o.price != null ? o.price : (o.rarity ? auto : (c.price != null ? c.price : auto)));
  let stats = [];
  if (sell) {
    if (o.stats) stats = o.stats;
    else if (o.rarity || c.stats.length === 0) stats = DATA.cfg.defaultStats.map(k => ({type: k, value: DATA.cfg.bonus[rarity]}));
    else stats = c.stats;
  }
  return {kind, rarity, price, stats, edited: !!(o.kind || o.rarity || o.price != null || o.stats)};
}

function fill(sel, items, labels) {
  items.forEach(v => { const o = document.createElement('option'); o.value = v; o.textContent = labels ? (labels[v] || v) : v; sel.appendChild(o); });
}
fill(el('fGroup'), [...new Set(DATA.costumes.map(c => c.grp))]);
fill(el('fKind'), KINDS, KIND_VI);
fill(el('fRar'), RARS, RAR_VI);
fill(el('fGender'), [...new Set(DATA.costumes.map(c => c.g))]);
fill(el('bulkRar'), RARS, RAR_VI);
fill(el('bulkKind'), KINDS, KIND_VI);

// ---- rendering (lazy) ----
const imgCache = new Map();
function loadImg(icon) {
  if (!imgCache.has(icon)) imgCache.set(icon, new Promise(res => {
    const im = new Image(); im.onload = () => res(im); im.onerror = () => res(null);
    im.src = IMGS[icon] ? 'data:image/png;base64,' + IMGS[icon] : 'data/icon/x2/' + icon + '.png';
  }));
  return imgCache.get(icon);
}
async function draw(canvas, c) {
  const order = ['head','leg','body'];                 // z-order: Head -> Leg -> Body (NroMannequin)
  const items = [];
  for (const slot of order) {
    const f = c.parts[slot]; if (!f) continue;
    const im = await loadImg(f[0]); if (!im) continue;
    const X = (POSE[slot][0] + f[1]) * 2, Y = (f[2] - POSE[slot][1]) * 2;   // x2 images: 1 NRO px = 2 image px
    items.push({im, X, Y, w: im.naturalWidth, h: im.naturalHeight});
  }
  const ctx = canvas.getContext('2d');
  ctx.clearRect(0, 0, canvas.width, canvas.height);
  if (!items.length) { ctx.fillStyle = '#999'; ctx.fillText('không có hình', 8, 20); return; }
  const x0 = Math.min(...items.map(i => i.X)), y0 = Math.min(...items.map(i => i.Y));
  const x1 = Math.max(...items.map(i => i.X + i.w)), y1 = Math.max(...items.map(i => i.Y + i.h));
  const W = x1 - x0, H = y1 - y0;
  const s = Math.min(1, (canvas.width - 12) / W, (canvas.height - 12) / H);
  ctx.imageSmoothingEnabled = s < 1;
  const ox = (canvas.width - W * s) / 2 - x0 * s, oy = (canvas.height - H * s) / 2 - y0 * s;
  for (const i of items) ctx.drawImage(i.im, ox + i.X * s, oy + i.Y * s, i.w * s, i.h * s);
}
const io = new IntersectionObserver(es => es.forEach(e => {
  if (e.isIntersecting) { io.unobserve(e.target); draw(e.target, byId.get(+e.target.dataset.id)); }
}), {rootMargin: '300px'});

// ---- cards ----
function selectHtml(id, field, values, labels, cur, disabled) {
  return '<select data-id="' + id + '" data-f="' + field + '"' + (disabled ? ' disabled' : '') + '>' +
    values.map(v => '<option value="' + v + '"' + (v === cur ? ' selected' : '') + '>' + (labels[v] || v) + '</option>').join('') + '</select>';
}
function statRows(c, e) {
  const used = new Set(e.stats.map(s => s.type));
  const rows = e.stats.map((s, i) => {
    const def = STAT[s.type], max = def && def.max, over = max != null && s.value > max;
    const opts = PICK.map(p => '<option value="' + p.key + '"' + (p.key === s.type ? ' selected' : '') + (used.has(p.key) && p.key !== s.type ? ' disabled' : '') + '>' + p.nameVi + (p.unit === 'pct' ? ' (%)' : p.unit === 'flat' ? ' (+số)' : ' (hiệu ứng)') + '</option>').join('');
    return '<div class="r"><select data-id="' + c.id + '" data-f="stype" data-i="' + i + '">' + opts + '</select>' +
      '<input type="number" step="0.5" data-id="' + c.id + '" data-f="sval" data-i="' + i + '" value="' + s.value + '" title="' + (max != null ? 'gợi ý tối đa ' + max : '') + '">' +
      '<button class="mini" data-id="' + c.id + '" data-f="sdel" data-i="' + i + '" title="Xóa dòng">×</button></div>' +
      (over ? '<div class="warn">Vượt gợi ý tối đa (' + max + ')</div>' : '');
  }).join('');
  return '<div class="sts"><div class="t">Chỉ số của skin</div>' + rows +
    '<button class="mini" data-id="' + c.id + '" data-f="sadd">+ Thêm chỉ số</button></div>';
}
function nroLine(c) {
  const n = c.nro;
  const st = c.nroStats.length ? ' · NRO gắn: ' + c.nroStats.map(s => (STAT[s.type] ? STAT[s.type].nameVi : s.type) + ' ' + s.nroValue).join(', ') : '';
  return (n.sold ? 'Shop NRO' : (n.event ? 'Sự kiện' : 'Không bán')) + (n.gem ? ' · ' + n.gem + ' ngọc' : '') + st + ' · ' + (n.desc || '').slice(0, 40);
}
function cardHtml(c) {
  const e = eff(c), sell = sellable(e.kind);
  return '<div class="card k-' + e.kind + (e.edited ? ' edited' : '') + '" data-card="' + c.id + '">' +
    '<div class="stage"><canvas width="200" height="190" data-id="' + c.id + '"></canvas></div>' +
    '<div class="name">#' + c.id + ' ' + c.name + '</div>' +
    '<div class="meta">' + c.g + ' · ' + c.grp + (e.rarity ? ' · <span class="badge b-' + e.rarity + '">' + RAR_VI[e.rarity] + '</span>' : '') + '</div>' +
    '<div class="ctl">' +
      '<label>Loại' + selectHtml(c.id, 'kind', KINDS, KIND_VI, e.kind, false) + '</label>' +
      '<label>Độ hiếm' + selectHtml(c.id, 'rarity', RARS, RAR_VI, e.rarity || 'COMMON', !sell) + '</label>' +
      '<label style="grid-column:1/3">Giá (đá quý)<input type="number" min="0" step="10" data-id="' + c.id + '" data-f="price" value="' + (e.price == null ? '' : e.price) + '"' + (sell ? '' : ' disabled') + '></label>' +
    '</div>' +
    (sell ? statRows(c, e) : '') +
    (e.kind === 'FUSION_FORM' ? '<div class="meta">Hợp thể Potara' + (c.fr ? ' (' + c.fr + ')' : '') + ': chỉ nhân vật chủ khi dùng Bông tai mới mặc được; AI không mặc.</div>' : '') +
    '<div class="nro">' + nroLine(c) + '</div></div>';
}
function visible(c) {
  const e = eff(c), q = el('q').value.trim().toLowerCase();
  if (q && !(String(c.id) === q || c.name.toLowerCase().includes(q))) return false;
  if (el('fGroup').value && c.grp !== el('fGroup').value) return false;
  if (el('fKind').value && e.kind !== el('fKind').value) return false;
  if (el('fRar').value && e.rarity !== el('fRar').value) return false;
  if (el('fGender').value && c.g !== el('fGender').value) return false;
  const s = el('fSrc').value;
  if (s === 'shop' && !c.nro.sold) return false;
  if (s === 'event' && !c.nro.event) return false;
  if (s === 'edited' && !e.edited) return false;
  return true;
}
let shown = [];
function render() {
  io.disconnect();
  shown = DATA.costumes.filter(visible);
  el('grid').innerHTML = shown.map(cardHtml).join('');
  el('grid').querySelectorAll('canvas').forEach(cv => io.observe(cv));
  stats();
}
function stats() {
  const all = DATA.costumes.map(eff);
  const k = {}, r = {};
  all.forEach(e => { k[e.kind] = (k[e.kind] || 0) + 1; if (e.rarity) r[e.rarity] = (r[e.rarity] || 0) + 1; });
  const total = all.reduce((a, e) => a + (e.price || 0), 0);
  el('stats').textContent = 'Đang hiện ' + shown.length + '/' + DATA.costumes.length + ' · ' +
    KINDS.filter(x => k[x]).map(x => KIND_VI[x] + ' ' + k[x]).join(' · ') + ' · ' +
    RARS.map(x => RAR_VI[x] + ' ' + (r[x] || 0)).join(' · ') + ' · Đã sửa tay ' + all.filter(e => e.edited).length +
    ' · Tổng giá mua hết ' + total.toLocaleString('vi-VN') + ' đá quý';
}
function setOver(id, patch) {
  const o = over[id] || {};
  Object.assign(o, patch);
  Object.keys(o).forEach(k => { if (o[k] === null || o[k] === undefined) delete o[k]; });
  if (o.kind === byId.get(id).kind) delete o.kind;
  if (!Object.keys(o).length) delete over[id]; else over[id] = o;
  save();
}
function refreshCard(id) {
  const c = byId.get(id), node = el('grid').querySelector('[data-card="' + id + '"]');
  if (!node) return;
  const cv = node.querySelector('canvas');
  const tmp = document.createElement('div'); tmp.innerHTML = cardHtml(c);
  const fresh = tmp.firstChild;
  fresh.querySelector('.stage').replaceChild(cv, fresh.querySelector('canvas'));
  node.replaceWith(fresh);
  stats();
}
function curStats(id) { return eff(byId.get(id)).stats.map(s => ({type: s.type, value: s.value})); }
el('grid').addEventListener('click', ev => {
  const t = ev.target; if (t.tagName !== 'BUTTON') return;
  const id = +t.dataset.id, f = t.dataset.f; if (!f) return;
  const s = curStats(id);
  if (f === 'sadd') { const free = PICK.find(p => !s.some(x => x.type === p.key)); if (!free) return; s.push({type: free.key, value: free.max ? Math.min(free.max, 1) : 1}); }
  else if (f === 'sdel') s.splice(+t.dataset.i, 1);
  else return;
  setOver(id, {stats: s}); refreshCard(id);
});
el('grid').addEventListener('change', ev => {
  const t = ev.target, id = +t.dataset.id, f = t.dataset.f;
  if (!f) return;
  if (f === 'kind') { setOver(id, {kind: t.value}); if (el('fKind').value) render(); else refreshCard(id); return; }
  if (f === 'rarity') setOver(id, {rarity: t.value, price: null, stats: null});
  else if (f === 'price') setOver(id, {price: t.value === '' ? null : Number(t.value)});
  else if (f === 'stype') { const s = curStats(id); s[+t.dataset.i].type = t.value; setOver(id, {stats: s}); }
  else if (f === 'sval') { const s = curStats(id); s[+t.dataset.i].value = Number(t.value); setOver(id, {stats: s}); }
  else return;
  if (el('fRar').value && f === 'rarity') render(); else refreshCard(id);
});
['q','fGroup','fKind','fRar','fGender','fSrc'].forEach(i => el(i).addEventListener('input', render));
el('size').addEventListener('input', () => { el('grid').style.setProperty('--w', el('size').value + 'px'); });
el('bulkRarGo').onclick = () => { const v = el('bulkRar').value; if (!v) return;
  const n = shown.filter(c => sellable(eff(c).kind)).length;
  if (!confirm('Đặt độ hiếm "' + RAR_VI[v] + '" cho ' + n + ' skin có giá đang hiện? (giá và chỉ số tính lại theo độ hiếm)')) return;
  shown.forEach(c => { if (sellable(eff(c).kind)) setOver(c.id, {rarity: v, price: null, stats: null}); }); render(); };
el('bulkKindGo').onclick = () => { const v = el('bulkKind').value; if (!v) return;
  if (!confirm('Đặt loại "' + KIND_VI[v] + '" cho ' + shown.length + ' skin đang hiện?')) return;
  shown.forEach(c => setOver(c.id, {kind: v})); render(); };

// ---- stat catalog panel ----
el('btnStats').onclick = () => {
  const p = el('statsPanel');
  if (!p.hidden) { p.hidden = true; return; }
  const grp = {YES: 'Chỉ số dùng được trong BattleBall', EFFECT: 'Hiệu ứng đặc biệt (cần cơ chế riêng)', NO: 'Không áp dụng (game chơi đơn, đã bỏ)'};
  p.innerHTML = '<b>Các chỉ số một skin có thể mang (kiểm tra trong mã NRO: NPoint.addOption, và dữ liệu 414 costume)</b>' +
    Object.keys(grp).map(a => '<h4 style="margin:8px 0 2px">' + grp[a] + '</h4><table style="border-collapse:collapse;width:100%;font-size:12px">' +
      DATA.statCatalog.filter(s => s.applies === a).map(s => '<tr><td style="padding:2px 6px;border-top:1px solid var(--line)">' + s.nameVi +
        '</td><td style="padding:2px 6px;border-top:1px solid var(--line)">' + (s.unit === 'pct' ? '%' : s.unit === 'flat' ? 'số cộng thẳng' : s.unit === 'flag' ? 'bật/tắt' : '') +
        '</td><td style="padding:2px 6px;border-top:1px solid var(--line)">gợi ý tối đa: ' + (s.max != null ? s.max : '—') +
        '</td><td style="padding:2px 6px;border-top:1px solid var(--line)">NRO: ' + s.n + ' costume' + (s.n ? ' (' + s.vmin + ' đến ' + s.vmax + ')' : '') + '</td></tr>').join('') + '</table>').join('');
  p.hidden = false;
};

// ---- export / import ----
function rows() {
  return DATA.costumes.map(c => { const e = eff(c); return {costumeId: c.id, name: c.name, gender: c.g, bodyGroup: c.grp, kind: e.kind,
    rarity: e.rarity, gemPrice: e.price, stats: e.stats.map(s => ({type: s.type, value: s.value})), fusionRace: c.fr || null,
    fusionOnlyMainCharacter: e.kind === 'FUSION_FORM' ? true : null, rarityStatus: e.edited ? 'MANUAL' : 'HEURISTIC'}; });
}
// Export: some browsers/viewers block downloads from a local page, so we ALWAYS show the text in a box (copy / save) as well.
function tryDownload(name, text, type) {
  try {
    const url = URL.createObjectURL(new Blob([text], {type}));
    const a = document.createElement('a'); a.href = url; a.download = name; a.style.display = 'none';
    document.body.appendChild(a); a.click();
    setTimeout(() => { document.body.removeChild(a); URL.revokeObjectURL(url); }, 4000);
    return true;
  } catch (e) { return false; }
}
function showExport(name, text, type) {
  const ok = tryDownload(name, text, type);
  el('expTitle').textContent = 'Xuất ' + name + ' (' + text.length.toLocaleString('vi-VN') + ' ký tự)';
  el('expMsg').textContent = ok ? 'Đã yêu cầu tải file "' + name + '" (xem thư mục Tải xuống). Nếu không thấy file, bấm "Sao chép" rồi dán vào Notepad và lưu thành ' + name + ', hoặc dán thẳng cho trợ lý.'
                                : 'Trình duyệt chặn tải file. Bấm "Sao chép" rồi dán vào Notepad và lưu thành ' + name + ', hoặc dán thẳng cho trợ lý.';
  el('expText').value = text;
  el('expBox').hidden = false;
  el('expText').focus(); el('expText').select();
  el('expSave').onclick = () => { tryDownload(name, text, type); };
}
el('expCopy').onclick = () => {
  const t = el('expText'); t.focus(); t.select();
  let ok = false;
  try { ok = document.execCommand('copy'); } catch (e) {}
  if (!ok && navigator.clipboard) { navigator.clipboard.writeText(t.value).then(() => { el('expMsg').textContent = 'Đã sao chép vào clipboard.'; }, () => { el('expMsg').textContent = 'Không sao chép tự động được: nhấn Ctrl+C khi nội dung đang được chọn.'; }); }
  else el('expMsg').textContent = ok ? 'Đã sao chép vào clipboard.' : 'Không sao chép tự động được: nhấn Ctrl+C khi nội dung đang được chọn.';
};
el('expClose').onclick = () => { el('expBox').hidden = true; };
el('btnExport').onclick = () => showExport('skin_pricing_edited.json', JSON.stringify({version: 4, exportedAt: new Date().toISOString(), costumes: rows()}, null, 1), 'application/json');
el('btnCsv').onclick = () => { const r = rows().map(x => Object.assign({}, x, {stats: x.stats.map(s => s.type + '=' + s.value).join(';')})), cols = Object.keys(r[0]);
  const esc = v => v == null ? '' : '"' + String(v).replace(/"/g, '""') + '"';
  showExport('skin_pricing_edited.csv', '﻿' + [cols.join(',')].concat(r.map(x => cols.map(k => esc(x[k])).join(','))).join('\r\n'), 'text/csv'); };
el('btnImport').onclick = () => el('file').click();
el('file').onchange = ev => { const f = ev.target.files[0]; if (!f) return; const rd = new FileReader();
  rd.onload = () => { try { const j = JSON.parse(rd.result); over = {};
    (j.costumes || []).forEach(x => { const c = byId.get(x.costumeId); if (!c) return; const o = {};
      if (x.kind && x.kind !== c.kind) o.kind = x.kind;
      if (sellable(x.kind)) { if (x.rarity && x.rarity !== c.rarity) o.rarity = x.rarity; if (x.gemPrice != null && x.gemPrice !== c.price) o.price = x.gemPrice;
        if (Array.isArray(x.stats) && JSON.stringify(x.stats) !== JSON.stringify(c.stats)) o.stats = x.stats; }
      if (Object.keys(o).length) over[c.id] = o; });
    save(); render(); } catch (e) { alert('File JSON không hợp lệ: ' + e.message); } };
  rd.readAsText(f); ev.target.value = ''; };
el('btnReset').onclick = () => { if (confirm('Xóa mọi chỉnh sửa trong trình duyệt và quay về dữ liệu đề xuất ban đầu?')) { over = {}; save(); render(); } };
render();
</script>
</body>
</html>
'''


def main():
    seed = json.load(open(SEED, encoding="utf-8"))
    statcat = json.load(open(STATS, encoding="utf-8"))
    parts = {r["id"]: r["DATA"] for r in json.load(open(PART, encoding="utf-8"))["rows"]}
    cat = {e["id"]: e for e in json.load(open(CATALOG, encoding="utf-8"))["costumes"]}

    def frame(pid, sub):
        d = parts.get(pid)
        if not d or sub >= len(d):
            return None
        icon, dx, dy = d[sub]
        return [icon, dx, dy]

    out = []
    for s in seed["costumes"]:
        e = cat[s["costumeId"]]
        body = e["bodyPartId"] if e["bodyPartId"] >= 0 else DEFAULT_BODY
        leg = e["legPartId"] if e["legPartId"] >= 0 else DEFAULT_LEG
        out.append(dict(
            id=s["costumeId"], name=s["name"], g=s["genderName"], grp=s["bodyGroup"], kind=s["kind"],
            rarity=s.get("rarity"), price=s.get("gemPrice"), stats=s.get("stats", []),
            nroStats=s.get("nroStats", []), fr=s.get("fusionRace"),
            nro=dict(desc=s["nro"]["description"], sold=s["nro"]["soldInNroShop"], gem=s["nro"]["nroGemCost"],
                     event=s["nro"]["eventItem"]),
            parts=dict(head=frame(e["headPartId"], 0), leg=frame(leg, 1), body=frame(body, 1))))
    cat_js = [dict(key=x["key"], nameVi=x["nameVi"], unit=x["unit"], applies=x["applies"], max=x["suggestedMaxPerSkin"],
                   n=x["nroCostumeEntries"], vmin=x["nroValueMin"], vmax=x["nroValueMax"]) for x in statcat["stats"]]
    data = dict(cfg=dict(basePrice=seed["basePrice"], mult=seed["rarityPriceMult"], bonus=seed["rarityStatBonusPct"],
                         defaultStats=seed["defaultStatKeys"]),
                statCatalog=cat_js, costumes=out)
    blob = json.dumps(data, ensure_ascii=False, separators=(",", ":")).replace("</", "<\\/")
    icons = sorted({f[0] for c in out for f in c["parts"].values() if f})
    imgs, missing = {}, []
    for i in icons:
        path = os.path.join(SRC, "data", "icon", "x2", "%d.png" % i)
        if os.path.exists(path):
            imgs[i] = base64.b64encode(open(path, "rb").read()).decode("ascii")
        else:
            missing.append(i)
    html = (TEMPLATE.replace("__DATA__", blob).replace("__COUNT__", str(len(out)))
            .replace("__IMGS__", json.dumps(imgs, separators=(",", ":"))))
    with open(OUT, "w", encoding="utf-8", newline="\n") as f:
        f.write(html)
    miss = [c["id"] for c in out if not all(c["parts"].values())]
    print("wrote", OUT, "(%d KB)" % (len(html) // 1024), "| costumes:", len(out), "| embedded icons:", len(imgs),
          "| missing icon files:", missing[:5], "| missing slot frame:", miss[:5])


if __name__ == "__main__":
    main()
