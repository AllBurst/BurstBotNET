namespace BurstBotLib.Shared.Models.Game.ChinesePoker

open System.Collections.Generic
open System.Linq
open System.Text.Json
open System.Text.Json.Serialization
open BurstBotLib.Shared.Models.Game.ChinesePoker
open BurstBotShared.Shared.Models.Game.Serializables
open Thoth.Json.Net

type ChinesePokerPlayerState =
    { [<JsonPropertyName("game_id")>]
      GameId: string
      [<JsonPropertyName("player_id")>]
      PlayerId: uint64
      [<JsonPropertyName("player_name")>]
      PlayerName: string
      [<JsonPropertyName("channel_id")>]
      ChannelId: uint64
      [<JsonPropertyName("cards")>]
      Cards: Card list
      [<JsonPropertyName("played_cards")>]
      PlayedCards: Dictionary<ChinesePokerGameProgress, ChinesePokerCombination>
      [<JsonPropertyName("naturals")>]
      Naturals: ChinesePokerNatural option
      [<JsonPropertyName("avatar_url")>]
      AvatarUrl: string }

    static member Encode(playerState: ChinesePokerPlayerState) =
        let playedCards =
            playerState
                .PlayedCards
                .Select(fun item -> (item.Key.ToString(), ChinesePokerCombinationSerialization.Encode item.Value))
                .ToArray()

        let cardMap = Map.ofArray playedCards

        Encode.object [ "game_id", Encode.string playerState.GameId
                        "player_id", Encode.uint64 playerState.PlayerId
                        "player_name", Encode.string playerState.PlayerName
                        "channel_id", Encode.uint64 playerState.ChannelId
                        "cards", Encode.string (JsonSerializer.Serialize(playerState.Cards))
                        "played_cards", Encode.dict cardMap
                        "naturals", Encode.option ChinesePokerNaturalSerialization.Encode playerState.Naturals
                        "avatar_url", Encode.string playerState.AvatarUrl ]

    static member Decode: Decoder<ChinesePokerPlayerState> =
        Decode.object
            (fun get ->
                let cards =
                    get.Required.Field "cards" Decode.string
                    |> JsonSerializer.Deserialize<Card list>

                let cardMap =
                    get.Required.Field "played_cards" (Decode.dict ChinesePokerCombinationSerialization.Decode)

                let playedCards =
                    cardMap
                    |> Map.map
                        (fun k v ->
                            KeyValuePair(
                                (match k with
                                 | "NotAvailable" -> ChinesePokerGameProgress.NotAvailable
                                 | "Starting" -> ChinesePokerGameProgress.Starting
                                 | "FrontHand" -> ChinesePokerGameProgress.FrontHand
                                 | "MiddleHand" -> ChinesePokerGameProgress.MiddleHand
                                 | "BackHand" -> ChinesePokerGameProgress.BackHand
                                 | "Ending" -> ChinesePokerGameProgress.Ending
                                 | "Closed" -> ChinesePokerGameProgress.Closed
                                 | _ -> ChinesePokerGameProgress.NotAvailable),
                                v
                            ))
                    |> Map.values

                { GameId = get.Required.Field "game_id" Decode.string
                  PlayerId = get.Required.Field "player_id" Decode.uint64
                  PlayerName = get.Required.Field "player_name" Decode.string
                  ChannelId = get.Required.Field "channel_id" Decode.uint64
                  Cards = cards
                  PlayedCards = Dictionary<ChinesePokerGameProgress, ChinesePokerCombination>(playedCards)
                  Naturals = get.Optional.Field "naturals" ChinesePokerNaturalSerialization.Decode
                  AvatarUrl = get.Required.Field "avatar_url" Decode.string })
