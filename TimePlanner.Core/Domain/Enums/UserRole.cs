using System;
using System.Collections.Generic;
using System.Text;

namespace TimePlanner.Core.Domain.Enums
{
    //-----------------------------
    //role based access is planned for hosted implementations, but we do not yet enforce within the MVP, this enum is a placeholder
    public enum UserRole
    {
        Developer,
        Admin,
        Billing
    }
}
//------------------------------EOF-----------------------------\\