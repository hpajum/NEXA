using System.Collections.Generic;

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
}
