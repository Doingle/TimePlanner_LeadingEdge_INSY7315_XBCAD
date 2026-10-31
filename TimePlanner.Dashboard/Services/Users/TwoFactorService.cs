using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Identity;
using TimePlanner.Dashboard.Data;

namespace TimePlanner.Dashboard.Services.Users
{
    //-----------------------------
    //what a person types into their authenticator app: the key to type in, and the same thing as an otpauth link that phones can open
    public record TwoFactorSetup(string Key, string Uri);

    //-----------------------------
    //the outcome of turning two-factor on. The recovery codes are only ever returned here, once, and are not shown again
    public record TwoFactorEnableResult(bool Success, string? Error, IReadOnlyList<string> RecoveryCodes)
    {
        public static TwoFactorEnableResult Fail(string error) => new(false, error, Array.Empty<string>());
    }

    //-----------------------------
    //authenticator app (time based one time password) sign in, kept on top of ASP.NET Identity's own token provider and recovery codes.
    //Nothing here stores anything new: the key and the recovery codes live in Identity's user token table and the on/off flag on the account
    public class TwoFactorService
    {
        public const string Issuer = "Time Planner";
        public const int RecoveryCodeCount = 10;

        private readonly UserManager<ApplicationUser> _users;
        private readonly AuditLogger _audit;

        public TwoFactorService(UserManager<ApplicationUser> users, AuditLogger audit)
        {
            _users = users;
            _audit = audit;
        }

        private string Provider => _users.Options.Tokens.AuthenticatorTokenProvider;

        //-----------------------------
        //digits only, the way an app shows them. People copy codes with spaces or a dash in them
        private static string CleanCode(string? code) => (code ?? string.Empty).Replace(" ", "").Replace("-", "");

        //-----------------------------
        //how many recovery codes are still unused
        public Task<int> RecoveryCodesLeftAsync(ApplicationUser user) => _users.CountRecoveryCodesAsync(user);

        //-----------------------------
        //the key and link to add this account to an authenticator app. Nothing is on until a code from the app has been checked.
        //returns null when two-factor is already on, so a running setup can never swap the key out from under it
        public async Task<TwoFactorSetup?> BeginSetupAsync(ApplicationUser user)
        {
            if (await _users.GetTwoFactorEnabledAsync(user))
                return null;

            var key = await _users.GetAuthenticatorKeyAsync(user);
            if (string.IsNullOrEmpty(key))
            {
                await _users.ResetAuthenticatorKeyAsync(user);
                key = await _users.GetAuthenticatorKeyAsync(user);
            }

            var label = UrlEncoder.Default.Encode($"{Issuer}:{user.Email}");
            var uri = $"otpauth://totp/{label}?secret={key}&issuer={UrlEncoder.Default.Encode(Issuer)}&digits=6";
            return new TwoFactorSetup(string.Join(" ", Chunk(key!.ToLowerInvariant(), 4)), uri);
        }

        private static IEnumerable<string> Chunk(string text, int size)
        {
            for (var i = 0; i < text.Length; i += size)
                yield return text.Substring(i, Math.Min(size, text.Length - i));
        }

        //-----------------------------
        //turns two-factor on once a code from the app proves the key was typed in correctly, and creates the recovery codes.
        //the security stamp changes, which signs the person out everywhere else
        public async Task<TwoFactorEnableResult> EnableAsync(ApplicationUser user, string? code)
        {
            if (await _users.GetTwoFactorEnabledAsync(user))
                return TwoFactorEnableResult.Fail("Two-factor sign in is already on.");
            if (string.IsNullOrEmpty(await _users.GetAuthenticatorKeyAsync(user)))
                return TwoFactorEnableResult.Fail("Start the setup again to get a new key.");
            if (!await _users.VerifyTwoFactorTokenAsync(user, Provider, CleanCode(code)))
                return TwoFactorEnableResult.Fail("That code is not right. Check the app and try the new code.");

            await _users.SetTwoFactorEnabledAsync(user, true);
            var codes = await _users.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount);
            await _audit.LogAsync(AuditActions.TwoFactorEnabled, user.Email);
            return new TwoFactorEnableResult(true, null, codes!.ToList());
        }

        //-----------------------------
        //checks the second step of a sign in: a six digit code from the app, or one of the recovery codes (each works once).
        //a wrong code counts towards the same lockout as a wrong password, and a right one clears it
        public async Task<bool> CheckLoginCodeAsync(ApplicationUser user, string? code, string via)
        {
            var clean = CleanCode(code);
            var valid = false;

            if (clean.Length == 6 && clean.All(char.IsAsciiDigit))
            {
                valid = await _users.VerifyTwoFactorTokenAsync(user, Provider, clean);
            }
            else if (clean.Length > 0 && (await _users.RedeemTwoFactorRecoveryCodeAsync(user, (code ?? string.Empty).Trim().Replace(" ", ""))).Succeeded)
            {
                valid = true;
                await _audit.LogAsync(AuditActions.RecoveryCodeUsed, via, user.Email, user.Id);
            }

            if (valid)
                await _users.ResetAccessFailedCountAsync(user);
            else
                await _users.AccessFailedAsync(user);
            return valid;
        }

        //-----------------------------
        //turns two-factor off. It needs the current password and a code, so a stolen session alone cannot remove the second step
        public async Task<OpResult> DisableAsync(ApplicationUser user, string? password, string? code)
        {
            if (string.IsNullOrEmpty(password) || string.IsNullOrWhiteSpace(code))
                return OpResult.Fail("Enter your password and a code to turn two-factor off.");
            if (await _users.IsLockedOutAsync(user))
                return OpResult.Fail("Too many wrong attempts, try again later.");
            if (!await _users.CheckPasswordAsync(user, password))
            {
                await _users.AccessFailedAsync(user);
                return OpResult.Fail("Your password is not correct.");
            }
            if (!await CheckLoginCodeAsync(user, code, "turning two-factor off"))
                return OpResult.Fail("That code is not right.");

            await ClearAsync(user);
            await _audit.LogAsync(AuditActions.TwoFactorDisabled, user.Email);
            return OpResult.Ok();
        }

        //-----------------------------
        //removes two-factor from an account completely: the switch, the key and every recovery code. Used when someone turns it off themselves
        //and when an admin resets it for a person who lost their phone. The security stamp changes, so open sessions and tokens end
        public async Task ClearAsync(ApplicationUser user)
        {
            await _users.SetTwoFactorEnabledAsync(user, false);
            await _users.ResetAuthenticatorKeyAsync(user);
            await _users.GenerateNewTwoFactorRecoveryCodesAsync(user, 0);
            await _users.UpdateSecurityStampAsync(user);
        }
    }
}
//------------------------------EOF-----------------------------\\
