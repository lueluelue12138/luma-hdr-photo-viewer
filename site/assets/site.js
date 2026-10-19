// Luma homepage behaviour: scroll reveal, navigation hairline, the hero SDR / HDR comparison, the latest release and the Live Photos demo.
// The page is fully readable without this file. What it adds: on an HDR display a second copy of the hero's own image
// (served from the cache), and once per hour a request to GitHub's public API for the latest release.
(() => {
  'use strict';

  // Tell the inline guard in <head> that this file loaded; otherwise it un-hides the page after 4 s.
  document.documentElement.dataset.ready = '1';

  const reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)');

  // Reveal blocks as they enter the viewport.
  const blocks = document.querySelectorAll('[data-reveal]');
  if ('IntersectionObserver' in window && !reduceMotion.matches) {
    const observer = new IntersectionObserver((entries) => {
      for (const entry of entries) {
        if (!entry.isIntersecting) continue;
        entry.target.classList.add('is-in');
        observer.unobserve(entry.target);
      }
    }, { rootMargin: '0px 0px -8% 0px', threshold: 0.1 });
    blocks.forEach((block) => observer.observe(block));
  } else {
    blocks.forEach((block) => block.classList.add('is-in'));
  }

  // Hairline under the sticky navigation once the page has moved.
  const nav = document.querySelector('.nav');
  const syncNav = () => nav.classList.toggle('is-scrolled', window.scrollY > 8);
  window.addEventListener('scroll', syncNav, { passive: true });
  syncNav();

  // Hero before/after. The hero is one AVIF image that carries an HDR gain map. Every visitor gets that one file and an
  // SDR display simply shows its base image. On an HDR display, in a browser that renders HDR images and can limit them
  // with CSS dynamic-range-limit, a second copy of the same file (served from the cache) is layered over the first: the
  // lower copy is limited to SDR, the upper one is shown in full HDR, and a draggable divider reveals it. The two screenshots
  // label the window differently ("SDR · 原图" / "RTX HDR 已生效"). SDR-only visitors see the captured RTX badge as a promotional
  // overlay; in comparison mode it moves into the box and is clipped with the HDR half, revealing the original SDR badge on the left.
  const stage = document.querySelector('[data-stage]');
  const sdr = stage && stage.querySelector('img');
  const hdrCapable = () => window.matchMedia('(dynamic-range: high)').matches
    && !!window.CSS && CSS.supports('dynamic-range-limit', 'no-limit');
  const decoded = (src) => {
    const image = new Image();
    image.src = src;
    return image.decode().then(() => image, () => null);
  };

  const buildCompare = (hdr) => {
    const box = document.createElement('div');
    box.className = 'cmp';
    box.setAttribute('role', 'group');
    box.setAttribute('aria-label', 'SDR 与 HDR 显示对比');
    const lower = sdr.closest('picture') || sdr;   // move the whole <picture>: taking the <img> out would make it fall back to the WebP
    lower.before(box);
    sdr.classList.add('cmp__sdr');
    sdr.alt = 'Luma 看图窗口的 SDR 基底：蓝天白云下的草甸与雪山，右上角提示“SDR · 原图”；右侧可对比 RTX HDR 增强。';
    hdr.className = 'cmp__hdr';
    hdr.alt = '';
    box.append(lower, hdr);
    const badge = stage.querySelector('.hero-badge');
    if (badge) {
      box.append(badge);
    }
    box.insertAdjacentHTML('beforeend',
      '<span class="cmp__tag cmp__tag--sdr" aria-hidden="true">SDR</span>' +
      '<span class="cmp__tag cmp__tag--hdr" aria-hidden="true">HDR</span>' +
      '<span class="cmp__line"></span>' +
      '<span class="cmp__knob"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M9 7l-5 5 5 5M15 7l5 5-5 5"/></svg></span>' +
      '<input class="cmp__range" type="range" min="0" max="100" step="0.1" value="50" aria-label="拖动对比 SDR 与 HDR 显示">');
    const range = box.querySelector('.cmp__range');
    const sync = () => {
      box.style.setProperty('--p', range.value + '%');
      range.setAttribute('aria-valuetext', '分界线在 ' + Math.round(range.value) + '%，左侧 SDR，右侧 HDR');
    };
    range.addEventListener('input', sync);
    range.addEventListener('keydown', (event) => {
      const delta = { ArrowLeft: -2, ArrowDown: -2, ArrowRight: 2, ArrowUp: 2 }[event.key];
      if (delta === undefined) return;
      event.preventDefault();
      range.value = Math.min(100, Math.max(0, Number(range.value) + delta));
      sync();
    });
    sync();
    const caption = stage.querySelector('figcaption');
    if (caption) caption.textContent = '拖动分界线：左侧是 SDR 预览，右侧是 RTX HDR 增强后的显示（同一窗口的 Windows 实机素材）。实际观感取决于你的屏幕、系统 HDR 设置和浏览器。';
  };

  if (sdr && hdrCapable()) {
    const start = () => {
      if (!/\.avif($|[?#])/.test(sdr.currentSrc)) return;      // the WebP fallback carries no HDR
      // The same file as the visible image, served from the cache; if it cannot be decoded, keep the plain image.
      decoded(sdr.currentSrc).then((hdr) => { if (hdr) buildCompare(hdr); });
    };
    if (document.readyState === 'complete') start(); else window.addEventListener('load', start, { once: true });
  }

  // Latest release. The page names no version of its own. If GitHub's public API answers, the version, the file sizes and direct download
  // links are filled in (the answer is kept for an hour, which also keeps a shared address under the API's hourly limit). If it does not,
  // nothing is shown and the download buttons stay on the Releases page.
  const REPO = 'lueluelue12138/luma-hdr-photo-viewer';
  const RELEASES = 'https://github.com/' + REPO + '/releases/';
  const CACHE_KEY = 'luma-latest-release';

  const cachedRelease = () => {
    try {
      const saved = JSON.parse(localStorage.getItem(CACHE_KEY));
      if (saved && Date.now() - saved.time < 3600e3) return saved.release;
    } catch (error) { /* storage blocked or an old entry: ask again */ }
    return null;
  };

  const fetchRelease = async () => {
    const cached = cachedRelease();
    if (cached) return cached;
    const abort = new AbortController();
    const timer = setTimeout(() => abort.abort(), 6000);     // a slow or blocked API must not hold anything up
    try {
      const response = await fetch('https://api.github.com/repos/' + REPO + '/releases/latest',
        { headers: { Accept: 'application/vnd.github+json' }, signal: abort.signal });
      if (!response.ok) return null;
      const json = await response.json();
      const release = {
        tag: json.tag_name, url: json.html_url,
        assets: (json.assets || []).map((asset) => ({ name: asset.name, size: asset.size, url: asset.browser_download_url })),
      };
      try { localStorage.setItem(CACHE_KEY, JSON.stringify({ time: Date.now(), release })); } catch (error) { /* ignore */ }
      return release;
    } catch (error) {
      return null;
    } finally {
      clearTimeout(timer);
    }
  };

  const showRelease = (release) => {
    const match = /^v?(\d+(?:\.\d+){1,3})$/.exec(String(release.tag));
    if (!match || !String(release.url).startsWith(RELEASES)) return;          // anything unexpected: show nothing
    const version = match[1];
    const mib = (bytes) => (bytes / 1048576).toFixed(2) + ' MiB';

    const line = document.querySelector('[data-release="line"]');
    const sep = document.querySelector('[data-release="sep"]');
    if (line && sep) {
      line.textContent = '当前正式版 ' + version;
      line.hidden = sep.hidden = false;
    }
    const notes = document.querySelector('[data-release="notes"]');
    if (notes) {
      notes.textContent = version + ' 更新说明';
      notes.href = release.url;
    }
    const files = { setup: /windows-x64.*setup\.exe$/i, portable: /windows-x64.*portable\.zip$/i };
    for (const [key, pattern] of Object.entries(files)) {
      const button = document.querySelector('[data-asset="' + key + '"]');
      const asset = (release.assets || []).find((candidate) => pattern.test(candidate.name) && String(candidate.url).startsWith(RELEASES + 'download/'));
      if (!button || !asset) continue;
      button.href = asset.url;
      button.querySelector('small').textContent = (key === 'setup' ? version + ' · ' : '') + mib(asset.size);
    }
  };

  if ('fetch' in window && 'AbortController' in window) {
    const load = () => fetchRelease().then((release) => { if (release) showRelease(release); });
    if (document.readyState === 'complete') load(); else window.addEventListener('load', load, { once: true });
  }

  // Live Photos demo: a short, clearly labelled illustration (the photo drifts, rings spread from the Live icon). No video is played.
  const demo = document.getElementById('live-demo');
  if (demo) {
    const button = demo.querySelector('.live-btn');
    const status = document.getElementById('live-state');
    const idle = '点这里，试试实况回放';
    const busy = '播放中 · 示意动画';
    let timer = 0;

    const setPlaying = (playing) => {
      window.clearTimeout(timer);
      demo.classList.toggle('is-playing', playing);
      button.setAttribute('aria-pressed', String(playing));
      status.textContent = playing ? busy : idle;
      if (playing) timer = window.setTimeout(() => setPlaying(false), 3200);
    };

    button.addEventListener('click', () => setPlaying(button.getAttribute('aria-pressed') !== 'true'));
    document.addEventListener('keydown', (event) => {
      if (event.key === 'Escape' && demo.classList.contains('is-playing')) setPlaying(false);
    });
  }
})();
