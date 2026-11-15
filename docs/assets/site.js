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

  // Keep the verified, versioned HTML fallback if GitHub is blocked or unavailable.
  const releaseStatus = document.querySelector('[data-release-status]');
  const updateText = (selector, value) => document.querySelectorAll(selector).forEach(el => { el.textContent = value; });
  if (document.querySelector('[data-release-tag], [data-latest-download]')) {
    const controller = new AbortController();
    const abortTimer = window.setTimeout(() => controller.abort(), 6500);
    fetch('https://api.github.com/repos/BTYRE1916/NunuWindboundBard-FFXIV/releases/latest', {
      headers: { Accept: 'application/vnd.github+json' }, signal: controller.signal
    })
      .then(r => { if (!r.ok) throw new Error('Release unavailable'); return r.json(); })
      .then(data => {
        const asset = window.WindboundRelease?.selectInstaller(data);
        if (!asset) throw new Error('No matching compiled installer');
        const normalized = window.WindboundRelease.version(data.tag_name);
        updateText('[data-release-tag]', data.tag_name);
        updateText('[data-release-assembly]', normalized + '.0');
        updateText('[data-release-size]', (asset.size / 1048576).toFixed(1) + ' MiB');
        if (data.published_at && Number.isFinite(Date.parse(data.published_at))) {
          updateText('[data-release-date]', new Intl.DateTimeFormat('en-GB', { day:'numeric', month:'long', year:'numeric' }).format(new Date(data.published_at)));
        }
        document.querySelectorAll('[data-latest-release]').forEach(a => { a.href = data.html_url; });
        document.querySelectorAll('[data-latest-download]').forEach(a => { a.href = asset.browser_download_url; });
        if (releaseStatus) releaseStatus.textContent = 'Release information checked with GitHub. The button links to the compiled installer asset.';
      })
      .catch(() => {
        if (releaseStatus) releaseStatus.textContent = 'Live release information is unavailable. The button keeps the versioned installer link; you can also open GitHub releases below.';
      })
      .finally(() => window.clearTimeout(abortTimer));
  }

  const faqSearch = document.getElementById('faq-search');
  if (faqSearch) {
    const entries = Array.from(document.querySelectorAll('.faq-grid details'));
    const status = document.getElementById('faq-search-status');
    const filter = () => {
      const query = faqSearch.value.trim().toLocaleLowerCase();
      let count = 0;
      entries.forEach(entry => {
        const match = !query || entry.textContent.toLocaleLowerCase().includes(query);
        entry.hidden = !match;
        if (match) count++;
      });
      if (status) status.textContent = query ? `${count} matching ${count === 1 ? 'answer' : 'answers'}.` : `${entries.length} answers. Search by feature or error message.`;
    };
    faqSearch.addEventListener('input', filter);
    filter();
    const openHash = () => {
      let id;
      try { id = decodeURIComponent(window.location.hash.slice(1)); } catch (_) { return; }
      const target = document.getElementById(id);
      if (target?.tagName === 'DETAILS') {
        faqSearch.value = '';
        filter();
        target.open = true;
      }
    };
    window.addEventListener('hashchange', openHash);
    openHash();
  }

  // An illustration only. Real MIDI editing takes place in the plugin.
  const workbench = document.querySelector('[data-workbench-demo]');
  if (workbench) {
    let ranged = false;
    let split = false;
    const notes = [
      {x:95, y:18, width:70}, {x:178, y:68, width:51}, {x:237, y:93, width:82},
      {x:331, y:143, width:55}, {x:398, y:208, width:69}, {x:478, y:118, width:61},
      {x:551, y:43, width:55}, {x:621, y:193, width:63}
    ];
    const draw = () => {
      const group = workbench.querySelector('[data-demo-notes]');
      group.replaceChildren();
      notes.forEach((note, index) => {
        const rect = document.createElementNS('http://www.w3.org/2000/svg', 'rect');
        let y = note.y;
        if (ranged) { while (y < 50) y += 48; while (y > 168) y -= 48; }
        const attrs = { x:note.x, y, width:note.width, height:13, rx:4, fill:index % 3 ? '#e9a4ca' : '#b4a2ea', class:'forge-note' };
        Object.entries(attrs).forEach(([key, value]) => rect.setAttribute(key, value));
        group.append(rect);
      });
      workbench.querySelector('[data-demo-mode]').textContent = ranged ? 'C3–C6 adapted' : 'Original pitches';
      const tracks = workbench.querySelector('[data-demo-tracks]');
      tracks.replaceChildren();
      (split ? ['Melody', 'Bass Drum', 'Snare Drum', 'Cymbal', 'Bongo', 'Other Percussion'] : ['Melody', 'GM drum kit']).forEach(name => {
        const label = document.createElement('span'); label.className = 'tag'; label.textContent = name; tracks.append(label);
      });
      workbench.querySelector('[data-forge-action="range"]').setAttribute('aria-pressed', String(ranged));
      workbench.querySelector('[data-forge-action="drums"]').setAttribute('aria-pressed', String(split));
      workbench.querySelector('[data-demo-status]').textContent = ranged || split
        ? `${ranged ? 'Pitched notes moved into the green range. ' : ''}${split ? 'The kit is shown as named percussion parts. ' : ''}Illustration only; edit your own MIDI in the plugin.`
        : 'A visual example. Import and edit your own MIDI inside the plugin.';
    };
    workbench.querySelectorAll('[data-forge-action]').forEach(button => button.addEventListener('click', () => {
      if (button.dataset.forgeAction === 'range') ranged = !ranged;
      else if (button.dataset.forgeAction === 'drums') split = !split;
      else { ranged = false; split = false; }
      draw();
    }));
    draw();
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
