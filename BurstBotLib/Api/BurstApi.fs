namespace BurstBotLib.Api

open System
open BurstBotShared.Api
open BurstBotShared.Shared.Models.Config
open BurstBotShared.Shared.Models.Data.Serializables
open BurstBotShared.Shared.Models.Game.Serializables
open DSharpPlus.Entities
open DSharpPlus.EventArgs
open Microsoft.Extensions.Logging
open Flurl.Http

module BurstApi =
    let WaitForChinesePokerGame (waitingData: GenericJoinStatus) (e: InteractionCreateEventArgs) (invokingMember: DiscordMember) (botUser: DiscordUser) description (burstApi: BurstApi) (config: Config) (logger: ILogger) = task {
        let! matchData = burstApi.GenericWaitForGame(waitingData, e, invokingMember, botUser, "Chinese Poker", description, logger)
        if matchData = null then
            return None
        else
            let guild = e.Interaction.Guild
            let! textChannel = burstApi.CreatePlayerChannel(guild, invokingMember)
            let! invokerTip = burstApi.SendRawRequest($"/tip/{invokingMember.Id}", ApiRequestType.Get, Nullable()).ReceiveJson<RawTip>()
            return Some (matchData, 0)
    }