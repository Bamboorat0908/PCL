'use strict';

const base = '/BRCL/InkAdmin';
const form = document.getElementById('login');
const field = document.getElementById('key');
const message = document.getElementById('message');
const submit = form.querySelector('button');

form.addEventListener('submit', async event => {
  event.preventDefault();
  if (submit.disabled) return;
  submit.disabled = true;
  submit.textContent = '正在登录…';
  message.textContent = '';
  field.removeAttribute('aria-invalid');
  try {
    const response = await fetch(base + '/api/login', {
      method: 'POST', credentials: 'same-origin', cache: 'no-store',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ key: field.value })
    });
    if (response.ok) {
      field.value = '';
      location.replace(base);
      return;
    }
    if (response.status === 401) {
      message.textContent = '管理密钥有误，请重新输入。';
      field.setAttribute('aria-invalid', 'true');
      field.focus();
      field.select();
    } else if (response.status === 429) {
      message.textContent = '尝试次数过多，请稍后再试。';
    } else {
      message.textContent = '登录服务暂时无法处理请求，请稍后重试。';
    }
  } catch {
    message.textContent = '无法连接到管理服务，请检查网络后重试。';
  } finally {
    submit.disabled = false;
    submit.textContent = '进入管理页';
  }
});
