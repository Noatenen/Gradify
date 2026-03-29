namespace AuthWithAdmin.Server.AuthHelpers;

public interface ITokenBlacklistService
{
    void AddToBlacklist(string token);
    bool IsBlacklisted(string token);
    void RemoveExpiredTokens();
}