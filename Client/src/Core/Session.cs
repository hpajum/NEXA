using System;

namespace Nexa
{
    /// <summary>Everything the workspace needs to know about the signed-in account.</summary>
    internal sealed class SessionInfo
    {
        public bool Preview;
        public Uri Server;
        public string Account;
        public string StatePath;
        public string VaultPath;
        public string SavedLoginPath;
        public Workspace State;
    }

    public sealed class FileEntry
    {
        public string Path { get; private set; }
        public string Name { get; private set; }
        public FileEntry(string path)
        {
            Path = path;
            Name = System.IO.Path.GetFileName(path);
        }
        public override string ToString() { return Name; }
    }
}
