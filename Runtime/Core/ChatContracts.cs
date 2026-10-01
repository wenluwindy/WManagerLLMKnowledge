using System;
using System.Collections.Generic;

namespace WManager.Knowledge
{
    public enum ChatRole { User, Assistant }

    [Serializable]
    public sealed class ChatMessage
    {
        public ChatRole Role;
        public string Text;
        public string CreatedUtc;
    }

    public sealed class ChatReply
    {
        public string Text;
        public int PromptTokens;
        public int DroppedTurns;
        public long FirstTokenMilliseconds = -1;
        public long ElapsedMilliseconds;
        public IReadOnlyList<ChatMessage> History;
    }
}
