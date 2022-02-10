using BurstBotNET.Commands;
using BurstBotNET.Commands.BlackJack;
using BurstBotNET.Commands.ChaseThePig;
using BurstBotNET.Commands.ChinesePoker;
using BurstBotNET.Commands.Help;
using BurstBotNET.Commands.NinetyNine;
using BurstBotNET.Commands.OldMaid;
using BurstBotNET.Commands.RedDotsPicking;
using BurstBotNET.Commands.Rewards;
using BurstBotNET.Commands.Trade;
using BurstBotShared.Shared.Models.Game.BlackJack;
using BurstBotShared.Shared.Models.Game.ChaseThePig;
using BurstBotShared.Shared.Models.Game.ChinesePoker;
using BurstBotShared.Shared.Models.Game.OldMaid;
using BurstBotShared.Shared.Models.Game.NinetyNine;
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
        serviceCollection.AddCommandTree()
            .WithCommandGroup<About>()
            .WithCommandGroup<Balance>()
            .WithCommandGroup<Daily>()
            .WithCommandGroup<Ping>()
            .WithCommandGroup<Start>()
            .WithCommandGroup<Weekly>()
            .WithCommandGroup<Help>()
            .WithCommandGroup<ContextCommands>()
            .WithCommandGroup<Trade>()
            .WithCommandGroup<BlackJack>()
            .WithCommandGroup<ChinesePoker>()
            .WithCommandGroup<NinetyNine>()
            .WithCommandGroup<OldMaid>()
            .WithCommandGroup<RedDotsPicking>()
            .WithCommandGroup<ChaseThePig>();
        
        return serviceCollection
            .AddPagination()
            .AddInteractiveEntity<BlackJackDropDownEntity>()
            .AddInteractiveEntity<BlackJackButtonEntity>()
            .AddInteractiveEntity<ChinesePokerDropDownEntity>()
            .AddInteractiveEntity<ChinesePokerButtonEntity>()
            .AddInteractiveEntity<OldMaidButtonEntity>()
            .AddInteractiveEntity<OldMaidDropDownEntity>()
            .AddInteractiveEntity<RedDotsDropDownEntity>()
            .AddInteractiveEntity<RedDotsButtonEntity>()
            .AddInteractiveEntity<ChasePigDropDownEntity>()
            .AddInteractiveEntity<ChasePigButtonEntity>()
            .AddInteractiveEntity<NinetyNineDropDownEntity>()
            .AddInteractiveEntity<NinetyNineButtonEntity>();
    }
}