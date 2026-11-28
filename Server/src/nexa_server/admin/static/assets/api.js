// JSON client for /admin/api. Adds the CSRF header to mutations and turns errors into ApiError.

let csrf = "";
let onUnauthorized = () => {};

export class ApiError extends Error {
  constructor(status, message, fields = null) {
    super(message);
    this.status = status;
    this.fields = fields; // {settingKey: message} for 422 responses
  }
}

export function setCsrf(token) { csrf = token || ""; }
export function setUnauthorizedHandler(fn) { onUnauthorized = fn; }

const MESSAGES = {
  401: "نشست شما به پایان رسیده است. دوباره وارد شوید.",
  403: "اجازهٔ این کار را ندارید.",
  429: "تلاش‌های زیاد. کمی بعد دوباره امتحان کنید.",
  503: "سرویس در دسترس نیست.",
};

export async function api(path, { method = "GET", body, query } = {}) {
  let url = "/admin/api" + path;
  if (query) {
    const qs = new URLSearchParams(Object.entries(query).filter(([, v]) => v !== undefined && v !== ""));
    if ([...qs].length) url += "?" + qs;
  }
  const headers = { Accept: "application/json" };
  if (body !== undefined) headers["Content-Type"] = "application/json";
  if (method !== "GET") headers["X-CSRF-Token"] = csrf;

  let res;
  try {
    res = await fetch(url, { method, headers, body: body === undefined ? undefined : JSON.stringify(body), credentials: "same-origin" });
  } catch {
    throw new ApiError(0, "ارتباط با سرور برقرار نشد.");
  }
  let data = null;
  try { data = await res.json(); } catch { /* empty body */ }
  if (res.ok) return data;

  if (res.status === 401 && path !== "/login" && path !== "/me") onUnauthorized();
  const detail = data && data.detail;
  if (detail && typeof detail === "object" && detail.fields) {
    throw new ApiError(res.status, "برخی مقادیر نامعتبرند.", detail.fields);
  }
  const persian = typeof detail === "string" && /[؀-ۿ]/.test(detail) ? detail : null;
  throw new ApiError(res.status, persian || MESSAGES[res.status] || "خطای غیرمنتظره رخ داد.");
}
