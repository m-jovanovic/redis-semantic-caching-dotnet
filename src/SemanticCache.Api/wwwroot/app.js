"use strict";

(() => {
  const $ = (selector, root = document) => root.querySelector(selector);
  const $$ = (selector, root = document) => [...root.querySelectorAll(selector)];
  const params = new URLSearchParams(location.search);
  const inspectorOnly = params.get("inspector") === "1";
  const sessionKey = "semantic-cache-lab-session";
  const suppliedSession = params.get("sessionId");
  let savedSession;
  try {
    savedSession = sessionStorage.getItem(sessionKey);
  } catch {
    /* Session storage can be disabled. */
  }
  const validSession = (value) => typeof value === "string" && /^[a-zA-Z0-9_-]{1,80}$/.test(value);
  const sessionId = validSession(suppliedSession)
    ? suppliedSession
    : validSession(savedSession)
      ? savedSession
      : crypto.randomUUID();
  try {
    sessionStorage.setItem(sessionKey, sessionId);
  } catch {
    /* The session still works in memory. */
  }
  if (inspectorOnly) document.body.classList.add("inspector-only");
  $("#session-id").textContent = `Session ${sessionId.slice(0, 8)}`;
  $("#session-id").title = `Session ${sessionId}`;
  const sessionChannel =
    typeof BroadcastChannel === "function"
      ? new BroadcastChannel(`semantic-cache-${sessionId}`)
      : null;
  sessionChannel?.addEventListener("message", (event) => {
    if (!inspectorOnly || !validSession(event.data?.nextSession)) return;
    const next = new URL(location.href);
    next.searchParams.set("sessionId", event.data.nextSession);
    location.replace(next.href);
  });

  const state = {
    status: null,
    statusError: null,
    mode: "off",
    tenantId: "acme",
    busy: false,
    requests: new Map(),
    selected: null,
    follow: true,
    filter: "steps",
    openEvents: new Set(),
    messages: [],
    stream: null,
    streamState: "Connecting",
    thresholdTouched: false,
    cacheLoading: false,
    workload: false,
    traceRenderPending: false,
    toastTimer: null,
    historyTimer: null,
    comparison: [],
    comparisonScope: null,
    comparing: false,
    inspectorWindow: null,
    lastFinishedView: null,
  };
  const samplePrompts = [
    "How many rows can I export?",
    "How many rows can I export?",
    "What is the maximum number of rows in an export?",
    "How many columns can I export?",
    "What is the log retention period?",
  ];
  const sourceNames = {
    model: "Generated",
    exact: "Exact hit",
    semantic: "Semantic hit",
    playbook: "Playbook FAQ",
    bypass: "Not stored",
    error: "Error",
    pending: "In progress",
  };
  const categoryNames = {
    redis: "RD",
    model: "AI",
    tool: "fn",
    embedding: "EM",
    check: "?=",
    playbook: "PB",
    cache: "CA",
    provider: "HTTP",
    request: "RQ",
  };
  const esc = (value) =>
    String(value ?? "").replace(
      /[&<>"']/g,
      (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c],
    );
  const finite = (value) => typeof value === "number" && Number.isFinite(value);
  const number = (value) => (finite(value) ? value.toLocaleString() : "—");
  const duration = (value) =>
    !finite(value)
      ? "—"
      : value < 1000
        ? `${value.toFixed(value < 10 ? 1 : 0)} ms`
        : `${(value / 1000).toFixed(2)} s`;
  const clock = (value) => {
    const date = new Date(value);
    return Number.isNaN(date.getTime())
      ? ""
      : date.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit" });
  };
  const dateLabel = (value) => {
    const date = new Date(value);
    return Number.isNaN(date.getTime()) ? "" : date.toLocaleString();
  };
  const pretty = (value) => JSON.stringify(value, null, 2) ?? "null";
  const sourceClass = (source) => (Object.hasOwn(sourceNames, source) ? source : "pending");
  const badge = (source) =>
    `<span class="source-badge ${sourceClass(source)}">${esc(sourceNames[source] || source || "In progress")}</span>`;

  async function api(path, options = {}) {
    const response = await fetch(path, {
      ...options,
      headers: {
        Accept: "application/json",
        ...(options.body ? { "Content-Type": "application/json" } : {}),
        ...options.headers,
      },
    });
    const content = await response.text();
    let data;
    try {
      data = content ? JSON.parse(content) : {};
    } catch {
      throw new Error(`The API returned an unreadable response (${response.status}).`);
    }
    if (!response.ok) {
      const error = new Error(data.error || `Request failed (${response.status}).`);
      error.requestId = data.requestId;
      throw error;
    }
    return data;
  }

  function toast(message, isError = false) {
    clearTimeout(state.toastTimer);
    const target = $("#toast");
    target.textContent = message;
    target.className = `toast${isError ? " error" : ""}`;
    target.hidden = false;
    state.toastTimer = setTimeout(
      () => {
        target.hidden = true;
      },
      isError ? 7000 : 4000,
    );
  }

  function tenantPolicy() {
    return state.status?.tenants?.find((t) => t.id === state.tenantId);
  }
  function renderScope() {
    const policy = tenantPolicy();
    $("#scope-summary").textContent = policy
      ? `${policy.name} · policy revision ${policy.revision} · ${number(policy.exportLimit)} export rows · ${number(policy.retentionDays)} days retention`
      : "";
  }
  function setBusy(busy) {
    state.busy = busy;
    renderControls();
  }
  function renderControls() {
    const ready = state.status?.ready === true;
    $("#send-button").disabled = state.busy || !ready;
    $("#send-button").innerHTML = state.busy
      ? '<span class="spinner"></span> Running'
      : 'Send question <svg viewBox="0 0 24 24"><path d="M12 19V5m-6 6 6-6 6 6"/></svg>';
    $("#run-workload").disabled = state.busy || !ready;
    $("#run-workload").innerHTML = state.workload
      ? '<span class="spinner"></span> Running requests'
      : "Run sample requests";
    $("#tenant-select").disabled = state.busy;
    $("#threshold").disabled = state.busy || state.mode !== "semantic";
    $("#request-progress").hidden = !state.busy;
    $("#open-policy").disabled = state.busy || !state.status?.tenants?.length;
    $("#reset-cache").disabled = state.busy;
    $("#open-reset").disabled = state.busy || !ready;
    $("#open-comparison").disabled = state.busy || !ready;
    $("#run-comparison").disabled = state.busy || !ready;
    $("#confirm-reset").disabled = state.busy || !ready;
    $("#save-policy").disabled = state.busy;
    $$("[data-mode]").forEach((button) => {
      const active = button.dataset.mode === state.mode;
      button.classList.toggle("active", active);
      button.setAttribute("aria-pressed", String(active));
      button.disabled = state.busy;
    });
    $$("[data-prompt]").forEach((button) => {
      button.disabled = state.busy;
    });
  }

  async function loadStatus(showFeedback = false) {
    $("#refresh-status").disabled = true;
    try {
      state.status = await api("/api/status");
      state.statusError = null;
      const ready = state.status.ready === true;
      const chip = $("#service-state");
      chip.className = `status-chip ${ready ? "ready" : "error"}`;
      chip.innerHTML = `<i></i>${ready ? "Services ready" : "Services unavailable"}`;
      chip.title = pretty({ redis: state.status.redis, models: state.status.models });
      const tenants = state.status.tenants;
      if (Array.isArray(tenants) && tenants.length) {
        if (!tenants.some((t) => t.id === state.tenantId)) state.tenantId = tenants[0].id;
        $("#tenant-select").innerHTML = tenants
          .map(
            (t) =>
              `<option value="${esc(t.id)}" ${t.id === state.tenantId ? "selected" : ""}>${esc(t.name || t.id)}</option>`,
          )
          .join("");
      }
      if (!state.thresholdTouched && finite(state.status.threshold))
        $("#threshold").value = state.status.threshold;
      $("#playbooks").disabled = !state.status.playbookConfigured;
      if (!state.status.playbookConfigured) $("#playbooks").checked = false;
      $("#playbooks").title = state.status.playbookConfigured
        ? ""
        : "Set the AgentPlaybook values in .env to enable this.";
      renderScope();
      $("#model-summary").textContent =
        `${state.status.provider || "Model provider"} · ${state.status.chatModel || "Chat model unknown"} · ${state.status.embeddingModel || "Embedding model unknown"}`;
      const notice = $("#service-notice");
      notice.hidden = ready;
      if (!ready)
        notice.innerHTML = `<strong>The local services are not ready yet.</strong>Start Redis and the model service, then refresh status. <details><summary>Service response</summary><pre>${esc(pretty({ redis: state.status.redis, models: state.status.models }))}</pre></details>`;
      if (showFeedback)
        toast(
          ready
            ? "Redis and model services are ready."
            : "Some local services are still unavailable.",
          !ready,
        );
    } catch (error) {
      state.statusError = error.message;
      if (state.status) state.status.ready = false;
      $("#service-state").className = "status-chip error";
      $("#service-state").innerHTML = "<i></i>API unavailable";
      $("#service-notice").hidden = false;
      $("#service-notice").textContent = `${error.message} Refresh status to retry.`;
      if (showFeedback) toast(error.message, true);
    } finally {
      $("#refresh-status").disabled = false;
      renderControls();
    }
  }

  function requestRecord(id, startedAt) {
    if (!state.requests.has(id))
      state.requests.set(id, {
        requestId: id,
        startedAt: startedAt || new Date().toISOString(),
        finished: false,
        result: null,
        events: new Map(),
        message: "",
      });
    return state.requests.get(id);
  }

  function ingestEvent(event) {
    if (!event || !event.requestId || (event.sessionId && event.sessionId !== sessionId)) return;
    const request = requestRecord(event.requestId, event.timestamp);
    const key =
      event.sequence ?? `${event.timestamp}:${event.category}:${event.operation}:${event.status}`;
    const isNew = !request.events.has(key);
    request.events.set(key, event);
    const data = event.data;
    if (!request.message && data && typeof data === "object" && typeof data.message === "string")
      request.message = data.message;
    if (
      event.category === "request" &&
      (event.status === "completed" || event.status === "error")
    ) {
      request.finished = true;
      if (event.status === "error") request.error = data?.error || "Request failed.";
      if (!request.result && data && typeof data === "object" && data.source && data.usage)
        request.result = data;
      if (isNew) {
        // The completion event can precede Result being assigned on the server.
        clearTimeout(state.historyTimer);
        state.historyTimer = setTimeout(() => void loadHistory(), 200);
      }
    }
    if (state.follow) state.selected = newestRequests()[0]?.requestId || event.requestId;
    scheduleTraceRender();
  }

  function ingestResult(result, message) {
    if (!result?.requestId) return;
    const request = requestRecord(result.requestId);
    request.finished = true;
    request.result = result;
    if (message) request.message = message;
    for (const event of result.events || []) ingestEvent(event);
    if (state.follow) state.selected = result.requestId;
    renderSessionStats();
    scheduleTraceRender();
  }

  function newestRequests() {
    return [...state.requests.values()].sort(
      (a, b) => new Date(b.startedAt) - new Date(a.startedAt),
    );
  }

  async function loadHistory(showFeedback = false) {
    $("#reload-history").disabled = true;
    try {
      const history = await api(`/api/requests?sessionId=${encodeURIComponent(sessionId)}`);
      if (!Array.isArray(history)) throw new Error("Trace history has an unexpected format.");
      for (const item of history) {
        const request = requestRecord(item.requestId, item.startedAt);
        request.startedAt = item.startedAt || request.startedAt;
        request.finished = item.finished === true;
        if (item.result?.error) request.error = item.result.error;
        else if (item.result) request.result = item.result;
        for (const event of item.events || []) ingestEvent(event);
      }
      if (state.follow || !state.selected) state.selected = newestRequests()[0]?.requestId || null;
      if (!inspectorOnly && !state.messages.length && !state.busy) {
        state.messages = [...state.requests.values()]
          .filter((request) => request.message && (request.result?.answer || request.error))
          .sort((a, b) => new Date(a.startedAt) - new Date(b.startedAt))
          .map((request) => ({
            text: request.message,
            tenantId: request.result?.tenantId || "",
            mode: request.result?.mode || "",
            result: request.result,
            error: request.error,
            requestId: request.requestId,
          }));
        if (state.messages.length) renderMessages();
      }
      renderSessionStats();
      renderInspector();
      if (showFeedback)
        toast(
          `Loaded ${history.length} request${history.length === 1 ? "" : "s"} from this session.`,
        );
    } catch (error) {
      if (showFeedback) toast(error.message, true);
      else console.warn("Trace history:", error.message);
    } finally {
      $("#reload-history").disabled = false;
    }
  }

  function connectEvents() {
    state.stream?.close();
    const stream = new EventSource(`/api/events?sessionId=${encodeURIComponent(sessionId)}`);
    state.stream = stream;
    stream.addEventListener("trace", (event) => {
      try {
        ingestEvent(JSON.parse(event.data));
      } catch (error) {
        console.warn("Invalid trace event:", error);
      }
    });
    stream.addEventListener("open", () => {
      state.streamState = "Live";
      $("#stream-state").className = "stream-state connected";
      $("#stream-state").innerHTML = "<i></i>Live";
      // History fills the gap after a reconnect. Sequence IDs prevent duplicate events.
      void loadHistory();
    });
    stream.addEventListener("error", () => {
      state.streamState = "Reconnecting";
      $("#stream-state").className = "stream-state error";
      $("#stream-state").innerHTML = "<i></i>Reconnecting";
    });
  }

  function renderSessionStats() {
    const results = [...state.requests.values()].map((r) => r.result).filter((r) => r && r.usage);
    if (!results.length) {
      ["requests", "hits", "models", "embeddings", "checks", "input", "tokens"].forEach((key) => {
        $(`#stat-${key}`).textContent = "0";
      });
      return;
    }
    $("#stat-requests").textContent = number(results.length);
    $("#stat-hits").textContent = number(
      results.filter((r) => ["exact", "semantic", "playbook"].includes(r.source)).length,
    );
    $("#stat-models").textContent = number(
      results.reduce((sum, r) => sum + (r.usage.modelCalls || 0), 0),
    );
    $("#stat-embeddings").textContent = number(
      results.reduce((sum, r) => sum + (r.usage.embeddingCalls || 0), 0),
    );
    $("#stat-checks").textContent = number(
      results.reduce((sum, r) => sum + (r.usage.checkCalls || 0), 0),
    );
    $("#stat-input").textContent = number(
      results.reduce(
        (sum, r) => sum + (r.usage.inputTokens || 0) + (r.usage.checkInputTokens || 0),
        0,
      ),
    );
    $("#stat-tokens").textContent = number(
      results.reduce(
        (sum, r) => sum + (r.usage.outputTokens || 0) + (r.usage.checkOutputTokens || 0),
        0,
      ),
    );
  }

  function usageMetrics(result, detailed = false) {
    const usage = result.usage || {};
    const values = detailed
      ? [
          ["chat calls", usage.modelCalls],
          ["tool calls", usage.toolCalls],
          ["embedding calls", usage.embeddingCalls],
          ["check calls", usage.checkCalls],
          ["input tokens", usage.inputTokens],
          ["output tokens", usage.outputTokens],
          // Only worth a place when they happened.
          ...[
            ["playbook calls", usage.playbookCalls],
            ["check tokens", (usage.checkInputTokens || 0) + (usage.checkOutputTokens || 0)],
            ["embedding tokens", usage.embeddingTokens],
          ].filter(([, value]) => value > 0),
        ]
      : [
          ["input tokens", usage.inputTokens],
          ["output tokens", usage.outputTokens],
          ["check tokens", (usage.checkInputTokens || 0) + (usage.checkOutputTokens || 0)],
          ["embedding tokens", usage.embeddingTokens],
        ];
    return values
      .map(
        ([label, value]) =>
          `<span${label === "chat calls" ? ' title="Chat inference requests; embeddings and provider metadata checks are counted separately."' : ""}><b>${number(value)}</b> ${label}</span>`,
      )
      .join("");
  }

  function workMetrics(result) {
    const usage = result.usage || {};
    return `<div class="work-metrics">${[
      ["Chat calls", usage.modelCalls],
      ["Tool calls", usage.toolCalls],
      ["Embeddings", usage.embeddingCalls],
      ["Checks", usage.checkCalls],
    ]
      .map(([label, value]) => `<div><strong>${number(value)}</strong><span>${label}</span></div>`)
      .join(
        "",
      )}<div><strong>${esc(duration(result.durationMs))}</strong><span>Backend time</span></div></div>`;
  }

  function renderMessages() {
    const target = $("#messages");
    if (!state.messages.length) return;
    target.innerHTML = state.messages
      .map((message) => {
        const result = message.result;
        let answer;
        if (message.error) {
          answer = `<div class="answer-topline">${badge("error")}${message.requestId ? `<button class="text-button" data-show-request="${esc(message.requestId)}">View trace ↗</button>` : ""}</div><div class="answer-content">${esc(message.error)}</div>`;
        } else if (!result) {
          answer =
            '<div class="pending-line"><span class="spinner"></span>Request in progress. Watch the inspector for each step.</div>';
        } else {
          const candidate = result.candidate;
          answer = `<div class="answer-topline">${badge(result.source)}<span class="answer-revision">Policy revision ${esc(result.policyRevision)}</span><button class="text-button" data-show-request="${esc(result.requestId)}">View trace ↗</button></div><div class="answer-content">${esc(result.answer)}</div>${workMetrics(result)}<div class="answer-stats">${usageMetrics(result)}</div>${result.cacheReason || candidate ? `<div class="answer-note">${esc(result.cacheReason || "")}${candidate ? `<span class="answer-candidate"><strong>Candidate:</strong> ${esc(candidate.question)}${finite(candidate.distance) ? ` · distance ${esc(candidate.distance.toFixed(4))}` : ""}</span>` : ""}</div>` : ""}`;
        }
        return `<article class="message"><div class="message-question">${esc(message.text)}</div><div class="message-context"><span>${esc(message.tenantId)}</span><span>·</span><span>${esc(message.mode)} mode</span></div><div class="message-answer">${answer}</div></article>`;
      })
      .join("");
    // Keep the newest question and its answer in view.
    const newest = target.lastElementChild.getBoundingClientRect().top;
    const top = target.scrollTop + newest - target.getBoundingClientRect().top - 12;
    target.scrollTo({ top, behavior: "instant" });
  }

  async function sendQuestion(text, withinWorkload = false) {
    if ((!withinWorkload && state.busy) || !text.trim() || state.status?.ready !== true)
      return false;
    const thresholdInput = $("#threshold");
    const threshold =
      state.mode === "semantic"
        ? thresholdInput.value.trim() === ""
          ? NaN
          : Number(thresholdInput.value)
        : null;
    if (
      state.mode === "semantic" &&
      (!Number.isFinite(threshold) || threshold < 0 || threshold > 0.3)
    ) {
      toast("Use a distance threshold from 0 to 0.3.", true);
      return false;
    }
    if (!withinWorkload) {
      $("#message-input").value = "";
      setBusy(true);
    }
    const item = {
      text: text.trim(),
      tenantId: state.tenantId,
      mode: state.mode,
      result: null,
      error: null,
      requestId: null,
    };
    state.messages.push(item);
    renderMessages();
    state.follow = true;
    try {
      const result = await api("/api/chat", {
        method: "POST",
        body: JSON.stringify({
          message: item.text,
          mode: item.mode,
          tenantId: item.tenantId,
          sessionId,
          threshold,
          verify: $("#verify").checked,
          playbooks: $("#playbooks").checked,
        }),
      });
      item.result = result;
      item.requestId = result.requestId;
      ingestResult(result, item.text);
      return true;
    } catch (error) {
      item.error = error.message;
      item.requestId = error.requestId;
      if (error.requestId) {
        const request = requestRecord(error.requestId);
        request.finished = true;
        request.error = error.message;
        request.message = item.text;
        state.selected = request.requestId;
      }
      await loadHistory();
      return false;
    } finally {
      renderMessages();
      renderInspector();
      if (!withinWorkload) {
        setBusy(false);
        $("#message-input").focus();
      }
    }
  }

  function scheduleTraceRender() {
    if (state.traceRenderPending) return;
    state.traceRenderPending = true;
    requestAnimationFrame(() => {
      state.traceRenderPending = false;
      renderInspector();
      renderSessionStats();
    });
  }

  function renderInspector() {
    const requests = newestRequests();
    $("#request-select").innerHTML = requests.length
      ? requests
          .map(
            (request) =>
              `<option value="${esc(request.requestId)}" ${request.requestId === state.selected ? "selected" : ""}>${esc(clock(request.startedAt))} · ${esc(request.requestId.slice(0, 8))} · ${esc(request.result?.source || (request.error ? "error" : request.finished ? "finished" : "running"))}${request.message ? ` · ${esc(request.message.slice(0, 48))}` : ""}</option>`,
          )
          .join("")
      : '<option value="">No requests yet</option>';
    $("#follow-latest").classList.toggle("active", state.follow);
    $("#follow-latest").setAttribute("aria-pressed", String(state.follow));
    const request = state.requests.get(state.selected);
    const summary = $("#request-summary");
    const result = request?.result;
    summary.hidden = !result && !request?.error;
    if (result) {
      const gate = [...request.events.values()].find(
        (event) => event.category === "cache" && event.operation === "distance gate",
      )?.data;
      const decision =
        gate?.candidate && finite(gate.threshold)
          ? `<p class="match-decision"><strong>${gate.accepted ? "Candidate" : "Rejected by distance"}</strong> · distance ${esc(gate.candidate.distance.toFixed(4))} ${gate.accepted ? "≤" : ">"} limit ${esc(gate.threshold.toFixed(4))}<span>${esc(gate.candidate.question)}</span></p>`
          : "";
      summary.innerHTML = `<div class="summary-topline">${badge(result.source)}<span class="event-meta">${esc(result.tenantId || "")} ${result.policyRevision != null ? `· revision ${esc(result.policyRevision)}` : ""}</span><span class="summary-duration">${esc(duration(result.durationMs))}</span></div><div class="summary-metrics">${usageMetrics(result, true)}</div>${decision}${result.cacheReason ? `<p class="summary-reason">${esc(result.cacheReason)}</p>` : ""}`;
    } else if (request?.error)
      summary.innerHTML = `${badge("error")}<p class="summary-reason">${esc(request.error)}</p>`;
    const allEvents = request
      ? [...request.events.values()].sort((a, b) => (a.sequence || 0) - (b.sequence || 0))
      : [];
    const events =
      state.filter === "all"
        ? allEvents
        : state.filter === "steps"
          ? allEvents.filter(
              (event) =>
                event.status === "error" ||
                event.category === "cache" ||
                (event.category !== "provider" &&
                  event.category !== "request" &&
                  event.status === "completed"),
            )
          : allEvents.filter((event) => event.category === state.filter);
    $("#event-count").textContent = allEvents.length
      ? `${events.length}${state.filter === "all" ? "" : ` of ${allEvents.length}`} event${events.length === 1 ? "" : "s"}`
      : "No events";
    $("#copy-trace").disabled = !request;
    const scroll = $("#trace-scroll");
    const atBottom = scroll.scrollHeight - scroll.scrollTop - scroll.clientHeight < 90;
    const target = $("#trace-events");
    if (!request) {
      target.innerHTML =
        '<div class="trace-empty"><svg viewBox="0 0 48 48"><path d="M8 10h32M8 24h32M8 38h32"/><circle cx="15" cy="10" r="4"/><circle cx="33" cy="24" r="4"/><circle cx="22" cy="38" r="4"/></svg><h3>The trace starts here.</h3><p>Send a request to see cache lookups,<br>model calls, tool inputs, and results.</p></div>';
      return;
    }
    if (!events.length) {
      target.innerHTML = `<div class="session-empty">${allEvents.length ? `No ${esc(state.filter)} events in this request.` : request.finished ? "This request has no recorded events." : "Waiting for trace events…"}</div>`;
      return;
    }
    target.innerHTML = events
      .map((event) => {
        const key = `${event.requestId}:${event.sequence}`;
        const category = Object.hasOwn(categoryNames, event.category) ? event.category : "request";
        const status = ["started", "completed", "error", "info"].includes(event.status)
          ? event.status
          : "info";
        const raw = pretty(event.data);
        const displayed =
          raw.length > 100000
            ? `${raw.slice(0, 100000)}\n… Display shortened. Copy the payload to inspect the full JSON.`
            : raw;
        return `<details class="trace-event status-${status}" data-event-key="${esc(key)}" ${state.openEvents.has(key) ? "open" : ""}><summary><span class="event-category ${category}">${esc(categoryNames[category])}</span><span class="event-main"><span class="event-operation">${esc(event.operation)}</span><span class="event-meta"><span>${esc(event.category)}</span><span>${esc(clock(event.timestamp))}</span><span>#${esc(event.sequence)}</span></span></span><span class="event-status ${status}">${esc(event.status)}</span>${finite(event.durationMs) ? `<span class="event-duration">${esc(duration(event.durationMs))}</span>` : ""}<span class="event-chevron" aria-hidden="true">›</span></summary><div class="event-body"><pre>${esc(displayed)}</pre><button class="text-button" data-copy-event="${esc(key)}">Copy payload JSON</button></div></details>`;
      })
      .join("");
    if (state.follow && request.finished && state.lastFinishedView !== request.requestId) {
      scroll.scrollTop = 0;
      state.lastFinishedView = request.requestId;
    } else if (atBottom && state.follow && !request.finished)
      scroll.scrollTop = scroll.scrollHeight;
  }

  async function copy(value) {
    try {
      await navigator.clipboard.writeText(typeof value === "string" ? value : pretty(value));
      toast("Copied to clipboard.");
    } catch {
      toast("Clipboard access is unavailable. Select and copy the visible text instead.", true);
    }
  }

  async function loadCache() {
    const target = $("#cache-list");
    const policy = tenantPolicy();
    $("#cache-tenant-name").textContent = policy?.name || state.tenantId;
    target.innerHTML =
      '<div class="session-empty"><span class="spinner"></span> Loading entries…</div>';
    $("#reload-cache").disabled = true;
    try {
      const response = await api(
        `/api/cache?tenantId=${encodeURIComponent(state.tenantId)}&sessionId=${encodeURIComponent(sessionId)}`,
      );
      const entries = Array.isArray(response) ? response : response.entries;
      if (!Array.isArray(entries)) throw new Error("Cache metadata has an unexpected format.");
      target.innerHTML = entries.length
        ? entries
            .map((entry) => {
              const scope = entry.scope || {};
              const revision = entry.revision ?? scope.revision;
              const created = entry.createdAt ? dateLabel(entry.createdAt) : "";
              const ttl = entry.ttlSeconds ?? entry.ttl;
              return `<article class="cache-entry"><h3>${esc(entry.question || entry.id || entry.key || "Cached entry")}</h3>${entry.answer ? `<p class="cache-answer">${esc(entry.answer)}</p>` : ""}<div class="cache-entry-meta"><span>${esc(entry.tenantId || scope.tenantId || state.tenantId)}</span>${revision != null ? `<span>Revision ${esc(revision)}${policy && revision !== policy.revision ? " · excluded by current revision" : ""}</span>` : ""}${created ? `<span>${esc(created)}</span>` : ""}${ttl != null ? `<span>TTL ${esc(ttl)}${finite(ttl) ? " s" : ""}</span>` : ""}<span>${esc(entry.hits ?? 0)} hits</span>${entry.shareable && state.status?.playbookConfigured ? `<button class="button primary" data-propose="${esc(entry.id)}">Propose to Agent Playbooks</button>` : ""}</div><details><summary>Stored metadata</summary><pre>${esc(pretty(entry))}</pre></details></article>`;
            })
            .join("")
        : '<div class="session-empty">No cached responses for this tenant yet.<br>Send a cache-eligible question to create an entry.</div>';
    } catch (error) {
      target.innerHTML = `<p class="inline-error">${esc(error.message)}</p>`;
    } finally {
      $("#reload-cache").disabled = false;
      void loadHistory();
    }
  }

  function openDialog(dialog) {
    dialog.showModal();
    document.body.classList.add("modal-open");
  }
  $$("dialog").forEach((dialog) => {
    dialog.addEventListener("close", () => document.body.classList.remove("modal-open"));
    $(".close-dialog", dialog)?.addEventListener("click", () => dialog.close());
  });
  $("#open-cache").addEventListener("click", () => {
    openDialog($("#cache-dialog"));
    void loadCache();
  });
  $("#reload-cache").addEventListener("click", () => void loadCache());
  $("#reset-cache").addEventListener("click", async () => {
    if (state.busy) return;
    setBusy(true);
    try {
      const result = await api(`/api/admin/reset?sessionId=${encodeURIComponent(sessionId)}`, {
        method: "POST",
      });
      toast(
        result.cleared === true
          ? "Demo cache cleared. Trace history is still available."
          : "Reset request completed.",
      );
      await loadCache();
    } catch (error) {
      toast(error.message, true);
    } finally {
      setBusy(false);
    }
  });
  $("#open-policy").addEventListener("click", () => {
    const policy = tenantPolicy();
    $("#policy-title").textContent = `${policy?.name || state.tenantId} export limit`;
    $("#export-limit").value = policy?.exportLimit ?? "";
    $("#policy-current").textContent = policy
      ? `Revision ${policy.revision} · Export limit ${number(policy.exportLimit)} · Retention ${number(policy.retentionDays)} days`
      : "Policy details unavailable.";
    $("#policy-error").hidden = true;
    $("#policy-result").textContent = "";
    openDialog($("#policy-dialog"));
  });
  $("#policy-form").addEventListener("submit", async (event) => {
    event.preventDefault();
    if (state.busy) return;
    const limit = Number($("#export-limit").value);
    if (!Number.isSafeInteger(limit) || limit < 1 || limit > 1000000) return;
    setBusy(true);
    $("#policy-error").hidden = true;
    try {
      const result = await api(
        `/api/tenants/${encodeURIComponent(state.tenantId)}/policy?sessionId=${encodeURIComponent(sessionId)}`,
        { method: "POST", body: JSON.stringify({ exportLimit: limit }) },
      );
      $("#policy-result").textContent = `Saved · revision ${result.revision}`;
      $("#policy-current").textContent =
        `Revision ${result.revision} · Export limit ${number(result.exportLimit)} · Retention ${number(result.retentionDays)} days`;
      await loadStatus();
      toast(
        `Policy updated for ${result.name || state.tenantId}. Ask about the export limit again.`,
      );
    } catch (error) {
      $("#policy-error").textContent = error.message;
      $("#policy-error").hidden = false;
    } finally {
      setBusy(false);
      void loadHistory();
    }
  });

  $("#chat-form").addEventListener("submit", (event) => {
    event.preventDefault();
    if (state.busy || !$("#message-input").value.trim()) return;
    const text = $("#message-input").value;
    void sendQuestion(text);
  });
  $("#message-input").addEventListener("keydown", (event) => {
    if (event.key === "Enter" && !event.shiftKey && !event.isComposing) {
      event.preventDefault();
      if (!$("#send-button").disabled) $("#chat-form").requestSubmit();
    }
  });
  document.addEventListener("click", (event) => {
    const prompt = event.target.closest("[data-prompt]");
    if (prompt) {
      $("#message-input").value = prompt.dataset.prompt;
      $("#message-input").focus();
    }
    const showRequest = event.target.closest("[data-show-request]");
    if (showRequest) {
      state.selected = showRequest.dataset.showRequest;
      state.follow = false;
      renderInspector();
      if (innerWidth < 860)
        $(".inspector-panel").scrollIntoView({ behavior: "smooth", block: "start" });
    }
    const propose = event.target.closest("[data-propose]");
    if (propose) {
      propose.disabled = true;
      api("/api/playbook/proposals", {
        method: "POST",
        body: JSON.stringify({ id: propose.dataset.propose, sessionId }),
      })
        .then((result) => {
          propose.textContent = "Waiting for review";
          toast(`Proposal ${result.proposalId} is waiting for review.`);
        })
        .catch((error) => {
          propose.disabled = false;
          toast(error.message, true);
        });
    }
    const copyEvent = event.target.closest("[data-copy-event]");
    if (copyEvent) {
      for (const request of state.requests.values()) {
        const item = [...request.events.values()].find(
          (e) => `${e.requestId}:${e.sequence}` === copyEvent.dataset.copyEvent,
        );
        if (item) {
          void copy(item.data);
          break;
        }
      }
    }
  });
  $$("[data-mode]").forEach((button) =>
    button.addEventListener("click", () => {
      state.mode = button.dataset.mode;
      renderControls();
    }),
  );
  $("#tenant-select").addEventListener("change", (event) => {
    state.tenantId = event.target.value;
    renderScope();
  });
  $("#threshold").addEventListener("input", () => {
    state.thresholdTouched = true;
  });
  $("#refresh-status").addEventListener("click", () => void loadStatus(true));
  $("#reload-history").addEventListener("click", () => void loadHistory(true));
  $("#request-select").addEventListener("change", (event) => {
    state.selected = event.target.value;
    state.follow = false;
    state.openEvents.clear();
    renderInspector();
    $("#trace-scroll").scrollTop = 0;
  });
  $("#follow-latest").addEventListener("click", () => {
    state.follow = !state.follow;
    if (state.follow) state.selected = newestRequests()[0]?.requestId;
    renderInspector();
  });
  $$("[data-filter]").forEach((button) =>
    button.addEventListener("click", () => {
      state.filter = button.dataset.filter;
      $$("[data-filter]").forEach((other) => {
        const active = other === button;
        other.classList.toggle("active", active);
        other.setAttribute("aria-pressed", String(active));
      });
      renderInspector();
    }),
  );
  $("#trace-events").addEventListener(
    "toggle",
    (event) => {
      const details = event.target;
      if (!details.matches("details[data-event-key]") || !details.isConnected) return;
      if (details.open) state.openEvents.add(details.dataset.eventKey);
      else state.openEvents.delete(details.dataset.eventKey);
    },
    true,
  );
  $("#copy-trace").addEventListener("click", () => {
    const request = state.requests.get(state.selected);
    if (request)
      void copy({
        requestId: request.requestId,
        sessionId,
        startedAt: request.startedAt,
        finished: request.finished,
        result: request.result,
        events: [...request.events.values()],
      });
  });
  $("#popout-inspector").addEventListener("click", () => {
    const url = new URL(location.pathname, location.origin);
    url.searchParams.set("inspector", "1");
    url.searchParams.set("sessionId", sessionId);
    const popup = window.open(url, "semantic-cache-inspector", "popup,width=950,height=950");
    state.inspectorWindow = popup;
    if (!popup)
      toast("Your browser blocked the inspector window. Allow popups for this local site.", true);
  });
  $("#run-workload").addEventListener("click", async () => {
    if (state.busy || !state.status?.ready) return;
    state.workload = true;
    setBusy(true);
    const prompts = samplePrompts;
    let completed = 0;
    try {
      for (const prompt of prompts) {
        $("#run-workload").textContent = `Request ${completed + 1} of ${prompts.length}`;
        if (!(await sendQuestion(prompt, true))) break;
        completed++;
      }
    } finally {
      state.workload = false;
      setBusy(false);
      toast(
        `${completed} of ${prompts.length} sample requests completed. The results and traces are above.`,
        completed < prompts.length,
      );
    }
  });
  $("#open-reset").addEventListener("click", () => {
    $("#reset-result").textContent = "";
    openDialog($("#reset-dialog"));
  });
  $("#confirm-reset").addEventListener("click", async () => {
    if (state.busy) return;
    setBusy(true);
    $("#reset-result").textContent = "Resetting demo data…";
    const preparationSession = `prepare-${crypto.randomUUID()}`;
    try {
      const status = await api("/api/status");
      for (const [id, exportLimit] of [
        ["acme", 10000],
        ["globex", 1000],
      ]) {
        if (status.tenants.find((t) => t.id === id)?.exportLimit !== exportLimit)
          await api(`/api/tenants/${id}/policy?sessionId=${preparationSession}`, {
            method: "POST",
            body: JSON.stringify({ exportLimit }),
          });
      }
      await api(`/api/admin/reset?sessionId=${preparationSession}`, { method: "POST" });
      const nextSession = crypto.randomUUID();
      const next = new URL(location.href);
      next.searchParams.set("sessionId", nextSession);
      sessionChannel?.postMessage({ nextSession });
      if (!sessionChannel && state.inspectorWindow && !state.inspectorWindow.closed) {
        const inspector = new URL(next);
        inspector.searchParams.set("inspector", "1");
        state.inspectorWindow.location.href = inspector.href;
      }
      location.replace(next.href);
    } catch (error) {
      $("#reset-result").textContent = `Reset did not finish: ${error.message}`;
      await loadStatus();
      setBusy(false);
    }
  });
  function renderComparison() {
    const target = $("#comparison-results");
    if (!state.comparison.length) {
      target.innerHTML =
        '<p class="comparison-empty">Run the same five questions with each cache mode.</p>';
      return;
    }
    target.innerHTML = `<p class="comparison-recorded">${esc(state.comparisonScope)}</p><div class="comparison-table-scroll"><table class="comparison-table"><thead><tr><th>Mode</th><th>Requests</th><th>Hits</th><th>Chat calls</th><th>Tool calls</th><th>Embeddings</th><th>Checks</th><th>Input tokens</th><th>Output tokens</th><th>HTTP time</th></tr></thead><tbody>${state.comparison
      .map((run) => {
        const sum = (key) =>
          run.results.reduce((total, item) => total + (item.result.usage[key] || 0), 0);
        return `<tr><th>${esc(run.mode)}${run.results.length < samplePrompts.length ? "<small>Incomplete: completed requests only</small>" : ""}</th><td>${run.results.length}/${samplePrompts.length}</td><td>${run.results.filter((item) => ["exact", "semantic"].includes(item.result.source)).length}</td><td>${sum("modelCalls")}</td><td>${sum("toolCalls")}</td><td>${sum("embeddingCalls")}</td><td>${sum("checkCalls")}</td><td>${number(sum("inputTokens") + sum("checkInputTokens"))}</td><td>${number(sum("outputTokens") + sum("checkOutputTokens"))}</td><td>${esc(duration(run.results.reduce((total, item) => total + item.httpMs, 0)))}</td></tr>`;
      })
      .join(
        "",
      )}</tbody></table></div>${state.comparison.map((run) => `<details class="comparison-answers"><summary>${esc(run.mode)}: inspect answers and cache decisions</summary>${run.results.map((item) => `<article><div>${badge(item.result.source)}<strong>${esc(item.question)}</strong></div><p>${esc(item.result.answer)}</p><small>${esc(item.result.tenantId)} · revision ${esc(item.result.policyRevision)} · ${esc(duration(item.httpMs))} HTTP</small></article>`).join("")}</details>`).join("")}`;
  }
  $("#open-comparison").addEventListener("click", () => {
    const policy = tenantPolicy();
    $("#comparison-scope").textContent =
      `Next run: ${policy?.name || state.tenantId} · revision ${policy?.revision} · max distance ${$("#threshold").value}.`;
    openDialog($("#comparison-dialog"));
  });
  $("#run-comparison").addEventListener("click", async () => {
    if (state.busy || !state.status?.ready) return;
    const threshold = Number($("#threshold").value);
    if (
      $("#threshold").value.trim() === "" ||
      !Number.isFinite(threshold) ||
      threshold < 0 ||
      threshold > 0.3
    ) {
      toast("Use a distance threshold from 0 to 0.3.", true);
      return;
    }
    const dialog = $("#comparison-dialog");
    const originalMode = state.mode;
    const tenantId = state.tenantId;
    state.comparing = true;
    state.comparison = [];
    const policy = tenantPolicy();
    state.comparisonScope = `Recorded run: ${policy?.name || tenantId} · starting revision ${policy?.revision} · max distance ${threshold} · ${new Date().toLocaleTimeString()}.`;
    state.follow = true;
    setBusy(true);
    $(".close-dialog", dialog).disabled = true;
    renderComparison();
    try {
      const warmSession = `warmup-${crypto.randomUUID()}`;
      $("#comparison-progress").textContent = "Warming up chat and embedding models…";
      await api("/api/chat", {
        method: "POST",
        body: JSON.stringify({
          message: samplePrompts[0],
          mode: "off",
          tenantId,
          sessionId: warmSession,
        }),
      });
      await api(`/api/admin/reset?sessionId=${warmSession}`, { method: "POST" });
      await api("/api/chat", {
        method: "POST",
        body: JSON.stringify({
          message: samplePrompts[0],
          mode: "semantic",
          tenantId,
          sessionId: warmSession,
          threshold,
        }),
      });
      for (const [mode, verify] of [
        ["off", true],
        ["exact", true],
        ["semantic", true],
        ["semantic", false],
      ]) {
        await api(`/api/admin/reset?sessionId=${warmSession}`, { method: "POST" });
        state.mode = mode;
        renderControls();
        const run = { mode: verify ? mode : `${mode}, no check`, results: [] };
        state.comparison.push(run);
        for (const [index, question] of samplePrompts.entries()) {
          $("#comparison-progress").textContent =
            `${mode[0].toUpperCase() + mode.slice(1)} · request ${index + 1} of ${samplePrompts.length}`;
          const started = performance.now();
          const result = await api("/api/chat", {
            method: "POST",
            body: JSON.stringify({
              message: question,
              mode,
              tenantId,
              sessionId,
              threshold,
              verify,
            }),
          });
          run.results.push({ question, result, httpMs: performance.now() - started });
          state.messages.push({
            text: question,
            tenantId,
            mode,
            result,
            requestId: result.requestId,
          });
          ingestResult(result, question);
          renderComparison();
        }
      }
      $("#comparison-progress").textContent = "Complete · 20 measured requests. Warm-up excluded.";
    } catch (error) {
      $("#comparison-progress").textContent = `Comparison stopped: ${error.message}`;
      renderComparison();
    } finally {
      state.mode = originalMode;
      state.comparing = false;
      setBusy(false);
      $(".close-dialog", dialog).disabled = false;
      renderMessages();
      renderInspector();
    }
  });
  $("#comparison-dialog").addEventListener("cancel", (event) => {
    if (state.comparing) event.preventDefault();
  });
  window.addEventListener("pagehide", (event) => {
    state.stream?.close();
    if (!event.persisted) sessionChannel?.close();
  });
  window.addEventListener("pageshow", (event) => {
    if (event.persisted) connectEvents();
  });
  renderControls();
  void loadStatus();
  void loadHistory();
  connectEvents();
})();
