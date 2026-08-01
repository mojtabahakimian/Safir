using System;

namespace Safir.Server.Security
{
    [Flags]
    public enum Pay2Perm
    {
        None = 0,
        Run = 1,
        See = 2,
        Inp = 4,
        Upd = 8,
        Del = 16
    }
}
