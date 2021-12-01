using BurstBotNET.Shared.Models.Config;
using BurstBotNET.Shared.Models.Data;
using Flurl.Http;

namespace BurstBotNET.Api;

public class BurstApi
{
    private readonly string _serverEndpoint;
    private readonly int _socketPort;
    private readonly string _socketEndpoint;

    public BurstApi(Config config)
    {
        _serverEndpoint = config.ServerEndpoint;
        _socketEndpoint = config.SocketEndpoint;
        _socketPort = config.SocketPort;
    }

    public async Task<TResponseType> SendRawRequest<TResponseType, TPayloadType>(string endpoint, ApiRequestType requestType, TPayloadType? payload)
    {
        var url = _serverEndpoint + endpoint;
        return requestType switch
        {
            ApiRequestType.Get => await url.GetJsonAsync<TResponseType>(),
            ApiRequestType.Post => await Post<TResponseType, TPayloadType>(url, payload),
            _ => throw new InvalidOperationException("Unsupported raw request type.")
        };
    }

    public async Task<IFlurlResponse> JoinGame<TPayloadType>(string endpoint, TPayloadType payload)
        => await (_serverEndpoint + endpoint).PostJsonAsync(payload);

    public async Task<IFlurlResponse> GetReward(PlayerRewardType rewardType, long playerId)
    {
        var endpoint = rewardType switch
        {
            PlayerRewardType.Daily => $"{_serverEndpoint}/player/{playerId}/daily",
            PlayerRewardType.Weekly => $"{_serverEndpoint}/player/{playerId}/weekly",
            _ => throw new ArgumentException("Incorrect reward type.")
        };

        return await endpoint.GetAsync();
    }

    private static async Task<TResponseType> Post<TResponseType, TPayloadType>(string url, TPayloadType? payload)
    {
        if (payload == null)
        {
            throw new ArgumentException("The payload cannot be null when sending POST requests.");
        }

        var response = await url.PostJsonAsync(payload);
        return await response.GetJsonAsync<TResponseType>();
    }
}