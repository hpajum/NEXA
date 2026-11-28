import { api, setCsrf } from "../api.js";
import { h, toast } from "../ui.js";

export default {
  title: "حساب مدیر",
  icon: "⚿",

  async render(el, ctx) {
    const current = h("input", { class: "input ltr", type: "password", autocomplete: "current-password", required: true });
    const next = h("input", { class: "input ltr", type: "password", autocomplete: "new-password", required: true, minlength: "12" });
    const repeat = h("input", { class: "input ltr", type: "password", autocomplete: "new-password", required: true });
    const err = h("div", { class: "err" });
    const btn = h("button", { class: "btn primary", type: "submit" }, "تغییر رمز");

    const row = (label, input, help) => h("div", { class: "field" }, h("div", { class: "lbl" }, label), h("div", {}, input, help ? h("div", { class: "help" }, help) : null));

    const form = h("form", { onsubmit: async (e) => {
      e.preventDefault();
      err.textContent = "";
      if (next.value !== repeat.value) { err.textContent = "تکرار رمز با رمز جدید یکسان نیست."; return; }
      btn.disabled = true;
      try {
        const res = await api("/password", { method: "POST", body: { current: current.value, new: next.value } });
        setCsrf(res.csrf);
        current.value = next.value = repeat.value = "";
        toast("رمز مدیر تغییر کرد. نشست‌های دیگر خارج شدند.", "ok");
        ctx.signedIn({ ...ctx.session, csrf: res.csrf, must_change_password: false });
      } catch (ex) {
        err.textContent = ex.message;
      } finally { btn.disabled = false; }
    } },
      row("رمز فعلی", current),
      row("رمز جدید", next, "دست‌کم ۱۲ نویسه. پس از تغییر، همهٔ نشست‌های دیگر مدیر باطل می‌شوند."),
      row("تکرار رمز جدید", repeat),
      h("div", { class: "field" }, h("div", {}), h("div", {}, err, btn)));

    el.append(
      ctx.session.must_change_password ? h("div", { class: "notice" }, "این رمز اولیه است که از ‎.env‎ تنظیم شده است. آن را تغییر دهید و سپس NEXA_ADMIN_INITIAL_PASSWORD را از ‎.env‎ حذف کنید.") : null,
      h("div", { class: "card" }, h("div", { class: "card-h" }, h("h2", {}, "تغییر رمز مدیر")), h("div", { class: "card-b" }, form)),
      h("div", { class: "card" }, h("div", { class: "card-b muted small" },
        "رمز را فراموش کرده‌اید؟ روی سرور اجرا کنید: ", h("code", { class: "ltr" }, "docker compose exec nexa-server nexa-admin-password"))));
  },
};
