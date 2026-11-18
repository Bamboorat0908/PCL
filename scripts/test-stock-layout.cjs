// Opt-in integration check: fresh headless Edge profile, public GET-only market page, no desktop input.
const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');
const { spawn } = require('node:child_process');
const http = require('node:http');
const assert = require('node:assert/strict');
const snapshotHtml = process.argv[3];
const snapshotCss = process.argv[4];
const source = fs.readFileSync(path.join(__dirname, '../Plain Craft Launcher 2/Pages/PageStock/StockEmbedded.js'), 'utf8')
  .replace('__BRCL_ALLOW_TEST__', snapshotHtml ? 'true' : 'false');
const outputDir = process.argv[2] || path.join(os.tmpdir(), 'brcl-stock-layout-results');
fs.mkdirSync(outputDir, { recursive: true });
const profile = fs.mkdtempSync(path.join(os.tmpdir(), 'brcl-stock-headless-'));
const edgePath = 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe';
const child = spawn(edgePath, ['--headless=new', '--disable-gpu', '--no-first-run', '--no-default-browser-check',
  '--remote-debugging-port=0', `--user-data-dir=${profile}`, 'about:blank'], { windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });

let socket;
let snapshotServer;
const pending = new Map();
let nextId = 0;
async function main() {
  let url = 'https://zhushu.homes/stock/';
  if (snapshotHtml) {
    assert(snapshotCss, 'Provide the downloaded CSS with the HTML snapshot');
    // Layout fixture: actual fetched site HTML/CSS, with business scripts removed. No authentication or trading requests.
    let html = fs.readFileSync(snapshotHtml, 'utf8').replace(/<script\b[^>]*>[\s\S]*?<\/script>/gi, '');
    html = html.replace(/<link\b[^>]*href="static\/app\.css[^>]*>/i, '<style>' + fs.readFileSync(snapshotCss, 'utf8') + '</style>')
      .replace(/<link\b[^>]*>/gi, '');
    const rows = ['鼠鼠证券', '绿宝石商会', '蒸汽科技', '钻石矿业', '维度网络货币'].map(name =>
      `<button class="pair-row"><span class="pair-id"><strong>${name}</strong></span><span class="pair-numbers"><strong>100.00</strong></span></button>`).join('');
    html = html.replace(/(<div[^>]*id="pairList"[^>]*>)/, '$1' + rows);
    snapshotServer = http.createServer((request, response) => { response.writeHead(200, { 'Content-Type':'text/html; charset=utf-8' }); response.end(html); });
    await new Promise(resolve => snapshotServer.listen(0, '127.0.0.1', resolve));
    url = `http://127.0.0.1:${snapshotServer.address().port}/stock/`;
    console.log('Using fetched HTML/CSS snapshot with static layout fixtures.');
  }
  const endpoint = await new Promise((resolve, reject) => {
    let data = '';
    const timeout = setTimeout(() => reject(Error('Headless Edge startup timed out')), 20000);
    child.stderr.on('data', chunk => {
      data += chunk;
      const match = /DevTools listening on (ws:\/\/[^\s]+)/.exec(data);
      if (match) { clearTimeout(timeout); resolve(match[1]); }
    });
    child.once('error', error => { clearTimeout(timeout); reject(error); });
    child.once('exit', code => { clearTimeout(timeout); reject(Error('Headless Edge exited: ' + code)); });
  });
  socket = new WebSocket(endpoint);
  await new Promise((resolve, reject) => { socket.addEventListener('open', resolve, { once:true }); socket.addEventListener('error', reject, { once:true }); });
  socket.addEventListener('message', event => {
    const message = JSON.parse(event.data);
    if (message.id && pending.has(message.id)) {
      const entry = pending.get(message.id); pending.delete(message.id); clearTimeout(entry.timeout);
      message.error ? entry.reject(Error(JSON.stringify(message.error))) : entry.resolve(message.result);
    }
  });
  function send(method, params = {}, sessionId) {
    return new Promise((resolve, reject) => {
      const id = ++nextId;
      const timeout = setTimeout(() => { pending.delete(id); reject(Error('CDP timed out: ' + method)); }, 15000);
      pending.set(id, { resolve, reject, timeout });
      socket.send(JSON.stringify({ id, method, params, ...(sessionId ? { sessionId } : {}) }));
    });
  }
  globalThis.closeBrowser = () => send('Browser.close');
  const { targetId } = await send('Target.createTarget', { url:'about:blank' });
  const { sessionId } = await send('Target.attachToTarget', { targetId, flatten:true });
  const call = (method, params) => send(method, params, sessionId);
  const evaluate = async expression => {
    const result = await call('Runtime.evaluate', { expression, returnByValue:true, awaitPromise:true });
    if (result.exceptionDetails) throw Error(JSON.stringify(result.exceptionDetails));
    return result.result.value;
  };
  await call('Page.enable');
  await call('Page.addScriptToEvaluateOnNewDocument', { source });
  await call('Emulation.setDeviceMetricsOverride', { width:700, height:760, deviceScaleFactor:1, mobile:false });
  await call('Page.navigate', { url });
  const start = Date.now();
  while (Date.now() - start < 30000) {
    if (await evaluate("document.readyState !== 'loading' && !!document.querySelector('.chart-card') && !!document.querySelector('.pair-row')")) break;
    await new Promise(resolve => setTimeout(resolve, 250));
  }
  assert(await evaluate("!!document.querySelector('.chart-card') && !!document.querySelector('.pair-row')"), 'Public market page did not render: ' + JSON.stringify(await evaluate("({url:location.href,title:document.title,text:document.body?.innerText.slice(0,200)})")));
  const results = [];
  for (const width of [700, 1000, 1400]) {
    await call('Emulation.setDeviceMetricsOverride', { width, height:760, deviceScaleFactor:1, mobile:false });
    await evaluate('new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))');
    const result = await evaluate(`(function(){
      const chart=document.querySelector('.chart-card');
      return { width:innerWidth, overflow:document.documentElement.scrollWidth-innerWidth,
        headerHeight:document.querySelector('.topbar').getBoundingClientRect().height,
        columns:getComputedStyle(document.querySelector('.workspace')).gridTemplateColumns,
        chartTop:Math.round(chart.getBoundingClientRect().top),
        chartWidth:Math.round(chart.getBoundingClientRect().width),
        leftDisplay:getComputedStyle(document.querySelector('.left-column')).display,
        styleCount:document.querySelectorAll('#brcl-embedded-layout').length,
        background:getComputedStyle(document.body).backgroundColor,
        quoteBackground:getComputedStyle(document.querySelector('.quote-card')).backgroundColor,
        compactMedia:matchMedia('(max-width:540px)').matches,
        balanceColumns:getComputedStyle(document.querySelector('.balance-strip')).gridTemplateColumns };
    })()`);
    assert.equal(result.styleCount, 1, 'Adapter style must be installed once');
    assert(result.overflow <= 1, `Horizontal document overflow at ${width}px: ${result.overflow}`);
    assert(result.headerHeight <= 52, `Header was not compact at ${width}px`);
    assert(result.chartWidth >= 440, `Chart too narrow at ${width}px: ${result.chartWidth}`);
    if (width <= 980) assert.equal(result.leftDisplay, 'contents');
    if (width <= 980) assert(result.chartTop < 420, 'Compact layout pushes the chart too far below the fold');
    const screenshot = await call('Page.captureScreenshot', { format:'png', captureBeyondViewport:false });
    fs.writeFileSync(path.join(outputDir, `stock-embedded-${width}.png`), Buffer.from(screenshot.data, 'base64'));
    results.push(result);
    console.log('PASS ' + JSON.stringify(result));
  }
  await evaluate(source);
  assert.equal(await evaluate("document.querySelectorAll('#brcl-embedded-layout').length"), 1, 'Repeated injection duplicated styles');
  await evaluate("document.documentElement.setAttribute('data-theme','night')");
  const dark = await evaluate("({body:getComputedStyle(document.body).backgroundColor,quote:getComputedStyle(document.querySelector('.quote-card')).backgroundColor})");
  assert.equal(dark.body, 'rgb(17, 20, 22)');
  assert.equal(dark.quote, 'rgb(28, 32, 34)');
  const darkScreenshot = await call('Page.captureScreenshot', { format:'png', captureBeyondViewport:false });
  fs.writeFileSync(path.join(outputDir, 'stock-embedded-dark.png'), Buffer.from(darkScreenshot.data, 'base64'));
  console.log('PASS dark background and card palette');
  fs.writeFileSync(path.join(outputDir, 'layout-results.json'), JSON.stringify(results, null, 2));
  console.log('PASS repeated injection; 3 responsive viewport checks passed.');
}
main().catch(error => { console.error(error.message); process.exitCode = 1; }).finally(async () => {
  if (globalThis.closeBrowser) { try { await closeBrowser(); } catch (_) {} }
  if (socket) socket.close();
  for (const entry of pending.values()) { clearTimeout(entry.timeout); entry.reject(Error('Browser closed')); }
  pending.clear();
  child.kill();
  if (snapshotServer) snapshotServer.close();
});
