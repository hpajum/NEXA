using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace Nexa
{
    public sealed class Workspace
    {
        public List<Project> Projects { get; set; }
        public Workspace() { Projects = new List<Project>(); }
    }

    public sealed class Project
    {
        public string Name { get; set; }
        public string Folder { get; set; }
        public bool CanWrite { get; set; }
        public List<Conversation> Chats { get; set; }
        public Project() { Chats = new List<Conversation>(); }
        public override string ToString() { return Name; }
    }

    public sealed class Conversation
    {
        public string Name { get; set; }
        public List<Message> Messages { get; set; }
        public Conversation() { Messages = new List<Message>(); }
        public override string ToString() { return Name; }
    }

    public sealed class Message
    {
        public string Text { get; set; }
        public string[] Files { get; set; }
    }

    internal static class LocalState
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunName = "NEXA";
        public static string AccountDirectory(Uri server, string account)
        {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NEXA");
            if (server == null) return Path.Combine(root, "Preview");
            string value = server.AbsoluteUri.TrimEnd('/') + "\n" + (account ?? "").ToUpperInvariant();
            using (var sha = SHA256.Create())
            {
                string key = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "");
                return Path.Combine(root, "Data", key);
            }
        }

        public static T Load<T>(string path) where T : class, new()
        {
            byte[] encrypted;
            try { encrypted = File.ReadAllBytes(path); }
            catch (FileNotFoundException) { return new T(); }
            catch (DirectoryNotFoundException) { return new T(); }

            byte[] userEnvelope = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.LocalMachine);
            byte[] plain = ProtectedData.Unprotect(userEnvelope, null, DataProtectionScope.CurrentUser);
            try
            {
                using (var stream = new MemoryStream(plain))
                {
                    var state = (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
                    if (state == null)
                        throw new System.Runtime.Serialization.SerializationException("Invalid stored data.");
                    return state;
                }
            }
            finally { Array.Clear(plain, 0, plain.Length); }
        }

        public static void Save<T>(string path, T state)
        {
            byte[] plain;
            using (var stream = new MemoryStream())
            {
                try
                {
                    new DataContractJsonSerializer(typeof(T)).WriteObject(stream, state);
                    plain = stream.ToArray();
                }
                finally { Array.Clear(stream.GetBuffer(), 0, (int)stream.Length); }
            }

            byte[] userEnvelope;
            try { userEnvelope = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser); }
            finally { Array.Clear(plain, 0, plain.Length); }
            byte[] encrypted = ProtectedData.Protect(userEnvelope, null, DataProtectionScope.LocalMachine);

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string pending = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(encrypted, 0, encrypted.Length);
                    stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(pending, path, null);
                else File.Move(pending, path);
            }
            finally
            {
                if (File.Exists(pending))
                {
                    try { File.Delete(pending); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }

        public static bool StartsWithWindows()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKey))
                return key != null && key.GetValue(RunName) is string;
        }

        public static void SetStartsWithWindows(bool enabled)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (enabled)
                    key.SetValue(RunName, "\"" + System.Windows.Forms.Application.ExecutablePath + "\" --tray");
                else
                    key.DeleteValue(RunName, false);
            }
        }
    }
}
