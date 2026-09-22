namespace MonsterSupergroup.NetworkCombat
{
    public sealed class ConnectionNoticeState
    {
        public uint Attempt { get; private set; }
        public bool Connected { get; private set; }
        public bool Intentional { get; private set; }
        public string Message { get; private set; }
        public int Priority { get; private set; }
        public void Begin() { Attempt++; Connected = Intentional = false; Message = null; Priority = 0; }
        public void MarkConnected() => Connected = true;
        public void LeaveLocally() { Intentional = true; Message = null; Priority = 0; }
        public bool Set(uint attempt, string message, int priority)
        {
            if (attempt != Attempt || Intentional || string.IsNullOrEmpty(message) || priority < Priority || (priority == Priority && Message != null)) return false;
            Message = message; Priority = priority; return true;
        }
    }
}
