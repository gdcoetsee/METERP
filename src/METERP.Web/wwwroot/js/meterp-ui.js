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
    if (board._kanbanAbort) board._kanbanAbort.abort();
    var ac = new AbortController();
    board._kanbanAbort = ac;
    var signal = ac.signal;
    var dragId = null;

    board.addEventListener('dragstart', function (e) {
      var card = e.target.closest('[data-deal-id]');
      if (!card || !board.contains(card)) return;
      dragId = card.getAttribute('data-deal-id');
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
      var lane = e.target.closest('[data-lane]');
      if (!lane || !board.contains(lane)) return;
      e.preventDefault();
      try { e.dataTransfer.dropEffect = 'move'; } catch (ex) { }
      board.querySelectorAll('.is-drop-target').forEach(function (el) {
        if (el !== lane) el.classList.remove('is-drop-target');
      });
      lane.classList.add('is-drop-target');
    }, { signal: signal });

    board.addEventListener('drop', function (e) {
      var lane = e.target.closest('[data-lane]');
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
      dotnet.invokeMethodAsync('OnKanbanDrop', id, stage, before || null);
    }, { signal: signal });
  }
};