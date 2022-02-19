using BurstBotShared.Shared.Models.Config;
using Flurl.Http;
using Microsoft.Extensions.Logging;

namespace BurstBotShared.Services;

public class AuthenticationService
{
    public string Token { get; private set; } = string.Empty;

    private readonly string _authEndpoint;
    private readonly string _authUsername;
    private readonly string _authPassword;
    private readonly ILogger<AuthenticationService> _logger;

    private DateTime _expiry = DateTime.Now;

    public AuthenticationService(Config config)
    {
        _authEndpoint = $"{config.LotteryEndpoint}/login";
        _authUsername = config.LotteryUsername;
        _authPassword = config.LotteryPassword;

        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole().SetMinimumLevel(LogLevel.Debug);
        });
        _logger = loggerFactory.CreateLogger<AuthenticationService>();
    }

    public async Task Login()
    {
        try
        {
            if (_expiry > DateTime.Now) return;

            var response = await _authEndpoint.PostJsonAsync(new
            {
                UserName = _authUsername,
                Password = _authPassword
            });
            
            var payload = await response.GetJsonAsync<Dictionary<string, dynamic>>();
            if (payload["token"] is string token)
            {
                Token = token;
            }

            if (payload["expiry"] is string expiry)
            {
                var parsedExpiry = DateTime.Parse(expiry);
                _expiry = parsedExpiry;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to authenticate with server: {Exception}", ex);
        }
        
    }
}