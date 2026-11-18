// Launcher-only presentation adapter. Trading data, forms and site routing stay owned by the site.
(function () {
  'use strict';
  if (window.top !== window) return;
  var isStock = location.protocol === 'https:' && location.hostname === 'zhushu.homes' && /^\/stock(?:\/|$)/i.test(location.pathname);
  if (!isStock && !__BRCL_ALLOW_TEST__) return;

  var css = `
html[data-brcl-embedded] {
  --bg:#e7e8e3; --surface:#f5f5ef; --surface-tint:#edefe6; --surface-hover:#e4e7dc;
  --text:#191d1f; --text-money:#191d1f; --text-deep:#343c3d; --muted:#555f60; --dim:#697372;
  --border:#c6cbc7; --border-strong:#afb7b1; --border-soft:#dce0d7; --border-green:#c6cbc7;
  --tint-green:#e9ebde; --tint-green-strong:#edefe5; --brand-deep:#343c31;
  --accent:#745900; --accent-deep:#745900; --accent-bg:#655000;
  --seg-bg:#e3e6dc; --seg-active:#4b513f; --chip-bg:#f5f5ef; --chip-text:#655000;
  --eyebrow:#655000; --kicker:#555f60; --shadow-panel:none;
  --font:'Segoe UI','Microsoft YaHei UI',system-ui,sans-serif;
  --brcl-grid:rgba(45,61,61,.055); --brcl-trace:rgba(116,89,0,.12);
}
html[data-brcl-embedded][data-theme='night'] {
  --bg:#111416; --surface:#1c2022; --surface-tint:#272c2e; --surface-hover:#303638;
  --text:#f1f2e9; --text-money:#f1f2e9; --text-deep:#d4dad4; --muted:#b3bab9; --dim:#929d9b;
  --border:#374041; --border-strong:#505c5c; --border-soft:#30383a; --border-green:#374041;
  --tint-green:#2b3028; --tint-green-strong:#252b27; --brand-deep:#343c31;
  --accent:#f2d649; --accent-deep:#dfc243; --accent-bg:#655000;
  --seg-bg:#272c2e; --seg-active:#f2d649; --chip-bg:#272c2e; --chip-text:#f2d649;
  --eyebrow:#f2d649; --kicker:#b3bab9;
  --brcl-grid:rgba(184,195,190,.045); --brcl-trace:rgba(242,214,73,.11);
}
html[data-brcl-embedded] body {
  position:relative; isolation:isolate;
  background-color:var(--bg)!important;
  background-image:linear-gradient(var(--brcl-grid) 1px,transparent 1px),linear-gradient(90deg,var(--brcl-grid) 1px,transparent 1px)!important;
  background-size:40px 40px!important; background-attachment:scroll!important;
}
html[data-brcl-embedded] body::before {
  content:''; position:fixed; right:0; top:45px; width:min(470px,70vw); height:260px;
  pointer-events:none; z-index:-1;
  background:linear-gradient(90deg,transparent,var(--brcl-trace));
  clip-path:polygon(0 80%,16% 80%,16% 62%,34% 62%,34% 69%,50% 69%,50% 39%,70% 39%,70% 20%,100% 20%,100% 20.7%,70.4% 20.7%,70.4% 39.7%,50.4% 39.7%,50.4% 69.7%,33.6% 69.7%,33.6% 62.7%,16.4% 62.7%,16.4% 80.7%,0 80.7%);
}
html[data-brcl-embedded] .topbar {
  height:50px!important; min-height:50px; padding:0 14px!important; gap:10px!important;
  background:var(--surface)!important; backdrop-filter:none!important; box-shadow:none!important;
  position:relative!important;
}
html[data-brcl-embedded] .brand { gap:8px!important; }
html[data-brcl-embedded] .brand-mark { width:28px!important; height:28px!important; border-radius:4px!important; }
html[data-brcl-embedded] .brand strong { font-size:14px!important; }
html[data-brcl-embedded] .brand small { display:none; }
html[data-brcl-embedded] .top-actions .text-button { min-height:30px!important; padding:5px 8px!important; border-radius:3px!important; }
html[data-brcl-embedded] .top-center { min-width:0; }
html[data-brcl-embedded] .main { max-width:none!important; padding:12px 14px 24px!important; }
html[data-brcl-embedded] .balance-strip { padding:10px 14px!important; margin-bottom:10px!important; border-radius:4px!important; background:var(--surface-tint)!important; border-color:var(--border)!important; }
html[data-brcl-embedded] .balance-strip::after { display:none; }
html[data-brcl-embedded] .balance-lead p { display:none; }
html[data-brcl-embedded] .workspace { gap:10px!important; height:auto!important; max-height:none!important; align-items:start!important; }
html[data-brcl-embedded] .center-column, html[data-brcl-embedded] .right-column { gap:10px!important; }
html[data-brcl-embedded] .left-column,
html[data-brcl-embedded] .center-column,
html[data-brcl-embedded] .right-column { height:auto!important; overflow:visible!important; }
html[data-brcl-embedded] .panel, html[data-brcl-embedded] .quote-card, html[data-brcl-embedded] .trade-card {
  border-radius:4px!important; padding:12px!important; box-shadow:none!important; min-width:0;
  background:var(--surface)!important; border-color:var(--border)!important;
}
html[data-brcl-embedded] .quote-card { margin-bottom:0!important; }
html[data-brcl-embedded] .chart-card { border-radius:4px!important; margin-bottom:0!important; }
html[data-brcl-embedded] .q-name h1 { font-size:18px!important; }
html[data-brcl-embedded] .q-price strong { font-size:26px!important; }
html[data-brcl-embedded] .chart-canvas-wrap { height:clamp(220px,42vh,380px)!important; }
html[data-brcl-embedded] .chart-card:fullscreen .chart-canvas-wrap,
html[data-brcl-embedded] .chart-card.chart-fullscreen .chart-canvas-wrap { height:100%!important; }
html[data-brcl-embedded] .pair-row { border-radius:3px!important; }
html[data-brcl-embedded] .market-panel .panel-heading { flex-wrap:wrap; gap:6px; }
html[data-brcl-embedded] .market-panel .panel-heading h2 { white-space:nowrap; }
html[data-brcl-embedded] .market-panel .pair-sort { margin-left:auto; }
html[data-brcl-embedded] .topbar .brand-mark { box-shadow:none!important; }
@media (min-width:1240px) {
  html[data-brcl-embedded] .workspace { grid-template-columns:178px minmax(0,1fr) 278px!important; }
}
@media (min-width:981px) and (max-width:1239px) {
  html[data-brcl-embedded] .workspace { grid-template-columns:168px minmax(0,1fr)!important; }
  html[data-brcl-embedded] .right-column { grid-column:2!important; }
  html[data-brcl-embedded] .chart-card { padding-right:10px!important; }
  html[data-brcl-embedded] .flow-panel { position:static!important; width:auto!important; margin-top:8px; height:100px!important; max-height:100px!important; }
}
@media (max-width:980px) {
  html[data-brcl-embedded] .workspace { grid-template-columns:minmax(0,1fr)!important; }
  html[data-brcl-embedded] .left-column,
  html[data-brcl-embedded] .center-column,
  html[data-brcl-embedded] .right-column { display:contents!important; }
  html[data-brcl-embedded] .market-panel { order:1; position:static!important; padding:8px 10px!important; }
  html[data-brcl-embedded] .market-panel .panel-heading { margin-bottom:6px!important; }
  html[data-brcl-embedded] .market-panel .panel-heading h2 { font-size:13px!important; }
  html[data-brcl-embedded] .pair-list { display:flex!important; flex-direction:row!important; gap:6px!important; overflow-x:auto; padding-bottom:3px; }
  html[data-brcl-embedded] .pair-row { flex:0 0 134px; flex-direction:column!important; align-items:flex-start!important; padding:7px 9px!important; }
  html[data-brcl-embedded] .pair-numbers { text-align:left!important; }
  html[data-brcl-embedded] .pair-id small { display:none; }
  html[data-brcl-embedded] .quote-card { order:2; }
  html[data-brcl-embedded] .chart-card { order:3; }
  html[data-brcl-embedded] .trade-card { order:4; }
  html[data-brcl-embedded] .side-disclaimer { order:9; }
  html[data-brcl-embedded] .balance-strip { display:grid!important; grid-template-columns:minmax(160px,1.3fr) repeat(3,minmax(0,1fr))!important; gap:8px!important; }
  html[data-brcl-embedded] .balance-lead { grid-column:auto!important; }
  html[data-brcl-embedded] .balance-strip>.balance-lead,
  html[data-brcl-embedded] .balance-strip>.metric { grid-area:auto!important; }
  html[data-brcl-embedded] .balance-lead .eyebrow { display:none!important; }
  html[data-brcl-embedded] .balance-strip>.metric { display:flex!important; min-width:0!important; padding:0 0 0 10px!important; border-top:0!important; border-left:1px solid var(--border)!important; }
  html[data-brcl-embedded] .balance-day { grid-column:1/-1; }
  html[data-brcl-embedded] .q-metrics { gap:6px 12px!important; margin-top:8px!important; padding-top:8px!important; }
  html[data-brcl-embedded] .q-since { margin-top:4px!important; }
}
@media (max-width:760px) {
  html[data-brcl-embedded] .top-center, html[data-brcl-embedded] .top-actions .session-pill { display:none!important; }
  html[data-brcl-embedded] .main { padding:10px!important; }
  html[data-brcl-embedded] .topbar { padding:0 10px!important; }
}
@media (max-width:540px) {
  html[data-brcl-embedded] .balance-strip { grid-template-columns:minmax(0,1fr) minmax(0,1fr)!important; }
  html[data-brcl-embedded] .balance-strip>.metric:nth-child(n+4) { display:none!important; }
  html[data-brcl-embedded] .q-metrics { grid-template-columns:repeat(2,minmax(0,1fr))!important; }
}
`;

  function installStyle() {
    if (!document.head || !document.documentElement) return false;
    document.documentElement.setAttribute('data-brcl-embedded', '');
    if (!document.getElementById('brcl-embedded-layout')) {
      var style = document.createElement('style');
      style.id = 'brcl-embedded-layout';
      style.textContent = css;
      document.head.appendChild(style);
    }
    return true;
  }
  if (!installStyle()) {
    var observer = new MutationObserver(function () {
      if (installStyle()) observer.disconnect();
    });
    observer.observe(document, { childList:true, subtree:true });
    document.addEventListener('DOMContentLoaded', function () { installStyle(); observer.disconnect(); }, { once:true });
  }
})();
