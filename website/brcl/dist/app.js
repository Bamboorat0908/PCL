"use strict";

(() => {
  const screens = {
    "launch": {
      "src": "/BRCL/assets/screens/launch-1.0.2.7.webp",
      "alt": "BRCL 首页布局预览：服务器状态、趣味日历与每日运势示意",
      "caption": "布局预览 · 首页日历与每日运势，结果为示例",
      "index": "01 / 06"
    },
    "bank": {
      "src": "/BRCL/assets/screens/bank-1.0.2.7.webp",
      "alt": "BRCL 银行账户总览布局预览，使用示例数据，非真实账户",
      "caption": "布局预览 · 银行账户总览，示例数据，非真实账户",
      "index": "02 / 06"
    },
    "presets": {
      "src": "/BRCL/assets/screens/presets-1.0.2.7.webp",
      "alt": "TRM 1.11 高预设与低预设选择面板",
      "caption": "界面预览 · TRM 1.11 高、低预设选择",
      "index": "03 / 06"
    },
    "news": {
      "src": "/BRCL/assets/screens/news.webp",
      "alt": "BRCL 新闻频道布局预览",
      "caption": "布局预览 · 新闻频道，给阅读留出更多空间",
      "index": "04 / 06"
    },
    "settings": {
      "src": "/BRCL/assets/screens/settings.webp",
      "alt": "BRCL 偏好设置布局预览",
      "caption": "布局预览 · 偏好设置，常用选项与个人习惯",
      "index": "05 / 06"
    },
    "map": {
      "src": "/BRCL/assets/screens/map.webp",
      "alt": "BRCL 卫星地图界面",
      "caption": "卫星地图 · 从另一个角度探索 BRS",
      "index": "06 / 06"
    }
  };
  const tabs = Array.from(document.querySelectorAll(".showcase-tab"));
  const image = document.getElementById("showcase-image");
  const panel = document.getElementById("showcase-panel");
  const caption = document.getElementById("screen-caption");
  const index = document.getElementById("screen-index");
  const openImage = document.getElementById("showcase-open");
  let selectionVersion = 0;
  const selectScreen = tab => {
    const screen = screens[tab.dataset.screen];
    if (!screen) return;
    for (const item of tabs) {
      const selected = item === tab;
      item.classList.toggle("is-selected", selected);
      item.setAttribute("aria-selected", String(selected));
      item.tabIndex = selected ? 0 : -1;
    }
    panel.setAttribute("aria-labelledby", tab.id);
    caption.textContent = screen.caption;
    index.textContent = screen.index;
    const current = ++selectionVersion;
    image.classList.add("is-loading");
    const next = new Image();
    next.onload = () => {
      if (current !== selectionVersion) return;
      image.src = screen.src;
      image.alt = screen.alt;
      openImage.href = screen.src;
      image.classList.remove("is-loading");
    };
    next.onerror = () => {
      if (current !== selectionVersion) return;
      image.classList.remove("is-loading");
      caption.textContent = "这张界面图暂时无法加载，请稍后重试。";
    };
    next.src = screen.src;
  };
  for (const tab of tabs) {
    tab.addEventListener("click", () => selectScreen(tab));
    tab.addEventListener("keydown", event => {
      const position = tabs.indexOf(tab);
      let next;
      if (event.key === "ArrowRight") next = tabs[(position + 1) % tabs.length];
      else if (event.key === "ArrowLeft") next = tabs[(position + tabs.length - 1) % tabs.length];
      else if (event.key === "Home") next = tabs[0];
      else if (event.key === "End") next = tabs[tabs.length - 1];
      if (!next) return;
      event.preventDefault();
      selectScreen(next);
      next.focus();
      next.scrollIntoView({ block: "nearest", inline: "nearest", behavior: "auto" });
    });
  }

  const shaElement = document.getElementById("release-sha");
  const copyButton = document.getElementById("copy-sha");
  const bundledChecksum = shaElement.textContent.trim();
  let checksum = /^[a-f\d]{64}$/i.test(bundledChecksum) ? bundledChecksum.toUpperCase() : "";
  copyButton.disabled = !checksum;
  copyButton.addEventListener("click", async () => {
    if (!checksum) return;
    const status = document.getElementById("copy-status");
    try {
      await navigator.clipboard.writeText(checksum);
      status.textContent = "已复制 SHA-256";
    } catch {
      const selection = window.getSelection();
      const range = document.createRange();
      range.selectNodeContents(shaElement);
      selection.removeAllRanges();
      selection.addRange(range);
      shaElement.focus();
      status.textContent = "请手动复制上方已选中的校验码。";
    }
  });

  const loadRelease = async () => {
    const controller = new AbortController();
    const timeout = window.setTimeout(() => controller.abort(), 6000);
    try {
      const response = await fetch("/BRCL/release.json", { cache: "no-store", signal: controller.signal, credentials: "same-origin" });
      if (!response.ok) throw new Error("Release metadata unavailable");
      const release = await response.json();
      const displayVersion = typeof release.display_version === "string" ? release.display_version.trim() : "";
      const size = Number(release.size);
      if (!displayVersion || displayVersion.length > 50 || !Number.isSafeInteger(size) || size <= 0) throw new Error("Invalid release metadata");
      if (typeof release.sha256 !== "string" || !/^[a-f\d]{64}$/i.test(release.sha256)) throw new Error("Invalid release checksum");
      document.getElementById("release-version").textContent = displayVersion;
      document.getElementById("release-size").textContent = (size / 1048576).toLocaleString("zh-CN", { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + " MiB";
      let build = "";
      if (typeof release.site_release === "string" && /^\d{4}-\d{2}-\d{2}$/.test(release.site_release)) build = " · 官网构建 " + release.site_release.replaceAll("-", ".");
      document.getElementById("release-label").textContent = displayVersion + build;
      checksum = release.sha256.toUpperCase();
      shaElement.textContent = checksum;
      copyButton.disabled = false;
      if (typeof release.download_url === "string") {
        const download = new URL(release.download_url, location.origin);
        if (download.origin === location.origin && /^\/BRCL\/download\/[A-Za-z0-9._-]+\.exe$/i.test(download.pathname)) {
          for (const anchor of document.querySelectorAll("[data-download]")) anchor.href = download.href;
        }
      }
      document.getElementById("release-status").textContent = "单 EXE 文件 · 下载后直接运行，无需配套 DLL 或配置文件。";
    } catch {
      if (!checksum) {
        document.getElementById("release-status").textContent = "版本信息暂时不可用，下载入口仍可使用。请以下载文件为准。";
        shaElement.textContent = "暂时无法读取校验信息，请稍后刷新页面。";
      }
    } finally {
      window.clearTimeout(timeout);
    }
  };
  loadRelease();
})();
