namespace TimePlanner.Dashboard.Data
{
    //-----------------------------
    //a long lived api credential that is swapped for a new access token without typing the password again.
    //only a hash is stored, so a copy of the database cannot be used to sign in. Every use replaces the token with a new one in the same family,
    //so a token that is used a second time proves it was copied and the whole family is ended
    public class RefreshToken
    {
        public int Id { get; set; }

        public string UserId { get; set; } = string.Empty;

        //SHA-256 of the token the client holds, in base 64
        public string TokenHash { get; set; } = string.Empty;

        //every token handed out from one password login shares a family, which is what gets ended on theft or sign out
        public string FamilyId { get; set; } = string.Empty;
        public DateTime FamilyStartedUtc { get; set; }

        public DateTime CreatedUtc { get; set; }
        public DateTime ExpiresUtc { get; set; }

        //set when the token has been swapped for the next one
        public DateTime? UsedUtc { get; set; }

        //set when the family was ended by sign out, theft detection or the account changing
        public DateTime? RevokedUtc { get; set; }

        //the account's security stamp when this was issued, a password change or deactivation makes it stale
        public string Stamp { get; set; } = string.Empty;
    }
}
//------------------------------EOF-----------------------------\
