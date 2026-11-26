using System;
using System.Collections.Generic;

namespace Nexa
{
    public sealed class SavedLogin
    {
        public string Server { get; set; }
        public string Account { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }

        internal bool IsForServer(Uri server)
        {
            return server != null && String.Equals(Server, server.AbsoluteUri.TrimEnd('/'), StringComparison.Ordinal) &&
                !String.IsNullOrWhiteSpace(Account) && !String.IsNullOrWhiteSpace(Username) &&
                !String.IsNullOrEmpty(Password);
        }
    }

    public sealed class CredentialVault
    {
        public List<WebCredential> Entries { get; set; }
        public CredentialVault() { Entries = new List<WebCredential>(); }
    }

    public sealed class WebCredential
    {
        public string Site { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }

        public override string ToString() { return Site + "  —  " + Username; }

        internal static Uri SiteUrl(string input)
        {
            Uri uri;
            return Uri.TryCreate(input.Trim(), UriKind.Absolute, out uri) &&
                uri.Scheme == Uri.UriSchemeHttps && !String.IsNullOrEmpty(uri.Host) &&
                uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0 ? uri : null;
        }
    }
}
