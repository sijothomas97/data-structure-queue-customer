"use strict";

(() => {
  const queueEl = document.getElementById("queue");
  const emptyEl = document.getElementById("empty");
  const statusEl = document.getElementById("status");
  const connEl = document.getElementById("connection");
  const cards = new Map(); // customer id -> <li> element

  // ---------- rendering with FLIP move animation ----------

  function cardHtml(entry) {
    return `
      <span class="pos">${entry.position}</span>
      <div class="name">${escapeHtml(entry.name)}</div>
      <div class="meta">age ${entry.age}</div>
      <div class="wait" data-enqueued="${entry.enqueuedAt}">waiting ${formatWait(entry.waitSeconds)}</div>`;
  }

  function render(entries, changeKind) {
    // FLIP step 1: record current positions.
    const firstRects = new Map();
    for (const [id, el] of cards) firstRects.set(id, el.getBoundingClientRect());

    const seen = new Set();
    entries.forEach((entry, index) => {
      seen.add(entry.id);
      let el = cards.get(entry.id);
      if (!el) {
        el = document.createElement("li");
        el.className = "card enter";
        el.dataset.id = entry.id;
        cards.set(entry.id, el);
        el.addEventListener("animationend", () => el.classList.remove("enter", "flash"));
      }
      el.innerHTML = cardHtml(entry);
      el.classList.toggle("front", index === 0);
      queueEl.appendChild(el); // appending in order reorders existing nodes
    });

    for (const [id, el] of cards) {
      if (!seen.has(id)) {
        el.remove();
        cards.delete(id);
      }
    }

    // FLIP steps 2-4: invert and play for cards that moved.
    for (const [id, el] of cards) {
      const first = firstRects.get(id);
      if (!first) continue;
      const last = el.getBoundingClientRect();
      const dx = first.left - last.left;
      if (Math.abs(dx) < 1) continue;
      el.classList.remove("moving");
      el.style.transform = `translateX(${dx}px)`;
      void el.offsetWidth; // force reflow
      el.classList.add("moving");
      el.style.transform = "";
      if (changeKind === "reversed") el.classList.add("flash");
    }

    emptyEl.classList.toggle("hidden", entries.length > 0);
  }

  // ---------- live wait times ----------

  setInterval(() => {
    const now = Date.now();
    for (const el of document.querySelectorAll(".wait[data-enqueued]")) {
      const seconds = Math.max(0, (now - Date.parse(el.dataset.enqueued)) / 1000);
      el.textContent = `waiting ${formatWait(seconds)}`;
    }
  }, 1000);

  function formatWait(seconds) {
    seconds = Math.floor(seconds);
    if (seconds < 60) return `${seconds}s`;
    const m = Math.floor(seconds / 60);
    if (m < 60) return `${m}m ${seconds % 60}s`;
    return `${Math.floor(m / 60)}h ${m % 60}m`;
  }

  function escapeHtml(text) {
    const div = document.createElement("div");
    div.textContent = text;
    return div.innerHTML;
  }

  // ---------- metrics ----------

  async function refreshMetrics() {
    try {
      const res = await fetch("/api/metrics");
      if (!res.ok) return;
      const m = await res.json();
      document.getElementById("m-length").textContent = m.currentLength;
      document.getElementById("m-served").textContent = m.totalServed;
      document.getElementById("m-avgwait").textContent = formatWait(m.averageWaitSeconds);
      document.getElementById("m-longest").textContent = formatWait(m.longestCurrentWaitSeconds);
      document.getElementById("m-throughput").textContent = m.throughputPerMinuteLastHour.toFixed(2);
    } catch {
      /* metrics are best-effort */
    }
  }
  setInterval(refreshMetrics, 10000);

  // ---------- controls ----------

  function setStatus(message, isError) {
    statusEl.textContent = message;
    statusEl.classList.toggle("error", Boolean(isError));
  }

  async function apiProblemMessage(res) {
    try {
      const body = await res.json();
      if (body.errors) return Object.values(body.errors).flat().join(" ");
      return body.message || body.title || `Request failed (${res.status}).`;
    } catch {
      return `Request failed (${res.status}).`;
    }
  }

  document.getElementById("enqueue-form").addEventListener("submit", async (event) => {
    event.preventDefault();
    const nameInput = document.getElementById("name");
    const ageInput = document.getElementById("age");
    const res = await fetch("/api/queue", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ name: nameInput.value, age: Number(ageInput.value) }),
    });
    if (res.ok) {
      const entry = await res.json();
      setStatus(`${entry.name} joined at position ${entry.position}.`);
      nameInput.value = "";
      ageInput.value = "";
      nameInput.focus();
    } else {
      setStatus(await apiProblemMessage(res), true);
    }
  });

  document.getElementById("dequeue").addEventListener("click", async () => {
    const res = await fetch("/api/queue/dequeue", { method: "POST" });
    if (res.ok) {
      const served = await res.json();
      setStatus(`Served ${served.name} after ${formatWait(served.waitSeconds)}.`);
    } else {
      setStatus(await apiProblemMessage(res), true);
    }
  });

  document.getElementById("reverse").addEventListener("click", async () => {
    const count = Number(document.getElementById("reverse-count").value);
    const res = await fetch(`/api/queue/reverse?count=${count}`, { method: "POST" });
    if (res.ok) {
      const result = await res.json();
      setStatus(`Reversed the first ${result.reversed} customer(s).`);
    } else {
      setStatus(await apiProblemMessage(res), true);
    }
  });

  // ---------- SignalR live updates ----------

  const connection = new signalR.HubConnectionBuilder()
    .withUrl("/hubs/queue")
    .withAutomaticReconnect()
    .build();

  connection.on("QueueChanged", (changeKind, snapshot) => {
    render(snapshot, changeKind);
    refreshMetrics();
  });

  function setConnected(connected) {
    connEl.textContent = connected ? "live" : "reconnecting…";
    connEl.classList.toggle("conn-on", connected);
    connEl.classList.toggle("conn-off", !connected);
  }

  connection.onreconnecting(() => setConnected(false));
  connection.onreconnected(() => {
    setConnected(true);
    loadInitial();
  });
  connection.onclose(() => setConnected(false));

  async function loadInitial() {
    try {
      const res = await fetch("/api/queue");
      if (res.ok) render(await res.json(), "init");
    } catch {
      setStatus("Could not load the queue.", true);
    }
    refreshMetrics();
  }

  (async () => {
    await loadInitial();
    try {
      await connection.start();
      setConnected(true);
    } catch {
      setConnected(false);
      setStatus("Live updates unavailable; refresh to retry.", true);
    }
  })();
})();
