import { api } from "../api.js";
import { h, clear, num, ago, duration, statusBadge, CONNECTOR_NAMES, ltr } from "../ui.js";
import { describeEvent } from "./audit.js";

const REFRESH_MS = 30000;

export default {
  title: "داشبورد",
  icon: "◧",

  async render(el, ctx) {
    const body = h("div", { class: "grid", style: "gap:18px" }, h("div", { class: "muted" }, "در حال بارگذاری…"));
    const refresh = h("button", { class: "btn sm", onclick: () => load() }, "به‌روزرسانی");
    ctx.actions.append(refresh);
    el.append(body);

    async function load() {
      refresh.disabled = true;
      try {
        const d = await api("/overview");
        clear(body, view(d, ctx));
      } catch (e) {
        clear(body, h("div", { class: "notice" }, e.message));
      } finally { refresh.disabled = false; }
    }
    await load();
    const timer = setInterval(load, REFRESH_MS);
    return () => clearInterval(timer);
  },
};

function view(d, ctx) {
  const u = d.users;
  const stat = (v, l, extra = null) => h("div", { class: "card stat" }, h("div", { class: "v" }, v), h("div", { class: "l" }, l), extra);
  const policy = d.policy_mode === "deny"
    ? h("span", { class: "badge" }, "deny — فقط گفتگوی عمومی")
    : h("span", { class: "badge warn" }, "permissive — حالت توسعه");

  return [
    u.total === 0 ? h("div", { class: "notice info" },
      "هنوز هیچ کاربری مجاز نشده است؛ تا وقتی کاربری را از بخش «کاربران» اضافه نکنید، هیچ‌کس نمی‌تواند وارد کلاینت شود. ",
      h("a", { href: "#/users" }, "افزودن کاربر")) : null,
    h("div", { class: "grid stats" },
      stat(num(u.enabled), "کاربران فعال"),
      stat(num(u.disabled), "کاربران مسدود"),
      stat(num(u.active_24h), "ورود در ۲۴ ساعت اخیر"),
      stat(duration(d.server.uptime_seconds), "مدت روشن بودن سرور", h("div", { class: "muted small ltr" }, "v" + d.server.version))),
    h("div", { class: "grid two" },
      h("div", { class: "card" },
        h("div", { class: "card-h" }, h("h2", {}, "وضعیت اتصال‌ها"), h("a", { href: "#/settings", class: "small" }, "تنظیمات")),
        h("div", { class: "card-b" },
          h("div", { class: "checks" }, Object.entries(d.checks).map(([k, v]) =>
            h("div", { class: "check" }, h("span", {}, CONNECTOR_NAMES[k] || k), statusBadge(v)))),
          h("div", { class: "muted small", style: "margin-top:12px" }, "سیاست دسترسی داده: ", policy))),
      h("div", { class: "card" },
        h("div", { class: "card-h" }, h("h2", {}, "رویدادهای اخیر"), h("a", { href: "#/audit", class: "small" }, "همه")),
        h("div", { class: "card-b" },
          d.recent.length
            ? h("ul", { class: "feed" }, d.recent.map((e) => h("li", {},
                h("span", {}, describeEvent(e.action), e.target ? [" ", ltr(e.target, "muted")] : null),
                h("span", { class: "muted small" }, ago(e.ts)))))
            : h("div", { class: "muted" }, "رویدادی ثبت نشده است.")))),
  ];
}
