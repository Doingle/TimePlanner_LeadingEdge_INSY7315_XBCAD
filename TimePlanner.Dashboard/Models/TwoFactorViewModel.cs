using System.ComponentModel.DataAnnotations;

namespace TimePlanner.Dashboard.Models
{
    //-----------------------------
    //the data posted by the second step of signing in: a code from the authenticator app, or a recovery code
    public class TwoFactorViewModel
    {
        [Required, StringLength(32)]
        public string Code { get; set; } = string.Empty;

        //true while the person is entering a recovery code instead of an app code
        public bool UseRecoveryCode { get; set; }

        //where to send the user after signing in, only ever followed if it is a local url
        public string? ReturnUrl { get; set; }
    }
}
//------------------------------EOF-----------------------------\\
