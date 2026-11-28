import { api } from "../api.js";
import { h, clear, ago, dateTime, ltr, toast, modal, confirmDialog, debounce, busy, num } from "../ui.js";

export default {
  title: "کاربران",
  icon: "◉",

  async render(el, ctx) {
    const filters = { q: "", status: "all" };
    const table = h("div", {}, h("div", { class: "empty" }, "در حال بارگذاری…"));
    const count = h("span", { class: "muted small" });

    const search = h("input", { class: "input", style: "max-width:280px", placeholder: "جستجو در نام، حساب یا یادداشت…",
      oninput: debounce((e) => { filters.q = e.target.value; load(); }) });
    const status = h("select", { class: "input", style: "max-width:160px", onchange: (e) => { filters.status = e.target.value; load(); } },
      h("option", { value: "all" }, "همه"), h("option", { value: "enabled" }, "فعال"), h("option", { value: "disabled" }, "مسدود"));

    ctx.actions.append(h("button", { class: "btn primary", onclick: () => addDialog().then((added) => added && load()) }, "＋ افزودن کاربر از AD"));
    el.append(
      h("div", { class: "notice info" }, "فقط کاربرانی که در این فهرست «فعال» باشند می‌توانند وارد NEXA شوند. مسدود کردن یک کاربر بلافاصله دسترسی او را قطع می‌کند."),
      h("div", { class: "card" },
        h("div", { class: "card-h" }, h("div", { class: "toolbar" }, search, status), count),
        table));

    async function load() {
      try {
        const { users } = await api("/users", { query: filters });
        count.textContent = `${num(users.length)} کاربر`;
        clear(table, users.length ? renderTable(users, load) : h("div", { class: "empty" },
          filters.q || filters.status !== "all" ? "کاربری با این فیلتر پیدا نشد." : "هنوز کاربری اضافه نشده است."));
      } catch (e) {
        clear(table, h("div", { class: "empty" }, e.message));
      }
    }
    await load();
  },
};

function renderTable(users, reload) {
  return h("table", { class: "t" },
    h("thead", {}, h("tr", {}, ["کاربر", "وضعیت", "آخرین ورود", "تعداد ورود", "یادداشت", ""].map((t) => h("th", {}, t)))),
    h("tbody", {}, users.map((u) => row(u, reload))));
}

function row(u, reload) {
  const toggle = h("button", { class: "btn sm " + (u.enabled ? "danger" : ""), onclick: (e) => busy(e.currentTarget, async () => {
    await api(`/users/${u.id}`, { method: "PATCH", body: { enabled: !u.enabled } });
    toast(u.enabled ? "کاربر مسدود شد." : "کاربر فعال شد.", "ok");
    reload();
  }) }, u.enabled ? "مسدود کردن" : "فعال کردن");

  const edit = h("button", { class: "btn sm", onclick: () => noteDialog(u).then((changed) => changed && reload()) }, "یادداشت");

  const remove = h("button", { class: "btn sm danger", onclick: async (e) => {
    const ok = await confirmDialog("حذف کاربر", `حساب ${u.account} از فهرست کاربران مجاز حذف شود؟ این کاربر دیگر نمی‌تواند وارد شود.`, { danger: true, ok: "حذف" });
    if (!ok) return;
    await busy(e.currentTarget, async () => { await api(`/users/${u.id}`, { method: "DELETE" }); toast("کاربر حذف شد.", "ok"); reload(); });
  } }, "حذف");

  return h("tr", {},
    h("td", {}, h("div", {}, u.display_name || "—"), ltr(u.account, "muted small")),
    h("td", {}, h("span", { class: "badge " + (u.enabled ? "ok" : "bad") }, u.enabled ? "فعال" : "مسدود")),
    h("td", { title: dateTime(u.last_login_at) }, ago(u.last_login_at)),
    h("td", {}, num(u.login_count)),
    h("td", { class: "muted small", style: "max-width:260px" }, u.note || "—"),
    h("td", { class: "actions" }, h("div", { class: "toolbar", style: "justify-content:flex-end;gap:6px" }, edit, toggle, remove)));
}

function noteDialog(u) {
  return modal("یادداشت کاربر", (close) => {
    const note = h("textarea", { class: "input", maxlength: "500", value: u.note });
    const save = h("button", { class: "btn primary", onclick: (e) => busy(e.currentTarget, async () => {
      await api(`/users/${u.id}`, { method: "PATCH", body: { note: note.value } });
      toast("ذخیره شد.", "ok");
      close(true);
    }) }, "ذخیره");
    return { body: [h("div", { class: "muted small", style: "margin-bottom:6px" }, ltr(u.account)), note],
             footer: [save, h("button", { class: "btn", onclick: () => close(false) }, "انصراف")] };
  });
}

function addDialog() {
  let added = false;
  return modal("افزودن کاربر از Active Directory", (close) => {
    const results = h("div", { class: "results hidden" });
    const hint = h("div", { class: "muted small", style: "margin-top:6px" }, "نام، نام کاربری یا ایمیل را تایپ کنید (دست‌کم ۲ نویسه).");
    const note = h("input", { class: "input", maxlength: "500", placeholder: "یادداشت (اختیاری) — مثلاً واحد یا دلیل دسترسی" });
    let seq = 0;

    const doSearch = debounce(async (q) => {
      const mine = ++seq;
      if (q.trim().length < 2) { results.classList.add("hidden"); return; }
      hint.textContent = "در حال جستجو…";
      try {
        const { results: found } = await api("/directory/search", { query: { q } });
        if (mine !== seq) return;
        hint.textContent = found.length ? `${num(found.length)} نتیجه` : "کاربری پیدا نشد.";
        clear(results, found.map(resultRow));
        results.classList.toggle("hidden", !found.length);
      } catch (e) {
        if (mine === seq) hint.textContent = e.message;
      }
    }, 350);

    function resultRow(r) {
      const btn = h("button", { class: "btn sm primary", disabled: r.added, onclick: async () => {
        btn.disabled = true;
        try {
          await api("/users", { method: "POST", body: { account: r.account, note: note.value } });
          added = true;
          r.added = true;
          btn.textContent = "اضافه شد";
          toast(`${r.display_name || r.account} اضافه شد.`, "ok");
        } catch (e) {
          btn.disabled = false;
          toast(e.message, "bad");
        }
      } }, r.added ? "در فهرست" : "افزودن");
      return h("div", { class: "result" },
        h("div", { class: "who" },
          h("div", {}, r.display_name || "—", r.ad_disabled ? [" ", h("span", { class: "badge bad" }, "در AD غیرفعال")] : null),
          h("div", { class: "muted small" }, ltr(r.account), r.mail ? [" · ", ltr(r.mail)] : null)),
        btn);
    }

    const input = h("input", { class: "input", placeholder: "جستجو در AD…", oninput: (e) => doSearch(e.target.value) });
    return {
      body: [input, hint, results, h("div", { style: "margin-top:12px" }, note)],
      footer: [h("button", { class: "btn", onclick: () => close(added) }, "بستن")],
    };
  }).then((v) => v || added);
}
