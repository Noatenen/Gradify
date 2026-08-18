using System.Security.Claims;
using System.Text.Json;

namespace AuthWithAdmin.Client
{
    //לא לגעת
    //
    // EXCEPTION TO THE ABOVE, AND THE ONLY ONE: ParseBase64WithoutPadding used
    // to call Convert.FromBase64String on a JWT segment. JWT segments are
    // Base64URL (RFC 7515 §2), whose alphabet uses '-' and '_' where standard
    // Base64 uses '+' and '/'. Convert.FromBase64String rejects both characters
    // with FormatException, so a perfectly valid server-issued token could fail
    // to parse purely because of which bytes its payload happened to encode to.
    //
    // The blast radius was the whole application, not one screen:
    // AuthStateProvider.GetAuthenticationStateAsync parses the token on EVERY
    // boot, so the exception escaped before any component rendered and left the
    // app on its "Loading..." shell forever — with no error, because nothing had
    // rendered that could show one. Worse, the refresh path made it sticky: a
    // token whose parse failed is reported expired, refresh succeeds, and the
    // FRESH token is then parsed by the same broken decoder and throws again.
    //
    // Only the decoding is corrected. Signing, the key, signature validation
    // (which happens server-side in TokenService/AuthCheck and never here),
    // claim names, roles and lifetimes are all untouched — this class only ever
    // read a token the server had already issued.
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

            // A payload of literal "null" deserializes to null rather than
            // throwing. Without this the foreach below is a NullReferenceException
            // on the boot path — the same permanent "Loading..." as before.
            if (keyValuePairs is null)
                return claims;

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
        /// <summary>
        /// Decodes one Base64URL JWT segment.
        ///
        /// <para>The two substitutions are the whole difference between the JWT
        /// alphabet and the standard one. Translating rather than decoding with a
        /// Base64URL-only reader is deliberate: '+' and '/' are left alone, so a
        /// segment that happens to be plain Base64 still decodes, and no token
        /// already sitting in a user's localStorage can be rejected by this
        /// change.</para>
        ///
        /// <para>Padding is restored after the substitution, on the translated
        /// length. A remainder of 1 is not a valid Base64 length at all — it is
        /// left to fail rather than being padded into something that decodes to
        /// the wrong bytes.</para>
        /// </summary>
        private static byte[] ParseBase64WithoutPadding(string base64)
        {
            base64 = base64.Replace('-', '+').Replace('_', '/');

            switch (base64.Length % 4)
            {
                case 2: base64 += "=="; break;
                case 3: base64 += "="; break;
            }
            return Convert.FromBase64String(base64);
        }
    }

}
