using System.Security.Cryptography;

namespace TimePlanner.Dashboard.Security
{
    //-----------------------------
    //makes the temporary passwords an administrator hands out. They come from the operating system's secure random source and always meet the password
    //rules (upper and lower case, a digit and a symbol), look-alike characters such as 0 and O are left out so the password can be read out or typed
    public static class PasswordGenerator
    {
        private const string Lower = "abcdefghijkmnpqrstuvwxyz";
        private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        private const string Digits = "23456789";
        private const string Symbols = "!@#$%&*?+=";

        public const int Length = 16;

        public static string Create()
        {
            var all = Lower + Upper + Digits + Symbols;
            var chars = new List<char>
            {
                Pick(Lower), Pick(Upper), Pick(Digits), Pick(Symbols)
            };
            while (chars.Count < Length)
                chars.Add(Pick(all));

            //the guaranteed characters are always first, so shuffle
            for (var i = chars.Count - 1; i > 0; i--)
            {
                var j = RandomNumberGenerator.GetInt32(i + 1);
                (chars[i], chars[j]) = (chars[j], chars[i]);
            }
            return new string(chars.ToArray());
        }

        private static char Pick(string from) => from[RandomNumberGenerator.GetInt32(from.Length)];
    }
}
//------------------------------EOF-----------------------------\\
