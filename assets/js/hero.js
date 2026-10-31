// Primeira dobra: rede de pontos (as empresas do grupo) que reage ao mouse,
// brilho que segue o cursor, leve inclinação 3D do texto e CTA magnético.
(function () {
  var hero = document.getElementById('hero');
  var canvas = document.getElementById('field');
  var ctx = canvas.getContext('2d');
  var tilt = document.getElementById('tilt');
  var mark = document.getElementById('mark');
  var cta = document.getElementById('cta');
  var reduce = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  var finePointer = window.matchMedia('(pointer: fine)').matches;

  var W = 0, H = 0, dpr = 1, nodes = [];
  var mouse = { x: -9999, y: -9999, tx: -9999, ty: -9999, active: false };
  var norm = { x: 0, y: 0, tx: 0, ty: 0 }; // -1..1, suavizado

  function resize() {
    dpr = Math.min(window.devicePixelRatio || 1, 2);
    W = hero.clientWidth; H = hero.clientHeight;
    canvas.width = W * dpr; canvas.height = H * dpr;
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    var count = Math.min(130, Math.round(W * H / 13000));
    nodes = [];
    for (var i = 0; i < count; i++) {
      nodes.push({
        x: Math.random() * W, y: Math.random() * H,
        vx: (Math.random() - 0.5) * 0.25, vy: (Math.random() - 0.5) * 0.25,
        hub: Math.random() < 0.07
      });
    }
    if (reduce) draw();
  }

  function step() {
    // cursor virtual no toque: passeia sozinho
    if (!mouse.active) {
      var t = performance.now() / 4000;
      mouse.tx = W * (0.62 + 0.22 * Math.cos(t));
      mouse.ty = H * (0.45 + 0.2 * Math.sin(t * 1.3));
    }
    mouse.x += (mouse.tx - mouse.x) * 0.12;
    mouse.y += (mouse.ty - mouse.y) * 0.12;
    norm.x += (norm.tx - norm.x) * 0.08;
    norm.y += (norm.ty - norm.y) * 0.08;

    for (var i = 0; i < nodes.length; i++) {
      var n = nodes[i];
      var dx = mouse.x - n.x, dy = mouse.y - n.y, d = Math.sqrt(dx * dx + dy * dy);
      if (d < 200 && d > 0.1) { // atração suave ao cursor
        var f = (1 - d / 200) * 0.035;
        n.vx += dx / d * f; n.vy += dy / d * f;
      }
      n.vx *= 0.97; n.vy *= 0.97;
      n.vx += (Math.random() - 0.5) * 0.02; n.vy += (Math.random() - 0.5) * 0.02;
      n.x += n.vx; n.y += n.vy;
      if (n.x < -20) n.x = W + 20; if (n.x > W + 20) n.x = -20;
      if (n.y < -20) n.y = H + 20; if (n.y > H + 20) n.y = -20;
    }
  }

  function draw() {
    ctx.clearRect(0, 0, W, H);
    var link = Math.min(150, W / 7);
    for (var i = 0; i < nodes.length; i++) {
      var a = nodes[i];
      for (var j = i + 1; j < nodes.length; j++) {
        var b = nodes[j], dx = a.x - b.x, dy = a.y - b.y, d2 = dx * dx + dy * dy;
        if (d2 < link * link) {
          var o = 1 - Math.sqrt(d2) / link;
          ctx.strokeStyle = 'rgba(138,147,166,' + (o * 0.22) + ')';
          ctx.lineWidth = 1;
          ctx.beginPath(); ctx.moveTo(a.x, a.y); ctx.lineTo(b.x, b.y); ctx.stroke();
        }
      }
      var mdx = a.x - mouse.x, mdy = a.y - mouse.y, md = Math.sqrt(mdx * mdx + mdy * mdy);
      if (md < 220) { // fios do cursor até os pontos próximos
        ctx.strokeStyle = 'rgba(92,225,230,' + ((1 - md / 220) * 0.55) + ')';
        ctx.beginPath(); ctx.moveTo(mouse.x, mouse.y); ctx.lineTo(a.x, a.y); ctx.stroke();
      }
    }
    for (var k = 0; k < nodes.length; k++) {
      var n = nodes[k], near = Math.max(0, 1 - Math.hypot(n.x - mouse.x, n.y - mouse.y) / 220);
      var s = n.hub ? 3 : 1.6;
      ctx.fillStyle = n.hub ? 'rgba(92,225,230,' + (0.7 + near * 0.3) + ')' : 'rgba(238,241,247,' + (0.35 + near * 0.6) + ')';
      ctx.fillRect(n.x - s / 2, n.y - s / 2, s, s);
      if (n.hub) {
        ctx.strokeStyle = 'rgba(92,225,230,' + (0.18 + near * 0.4) + ')';
        ctx.strokeRect(n.x - 7, n.y - 7, 14, 14);
      }
    }
  }

  function frame() {
    if (!document.hidden) {
      step(); draw();
      hero.style.setProperty('--mx', mouse.x + 'px');
      hero.style.setProperty('--my', mouse.y + 'px');
      tilt.style.transform = 'perspective(1200px) rotateX(' + (-norm.y * 3) + 'deg) rotateY(' + (norm.x * 4) + 'deg) translate3d(' + (norm.x * 6) + 'px,' + (norm.y * 6) + 'px,0)';
      mark.style.transform = 'translate3d(' + (-norm.x * 28) + 'px, calc(-50% + ' + (-norm.y * 22) + 'px), 0)';
    }
    requestAnimationFrame(frame);
  }

  hero.addEventListener('pointermove', function (e) {
    if (e.pointerType !== 'mouse') return;
    var r = hero.getBoundingClientRect();
    mouse.active = true;
    mouse.tx = e.clientX - r.left; mouse.ty = e.clientY - r.top;
    norm.tx = (mouse.tx / r.width) * 2 - 1; norm.ty = (mouse.ty / r.height) * 2 - 1;
  });
  hero.addEventListener('pointerleave', function () { mouse.active = false; norm.tx = 0; norm.ty = 0; });

  // CTA magnético: puxa o botão em direção ao cursor quando ele se aproxima
  if (finePointer && !reduce) {
    window.addEventListener('pointermove', function (e) {
      var r = cta.getBoundingClientRect();
      var cx = r.left + r.width / 2, cy = r.top + r.height / 2;
      var dx = e.clientX - cx, dy = e.clientY - cy;
      var reach = Math.max(r.width, r.height) * 0.9;
      if (Math.abs(dx) < reach && Math.abs(dy) < reach) cta.style.transform = 'translate(' + dx * 0.18 + 'px,' + dy * 0.28 + 'px)';
      else cta.style.transform = '';
    });
  }

  window.addEventListener('resize', resize);
  resize();
  if (!reduce) requestAnimationFrame(frame);
})();
