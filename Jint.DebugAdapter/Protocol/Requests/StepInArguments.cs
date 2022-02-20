﻿using Jint.DebugAdapter.Protocol.Types;

namespace Jint.DebugAdapter.Protocol.Requests
{
    internal class StepInArguments : ProtocolArguments
    {
        public int ThreadId { get; set; }
        public bool? SingleThread { get; set; }
        public int? TargetId { get; set; }
        public SteppingGranularity? Granularity { get; set; }
    }
}
