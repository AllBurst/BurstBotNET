using BurstBotShared.Shared.Models.Data;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.Commands.Contexts;
using Remora.Results;

namespace BurstBotShared.Shared.Interfaces;

public interface IHelpInteraction
{
    static abstract Task<Result> ShowHelpMenu(InteractionContext context, State state,
        IDiscordRestInteractionAPI interactionApi);
}