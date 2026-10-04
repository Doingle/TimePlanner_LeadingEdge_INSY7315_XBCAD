using System.ComponentModel.DataAnnotations;

namespace TimePlanner.Dashboard.Models
{
    //-----------------------------
    //the data posted by the login form
    public class LoginViewModel
    {
        [Required, EmailAddress, StringLength(256)]
        public string Email { get; set; } = string.Empty;

        [Required, DataType(DataType.Password), StringLength(128)]
        public string Password { get; set; } = string.Empty;

        //where to send the user after login, only ever followed if it is a local url
        public string? ReturnUrl { get; set; }
    }
}
//------------------------------EOF-----------------------------\\
