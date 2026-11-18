'use strict';

(() => {
  const base = '/BRS/news';
  const list = document.getElementById('news-list');
  const status = document.getElementById('list-status');
  const detail = document.getElementById('news-detail');
  const count = document.getElementById('news-count');
  const more = document.getElementById('load-more');
  const refresh = document.getElementById('refresh');
  const filters = [...document.querySelectorAll('[data-category]')];
  const requestedId = new URL(location.href).searchParams.get('id');
  let selectedId = /^\d+$/.test(requestedId || '') ? Number(requestedId) : null;
  let category = '';
  let items = [];
  let total = 0;
  let listVersion = 0;
  let detailVersion = 0;
  let loading = false;

  const element = (tag, className, text) => {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (text !== undefined) node.textContent = text;
    return node;
  };

  function dateText(value, long = false) {
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) return '时间待更新';
    return new Intl.DateTimeFormat('zh-CN', long
      ? {year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', hour12: false}
      : {year: 'numeric', month: '2-digit', day: '2-digit'}).format(date);
  }

  async function request(path) {
    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), 15000);
    try {
      const response = await fetch(`${base}/api${path}`, {credentials: 'same-origin', cache: 'no-store', signal: controller.signal, headers: {'Accept': 'application/json'}});
      const data = await response.json().catch(() => ({}));
      if (!response.ok) throw new Error(data.error || '暂时无法获取新闻，请稍后重试。');
      return data;
    } catch (error) {
      if (controller.signal.aborted) throw new Error('连接超时，请稍后重试。');
      if (error instanceof TypeError) throw new Error('无法连接新闻服务，请检查网络后重试。');
      throw error;
    } finally {
      clearTimeout(timer);
    }
  }

  function showStatus(message, isError = false) {
    status.textContent = message;
    status.classList.toggle('error', isError);
  }

  function badgeFor(item) {
    return element('span', `badge${item.category === 'update' ? ' badge-update' : ''}`, item.category === 'update' ? '动态' : '公告');
  }

  function renderList() {
    list.replaceChildren();
    for (const item of items) {
      const button = element('button', `news-card${item.id === selectedId ? ' active' : ''}`);
      button.type = 'button';
      button.dataset.newsId = String(item.id);
      button.setAttribute('aria-pressed', String(item.id === selectedId));
      const top = element('div', 'card-topline');
      const badges = element('span', 'card-badges');
      badges.append(badgeFor(item));
      if (item.pinned) badges.append(element('span', 'badge badge-pin', '置顶'));
      top.append(badges, element('time', 'card-date', dateText(item.published_at)));
      button.append(top, element('h3', 'card-title', item.title));
      if (item.summary) button.append(element('p', 'card-summary', item.summary));
      button.addEventListener('click', () => selectItem(item.id, true, true));
      list.append(button);
    }
    count.textContent = `${total} 条`;
    more.hidden = items.length >= total;
  }

  function markSelection() {
    for (const button of list.querySelectorAll('.news-card')) {
      const active = Number(button.dataset.newsId) === selectedId;
      button.classList.toggle('active', active);
      button.setAttribute('aria-pressed', String(active));
    }
  }

  function renderEmpty(title = '暂时还没有新闻', message = '新的公告与动态发布后，会在这里展示。') {
    const wrap = element('div', 'empty-detail');
    const symbol = element('span', 'empty-symbol', '＋');
    symbol.setAttribute('aria-hidden', 'true');
    wrap.append(symbol, element('p', 'eyebrow', 'BRS NEWSROOM'), element('h2', '', title), element('p', '', message));
    detail.replaceChildren(wrap);
  }

  function renderDetail(item) {
    const meta = element('div', 'detail-meta');
    meta.append(badgeFor(item));
    if (item.pinned) meta.append(element('span', 'badge badge-pin', '置顶'));
    const time = element('time', 'detail-date', dateText(item.published_at, true));
    if (item.published_at) time.dateTime = item.published_at;
    meta.append(time);
    const title = element('h2', 'detail-title', item.title);
    const body = element('div', 'detail-body', item.content);
    detail.replaceChildren(meta, title);
    if (item.summary) detail.append(element('p', 'detail-summary', item.summary));
    detail.append(body, element('p', 'detail-footer', 'BRS / SERVER NEWS'));
    detail.setAttribute('aria-busy', 'false');
    document.title = `${item.title} · BRS 新闻`;
  }

  async function selectItem(id, updateUrl, userInitiated = false) {
    const version = ++detailVersion;
    selectedId = id;
    markSelection();
    if (updateUrl) {
      const url = new URL(location.href);
      url.searchParams.set('id', String(id));
      history.replaceState(null, '', url);
    }
    detail.setAttribute('aria-busy', 'true');
    renderEmpty('正在加载新闻…', '请稍候，正在获取完整内容。');
    try {
      const data = await request(`/news/${encodeURIComponent(id)}`);
      if (version !== detailVersion) return;
      renderDetail(data.item);
      if (userInitiated && window.matchMedia('(max-width: 760px)').matches) {
        detail.scrollIntoView({behavior: window.matchMedia('(prefers-reduced-motion: reduce)').matches ? 'instant' : 'smooth', block: 'start'});
      }
    } catch (error) {
      if (version !== detailVersion) return;
      detail.setAttribute('aria-busy', 'false');
      renderEmpty('新闻暂时无法打开', error.message);
      const retry = element('button', 'button button-quiet', '重新加载');
      retry.type = 'button';
      retry.addEventListener('click', () => selectItem(id, false));
      detail.firstChild.append(retry);
    }
  }

  async function loadNews(append = false) {
    if (append && loading) return;
    const version = ++listVersion;
    loading = true;
    refresh.disabled = true;
    more.disabled = true;
    list.setAttribute('aria-busy', 'true');
    showStatus(append ? '正在加载更多…' : '正在获取新闻…');
    const offset = append ? items.length : 0;
    try {
      const query = new URLSearchParams({limit: '30', offset: String(offset)});
      if (category) query.set('category', category);
      const data = await request(`/news?${query}`);
      if (version !== listVersion) return;
      items = append ? items.concat(data.items) : data.items;
      total = Number(data.total) || 0;
      renderList();
      showStatus(items.length ? '' : '当前分类暂无新闻。');
      if (!append) {
        if (selectedId && (!category || items.some(item => item.id === selectedId))) {
          await selectItem(selectedId, false);
        } else if (items.length) {
          await selectItem(items[0].id, true);
        } else {
          ++detailVersion;
          selectedId = null;
          document.title = '新闻 · BRS';
          detail.setAttribute('aria-busy', 'false');
          renderEmpty();
        }
      }
    } catch (error) {
      if (version !== listVersion) return;
      showStatus(error.message, true);
      if (!items.length) {
        detail.setAttribute('aria-busy', 'false');
        renderEmpty('连接暂时中断', '点击右上角「刷新」重试。');
      }
    } finally {
      if (version === listVersion) {
        loading = false;
        refresh.disabled = false;
        more.disabled = false;
        list.setAttribute('aria-busy', 'false');
      }
    }
  }

  filters.forEach(button => button.addEventListener('click', () => {
    if (category === button.dataset.category) return;
    category = button.dataset.category;
    ++detailVersion;
    selectedId = null;
    items = [];
    total = 0;
    renderList();
    detail.setAttribute('aria-busy', 'true');
    renderEmpty('正在切换分类…', '请稍候，正在获取新闻列表。');
    const url = new URL(location.href);
    url.searchParams.delete('id');
    history.replaceState(null, '', url);
    document.title = '新闻 · BRS';
    filters.forEach(filter => {
      const active = filter === button;
      filter.classList.toggle('active', active);
      filter.setAttribute('aria-pressed', String(active));
    });
    loadNews();
  }));
  more.addEventListener('click', () => loadNews(true));
  refresh.addEventListener('click', () => loadNews());
  loadNews();
})();
