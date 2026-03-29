namespace AuthWithAdmin.Server.AuthHelpers
{
    public class TokenCleanupBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<TokenCleanupBackgroundService> _logger;

        public TokenCleanupBackgroundService(
            IServiceProvider serviceProvider,
            ILogger<TokenCleanupBackgroundService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Token cleanup service started");

            // Wait 1 minute before first cleanup (give server time to start)
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var blacklistService = scope.ServiceProvider
                        .GetRequiredService<ITokenBlacklistService>();

                    if (blacklistService is DbTokenBlacklistService dbService)
                    {
                        dbService.RemoveExpiredTokens();
                    }

                    // Wait 24 hours until next cleanup
                    await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during token cleanup");
                    // Wait 1 hour before retrying on error
                    await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
                }
            }

            _logger.LogInformation("Token cleanup service stopped");
        }
    }
}