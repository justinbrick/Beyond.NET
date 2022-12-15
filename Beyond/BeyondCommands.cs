using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Discord;
using Discord.Interactions;
using System.Configuration;

namespace Beyond
{
 

    public sealed class BeyondCommands : InteractionModuleBase
    {
        private readonly BeyondDatabase _database;
        public BeyondCommands(BeyondDatabase database)
        {
            _database = database;
        }

        [SlashCommand("vote", "Vote for Gumby of the Month")]
        public async Task Vote(
            [Summary(description: "candidate")] IUser candidate
            )
        {
            try
            {
                var guildId = Context.Interaction.GuildId;
                var user = Context.Interaction.User;
                // If the user is voting for themselves, or they are voting for a bot, then tell them off.
                if (user.Id == candidate.Id || candidate.IsBot)
                {
                    await RespondAsync("You cannot vote for that person!");
                    return;
                }
                var key = new Dictionary<string, AttributeValue>
                {
                    ["guild"] = new AttributeValue { N = Context.Guild.Id.ToString() },
                    ["tag"] = new AttributeValue { S = $"election/{_database.GetShortDate(DateTime.UtcNow)}/{user.Id}" }
                };
                var request = new GetItemRequest { Key = key };
                var response = await _database.GetItemAsync(request);
                var lastCandidateId = response.Item.TryGetValue("candidate", out var lastCandidate) ? ulong.Parse(lastCandidate.N) : default;
                if (candidate.Id == lastCandidateId)
                {
                    await RespondAsync("You have already voted for this candidate.");
                    return;
                }
                key["candidate"] = new AttributeValue { N = candidate.Id.ToString() };
                var putRequest = new PutItemRequest { Item = key };
                await _database.PutItemAsync(putRequest);
                await RespondAsync(response.Item.Count == 0 ? "You have submit your vote." : "You have re-submitted your vote.");
            } catch (Exception e)
            {
                await RespondAsync($"There was an error submitting your vote. Error Type: {e.GetType()}");
                return;
            }
            await RespondAsync("How?");
        }

        [SlashCommand("setbanner", "Set the banner of the server.")]
        public async Task SetBanner(
            [Summary(description:"banner image")]IAttachment attachment
            )
        {

            // Stub: Set the banner for the server.
        }

        [SlashCommand("setname", "Set the name of the server.")]
        public async Task SetName(string name)
        {
            var guild = Context.Guild;
            var guildId = guild.Id;
            try
            {

            } catch (Exception e)
            {
                await RespondAsync($"There was a problem setting the guild name.\nError Type: {e.GetType().Name}");
            }
        }

        [SlashCommand("electionhistory", "Get the history of past elections.")]
        public async Task GetElectionHistory()
        {
        }
    }
}
