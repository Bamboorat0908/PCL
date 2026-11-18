'use strict';

(() => {
  const base = '/BRS/news';
  const byId = id => document.getElementById(id);
  const form = byId('editor-form');
  const loginForm = byId('login-form');
  const list = byId('admin-list');
  const status = byId('global-status');
  const fields = {title: byId('title'), summary: byId('summary'), content: byId('content'), category: byId('category'), status: byId('publication-status'), pinned: byId('pinned')};
  let csrf = '';
  let items = [];
  let selectedId = null;
  let baseline = '';
  let initialized = false;
  let saving = false;
  let authenticated = false;
  let listVersion = 0;

  const element = (tag, className, text) => {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (text !== undefined) node.textContent = text;
    return node;
  };

  function message(id, text, kind = '') {
    const target = typeof id === 'string' ? byId(id) : id;
    target.textContent = text;
    target.classList.toggle('error', kind === 'error');
    target.classList.toggle('success', kind === 'success');
  }

  function payload() {
    return {title: fields.title.value, summary: fields.summary.value, content: fields.content.value, category: fields.category.value, status: fields.status.value, pinned: fields.pinned.checked};
  }

  const dirty = () => initialized && JSON.stringify(payload()) !== baseline;
  const canDiscard = () => !dirty() || window.confirm('有尚未保存的修改，确定放弃这些修改吗？');

  async function request(path, method = 'GET', body) {
    const headers = {'Accept': 'application/json'};
    if (method !== 'GET') {
      headers['Content-Type'] = 'application/json';
      if (csrf) headers['X-CSRF-Token'] = csrf;
    }
    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), 20000);
    try {
      const response = await fetch(`${base}/api/admin${path}`, {method, credentials: 'same-origin', cache: 'no-store', signal: controller.signal, headers, body: body === undefined ? undefined : JSON.stringify(body)});
      const data = await response.json().catch(() => ({}));
      if (!response.ok) {
        if (response.status === 401 && path !== '/login' && path !== '/session') showLogin('登录已过期，请重新登录。');
        const error = new Error(data.error || '请求未能完成，请稍后重试。');
        error.status = response.status;
        throw error;
      }
      return data;
    } catch (error) {
      if (controller.signal.aborted) throw new Error('连接超时，请刷新列表确认操作结果后重试。');
      if (error instanceof TypeError) throw new Error('无法连接新闻服务，请检查网络后重试。');
      throw error;
    } finally {
      clearTimeout(timer);
    }
  }

  function showLogin(text = '', kind = 'error') {
    authenticated = false;
    csrf = '';
    byId('login-panel').hidden = false;
    byId('workspace').hidden = true;
    byId('account-bar').hidden = true;
    byId('password-panel').hidden = true;
    byId('password-form').reset();
    message(status, '');
    message('login-status', text, text ? kind : '');
  }

  async function showWorkspace(session) {
    authenticated = true;
    csrf = session.csrf_token;
    byId('account-name').textContent = session.username;
    byId('login-panel').hidden = true;
    byId('workspace').hidden = false;
    byId('account-bar').hidden = false;
    byId('password-panel').hidden = false;
    byId('password').value = '';
    message(status, '');
    if (!initialized) startNew(false);
    await loadItems();
  }

  function dateText(value) {
    if (!value) return '—';
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) return '—';
    return new Intl.DateTimeFormat('zh-CN', {year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', hour12: false}).format(date);
  }

  function renderList() {
    list.replaceChildren();
    const filter = byId('admin-filter').value;
    const shown = items.filter(item => filter === 'all' || item.status === filter);
    byId('admin-count').textContent = `${items.length} 条`;
    if (!shown.length) {
      list.append(element('p', 'empty-list', items.length ? '当前筛选下没有新闻。' : '暂无新闻。点击「新建新闻」，开始撰写第一条公告。'));
      return;
    }
    for (const item of shown) {
      const button = element('button', `news-card${item.id === selectedId ? ' active' : ''}`);
      button.type = 'button';
      button.dataset.newsId = String(item.id);
      button.setAttribute('aria-pressed', String(item.id === selectedId));
      const top = element('div', 'card-topline');
      const badges = element('span', 'card-badges');
      badges.append(element('span', `badge ${item.status === 'published' ? 'badge-published' : 'badge-draft'}`, item.status === 'published' ? '已发布' : '草稿'));
      if (item.pinned) badges.append(element('span', 'badge badge-pin', '置顶'));
      top.append(badges, element('span', 'card-date', item.category === 'update' ? '动态' : '公告'));
      button.append(top, element('h3', 'card-title', item.title), element('p', 'card-summary', `更新于 ${dateText(item.updated_at)}`));
      button.addEventListener('click', () => {
        if (saving || !canDiscard()) return;
        setEditor(item);
      });
      list.append(button);
    }
  }

  function markSelection() {
    for (const button of list.querySelectorAll('.news-card')) {
      const active = Number(button.dataset.newsId) === selectedId;
      button.classList.toggle('active', active);
      button.setAttribute('aria-pressed', String(active));
    }
  }

  async function loadItems() {
    const version = ++listVersion;
    byId('reload-items').disabled = true;
    message('items-status', '正在加载新闻列表…');
    try {
      const all = [];
      let offset = 0;
      for (;;) {
        const data = await request(`/news?limit=100&offset=${offset}`);
        if (version !== listVersion || !authenticated) return;
        all.push(...data.items);
        if (!data.has_more || !data.items.length) break;
        offset = data.next_offset === undefined ? all.length : data.next_offset;
      }
      items = all;
      renderList();
      message('items-status', '');
    } catch (error) {
      if (version === listVersion) message('items-status', error.message, 'error');
    } finally {
      if (version === listVersion) byId('reload-items').disabled = false;
    }
  }

  function updateEditorState() {
    const changed = dirty();
    const badge = byId('editor-state');
    badge.textContent = changed ? '有未保存修改' : selectedId ? fields.status.value === 'published' ? '已发布' : '草稿' : '尚未保存';
    badge.className = `badge ${!changed && selectedId && fields.status.value === 'published' ? 'badge-published' : 'badge-draft'}`;
    byId('save-item').textContent = fields.status.value === 'published' ? '保存并发布' : '保存草稿';
    byId('content-count').textContent = `${fields.content.value.length} / 50000`;
    if (changed) message('save-status', '');
  }

  function setEditor(item) {
    selectedId = item ? item.id : null;
    form.reset();
    fields.title.value = item ? item.title : '';
    fields.summary.value = item ? item.summary || '' : '';
    fields.content.value = item ? item.content || '' : '';
    fields.category.value = item ? item.category : 'announcement';
    fields.status.value = item ? item.status : 'draft';
    fields.pinned.checked = item ? Boolean(item.pinned) : false;
    byId('editor-heading').textContent = item ? '编辑新闻' : '新建新闻';
    byId('delete-item').hidden = !item;
    byId('view-item').hidden = !item || item.status !== 'published';
    if (item) byId('view-item').href = `${base}?id=${encodeURIComponent(item.id)}`;
    byId('editor-meta').textContent = item ? `创建于 ${dateText(item.created_at)}　·　更新于 ${dateText(item.updated_at)}${item.published_at ? `　·　发布于 ${dateText(item.published_at)}` : ''}` : '';
    initialized = true;
    baseline = JSON.stringify(payload());
    updateEditorState();
    message('save-status', '');
    markSelection();
  }

  function startNew(focus = true) {
    setEditor(null);
    if (focus) fields.title.focus();
  }

  function setSaving(value) {
    saving = value;
    byId('save-item').disabled = value;
    byId('delete-item').disabled = value;
    byId('new-item').disabled = value;
    byId('logout').disabled = value;
    byId('password-submit').disabled = value;
    for (const id of ['current-password', 'new-password', 'repeat-password']) byId(id).disabled = value;
    for (const field of Object.values(fields)) field.disabled = value;
  }

  loginForm.addEventListener('submit', async event => {
    event.preventDefault();
    byId('login-submit').disabled = true;
    message('login-status', '正在验证…');
    try {
      const data = await request('/login', 'POST', {username: byId('username').value.trim(), password: byId('password').value});
      await showWorkspace(data);
    } catch (error) {
      message('login-status', error.message, 'error');
    } finally {
      byId('login-submit').disabled = false;
    }
  });

  byId('logout').addEventListener('click', async () => {
    if (saving || !canDiscard()) return;
    setSaving(true);
    try {
      await request('/logout', 'POST', {});
      initialized = false;
      form.reset();
      items = [];
      list.replaceChildren();
      showLogin();
    } catch (error) {
      message(status, error.message, 'error');
    } finally {
      setSaving(false);
    }
  });

  byId('new-item').addEventListener('click', () => { if (!saving && canDiscard()) startNew(); });
  byId('reload-items').addEventListener('click', loadItems);
  byId('admin-filter').addEventListener('change', renderList);
  form.addEventListener('input', updateEditorState);
  form.addEventListener('change', updateEditorState);
  form.addEventListener('submit', async event => {
    event.preventDefault();
    if (saving) return;
    const data = payload();
    if (data.status === 'published' && !window.confirm(selectedId ? '确定保存并发布这条新闻？保存后读者将看到最新内容。' : '确定发布这条新闻？发布后将在网站和启动器中公开展示。')) return;
    setSaving(true);
    message('save-status', data.status === 'published' ? '正在发布…' : '正在保存草稿…');
    try {
      const result = await request(selectedId ? `/news/${selectedId}` : '/news', selectedId ? 'PUT' : 'POST', data);
      setEditor(result.item);
      message('save-status', result.item.status === 'published' ? '新闻已发布，网站与 BRCL 刷新后即可查看。' : '草稿已保存，仅管理员可见。', 'success');
      await loadItems();
    } catch (error) {
      message('save-status', error.message, 'error');
    } finally {
      setSaving(false);
    }
  });

  byId('delete-item').addEventListener('click', async () => {
    if (saving || !selectedId) return;
    if (!window.confirm(`确定永久删除「${fields.title.value}」？此操作无法恢复。`)) return;
    setSaving(true);
    message('save-status', '正在删除…');
    try {
      await request(`/news/${selectedId}`, 'DELETE', {});
      startNew(false);
      message('save-status', '新闻已删除。', 'success');
      await loadItems();
    } catch (error) {
      message('save-status', error.message, 'error');
    } finally {
      setSaving(false);
    }
  });

  byId('password-form').addEventListener('submit', async event => {
    event.preventDefault();
    if (saving) {
      message('password-status', '请等待当前保存操作完成。');
      return;
    }
    const newPassword = byId('new-password').value;
    if (newPassword !== byId('repeat-password').value) {
      message('password-status', '两次输入的新密码不一致。', 'error');
      byId('repeat-password').focus();
      return;
    }
    if (!canDiscard()) return;
    const currentPassword = byId('current-password').value;
    setSaving(true);
    message('password-status', '正在更新密码…');
    try {
      await request('/password', 'POST', {current_password: currentPassword, new_password: newPassword});
      initialized = false;
      form.reset();
      items = [];
      list.replaceChildren();
      showLogin('密码已更新，请使用新密码登录。', 'success');
      byId('password').focus();
    } catch (error) {
      message('password-status', error.message, 'error');
    } finally {
      setSaving(false);
    }
  });

  window.addEventListener('beforeunload', event => {
    if (dirty()) {
      event.preventDefault();
      event.returnValue = '';
    }
  });

  request('/session').then(showWorkspace).catch(error => {
    showLogin(error.status === 401 ? '' : error.message);
  });
})();
