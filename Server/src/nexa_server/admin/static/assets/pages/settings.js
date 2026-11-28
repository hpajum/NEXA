import { api } from "../api.js";
import { h, clear, toast, confirmDialog, statusBadge, CONNECTOR_NAMES, dateTime, ltr } from "../ui.js";

export default {
  title: "تنظیمات",
  icon: "⚙",

  async render(el, ctx) {
    let data = await api("/settings");
    let group = data.groups[0].id;
    const edits = {}; // key -> new value, or null = reset to .env
    const errors = {};

    const tabs = h("div", { class: "tabs" });
    const body = h("div", { class: "card-b" });
    const saveBtn = h("button", { class: "btn primary", disabled: true, onclick: save }, "ذخیره و اعمال");
    const discardBtn = h("button", { class: "btn", disabled: true, onclick: () => { for (const k in edits) delete edits[k]; for (const k in errors) delete errors[k]; draw(); } }, "لغو تغییرات");
    const testBtn = h("button", { class: "btn", onclick: test }, "آزمایش اتصال‌ها");
    const checks = h("div", { class: "checks hidden", style: "padding:0 18px 16px" });
    ctx.actions.append(testBtn, discardBtn, saveBtn);

    el.append(
      h("div", { class: "notice info" }, "مقادیری که اینجا ذخیره شوند بر مقادیر فایل ‎.env‎ اولویت دارند و بدون ری‌استارت اعمال می‌شوند. رمزها رمزنگاری‌شده ذخیره می‌شوند و هرگز نمایش داده نمی‌شوند."),
      h("div", { class: "card" }, tabs, checks, body),
      h("div", { class: "card" },
        h("div", { class: "card-h" }, h("h2", {}, "تنظیمات نیازمند ری‌استارت (فقط از ‎.env‎)")),
        h("table", { class: "t" }, h("tbody", {}, data.readonly.map((r) =>
          h("tr", {}, h("td", { class: "ltr", style: "width:40%" }, "NEXA_" + r.key.toUpperCase()), h("td", {}, ltr(r.value))))))));

    window.addEventListener("beforeunload", guard);
    draw();
    return () => window.removeEventListener("beforeunload", guard);

    function guard(e) { if (Object.keys(edits).length) { e.preventDefault(); e.returnValue = ""; } }

    function draw() {
      const dirtyGroups = new Set(data.fields.filter((f) => f.key in edits).map((f) => f.group));
      const errGroups = new Set(data.fields.filter((f) => f.key in errors).map((f) => f.group));
      clear(tabs, data.groups.map((g) => h("button", { class: "tab" + (g.id === group ? " active" : ""), onclick: () => { group = g.id; draw(); } },
        g.label, errGroups.has(g.id) ? " ⚠" : dirtyGroups.has(g.id) ? " •" : "")));
      clear(body, data.fields.filter((f) => f.group === group).map(fieldRow));
      if (errors._) body.prepend(h("div", { class: "notice", style: "margin-bottom:10px" }, errors._));
      const dirty = Object.keys(edits).length > 0;
      saveBtn.disabled = !dirty;
      discardBtn.disabled = !dirty;
    }

    function setEdit(f, value) {
      const original = f.kind === "secret" ? "" : f.value;
      if (value === original && f.kind !== "secret") delete edits[f.key];
      else if (f.kind === "secret" && value === "") delete edits[f.key];
      else edits[f.key] = value;
      delete errors[f.key];
      const dirty = Object.keys(edits).length > 0;
      saveBtn.disabled = !dirty;
      discardBtn.disabled = !dirty;
      const rowEl = body.querySelector(`[data-key="${f.key}"]`);
      if (rowEl) rowEl.classList.toggle("dirty", f.key in edits);
    }

    function fieldRow(f) {
      const pending = f.key in edits;
      const current = pending ? edits[f.key] : f.value;
      let control;
      if (f.kind === "bool") {
        const cb = h("input", { type: "checkbox", checked: (current ?? "") === "true", onchange: (e) => setEdit(f, e.target.checked ? "true" : "false") });
        control = h("label", { class: "switch" }, cb, "فعال");
      } else if (f.kind === "select") {
        control = h("select", { class: "input ltr", style: "max-width:260px", onchange: (e) => setEdit(f, e.target.value) },
          f.options.map((o) => h("option", { value: o, selected: o === current ? "selected" : null }, o)));
      } else if (f.kind === "secret") {
        control = h("input", { class: "input ltr", type: "password", autocomplete: "new-password",
          placeholder: f.is_set ? "•••••••• (تنظیم شده — برای تغییر مقدار جدید وارد کنید)" : "تنظیم نشده",
          value: pending && edits[f.key] !== null ? edits[f.key] : "", oninput: (e) => setEdit(f, e.target.value) });
      } else if (f.textarea) {
        control = h("textarea", { class: "input ltr", value: current ?? "", oninput: (e) => setEdit(f, e.target.value) });
      } else {
        control = h("input", { class: "input ltr", type: f.kind === "number" ? "number" : "text", step: "any",
          value: current ?? "", oninput: (e) => setEdit(f, e.target.value) });
      }

      const source = f.source === "panel"
        ? h("span", { class: "badge info", title: f.updated_at ? `آخرین تغییر: ${dateTime(f.updated_at)}` : "" }, "از پنل")
        : h("span", { class: "badge" }, "از ‎.env‎");
      const reset = f.source === "panel" && !(f.key in edits && edits[f.key] === null)
        ? h("button", { class: "btn sm ghost", title: "حذف مقدار پنل و بازگشت به ‎.env‎", onclick: () => { edits[f.key] = null; draw(); } }, "بازگشت به ‎.env‎")
        : pending && edits[f.key] === null ? h("span", { class: "badge warn" }, "به ‎.env‎ برمی‌گردد") : null;

      return h("div", { class: "field" + (pending ? " dirty" : ""), "data-key": f.key },
        h("div", {}, h("div", { class: "lbl" }, f.label), h("div", { class: "muted small ltr" }, "NEXA_" + f.key.toUpperCase())),
        h("div", {},
          h("div", { class: "field-row" }, control, source, reset),
          f.help ? h("div", { class: "help" }, f.help) : null,
          errors[f.key] ? h("div", { class: "err" }, errors[f.key]) : null));
    }

    async function save() {
      const keys = Object.keys(edits);
      const risky = keys.some((k) => k.startsWith("ldap_")) ? "تغییر تنظیمات AD می‌تواند ورود همهٔ کاربران را مختل کند. " : "";
      const ok = await confirmDialog("اعمال تنظیمات", `${risky}${keys.length} تغییر ذخیره و فوراً اعمال شود؟`);
      if (!ok) return;
      saveBtn.disabled = true;
      try {
        data = await api("/settings", { method: "PUT", body: { ...edits } });
        for (const k in edits) delete edits[k];
        for (const k in errors) delete errors[k];
        toast("تنظیمات ذخیره و اعمال شد.", "ok");
        draw();
        test();
      } catch (e) {
        for (const k in errors) delete errors[k];
        if (e.fields) {
          Object.assign(errors, e.fields);
          const firstBad = data.fields.find((f) => f.key in errors);
          if (firstBad) group = firstBad.group;
        }
        toast(e.message, "bad");
        draw();
      }
    }

    async function test() {
      testBtn.disabled = true;
      clear(checks, h("span", { class: "muted small" }, "در حال آزمایش…"));
      checks.classList.remove("hidden");
      try {
        const r = await api("/settings/test", { method: "POST" });
        clear(checks, Object.entries(r.checks).map(([k, v]) => h("div", { class: "check" }, h("span", {}, CONNECTOR_NAMES[k] || k), statusBadge(v))));
      } catch (e) {
        clear(checks, h("span", { class: "muted small" }, e.message));
      } finally { testBtn.disabled = false; }
    }
  },
};
