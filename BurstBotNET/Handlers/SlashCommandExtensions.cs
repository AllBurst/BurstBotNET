using BurstBotNET.Commands;
using BurstBotNET.Commands.BlackJack;
using BurstBotNET.Commands.ChinesePoker;
using BurstBotNET.Commands.NinetyNine;
using BurstBotNET.Commands.OldMaid;
using BurstBotNET.Commands.RedDotsPicking;
using BurstBotNET.Commands.Rewards;
using BurstBotShared.Shared.Models.Game.BlackJack;
using BurstBotShared.Shared.Models.Game.ChinesePoker;
using BurstBotShared.Shared.Models.Game.OldMaid;
using BurstBotShared.Shared.Models.Game.RedDotsPicking;
using Microsoft.Extensions.DependencyInjection;
using Remora.Commands.Extensions;
using Remora.Discord.Interactivity.Extensions;
using Remora.Discord.Pagination.Extensions;

namespace BurstBotNET.Handlers;

#pragma warning disable CA2252
public static class SlashCommandExtensions
{
    public static IServiceCollection AddSlashCommands(this IServiceCollection serviceCollection)
    {
        return serviceCollection
            .AddCommandGroup<About>()
            .AddCommandGroup<Balance>()
            .AddCommandGroup<Daily>()
            .AddCommandGroup<Ping>()
            .AddCommandGroup<Start>()
            .AddCommandGroup<Weekly>()
            .AddCommandGroup<BlackJack>()
            .AddCommandGroup<ChinesePoker>()
            .AddCommandGroup<NinetyNine>()
            .AddCommandGroup<OldMaid>()
            .AddCommandGroup<RedDotsPicking>()
            .AddPagination()
            .AddInteractiveEntity<BlackJackDropDownEntity>()
            .AddInteractiveEntity<BlackJackButtonEntity>()
            .AddInteractiveEntity<ChinesePokerDropDownEntity>()
            .AddInteractiveEntity<ChinesePokerButtonEntity>()
            .AddInteractiveEntity<OldMaidButtonEntity>()
            .AddInteractiveEntity<OldMaidDropDownEntity>()
            .AddInteractiveEntity<RedDotsDropDownEntity>();
    }
}