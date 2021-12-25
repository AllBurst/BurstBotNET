namespace BurstBotLib.Shared.Models.Game.ChinesePoker

open System.Text.Json.Serialization
open BurstBotShared.Shared.Models.Game.Serializables

type ChinesePokerCards =
    { [<JsonPropertyName("cards")>]
      Cards: Card list }
