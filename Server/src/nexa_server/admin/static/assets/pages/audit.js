import { api } from "../api.js";
import { h, clear, dateTime, ago, ltr, num } from "../ui.js";

const LABELS = {
  "admin.login": "ورود مدیر",
  "admin.login_failed": "تلاش ناموفق ورود مدیر",
  "admin.password_changed": "تغییر رمز مدیر",
  "admin.password_reset": "بازنشانی رمز مدیر (خط فرمان)",
  "admin.bootstrap": "تنظیم رمز اولیهٔ مدیر",
  "user.add": "افزودن کاربر",
  "user.remove": "حذف کاربر",
  "user.enable": "فعال‌سازی کاربر",
  "user.disable": "مسدودسازی کاربر",
  "user.note": "ویرایش یادداشت کاربر",
  "settings.update": "تغییر تنظیمات",
  "login.denied": "رد ورود (کاربر مجاز نیست)",
};
export const describeEvent = (action) => LABELS[action] || action;

const PAGE = 50;

export default {
  title: "گزارش رویدادها",
  icon: "☰",

  async render(el) {
    let offset = 0, action = "";
    const filter = h("select", { class: "input", style: "max-width:260px", onchange: (e) => { action = e.target.value; offset = 0; load(); } },
      h("option", { value: "" }, "همهٔ رویدادها"));
    const table = h("div", {});
    const pager = h("div", { class: "pager" });
    el.append(h("div", { class: "card" }, h("div", { class: "card-h" }, filter), table, pager));

    let actionsFilled = false;
    async function load() {
      try {
        const d = await api("/audit", { query: { limit: PAGE, offset, action } });
        if (!actionsFilled) {
          filter.append(...d.actions.map((a) => h("option", { value: a }, describeEvent(a))));
          actionsFilled = true;
        }
        clear(table, d.items.length ? h("table", { class: "t" },
          h("thead", {}, h("tr", {}, ["زمان", "رویداد", "هدف", "جزئیات", "انجام‌دهنده", "IP"].map((t) => h("th", {}, t)))),
          h("tbody", {}, d.items.map((e) => h("tr", {},
            h("td", { title: dateTime(e.ts) }, ago(e.ts)),
            h("td", {}, describeEvent(e.action)),
            h("td", {}, e.target ? ltr(e.target) : "—"),
            h("td", { class: "muted small" }, e.detail ? ltr(e.detail) : "—"),
            h("td", {}, e.actor),
            h("td", { class: "muted small" }, e.ip ? ltr(e.ip) : "—")))))
          : h("div", { class: "empty" }, "رویدادی ثبت نشده است."));
        const pages = Math.max(1, Math.ceil(d.total / PAGE));
        const page = Math.floor(offset / PAGE) + 1;
        clear(pager,
          h("span", { class: "muted small" }, `${num(d.total)} رویداد · صفحهٔ ${num(page)} از ${num(pages)}`),
          h("button", { class: "btn sm", disabled: page <= 1, onclick: () => { offset -= PAGE; load(); } }, "قبلی"),
          h("button", { class: "btn sm", disabled: page >= pages, onclick: () => { offset += PAGE; load(); } }, "بعدی"));
      } catch (e) {
        clear(table, h("div", { class: "empty" }, e.message));
      }
    }
    await load();
  },
};
