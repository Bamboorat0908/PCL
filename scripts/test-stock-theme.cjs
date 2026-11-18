// Exercises the actual JavaScript embedded in PageStock without network or WebView2.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.join(__dirname, '../Plain Craft Launcher 2/Pages/PageStock/PageStock.xaml.vb'), 'utf8');
const start = source.indexOf('(function(){');
const end = source.indexOf('})();', start) + 5;
assert(start >= 0 && end > start, 'Embedded theme script missing');
const script = source.slice(start, end);

function fixture(light, options = {}) {
  const timers = new Map();
  let serial = 0, clicks = 0, count = 0;
  const messages = [];
  const button = {
    disabled: !!options.disabled,
    getBoundingClientRect: () => ({ width: options.hidden ? 0 : 32, height: 32 }),
    click: () => { clicks++; if (!options.stuck) light = !light; },
  };
  const context = vm.createContext({
    window: {},
    document: { body: {}, documentElement: {}, querySelectorAll: selector => {
      assert(!selector.includes('span,div'), 'Must not scan arbitrary content for clickable elements');
      count++;
      return options.absent || count < (options.appearAt || 0) ? [] : [button];
    } },
    getComputedStyle: () => ({ backgroundColor: options.transparent ? 'rgba(0, 0, 0, 0)' : light ? 'rgb(255, 255, 255)' : 'rgb(20, 20, 20)' }),
    setTimeout: callback => { timers.set(++serial, callback); return serial; },
    clearTimeout: id => timers.delete(id),
    chrome: { webview: { postMessage: message => messages.push(message) } },
  });
  function run(wantLight) { return vm.runInContext(script.replace('WANT', String(wantLight)), context); }
  function tick() { const item = timers.entries().next().value; if (!item) return; timers.delete(item[0]); item[1](); }
  function drain() { for (let i = 0; timers.size && i < 30; i++) tick(); assert.equal(timers.size, 0, 'Retries must be bounded'); }
  return { run, tick, drain, messages, timers, get clicks() { return clicks; }, get light() { return light; } };
}

const cases = {
  'already aligned': () => { const f = fixture(true); f.run(true); f.drain(); assert.equal(f.clicks, 0); },
  'dark to light': () => { const f = fixture(false); f.run(true); f.drain(); assert.equal(f.clicks, 1); assert.equal(f.light, true); },
  'light to dark': () => { const f = fixture(true); f.run(false); f.drain(); assert.equal(f.clicks, 1); assert.equal(f.light, false); },
  'missing control': () => { const f = fixture(false, { absent: true }); f.run(true); f.drain(); assert.equal(f.clicks, 0); assert(f.messages.includes('brcl-theme:unavailable')); },
  'delayed SPA control': () => { const f = fixture(false, { appearAt: 4 }); f.run(true); f.drain(); assert.equal(f.clicks, 1); },
  'ineffective control is not repeatedly clicked': () => { const f = fixture(false, { stuck: true }); f.run(true); f.drain(); assert.equal(f.clicks, 1); },
  'hidden and disabled controls': () => { for (const options of [{hidden:true},{disabled:true}]) { const f = fixture(false, options); f.run(true); f.drain(); assert.equal(f.clicks, 0); } },
  'unknown background': () => { const f = fixture(false, { transparent: true }); f.run(true); f.drain(); assert.equal(f.clicks, 0); },
  'latest theme cancels queued work': () => { const f = fixture(false); f.run(true); const obsolete = [...f.timers.values()][0]; f.run(false); obsolete(); f.drain(); assert.equal(f.clicks, 0); },
  'rapid toggle after first click': () => { const f = fixture(false); f.run(true); f.tick(); f.run(false); f.drain(); assert.equal(f.clicks, 2); assert.equal(f.light, false); },
};
for (const [name, test] of Object.entries(cases)) { test(); console.log('PASS ' + name); }
console.log(`${Object.keys(cases).length} theme regression cases passed.`);
