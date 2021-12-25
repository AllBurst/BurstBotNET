namespace BurstBotLib.Shared.Models.Game.ChinesePoker

open System.Text.Json
open BurstBotLib.Shared.Models.Game.ChinesePoker
open BurstBotShared.Shared.Models.Game.Serializables
open Thoth.Json.Net

type ChinesePokerCombination =
    | None of ChinesePokerCards
    | OnePair of ChinesePokerCards
    | TwoPairs of ChinesePokerCards
    | ThreeOfAKind of ChinesePokerCards
    | Straight of ChinesePokerCards
    | Flush of ChinesePokerCards
    | FullHouse of ChinesePokerCards
    | FourOfAKind of ChinesePokerCards
    | StraightFlush of ChinesePokerCards

module ChinesePokerCombinationSerialization =
    let Encode (combination: ChinesePokerCombination) =
        let combinationType, cards =
            match combination with
            | None cards -> ("None", cards.Cards)
            | OnePair cards -> ("OnePair", cards.Cards)
            | TwoPairs cards -> ("TwoPairs", cards.Cards)
            | ThreeOfAKind cards -> ("ThreeOfAKind", cards.Cards)
            | Straight cards -> ("Straight", cards.Cards)
            | Flush cards -> ("Flush", cards.Cards)
            | FullHouse cards -> ("FullHouse", cards.Cards)
            | FourOfAKind cards -> ("FourOfAKind", cards.Cards)
            | StraightFlush cards -> ("StraightFlush", cards.Cards)

        Encode.object [ "combination_type", Encode.string combinationType
                        "cards", Encode.string (JsonSerializer.Serialize(cards)) ]

    let Decode: Decoder<ChinesePokerCombination> =
        Decode.object
            (fun get ->
                let rawCombinationType =
                    get.Required.Field "combination_type" Decode.string

                let rawCards =
                    get.Required.Field "cards" Decode.string
                    |> JsonSerializer.Deserialize<Card list>

                let cards = { ChinesePokerCards.Cards = rawCards }

                let combination =
                    match rawCombinationType with
                    | "None" -> None cards
                    | "OnePair" -> OnePair cards
                    | "TwoPairs" -> TwoPairs cards
                    | "ThreeOfAKind" -> ThreeOfAKind cards
                    | "Straight" -> Straight cards
                    | "Flush" -> Flush cards
                    | "FullHouse" -> FullHouse cards
                    | "FourOfAKind" -> FourOfAKind cards
                    | "StraightFlush" -> StraightFlush cards
                    | _ -> None cards

                combination)
