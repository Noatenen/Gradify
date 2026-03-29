using System.Security.Claims;
using System.Text.Json;

namespace AuthWithAdmin.Client
{
    //לא לגעת
    public static class JwtParser
    {
        public static IEnumerable<Claim> ParseClaimsFromJwt(string jwt)
        {
            var claims = new List<Claim>(); 
            var payload = jwt.Split('.');
            if (payload.Length<=1)
                return claims;
         
            // Decode the payload
            var jsonBytes = ParseBase64WithoutPadding(payload[1]);

            // Deserialize the payload into a dictionary
            var keyValuePairs = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonBytes);

            foreach (var kvp in keyValuePairs)
            {
                if (kvp.Key == ClaimTypes.Role)
                {
                    // Handle the roles claim which is an array
                    if (kvp.Value is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var role in jsonElement.EnumerateArray())
                        {
                            claims.Add(new Claim(ClaimTypes.Role, role.GetString()));
                        }
                    }
                    else
                    {
                        claims.Add(new Claim(kvp.Key, kvp.Value.ToString()));
                    }
                }
                else
                {
                    // Add other claims normally
                    claims.Add(new Claim(kvp.Key, kvp.Value.ToString()));

                }
            }

            return claims;
        }
        private static byte[] ParseBase64WithoutPadding(string base64)
        {
            switch (base64.Length % 4)
            {
                case 2: base64 += "=="; break;
                case 3: base64 += "="; break;
            }
            return Convert.FromBase64String(base64);
        }
    }

}
