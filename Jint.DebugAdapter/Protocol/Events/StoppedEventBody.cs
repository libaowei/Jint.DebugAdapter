﻿using Jint.DebugAdapter.Protocol.Types;

namespace Jint.DebugAdapter.Protocol.Events
{
    internal class StoppedEventBody : ProtocolEventBody
    {
        public StopReason Reason { get; set; }
        public string Description { get; set; }
        public int ThreadId { get; set; }
        public bool? PreserveFocusHint { get; set; }
        public string Text { get; set; }
        public bool? AllThreadsStopped { get; set; }
        public List<int> HitBreakpointIds { get; set; }
    }
}
