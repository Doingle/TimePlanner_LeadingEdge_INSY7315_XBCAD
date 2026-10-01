using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using TimePlanner.Dashboard.Data;

namespace TimePlanner.Dashboard.Services
{
    //-----------------------------
    //issues the signed access tokens the widget and other api clients send as a bearer header
    public class JwtTokenService
    {
        private readonly SigningCredentials _credentials;
        private readonly string _issuer;
        private readonly string _audience;
        private readonly TimeSpan _lifetime;

        public JwtTokenService(IConfiguration config)
        {
            _credentials = new SigningCredentials(KeyFrom(config), SecurityAlgorithms.HmacSha256);
            _issuer = config["Jwt:Issuer"] ?? throw new InvalidOperationException("Jwt:Issuer is not configured.");
            _audience = config["Jwt:Audience"] ?? throw new InvalidOperationException("Jwt:Audience is not configured.");
            _lifetime = TimeSpan.FromMinutes(config.GetValue("Jwt:ExpiryMinutes", 30));
        }

        //-----------------------------
        //the same key builds the signing credentials here and the validation parameters in Program.cs, and refuses to start without a strong one
        public static SymmetricSecurityKey KeyFrom(IConfiguration config)
        {
            var key = config["Jwt:Key"];
            if (string.IsNullOrEmpty(key) || Encoding.UTF8.GetByteCount(key) < 32)
                throw new InvalidOperationException("Jwt:Key must be configured with at least 32 bytes (user-secrets locally, an environment variable when hosted).");
            return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        }

        //-----------------------------
        //builds a token carrying the user id, email and roles, valid for the configured lifetime
        public (string Token, DateTime ExpiresUtc) Create(ApplicationUser user, IEnumerable<string> roles)
        {
            var expires = DateTime.UtcNow.Add(_lifetime);
            var claims = new List<Claim>
            {
                new("sub", user.Id),
                new("email", user.Email ?? string.Empty),
                new("jti", Guid.NewGuid().ToString())
            };
            //uid links the login to the time tracking profile, so endpoints can scope data to its owner without a lookup
            if (user.AppUserId != null)
                claims.Add(new Claim("uid", user.AppUserId.Value.ToString()));
            claims.AddRange(roles.Select(r => new Claim("role", r)));

            var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Issuer = _issuer,
                Audience = _audience,
                Expires = expires,
                SigningCredentials = _credentials
            });
            return (token, expires);
        }
    }
}
//------------------------------EOF-----------------------------\\
