// Small DOM helpers. Text always goes through textContent: no innerHTML with data anywhere.

export function h(tag, props = {}, ...children) {
  const el = document.createElement(tag);
  for (const [k, v] of Object.entries(props || {})) {
    if (v === undefined || v === null || v === false) continue;
    if (k === "class") el.className = v;
    else if (k === "style") el.style.cssText = v; // CSSOM: allowed by the strict CSP (attributes are not)
    else if (k.startsWith("on") && typeof v === "function") el.addEventListener(k.slice(2).toLowerCase(), v);
    else if (k === "value") el.value = v;
    else if (k === "checked") el.checked = !!v;
    else if (k === "disabled") el.disabled = !!v;
    else el.setAttribute(k, v === true ? "" : String(v));
  }
  append(el, children);
  return el;
}

function append(el, children) {
  for (const c of children.flat(Infinity)) {
    if (c === null || c === undefined || c === false) continue;
    el.append(c instanceof Node ? c : document.createTextNode(String(c)));
  }
}

export function clear(el, ...children) { el.replaceChildren(); append(el, children); return el; }

// --- formatting -------------------------------------------------------------------

const faNum = new Intl.NumberFormat("fa-IR");
const faDate = new Intl.DateTimeFormat("fa-IR", { dateStyle: "medium", timeStyle: "short" });

export const num = (n) => faNum.format(n ?? 0);
export const dateTime = (ts) => (ts ? faDate.format(new Date(ts * 1000)) : "—");

export function ago(ts) {
  if (!ts) return "هرگز";
  const s = Math.max(0, Math.floor(Date.now() / 1000 - ts));
  if (s < 60) return "لحظاتی پیش";
  if (s < 3600) return `${num(Math.floor(s / 60))} دقیقه پیش`;
  if (s < 86400) return `${num(Math.floor(s / 3600))} ساعت پیش`;
  if (s < 86400 * 30) return `${num(Math.floor(s / 86400))} روز پیش`;
  return dateTime(ts);
}

export function duration(sec) {
  const d = Math.floor(sec / 86400), hr = Math.floor((sec % 86400) / 3600), m = Math.floor((sec % 3600) / 60);
  if (d) return `${num(d)} روز و ${num(hr)} ساعت`;
  if (hr) return `${num(hr)} ساعت و ${num(m)} دقیقه`;
  return `${num(m)} دقیقه`;
}

/** Account ids (DOMAIN\sam) are LTR text inside an RTL page. */
export const ltr = (text, cls = "") => h("span", { class: ("ltr " + cls).trim() }, text);

// --- status ------------------------------------------------------------------------

const CHECK_LABELS = { ok: ["سالم", "ok"], configured: ["پیکربندی‌شده", "ok"], unavailable: ["در دسترس نیست", "bad"], not_configured: ["پیکربندی نشده", ""] };
export function statusBadge(status) {
  const [label, cls] = CHECK_LABELS[status] || [status, ""];
  return h("span", { class: "badge " + cls }, label);
}
export const CONNECTOR_NAMES = { ldap: "Active Directory", llm: "مدل زبانی", openmetadata: "OpenMetadata", cube: "Cube", clickhouse: "ClickHouse" };

// --- feedback ----------------------------------------------------------------------

export function toast(message, kind = "") {
  const el = h("div", { class: "toast " + kind, role: "status" }, message);
  document.getElementById("toasts").append(el);
  setTimeout(() => el.remove(), 4200);
}

/** Generic modal. `build(close)` returns {body, footer[]} nodes. Returns a promise resolved by close(value). */
export function modal(title, build) {
  return new Promise((resolve) => {
    const back = h("div", { class: "modal-back" });
    const close = (value) => { back.remove(); document.removeEventListener("keydown", onKey); resolve(value); };
    const onKey = (e) => { if (e.key === "Escape") close(undefined); };
    const { body, footer = [] } = build(close);
    const box = h("div", { class: "modal", role: "dialog", "aria-modal": "true" },
      h("div", { class: "card-h" }, h("h2", {}, title), h("button", { class: "btn ghost", onclick: () => close(undefined), "aria-label": "بستن" }, "✕")),
      h("div", { class: "card-b" }, body),
      footer.length ? h("div", { class: "modal-f" }, footer) : null);
    back.addEventListener("mousedown", (e) => { if (e.target === back) close(undefined); });
    document.addEventListener("keydown", onKey);
    back.append(box);
    document.body.append(back);
    const first = box.querySelector("input, select, textarea, .btn.primary");
    if (first) first.focus();
  });
}

export function confirmDialog(title, message, { danger = false, ok = "تأیید" } = {}) {
  return modal(title, (close) => ({
    body: h("p", {}, message),
    footer: [
      h("button", { class: "btn " + (danger ? "primary danger-fill" : "primary"), onclick: () => close(true) }, ok),
      h("button", { class: "btn", onclick: () => close(false) }, "انصراف"),
    ],
  })).then(Boolean);
}

export function debounce(fn, ms = 300) {
  let t;
  return (...args) => { clearTimeout(t); t = setTimeout(() => fn(...args), ms); };
}

/** Runs an async action while disabling a button; reports errors as a toast. */
export async function busy(button, action) {
  if (button) button.disabled = true;
  try { return await action(); }
  catch (e) { toast(e.message || "خطا", "bad"); }
  finally { if (button) button.disabled = false; }
}
