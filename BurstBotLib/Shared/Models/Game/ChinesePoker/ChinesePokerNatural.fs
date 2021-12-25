namespace BurstBotLib.Shared.Models.Game.ChinesePoker

open Thoth.Json.Net

type ChinesePokerNatural =
    | ThreeFlushes
    | ThreeStraights
    | SixAndAHalfPairs
    | FourTriples
    | FullColored
    | AllLowHighs
    | ThreeQuads
    | ThreeStraightFlushes
    | TwelveRoyalties
    | Dragon
    | CleanDragon

module ChinesePokerNaturalSerialization =
    let Encode (natural: ChinesePokerNatural) = Encode.string (natural.ToString())

    let Decode: Decoder<ChinesePokerNatural> =
        Decode.string
        |> Decode.andThen
            (function
            | "ThreeFlushes" -> Decode.succeed ThreeFlushes
            | "ThreeStraights" -> Decode.succeed ThreeStraights
            | "SixAndAHalfPairs" -> Decode.succeed SixAndAHalfPairs
            | "FourTriples" -> Decode.succeed FourTriples
            | "FullColored" -> Decode.succeed FullColored
            | "AllLowHighs" -> Decode.succeed AllLowHighs
            | "ThreeQuads" -> Decode.succeed ThreeQuads
            | "ThreeStraightFlushes" -> Decode.succeed ThreeStraightFlushes
            | "TwelveRoyalties" -> Decode.succeed TwelveRoyalties
            | "Dragon" -> Decode.succeed Dragon
            | "CleanDragon" -> Decode.succeed CleanDragon
            | invalid -> Decode.fail $"Failed to decode `{invalid}`. Invalid case for naturals.")
