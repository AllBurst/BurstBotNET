module BurstBotLib.ChinesePoker.ChinesePoker

open System
open System.Linq
open System.Threading.Tasks
open BurstBotShared.Api
open BurstBotShared.Shared.Models.Data
open BurstBotShared.Shared.Models.Data.Serializables
open BurstBotShared.Shared.Models.Game.Serializables
open BurstBotShared.Shared.Utilities
open DSharpPlus
open DSharpPlus.Entities
open DSharpPlus.EventArgs

let Join (client: DiscordClient) (e: InteractionCreateEventArgs) (state: State) =
    task {
        do! e.Interaction.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource)
        let mentionedPlayers = [ e.Interaction.User.Id ]

        let options =
            e
                .Interaction
                .Data
                .Options
                .ElementAt(0)
                .Options.ToArray()
            |> List.ofArray

        let baseBet = Single.Parse(string options.Head.Value)

        let mentionedPlayers =
            match options with
            | [] -> mentionedPlayers
            | [ _ ] -> mentionedPlayers
            | _ :: rest ->
                mentionedPlayers
                @ (List.map (fun (x: DiscordInteractionDataOption) -> x.Value :?> uint64) rest)

        // Try getting all players' tips and check if they have enough tips.
        let mutable invokerTip = None
        let invoker = e.Interaction.User

        let getTipTasks =
            mentionedPlayers
            |> List.map
                (fun player ->
                    task {
                        let! response =
                            state.BurstApi.SendRawRequest<Object>($"/tip/{player}", ApiRequestType.Get, Nullable())

                        if not response.ResponseMessage.IsSuccessStatusCode then
                            return None
                        else
                            let! playerTip = response.GetJsonAsync<RawTip>()

                            invokerTip <-
                                if invoker.Id = player then
                                    Some playerTip
                                else
                                    None

                            return
                                if (float32 playerTip.Amount) < baseBet then
                                    None
                                else
                                    Some playerTip
                    })

        let! allPlayerTips = Task.WhenAll(getTipTasks)

        let hasInvalidPlayer =
            allPlayerTips |> Array.exists (fun x -> x.IsNone)

        if hasInvalidPlayer then
            task {
                let! _ =
                    e.Interaction.EditOriginalResponseAsync(
                        DiscordWebhookBuilder()
                            .WithContent(
                                "Sorry, but either one of the players you invited hasn't joined the server yet, or he doesn't have enough tips to join a game!"
                            )
                    )

                ()
            }
            |> ignore

            return ()
        else
            ()

        let joinRequest =
            GenericJoinRequest(
                ClientType = ClientType.Discord,
                GameType = GameType.ChinesePoker,
                PlayerIds = mentionedPlayers.ToList()
            )

        let! joinGameResponse = state.BurstApi.SendRawRequest("/chinese_poker/join", ApiRequestType.Post, joinRequest)
        let playerCount = List.length mentionedPlayers

        let unit =
            if playerCount > 1 then
                "players"
            else
                "player"

        let joinStatus, reply =
            BurstApi
                .HandleMatchGameHttpStatuses(joinGameResponse, unit, GameType.ChinesePoker)
                .ToTuple()

        if joinStatus = null then
            let! _ = e.Interaction.EditOriginalResponseAsync reply
            return ()
        else
            ()

        let! invokingMember = e.Interaction.Guild.GetMemberAsync(invoker.Id)
        let botUser = client.CurrentUser
        let mutable reply = reply

        match joinStatus.StatusType with
        | GenericJoinStatusType.Start ->
            reply <-
                reply.AddEmbed(
                    Utilities.BuildGameEmbed(invokingMember, botUser, joinStatus, "Chinese Poker", "", Nullable())
                )

            let! message = e.Interaction.EditOriginalResponseAsync reply

            let! reactionResult =
                BurstApi.HandleStartGameReactions(
                    "Chinese Poker",
                    e,
                    message,
                    invokingMember,
                    botUser,
                    joinStatus,
                    mentionedPlayers,
                    "/chinese_poker/join/confirm",
                    state,
                    client.Logger
                )

            if not reactionResult.HasValue then
                failwith "Failed to handle reactions from invited players."
            else
                task {
                    let members, matchData = reactionResult.Value.ToTuple()
                    let guild = e.Interaction.Guild

                    members
                    |> Array.iter
                        (fun m ->
                            task {
                                let! textChannel = state.BurstApi.CreatePlayerChannel(guild, m)
                                ()
                            }
                            |> ignore)
                }
                |> ignore
        | _ -> ()

        ()
    }