using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Nexa
{
    internal static class CompanyLogin
    {
        private const int MaxIdentityBytes = 8192;

        public static Uri Server()
        {
            using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\NEXA"))
            {
                string value = key == null ? null : key.GetValue("ServerUrl") as string;
                Uri uri;
                if (!Uri.TryCreate(value, UriKind.Absolute, out uri) || uri.Scheme != Uri.UriSchemeHttps ||
                    uri.HostNameType != UriHostNameType.Dns || uri.IsLoopback ||
                    uri.Host.IndexOf('.') < 0 || uri.UserInfo.Length != 0 ||
                    uri.Query.Length != 0 || uri.Fragment.Length != 0)
                    return null;
                return uri;
            }
        }

        public static async Task<string> Authenticate(Uri server, string username, string password, CancellationToken cancellation)
        {
            if (server == null)
                throw new LoginException("آدرس سرور سازمان هنوز توسط مدیر سیستم تنظیم نشده یا معتبر نیست.");

            var endpoint = new Uri(server.AbsoluteUri.TrimEnd('/') + "/api/v1/session");
            byte[] body = PasswordBody(username, password);
            try
            {
                using (var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
                using (var client = new HttpClient(handler))
                using (var request = new HttpRequestMessage(HttpMethod.Post, endpoint))
                {
                    client.Timeout = TimeSpan.FromSeconds(12);
                    client.MaxResponseContentBufferSize = MaxIdentityBytes;
                    request.Headers.Accept.ParseAdd("application/json");
                    request.Headers.CacheControl = new CacheControlHeaderValue { NoStore = true };
                    request.Content = new ByteArrayContent(body);
                    request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
                    using (var response = await client.SendAsync(request, cancellation))
                        return await ReadIdentity(response);
                }
            }
            finally { Array.Clear(body, 0, body.Length); }
        }

        private static byte[] PasswordBody(string username, string password)
        {
            using (var stream = new MemoryStream())
            {
                try
                {
                    new DataContractJsonSerializer(typeof(PasswordRequest)).WriteObject(stream,
                        new PasswordRequest { Username = username, Password = password });
                    return stream.ToArray();
                }
                finally { Array.Clear(stream.GetBuffer(), 0, (int)stream.Length); }
            }
        }

        private static async Task<string> ReadIdentity(HttpResponseMessage response)
        {
            if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                throw new LoginException("نام کاربری و رمز را بررسی کنید. سرور ورود این حساب را نپذیرفت.");
            if ((int)response.StatusCode == 429)
                throw new LoginException("تعداد تلاش‌ها زیاد است. کمی بعد دوباره تلاش کنید.");
            if (response.StatusCode != HttpStatusCode.OK || response.Content == null ||
                response.Content.Headers.ContentType == null || response.Content.Headers.ContentType.MediaType != "application/json")
                throw new LoginException("پاسخ سرور با قرارداد ورود NEXA مطابقت ندارد.");

            using (Stream stream = await response.Content.ReadAsStreamAsync())
            {
                var serializer = new DataContractJsonSerializer(typeof(CompanyIdentity));
                var identity = (CompanyIdentity)serializer.ReadObject(stream);
                if (identity == null || string.IsNullOrWhiteSpace(identity.Account) || identity.Account.Length > 256 ||
                    identity.Account != identity.Account.Trim())
                    throw new LoginException("سرور شناسهٔ معتبر حساب را برنگرداند.");
                return identity.Account;
            }
        }
    }

    [DataContract]
    internal sealed class PasswordRequest
    {
        [DataMember(Name = "username")]
        public string Username { get; set; }

        [DataMember(Name = "password")]
        public string Password { get; set; }
    }

    [DataContract]
    internal sealed class CompanyIdentity
    {
        [DataMember(Name = "account")]
        public string Account { get; set; }
    }

    internal sealed class LoginException : Exception
    {
        public LoginException(string message) : base(message) { }
    }
}
