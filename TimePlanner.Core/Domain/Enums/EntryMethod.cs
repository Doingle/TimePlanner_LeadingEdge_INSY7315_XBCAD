using System;
using System.Collections.Generic;
using System.Text;

namespace TimePlanner.Core.Domain.Enums
{
    //-----------------------------
    //this enum describes how a time entry was captured either automatically, prompted automatically or manually input by the user
    public enum EntryMethod
    {
        Manual,
        AutoPrompted,
        AutoTracked
    }
}
//------------------------------EOF-----------------------------\\