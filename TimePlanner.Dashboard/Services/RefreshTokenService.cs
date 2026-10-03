using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TimePlanner.Dashboard.Data;

namespace TimePlanner.Dashboard.Services
{
    //-----------------------------
    //issues and swaps refresh tokens, so the widget can stay signed in for weeks without storing the password.
    //a token works once: using it hands back the next one. Seeing a used token again means a copy exists, so the whole family is ended.
    //the account's security stamp is stored with each token, so a password change or deactivation ends every token at once.
    //ponytail: two refreshes sent at the same moment with the same token count as theft and end the family, the client signs in again. Add a short grace window if that proves common
    public class RefreshTokenService
    {
        public enum Outcome { Ok, Invalid }

        public record Rotated(Outcome Outcome, ApplicationUser? User, string? Token, DateTime? ExpiresUtc, string? Reason);

        private readonly AuthDbContext _db;
        private readonly UserManager<ApplicationUser> _users;
        private readonly TimeSpan _lifetime;
        private readonly TimeSpan _maxAge;

        public RefreshTokenService(AuthDbContext db, UserManager<ApplicationUser> users, IConfiguration config)
        {
            _db = db;
            _users = users;
            _lifetime = TimeSpan.FromDays(config.GetValue("Jwt:RefreshDays", 30));
            _maxAge = TimeSpan.FromDays(config.GetValue("Jwt:RefreshMaxDays", 90));
        }

        //-----------------------------
        //starts a new family after a password login
        public async Task<(string Token, DateTime ExpiresUtc)> IssueAsync(ApplicationUser user)
        {
            var now = DateTime.UtcNow;
            //tokens that expired a day ago can no longer be used or recognised as stolen, so they are cleared out
            await _db.RefreshTokens.Where(t => t.UserId == user.Id && t.ExpiresUtc < now.AddDays(-1)).ExecuteDeleteAsync();
            return await AddAsync(user, Guid.NewGuid().ToString("N"), now, now);
        }

        //-----------------------------
        //swaps a refresh token for the next one, or says why it cannot be used. Every failure looks the same to the caller
        public async Task<Rotated> RotateAsync(string token)
        {
            var now = DateTime.UtcNow;
            var hash = Hash(token);
            var row = await _db.RefreshTokens.AsNoTracking().FirstOrDefaultAsync(t => t.TokenHash == hash);
            if (row == null)
                return Fail("unknown token");
            if (row.RevokedUtc != null)
                return Fail("token was ended");
            if (row.UsedUtc != null)
                return await ReuseAsync(row);
            if (row.ExpiresUtc <= now || row.FamilyStartedUtc.Add(_maxAge) <= now)
                return Fail("token expired");

            var user = await _users.FindByIdAsync(row.UserId);
            if (user == null || !user.IsActive || user.MustChangePassword || user.SecurityStamp != row.Stamp)
            {
                await RevokeFamilyAsync(row.FamilyId);
                return Fail("account changed");
            }

            //marking it used is one conditional update, so two requests carrying the same token cannot both succeed
            var claimed = await _db.RefreshTokens.Where(t => t.Id == row.Id && t.UsedUtc == null).ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedUtc, now));
            if (claimed == 0)
                return await ReuseAsync(row);

            var (next, expires) = await AddAsync(user, row.FamilyId, row.FamilyStartedUtc, now);
            return new Rotated(Outcome.Ok, user, next, expires, null);
        }

        //-----------------------------
        //ends the family a token belongs to, used by sign out. Returns the account it belonged to, or null when the token is not known
        public async Task<string?> RevokeAsync(string token)
        {
            var hash = Hash(token);
            var row = await _db.RefreshTokens.AsNoTracking().FirstOrDefaultAsync(t => t.TokenHash == hash);
            if (row == null)
                return null;
            await RevokeFamilyAsync(row.FamilyId);
            return row.UserId;
        }

        private async Task<Rotated> ReuseAsync(RefreshToken row)
        {
            await RevokeFamilyAsync(row.FamilyId);
            return new Rotated(Outcome.Invalid, null, null, null, "reuse");
        }

        private static Rotated Fail(string reason) => new(Outcome.Invalid, null, null, null, reason);

        private Task RevokeFamilyAsync(string familyId)
        {
            var now = DateTime.UtcNow;
            return _db.RefreshTokens.Where(t => t.FamilyId == familyId && t.RevokedUtc == null).ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedUtc, now));
        }

        //a token never lives past the end of its family's maximum age, even when it is swapped just before that
        private async Task<(string Token, DateTime ExpiresUtc)> AddAsync(ApplicationUser user, string familyId, DateTime familyStarted, DateTime now)
        {
            var token = Base64Url(RandomNumberGenerator.GetBytes(32));
            var expires = now.Add(_lifetime);
            var limit = familyStarted.Add(_maxAge);
            if (expires > limit)
                expires = limit;

            _db.RefreshTokens.Add(new RefreshToken
            {
                UserId = user.Id,
                TokenHash = Hash(token),
                FamilyId = familyId,
                FamilyStartedUtc = familyStarted,
                CreatedUtc = now,
                ExpiresUtc = expires,
                Stamp = user.SecurityStamp ?? string.Empty
            });
            await _db.SaveChangesAsync();
            return (token, expires);
        }

        private static string Hash(string token) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

        private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
//------------------------------EOF-----------------------------\\
