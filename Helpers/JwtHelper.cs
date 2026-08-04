using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IdentityModel.Tokens.Jwt;

namespace Raphael.Desktop.Helpers
{
    public static class JwtHelper
    {
        public static bool IsTokenExpired(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return true;

            var jwtToken = new JwtSecurityTokenHandler().ReadJwtToken(token);
            var exp = jwtToken.ValidTo;

            return exp < DateTime.UtcNow;
        }
    }
}
