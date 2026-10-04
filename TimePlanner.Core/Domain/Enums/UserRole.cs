using System;
using System.Collections.Generic;
using System.Text;

namespace TimePlanner.Core.Domain.Enums
{
    //-----------------------------
    //a developer sees and submits their own time, an admin manages accounts and sees everyone's. The dashboard enforces this per request.
    //the stored numbers are Developer 0 and Admin 1, a retired third role used to be 2 and is not reused
    public enum UserRole
    {
        Developer = 0,
        Admin = 1
    }
}
//------------------------------EOF-----------------------------\\