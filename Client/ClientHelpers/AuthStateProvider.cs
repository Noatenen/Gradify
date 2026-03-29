using Microsoft.AspNetCore.Components.Authorization;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using AuthWithAdmin.Shared.AuthSharedModels;
using AuthWithAdmin.Client.ClientHelpers;
using Microsoft.AspNetCore.Components;

namespace AuthWithAdmin.Client
{
    public class AuthStateProvider : AuthenticationStateProvider
    {
        private readonly HttpClient _httpClient;
        private readonly ILocalStorageService _localStorage;
        private readonly AuthenticationState _anonymous;
        private readonly NavigationManager _navigation;
        public AuthStateProvider(HttpClient httpClient, ILocalStorageService localStorage,NavigationManager navigation)
        {
            _httpClient = httpClient;
            _localStorage = localStorage;
            _anonymous = new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
            _navigation = navigation;
        }

        //בדיקה האם יש משתמש מחובר
        public override async Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            var token = await _localStorage.GetItemAsync($"authToken_{GetProjectScope()}");


            if (string.IsNullOrWhiteSpace(token))
            {
                return _anonymous;
            }

            if (IsTokenExpired(token))
            {
                //ניסיון יצירת טוקן חדש - אם עומד לפוג
                var refreshResponse = await _httpClient.GetAsync("api/users/refresh");
                if (refreshResponse.IsSuccessStatusCode)
                {
                    var newToken = await refreshResponse.Content.ReadAsStringAsync();

                    await _localStorage.SetItemAsync($"authToken_{GetProjectScope()}", newToken);
                    token = newToken;
                }
                else
                {
                    return _anonymous; // אם לא הצליח לרענן - פג התוקף
                }
            }
            _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(JwtParser.ParseClaimsFromJwt(token), "jwtAuthType")));
        }

        //הודעה שהשתנתה ההתחברות
        public async Task NotifyAuthenticationStateChanged(string token)
        {
            await _localStorage.SetItemAsync($"authToken_{GetProjectScope()}", token);

            var authState = Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(JwtParser.ParseClaimsFromJwt(token), "jwtAuthType"))));
            NotifyAuthenticationStateChanged(authState);
        }


     

        //התנתקות
        public async Task NotifyUserLogout()
        {
            await _localStorage.RemoveItemAsync($"authToken_{GetProjectScope()}");
            _httpClient.DefaultRequestHeaders.Authorization = null;

            var authState = Task.FromResult(_anonymous);
            NotifyAuthenticationStateChanged(authState);
        }

        //האם הטוקן פג תוקף
        private bool IsTokenExpired(string token)
        {

            IEnumerable<Claim> claims;
            try
            {
                claims = JwtParser.ParseClaimsFromJwt(token);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                return true;
            }
            
            if (claims == null || claims.Count() == 0)
                return true;
            var expiry = claims.FirstOrDefault(c => c.Type == "exp")?.Value;

            if (expiry != null && long.TryParse(expiry, out long exp))
            {
                var expDate = DateTimeOffset.FromUnixTimeSeconds(exp).DateTime;
                // האם עומד לפוג בקרוב
                return expDate < DateTime.UtcNow.AddMinutes(10);
            }

            return true; // If there's no expiration claim, consider the token expired
        }
        
        public string GetProjectScope()
        {
            var basePath = new Uri(_navigation.BaseUri).AbsolutePath.Trim('/');
            Console.WriteLine($"basePath: {basePath}");
            return basePath.Replace('/', '_'); // למשל: "proj1_website1"
        }


    }
}
