/* Windbound Bard site: no runtime dependencies, no tracking, no game integration. */
(() => {
  'use strict';
  const menuButton = document.getElementById('menu-button');
  const nav = document.getElementById('primary-nav');
  if (menuButton && nav) {
    const setOpen = open => {
      nav.classList.toggle('open', open);
      menuButton.setAttribute('aria-expanded', String(open));
      menuButton.setAttribute('aria-label', open ? 'Close navigation' : 'Open navigation');
      menuButton.textContent = open ? '✕' : '☰';
    };
    menuButton.addEventListener('click', () => setOpen(menuButton.getAttribute('aria-expanded') !== 'true'));
    nav.querySelectorAll('a').forEach(a => a.addEventListener('click', () => setOpen(false)));
    document.addEventListener('keydown', e => { if (e.key === 'Escape') setOpen(false); });
  }
  document.querySelectorAll('[data-nav]').forEach(a => {
    if (a.dataset.nav === document.body.dataset.page) {
      a.classList.add('active');
      a.setAttribute('aria-current', 'page');
    }
  });

  const fallbackCopy = text => {
    const input = document.createElement('textarea');
    input.value = text;
    input.setAttribute('readonly', '');
    input.style.position = 'fixed';
    input.style.opacity = '0';
    document.body.appendChild(input);
    input.select();
    let success = false;
    try { success = document.execCommand('copy'); } catch (e) { /* manual fallback */ }
    input.remove();
    return success;
  };
  document.querySelectorAll('[data-copy]').forEach(button => button.addEventListener('click', async () => {
    const value = button.dataset.copy;
    let copied = false;
    try {
      if (navigator.clipboard && window.isSecureContext) {
        await navigator.clipboard.writeText(value);
        copied = true;
      }
    } catch (e) { /* use fallback */ }
    if (!copied) copied = fallbackCopy(value);
    if (!copied) { window.prompt('Copy this text:', value); return; }
    const original = button.textContent;
    button.textContent = 'Copied ✓';
    button.setAttribute('aria-label', 'Copied to clipboard');
    window.setTimeout(() => {
      button.textContent = original;
      button.removeAttribute('aria-label');
    }, 1700);
  }));

  // A pinned fallback download is kept in the HTML for offline browsing and API failures.
  // Live release information is retrieved only from the project's public GitHub API.
  const releaseTag = document.querySelector('[data-release-tag]');
  const releaseDate = document.querySelector('[data-release-date]');
  const downloadLinks = document.querySelectorAll('[data-latest-download]');
  const releaseLinks = document.querySelectorAll('[data-latest-release]');
  if (releaseTag || downloadLinks.length) {
    const controller = new AbortController();
    const abortTimer = window.setTimeout(() => controller.abort(), 6500);
    fetch('https://api.github.com/repos/BTYRE1916/NunuWindboundBard-FFXIV/releases/latest', {
      headers: { 'Accept': 'application/vnd.github+json' },
      signal: controller.signal
    })
      .then(r => { if (!r.ok) throw new Error('Release unavailable'); return r.json(); })
      .then(data => {
        if (!data || !data.html_url || !data.tag_name) return;
        if (releaseTag) releaseTag.textContent = data.tag_name;
        if (releaseDate && data.published_at) {
          releaseDate.textContent = new Intl.DateTimeFormat('en-GB', { day:'numeric', month:'long', year:'numeric' }).format(new Date(data.published_at));
        }
        releaseLinks.forEach(a => a.href = data.html_url);
        const asset = (data.assets || []).find(a => /\.zip$/i.test(a.name || '') && /^https:\/\/github\.com\//.test(a.browser_download_url || ''));
        if (asset) downloadLinks.forEach(a => a.href = asset.browser_download_url);
      })
      .catch(() => { /* Static, verified fallback link remains in place. */ })
      .finally(() => window.clearTimeout(abortTimer));
  }

  // Only a visual, self-contained demonstration. It never sends commands to the game.
  const stage = document.querySelector('[data-stage-demo]');
  if (stage) {
    const svg = document.getElementById('stage-svg');
    const layer = document.getElementById('stage-dots');
    const ns = 'http://www.w3.org/2000/svg';
    const clamp = (v, lo, hi) => Math.max(lo, Math.min(hi, v));
    let dots = [];
    let dragged = null;
    const buildLayout = name => {
      const n = 8;
      if (name === 'line') return Array.from({ length:n }, (_,i) => ({x:62+i*54,y:164}));
      if (name === 'rows') return Array.from({ length:n }, (_,i) => ({x:86+(i%4)*110,y:i<4?104:210}));
      if (name === 'circle') return Array.from({ length:n }, (_,i) => ({x:250+150*Math.cos(i*2*Math.PI/n-Math.PI/2), y:152+104*Math.sin(i*2*Math.PI/n-Math.PI/2)}));
      return Array.from({ length:n }, (_,i) => ({x:64+i*53,y:155+72*Math.pow((i-3.5)/3.5,2)}));
    };
    const draw = () => {
      layer.replaceChildren();
      dots.forEach((point, index) => {
        const group = document.createElementNS(ns,'g');
        const circle = document.createElementNS(ns,'circle');
        circle.setAttribute('class','stage-dot');
        circle.setAttribute('cx',point.x);
        circle.setAttribute('cy',point.y);
        circle.setAttribute('r',19);
        circle.setAttribute('tabindex','0');
        circle.setAttribute('role','button');
        circle.setAttribute('aria-label', `Bard ${index+1}. Use arrow keys or drag to reposition.`);
        circle.dataset.index = String(index);
        const text = document.createElementNS(ns,'text');
        text.setAttribute('class','stage-number');
        text.setAttribute('x',point.x);
        text.setAttribute('y',point.y+1);
        text.textContent = String(index+1);
        group.append(circle,text);
        layer.append(group);
      });
    };
    const select = name => {
      dots = buildLayout(name);
      draw();
      stage.querySelectorAll('[data-layout]').forEach(button => button.setAttribute('aria-pressed', String(button.dataset.layout === name)));
    };
    stage.querySelectorAll('[data-layout]').forEach(button => button.addEventListener('click', () => select(button.dataset.layout)));
    const svgPoint = e => {
      const rect = svg.getBoundingClientRect();
      return {x:clamp((e.clientX-rect.left)*500/rect.width,40,460), y:clamp((e.clientY-rect.top)*310/rect.height,40,260)};
    };
    const moveDot = (index, point) => {
      dots[index] = {x:point.x, y:point.y};
      const circle = layer.querySelector(`.stage-dot[data-index="${index}"]`);
      if (!circle) return;
      const label = circle.nextElementSibling;
      circle.setAttribute('cx',point.x);circle.setAttribute('cy',point.y);
      label.setAttribute('x',point.x);label.setAttribute('y',point.y+1);
    };
    svg.addEventListener('pointerdown', e => {
      const target = e.target.closest('.stage-dot');
      if (!target) return;
      dragged = Number(target.dataset.index);
      svg.setPointerCapture?.(e.pointerId);
      moveDot(dragged,svgPoint(e));
    });
    svg.addEventListener('pointermove', e => { if (dragged !== null) moveDot(dragged,svgPoint(e)); });
    const drop = () => { dragged = null; };
    svg.addEventListener('pointerup', drop);
    svg.addEventListener('pointercancel', drop);
    svg.addEventListener('keydown', e => {
      const target = e.target.closest('.stage-dot');
      if (!target || !['ArrowUp','ArrowDown','ArrowLeft','ArrowRight'].includes(e.key)) return;
      e.preventDefault();
      const i = Number(target.dataset.index);
      const p = dots[i];
      const step = e.shiftKey ? 15 : 5;
      moveDot(i, {x:clamp(p.x + (e.key==='ArrowRight'?step:e.key==='ArrowLeft'?-step:0),40,460),y:clamp(p.y+(e.key==='ArrowDown'?step:e.key==='ArrowUp'?-step:0),40,260)});
    });
    select('arc');
  }
})();
