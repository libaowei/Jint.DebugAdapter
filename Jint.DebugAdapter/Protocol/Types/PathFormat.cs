﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Jint.DebugAdapter.Helpers;

namespace Jint.DebugAdapter.Protocol.Types
{
    /// <completionlist cref="PathFormat"/>
    public class PathFormat : StringEnum<PathFormat>
    {
        public static readonly PathFormat Path = Create("path");
        public static readonly PathFormat Uri = Create("uri");
    }
}
