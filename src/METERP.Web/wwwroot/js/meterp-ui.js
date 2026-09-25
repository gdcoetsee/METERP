window.meterpUi = {
  print: function () {
    window.print();
  },
  downloadText: function (filename, content, mimeType) {
    var blob = new Blob([content], { type: mimeType || 'text/plain;charset=utf-8' });
    var url = URL.createObjectURL(blob);
    var a = document.createElement('a');
    a.href = url;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
  },
  downloadBytes: function (filename, base64, mimeType) {
    var binary = atob(base64);
    var len = binary.length;
    var bytes = new Uint8Array(len);
    for (var i = 0; i < len; i++) {
      bytes[i] = binary.charCodeAt(i);
    }
    var blob = new Blob([bytes], { type: mimeType || 'application/octet-stream' });
    var url = URL.createObjectURL(blob);
    var a = document.createElement('a');
    a.href = url;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
  },
  pollNotifications: async function () {
    try {
      var res = await fetch('/api/notifications/poll', { credentials: 'same-origin' });
      if (!res.ok) return { unread: 0, items: [] };
      return await res.json();
    } catch (e) {
      return { unread: 0, items: [] };
    }
  },
  bindKanban: function (selector, dotnet) {
    var board = typeof selector === 'string' ? document.querySelector(selector) : selector;
    if (!board || !dotnet) return;
    if (board._kanbanDotnet === dotnet && board._kanbanAbort) return;
    if (board._kanbanAbort) board._kanbanAbort.abort();
    var ac = new AbortController();
    board._kanbanAbort = ac;
    board._kanbanDotnet = dotnet;
    var signal = ac.signal;
    var dragId = null;
    var pointer = { x: 0, y: 0, active: false };
    var raf = 0;

    function laneFromEvent(e) {
      var lane = e.target.closest('[data-lane]');
      if (lane && board.contains(lane)) return lane;
      var col = e.target.closest('.meterp-kanban-col');
      if (col && board.contains(col)) return col.querySelector('[data-lane]');
      return null;
    }

    function autoScroll() {
      if (!pointer.active) return;
      var edgeX = 80;
      var edgeY = 56;
      var b = board.getBoundingClientRect();
      if (pointer.x > b.right - edgeX)
        board.scrollLeft += Math.min(32, 10 + (pointer.x - (b.right - edgeX)) * 0.35);
      else if (pointer.x < b.left + edgeX)
        board.scrollLeft -= Math.min(32, 10 + ((b.left + edgeX) - pointer.x) * 0.35);

      var lane = document.elementFromPoint(pointer.x, pointer.y);
      lane = lane && lane.closest ? lane.closest('[data-lane]') : null;
      if (lane && board.contains(lane)) {
        var lr = lane.getBoundingClientRect();
        if (pointer.y > lr.bottom - edgeY) lane.scrollTop += 18;
        else if (pointer.y < lr.top + edgeY) lane.scrollTop -= 18;
      }
      raf = requestAnimationFrame(autoScroll);
    }

    board.addEventListener('dragstart', function (e) {
      var card = e.target.closest('[data-deal-id]');
      if (!card || !board.contains(card)) return;
      dragId = card.getAttribute('data-deal-id');
      try { e.dataTransfer.setData('text/plain', dragId); } catch (ex) { }
      try { e.dataTransfer.effectAllowed = 'move'; } catch (ex) { }
      card.classList.add('is-dragging');
      pointer.active = true;
      pointer.x = e.clientX;
      pointer.y = e.clientY;
      cancelAnimationFrame(raf);
      raf = requestAnimationFrame(autoScroll);
    }, { signal: signal });

    board.addEventListener('dragend', function () {
      dragId = null;
      pointer.active = false;
      cancelAnimationFrame(raf);
      board.querySelectorAll('.is-dragging').forEach(function (el) { el.classList.remove('is-dragging'); });
      board.querySelectorAll('.is-drop-target').forEach(function (el) { el.classList.remove('is-drop-target'); });
    }, { signal: signal });

    board.addEventListener('dragover', function (e) {
      e.preventDefault();
      pointer.x = e.clientX;
      pointer.y = e.clientY;
      try { e.dataTransfer.dropEffect = 'move'; } catch (ex) { }
      var lane = laneFromEvent(e);
      board.querySelectorAll('.is-drop-target').forEach(function (el) {
        if (el !== lane) el.classList.remove('is-drop-target');
      });
      if (lane) lane.classList.add('is-drop-target');
    }, { signal: signal });

    board.addEventListener('drop', function (e) {
      var lane = laneFromEvent(e);
      if (!lane || !board.contains(lane)) return;
      e.preventDefault();
      e.stopPropagation();
      lane.classList.remove('is-drop-target');
      var id = '';
      try { id = e.dataTransfer.getData('text/plain'); } catch (ex) { }
      if (!id) id = dragId || '';
      if (!id) return;
      var over = e.target.closest('[data-deal-id]');
      var before = over && over.getAttribute('data-deal-id') !== id ? over.getAttribute('data-deal-id') : '';
      var stage = lane.getAttribute('data-lane') || '';
      var card = board.querySelector('[data-deal-id="' + id + '"]');
      if (card && lane) {
        if (over && over !== card && lane.contains(over)) lane.insertBefore(card, over);
        else lane.appendChild(card);
      }
      dotnet.invokeMethodAsync('OnKanbanDrop', id, stage, before || null);
    }, { signal: signal });

    board.addEventListener('wheel', function (e) {
      if (e.shiftKey || Math.abs(e.deltaX) > Math.abs(e.deltaY)) return;
      var lane = e.target.closest('[data-lane]');
      if (lane && board.contains(lane) && lane.scrollHeight > lane.clientHeight + 4) return;
      if (board.scrollWidth <= board.clientWidth + 4) return;
      e.preventDefault();
      board.scrollLeft += e.deltaY;
    }, { signal: signal, passive: false });
  },
  bindSchedule: function (selector, dotnet) {
    var board = typeof selector === 'string' ? document.querySelector(selector) : selector;
    if (!board || !dotnet) return;
    if (board._schedDotnet === dotnet && board._schedAbort) return;
    if (board._schedAbort) board._schedAbort.abort();
    var ac = new AbortController();
    board._schedAbort = ac;
    board._schedDotnet = dotnet;
    var signal = ac.signal;
    var dragId = null;

    board.addEventListener('dragstart', function (e) {
      var card = e.target.closest('[data-job-id]');
      if (!card || !board.contains(card)) return;
      dragId = card.getAttribute('data-job-id');
      try { e.dataTransfer.setData('text/plain', dragId); } catch (ex) { }
      try { e.dataTransfer.effectAllowed = 'move'; } catch (ex) { }
      card.classList.add('is-dragging');
    }, { signal: signal });

    board.addEventListener('dragend', function () {
      dragId = null;
      board.querySelectorAll('.is-dragging').forEach(function (el) { el.classList.remove('is-dragging'); });
      board.querySelectorAll('.is-drop-target').forEach(function (el) { el.classList.remove('is-drop-target'); });
    }, { signal: signal });

    board.addEventListener('dragover', function (e) {
      var day = e.target.closest('[data-day]');
      if (!day || !board.contains(day)) return;
      e.preventDefault();
      try { e.dataTransfer.dropEffect = 'move'; } catch (ex) { }
      board.querySelectorAll('.is-drop-target').forEach(function (el) {
        if (el !== day) el.classList.remove('is-drop-target');
      });
      day.classList.add('is-drop-target');
    }, { signal: signal });

    board.addEventListener('drop', function (e) {
      var day = e.target.closest('[data-day]');
      if (!day || !board.contains(day)) return;
      e.preventDefault();
      e.stopPropagation();
      day.classList.remove('is-drop-target');
      var id = '';
      try { id = e.dataTransfer.getData('text/plain'); } catch (ex) { }
      if (!id) id = dragId || '';
      if (!id) return;
      var date = day.getAttribute('data-day') || '';
      dotnet.invokeMethodAsync('OnScheduleDrop', id, date);
    }, { signal: signal });
  }
};