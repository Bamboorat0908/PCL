'use strict';

const base = '/BRCL/InkAdmin';
const el = id => document.getElementById(id);
const amount = new Intl.NumberFormat('zh-CN');
const clock = new Intl.DateTimeFormat('zh-CN', {timeZone: 'Asia/Shanghai', year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', hour12: false});
const kinds = {minecraft: 'Minecraft', jvm: 'JVM', launcher: '启动器', unexpected: '待确认退出'};
const statuses = {open: '待处理', working: '处理中', fixed: '已修复', reopened: '修复后复发'};
let overview = null;
let groups = false;
let page = 1;
let filter = {q: '', kind: ''};
let selectedGroup = null;
let detail = null;
let overviewSeq = 0, listSeq = 0, detailSeq = 0;

function node(tag, text, cls) {
  const n = document.createElement(tag);
  if (text !== undefined) n.textContent = text;
  if (cls) n.className = cls;
  return n;
}
function action(text, callback, cls) {
  const button = node('button', text, cls);
  button.type = 'button';
  button.addEventListener('click', callback);
  return button;
}
function message(id, text = '', error = false) {
  el(id).textContent = text;
  el(id).classList.toggle('error', error);
}
function params() {
  return new URLSearchParams({days: el('days').value, version: el('version').value});
}
function stamp(value) {
  const date = new Date(typeof value === 'number' ? value * 1000 : value);
  return Number.isNaN(date.getTime()) ? '未知' : clock.format(date);
}
async function request(path, options = {}) {
  let response;
  try {
    response = await fetch(base + path, {cache: 'no-store', credentials: 'same-origin', ...options});
  } catch {
    throw Error('连接失败，请检查网络后重试。');
  }
  if (response.ok) return response.status === 204 ? null : response.json();
  if (response.status === 401) {
    if (el('report-dialog').open) el('report-dialog').close();
    el('dashboard').hidden = true;
    location.replace(base);
  }
  const messages = {400: '输入无效，请检查查询条件。', 401: '登录已失效。', 403: '当前会话没有访问权限。', 404: '找不到这份报告。', 429: '请求过于频繁，请稍后重试。', 503: '服务暂不可用，请稍后重试。'};
  throw Error(messages[response.status] || '请求失败（HTTP ' + response.status + '）。');
}
function post(path, body) {
  return request(path, {method: 'POST', headers: {'Content-Type': 'application/json'}, body: JSON.stringify(body)});
}
function makeTable(headers, rows, label) {
  const wrap = node('div', undefined, 'table-wrap');
  wrap.tabIndex = 0;
  wrap.setAttribute('role', 'region');
  wrap.setAttribute('aria-label', label);
  const table = node('table');
  const header = table.createTHead().insertRow();
  for (const title of headers) {
    const th = node('th', title);
    th.scope = 'col';
    header.append(th);
  }
  const body = table.createTBody();
  for (const cells of rows) {
    const row = body.insertRow();
    for (const value of cells) row.insertCell().append(value instanceof Node ? value : document.createTextNode(String(value ?? '—')));
  }
  wrap.append(table);
  return wrap;
}
function kindLabel(kind) {
  return node('span', kinds[kind] || kind, 'kind-tag ' + (Object.hasOwn(kinds, kind) ? kind : ''));
}
function resetMetrics() {
  overview = null;
  el('installs').textContent = el('rate').textContent = '—';
  el('sample').textContent = '正在读取可统计的游戏会话…';
  el('period').textContent = '';
}
async function loadOverview() {
  const seq = ++overviewSeq;
  resetMetrics();
  message('message');
  const query = params();
  query.set('view', 'crashes');
  try {
    const data = await request('/api/overview?' + query);
    if (seq !== overviewSeq) return;
    overview = data;
    el('installs').textContent = amount.format(data.total);
    el('rate').textContent = data.games ? new Intl.NumberFormat('zh-CN', {style:'percent', maximumFractionDigits:2}).format(data.crashed / data.games) : '—';
    el('sample').textContent = '已确认游戏崩溃 ' + amount.format(data.crashed) + ' 次 / 可统计游戏会话 ' + amount.format(data.games) + ' 次' + (data.games ? '' : '（暂无样本）');
    el('period').textContent = data.period_start + ' 至 ' + data.period_end + ' · 北京时间';
    const version = el('version').value;
    const versions = new Set(data.versions.map(x => x.version));
    for (const issue of data.issues) for (const v of (issue.versions || '').split(',')) if (v) versions.add(v);
    if (version) versions.add(version);
    el('version').replaceChildren(new Option('全部版本', ''));
    for (const v of Array.from(versions).sort((a,b) => b.localeCompare(a, 'en', {numeric:true}))) el('version').add(new Option(v,v));
    el('version').value = version;
    if (groups) renderGroups();
  } catch (error) {
    if (seq !== overviewSeq) return;
    el('sample').textContent = '统计暂不可用';
    message('message', error.message, true);
    if (groups) {
      el('reports').replaceChildren();
      message('list-status', '未能读取问题汇总。', true);
    }
  }
}
function listState() {
  el('pager').hidden = true;
  el('reports').replaceChildren();
  message('list-status', '正在查询…');
}
function groupFilter(issue) {
  selectedGroup = issue;
  filter = {q: '', kind: issue.kind};
  el('kind').value = issue.kind;
  el('q').value = '';
  switchView(false);
}
function renderGroups() {
  el('pager').hidden = true;
  el('reports').replaceChildren();
  if (!overview) {
    message('list-status', el('message').textContent || '正在读取问题汇总…');
    return;
  }
  const issues = overview.issues.filter(issue => !filter.kind || filter.kind === issue.kind).sort((a,b) => b.reports-a.reports || b.last.localeCompare(a.last));
  message('list-status', '本期同类问题 ' + amount.format(issues.length) + ' 组 · 按报告次数排序' + (overview.issues.length >= 500 ? '（显示前 500 组内的结果）' : ''));
  const rows = issues.map(issue => [
    action(issue.title, () => groupFilter(issue), 'plain'), kindLabel(issue.kind), amount.format(issue.reports), issue.last, statuses[issue.status]
  ]);
  el('reports').append(rows.length ? makeTable(['同类问题','类型','报告次数','最近发生','进度'],rows,'按问题汇总') : node('p','此范围暂无匹配的问题。','empty'));
}
function reportName(item) {
  const cell = node('div', undefined, 'report-name');
  cell.append(action(item.title, () => showReport(item), 'plain'), node('small', item.id));
  return cell;
}
async function loadReports() {
  const seq = ++listSeq;
  listState();
  const query = params();
  query.set('q', filter.q);
  query.set('kind', filter.kind);
  query.set('page', String(page));
  if (selectedGroup) query.set('issue', selectedGroup.id);
  try {
    const result = await request('/api/reports?' + query);
    if (seq !== listSeq || groups) return;
    const pages = Math.max(1, Math.ceil(result.total / result.page_size));
    if (page > pages) {
      page = pages;
      return loadReports();
    }
    message('list-status', '共 ' + amount.format(result.total) + ' 份报告 · 按接收时间倒序');
    if (!result.items.length) {
      el('reports').append(node('p', filter.q || filter.kind || selectedGroup ? '没有匹配的报告，可调整关键词或筛选条件。' : '此范围尚未收到报告。', 'empty'));
      return;
    }
    const rows = result.items.map(item => [reportName(item),kindLabel(item.kind),item.version,
      node('span', stamp(item.received), 'nowrap'), item.has_body ? '可查看' : '已到期']);
    el('reports').append(makeTable(['异常 / 报告编号','类型','BRCL 版本','接收时间（北京）','日志正文'],rows,'崩溃日志报告列表'));
    el('pager').hidden = pages === 1;
    el('page-label').textContent = '第 ' + amount.format(page) + ' / ' + amount.format(pages) + ' 页';
    el('prev').disabled = page <= 1;
    el('next').disabled = page >= pages;
  } catch (error) {
    if (seq === listSeq && !groups) message('list-status', error.message, true);
  }
}
function updateGroupChip() {
  el('issue-filter').hidden = !selectedGroup || groups;
  el('issue-filter-text').textContent = selectedGroup ? '同类问题 · ' + selectedGroup.title : '';
}
function switchView(showGroups) {
  groups = showGroups;
  page = 1;
  ++listSeq;
  el('issue-summary').setAttribute('aria-pressed', String(groups));
  el('report-list').setAttribute('aria-pressed', String(!groups));
  el('keyword-label').hidden = groups;
  updateGroupChip();
  if (groups) renderGroups();
  else loadReports();
}
function refreshScope() {
  page = 1;
  if (groups) listState();
  loadOverview();
  if (!groups) loadReports();
}
function addFact(name, value) {
  if (value !== null && value !== undefined && value !== '') el('facts').append(node('dt',name),node('dd',value));
}
function rank(file) {
  if (/^(jvm-crash|exception)\.log$|^crash-/.test(file.name)) return 0;
  if (file.name === 'console.log') return 1;
  return 2;
}
function excerpt(files) {
  const lines = [];
  for (const file of files) {
    const logs = file.text.split(/\r?\n/);
    for (let i = 0; i < logs.length && lines.length < 14; i++) {
      if (/Caused by:|Exception\b|Error\b|problematic frame|EXCEPTION_ACCESS_VIOLATION/i.test(logs[i])) {
        lines.push(logs[i].slice(0, 600));
        for (let j = 0; j < 3 && i+1 < logs.length && /^\s*(?:at\s|在\s|#\s*[CVJ]\s)/.test(logs[i+1]); j++) lines.push(logs[++i].slice(0, 600));
      }
    }
    if (lines.length >= 14) break;
  }
  return lines.join('\n');
}
function showFile() {
  const file = detail?.files?.[Number(el('file').value)];
  if (!file) return;
  const key = el('line-query').value.trim().toLowerCase();
  if (key) {
    const matches = [];
    file.text.split(/\r?\n/).forEach((line, i) => {
      if (line.toLowerCase().includes(key)) matches.push((i + 1) + '  ' + line);
    });
    el('log-content').textContent = matches.length ? matches.join('\n') : '此附件没有匹配的行。';
    el('line-count').textContent = '匹配 ' + amount.format(matches.length) + ' 行';
  } else {
    el('log-content').textContent = file.text || '该附件没有文本内容。';
    el('line-count').textContent = '';
  }
  el('log-content').scrollTop = 0;
}
function syncFixed() {
  const fixed = el('status').value === 'fixed';
  el('fixed').required = fixed;
  el('fixed').disabled = !fixed;
}
function setIssue(issue) {
  el('issue-controls').hidden = !issue;
  if (!issue) return;
  el('issue-info').textContent = issue.title + ' · 本期同类报告 ' + amount.format(issue.reports) + ' 次';
  el('status').querySelector('[value="reopened"]').hidden = issue.status !== 'reopened';
  el('status').value = issue.status;
  el('owner').value = issue.owner || '';
  el('fixed').value = issue.fixed || '';
  syncFixed();
}
function fillReport(item, report, issue) {
  addFact('报告类型',kinds[item.kind]);
  addFact('BRCL 版本',item.version);
  addFact('本期同类报告',issue && amount.format(issue.reports) + ' 次');
  addFact('接收时间',stamp(item.received));
  setIssue(issue);
  if (!report) {
    message('details-status','日志正文已到期（保留 30 天），报告记录仍可查询。');
    return;
  }
  addFact('发生时间',stamp(report.created_utc));
  addFact('操作系统',report.os);
  addFact('Minecraft',report.mc);
  addFact('Java',report.java);
  addFact('整合包',report.pack);
  if (!report.files?.length) {
    message('details-status','此报告没有日志附件。');
    return;
  }
  detail.files = [...report.files].sort((a,b) => rank(a)-rank(b));
  const stack = excerpt(detail.files);
  el('stack').textContent = stack;
  el('stack-box').hidden = !stack;
  el('stack-box').open = Boolean(stack);
  el('file').replaceChildren(...detail.files.map((file,i) => new Option(file.name, String(i))));
  el('line-query').value = '';
  el('viewer').hidden = false;
  message('details-status','');
  showFile();
}
async function showReport(item) {
  const seq = ++detailSeq;
  detail = {item, files: [], issue: null};
  el('detail-title').textContent = item.title;
  el('report-id').textContent = item.id;
  el('facts').replaceChildren();
  el('viewer').hidden = el('issue-controls').hidden = true;
  el('issue-controls').open = false;
  el('save').disabled = false;
  el('log-content').textContent = el('stack').textContent = '';
  message('details-status','正在读取报告…');
  if (!el('report-dialog').open) el('report-dialog').showModal();
  try {
    const query = params();
    query.set('id', item.id);
    const result = await request('/api/report?' + query);
    if (seq !== detailSeq) return;
    detail.issue = result.issue;
    fillReport(result.item || item,result.report,result.issue);
  } catch (error) {
    if (seq === detailSeq) message('details-status',error.message,true);
  }
}
async function copyText(value, success) {
  const seq = detailSeq;
  try {
    await navigator.clipboard.writeText(value);
    if (seq === detailSeq) message('details-status',success);
  } catch {
    if (seq === detailSeq) message('details-status','浏览器未允许复制，请选中文字后手动复制。',true);
  }
}
el('triage').addEventListener('submit', async event => {
  event.preventDefault();
  if (!detail?.issue || el('save').disabled) return;
  const seq = detailSeq;
  el('save').disabled = true;
  try {
    await post('/api/issue',{id:detail.issue.id,status:el('status').value,owner:el('owner').value.trim(),fixed:el('fixed').value.trim()});
    if (seq === detailSeq) message('details-status','处理记录已保存。');
    loadOverview();
  } catch (error) {
    if (seq === detailSeq) message('details-status',error.message,true);
  } finally {
    if (seq === detailSeq) el('save').disabled = false;
  }
});
el('copy-id').addEventListener('click',() => { if (detail) copyText(detail.item.id,'已复制报告编号。'); });
el('copy-log').addEventListener('click',() => {
  const file = detail?.files?.[Number(el('file').value)];
  if (file) copyText(file.text,'已复制当前附件全文。');
});
el('line-query').addEventListener('input',showFile);
el('file').addEventListener('change',showFile);
el('status').addEventListener('change',syncFixed);
el('close-detail').addEventListener('click',() => el('report-dialog').close());
el('report-dialog').addEventListener('close',() => {
  ++detailSeq;
  detail = null;
  el('log-content').textContent = el('stack').textContent = '';
});
el('search').addEventListener('submit', event => {
  event.preventDefault();
  page = 1;
  filter = {q:el('q').value.trim(),kind:el('kind').value};
  if (groups) renderGroups();
  else loadReports();
});
el('kind').addEventListener('change',() => {
  if (selectedGroup && selectedGroup.kind !== el('kind').value) {
    selectedGroup = null;
    updateGroupChip();
  }
  el('search').requestSubmit();
});
el('clear').addEventListener('click',() => {
  filter = {q:'',kind:''};
  selectedGroup = null;
  el('q').value = el('kind').value = el('version').value = '';
  el('days').value = '7';
  updateGroupChip();
  refreshScope();
});
el('clear-issue').addEventListener('click',() => {
  selectedGroup = null;
  updateGroupChip();
  page = 1;
  loadReports();
});
el('report-list').addEventListener('click',() => switchView(false));
el('issue-summary').addEventListener('click',() => switchView(true));
el('prev').addEventListener('click',() => { if (page > 1) { page--; loadReports(); } });
el('next').addEventListener('click',() => { page++; loadReports(); });
el('days').addEventListener('change',refreshScope);
el('version').addEventListener('change',refreshScope);
el('refresh').addEventListener('click',refreshScope);
el('logout').addEventListener('click',async () => {
  el('logout').disabled = true;
  try {
    await post('/api/logout',{});
    el('dashboard').hidden = true;
    location.replace(base);
  } catch (error) {
    message('message',error.message,true);
    el('logout').disabled = false;
  }
});
window.addEventListener('pageshow',event => { if (event.persisted) location.reload(); });
refreshScope();
