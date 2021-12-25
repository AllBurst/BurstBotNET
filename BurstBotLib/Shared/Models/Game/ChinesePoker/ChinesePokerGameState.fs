namespace BurstBotLib.Shared.Models.Game.ChinesePoker

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.Linq
open System.Text.Json.Serialization
open BurstBotLib.Shared.Models.Game.ChinesePoker
open Thoth.Json.Net

type ChinesePokerGameState =
    { [<JsonPropertyName("game_id")>]
      GameId: string
      [<JsonPropertyName("last_active_time")>]
      LastActiveTime: DateTime
      [<JsonPropertyName("players")>]
      Players: ConcurrentDictionary<uint64, ChinesePokerPlayerState>
      [<JsonPropertyName("progress")>]
      Progress: ChinesePokerGameProgress
      [<JsonPropertyName("base_bet")>]
      BaseBet: float32
      [<JsonPropertyName("units")>]
      Units: Dictionary<uint64, Dictionary<uint64, int>> }

    static member Encode(gameState: ChinesePokerGameState) =
        let players =
            gameState
                .Players
                .Select(fun item -> (item.Key.ToString(), ChinesePokerPlayerState.Encode(item.Value)))
                .ToArray()

        let units =
            gameState
                .Units
                .Select(fun item ->
                    let innerDict =
                        item
                            .Value
                            .Select(fun innerItem -> (innerItem.Key.ToString(), Encode.int innerItem.Value))
                            .ToArray()

                    let innerMap = Map.ofArray innerDict
                    (item.Key.ToString(), Encode.dict innerMap))
                .ToArray()

        Encode.object [ "game_id", Encode.string gameState.GameId
                        "last_active_time", Encode.datetime gameState.LastActiveTime
                        "players", Encode.dict (Map.ofArray players)
                        "progress", Encode.string (gameState.Progress.ToString())
                        "base_bet", Encode.float32 gameState.BaseBet
                        "units", Encode.dict (Map.ofArray units) ]

    static member Decode: Decoder<ChinesePokerGameState> =
        Decode.object
            (fun get ->
                let progress =
                    match get.Required.Field "progress" Decode.string with
                    | "NotAvailable" -> ChinesePokerGameProgress.NotAvailable
                    | "Starting" -> ChinesePokerGameProgress.Starting
                    | "FrontHand" -> ChinesePokerGameProgress.FrontHand
                    | "MiddleHand" -> ChinesePokerGameProgress.MiddleHand
                    | "BackHand" -> ChinesePokerGameProgress.BackHand
                    | "Ending" -> ChinesePokerGameProgress.Ending
                    | "Closed" -> ChinesePokerGameProgress.Closed
                    | _ -> ChinesePokerGameProgress.NotAvailable

                let players =
                    get.Required.Field "players" (Decode.dict ChinesePokerPlayerState.Decode)
                    |> Map.map (fun k v -> KeyValuePair(UInt64.Parse(k), v))
                    |> Map.values

                let units =
                    get.Required.Field "units" (Decode.dict (Decode.dict Decode.int))
                    |> Map.map
                        (fun k v ->
                            KeyValuePair(
                                UInt64.Parse(k),
                                Map.map (fun innerK innerV -> KeyValuePair(UInt64.Parse(innerK), innerV)) v
                                |> Map.values
                                |> Dictionary<uint64, int>
                            ))
                    |> Map.values

                { GameId = get.Required.Field "game_id" Decode.string
                  LastActiveTime = get.Required.Field "last_active_time" Decode.datetime
                  Players = ConcurrentDictionary<uint64, ChinesePokerPlayerState>(players)
                  Progress = progress
                  BaseBet = get.Required.Field "base_bet" Decode.float32
                  Units = Dictionary(units) })
