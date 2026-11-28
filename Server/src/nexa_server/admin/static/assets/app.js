// Admin SPA shell: session handling, hash router and the page registry.
//
// Adding a page = create assets/pages/<name>.js exporting
//   export default { title, icon, section?, render(container, ctx) -> optional cleanup fn }
// and add one line to PAGES below. No build step.

import { api, setCsrf, setUnauthorizedHandler } from "./api.js";
import { h, clear } from "./ui.js";

const PAGES = [
  { path: "dashboard", section: "نمای کلی", load: () => import("./pages/dashboard.js") },
  { path: "users", section: "مدیریت", load: () => import("./pages/users.js") },
  { path: "settings", section: "مدیریت", load: () => import("./pages/settings.js") },
  { path: "audit", section: "پایش", load: () => import("./pages/audit.js") },
  { path: "account", section: "حساب", load: () => import("./pages/account.js") },
];
const DEFAULT = "dashboard";

const root = document.getElementById("app");
const state = { session: null, modules: {}, cleanup: null };

async function loadModules() {
  await Promise.all(PAGES.map(async (p) => { state.modules[p.path] = (await p.load()).default; }));
}

// --- session ---------------------------------------------------------------------

async function checkSession() {
  try {
    const me = await api("/me");
    setCsrf(me.csrf);
    state.session = me;
  } catch {
    state.session = null;
  }
}

export function signedIn(session) {
  setCsrf(session.csrf);
  state.session = session;
  if (session.must_change_password) location.hash = "#/account";
  route();
}

async function logout() {
  try { await api("/logout", { method: "POST" }); } catch { /* ignore */ }
  setCsrf("");
  state.session = null;
  renderLogin();
}

setUnauthorizedHandler(() => { state.session = null; renderLogin("نشست شما به پایان رسیده است. دوباره وارد شوید."); });

// --- login view ---------------------------------------------------------------------

function renderLogin(message = "") {
  if (state.cleanup) { state.cleanup(); state.cleanup = null; }
  const err = h("div", { class: "err", role: "alert" }, message);
  const pass = h("input", { class: "input ltr", type: "password", autocomplete: "current-password", required: true, placeholder: "رمز عبور مدیر" });
  const btn = h("button", { class: "btn primary", type: "submit" }, "ورود");
  const form = h("form", {
    onsubmit: async (e) => {
      e.preventDefault();
      btn.disabled = true; err.textContent = "";
      try {
        const res = await api("/login", { method: "POST", body: { password: pass.value } });
        pass.value = "";
        signedIn(res);
      } catch (ex) {
        err.textContent = ex.status === 401 ? "رمز عبور نادرست است." : ex.message;
        pass.select();
      } finally { btn.disabled = false; }
    },
  }, pass, err, btn);
  clear(root, h("div", { class: "login" },
    h("div", { class: "card" },
      h("div", { class: "brand", style: "color:inherit;padding:0 0 6px" }, h("span", { class: "brand-mark", style: "color:#fff" }, "N"), "NEXA"),
      h("h1", {}, "پنل مدیریت"),
      h("div", { class: "muted small" }, "برای ادامه رمز مدیر را وارد کنید."),
      form)));
  root.classList.remove("boot");
  pass.focus();
}

// --- shell + router ---------------------------------------------------------------------

function currentPath() {
  const p = location.hash.replace(/^#\/?/, "").split("?")[0];
  return state.modules[p] ? p : DEFAULT;
}

function shell(active) {
  const nav = [];
  let section = null;
  for (const p of PAGES) {
    const mod = state.modules[p.path];
    if (p.section !== section) { section = p.section; nav.push(h("div", { class: "nav-section" }, section)); }
    nav.push(h("a", { href: "#/" + p.path, class: p.path === active ? "active" : "" }, h("span", { class: "ico" }, mod.icon || "•"), mod.title));
  }
  const title = h("h1", {}, state.modules[active].title);
  const actions = h("div", { class: "toolbar" });
  const content = h("div", { class: "content" });
  const layout = h("div", { class: "shell" },
    h("aside", { class: "side" },
      h("div", { class: "brand" }, h("span", { class: "brand-mark" }, "N"), "NEXA"),
      h("nav", { class: "nav" }, nav),
      h("div", { class: "side-foot" }, h("button", { onclick: logout }, "خروج"))),
    h("main", { class: "main" }, h("div", { class: "topbar" }, title, actions), content));
  return { layout, content, actions };
}

async function route() {
  if (!state.session) return renderLogin();
  if (state.cleanup) { state.cleanup(); state.cleanup = null; }
  const path = currentPath();
  const { layout, content, actions } = shell(path);
  clear(root, layout);
  root.classList.remove("boot");
  document.title = `NEXA · ${state.modules[path].title}`;
  if (state.session.must_change_password && path !== "account") {
    content.append(h("div", { class: "notice" }, "رمز اولیهٔ مدیر هنوز تغییر نکرده است. لطفاً از بخش «حساب مدیر» آن را عوض کنید."));
  }
  try {
    const ctx = { actions, session: state.session, signedIn, navigate: (p) => { location.hash = "#/" + p; } };
    const cleanup = await state.modules[path].render(content, ctx);
    if (typeof cleanup === "function") state.cleanup = cleanup;
  } catch (e) {
    content.append(h("div", { class: "notice" }, e.message || "بارگذاری صفحه ناموفق بود."));
  }
}

window.addEventListener("hashchange", route);

(async () => {
  await loadModules();
  await checkSession();
  route();
})();
