using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Discord;
using Discord.Interactions;
using System.Configuration;

namespace Beyond
{
    public class BeyondCommandService
    {
        public BeyondCommandService(BeyondDatabase db)
        {
            db.AddEndpointString("vote", "vote", "{yearMonth}/{guild}/{user}");
        }
    }

    public sealed class BeyondCommands : InteractionModuleBase
    {
        private readonly BeyondDatabase _database;
        public BeyondCommands(BeyondDatabase database, BeyondElectionService election)
        {
            _database = database;
        }

        [SlashCommand("vote", "Vote for Gumby of the Month")]
        public async Task Vote(
            [Summary(description:"candidate")] IUser candidate
            )
        {
            var guildId = Context.Interaction.GuildId;
            var user = Context.Interaction.User;
            // If the user is voting for themselves, or they are voting for a bot, then tell them off.
            if (user.Id == candidate.Id || candidate.IsBot)
            {
                await RespondAsync("You cannot vote for that person!");
                return;
            }

            var endpoint = _database.GetEndpointString("vote", Context);
            var tag = _database.GetTagString("vote", Context);
            var key = new Dictionary<string, AttributeValue>
            {
                ["endpoint"] = new AttributeValue { S = endpoint },
                ["tag"] = new AttributeValue { S = tag }
            };
            var request = new GetItemRequest
            {
                Key = key
            };
            try
            {
                var response = await _database.GetItemAsync(request);
                var item = response.Item;
                // If item count is 0, then we know this value did not exist.
                if (item.Count == 0)
                {
                    key["candidate"] = new AttributeValue { N = candidate.Id.ToString() };
                    var putRequest = new PutItemRequest
                    {
                        Item = key
                    };
                    var putResponse = await _database.PutItemAsync(putRequest);
                    await RespondAsync("You have submitted your vote.");
                    return;
                }
                // If the prior did not happen, then we have gotten an existing value.
                AttributeValue? candidateValue;
                item.TryGetValue("candidate", out candidateValue);
                candidateValue ??= new AttributeValue { N = "0" };
                var lastVoted = ulong.Parse(item["candidate"].N);
                // User has already voted for this candidate.
                if (lastVoted == candidate.Id)
                {
                    await RespondAsync("You have already voted for this candidate.");
                    return;
                }
                // If user has not voted for this candidate, update their value with this new candidate ID.
                var updates = new Dictionary<string, AttributeValueUpdate>
                {
                    ["candidate"] = new AttributeValueUpdate
                    {
                        Action = AttributeAction.PUT,
                        Value = new AttributeValue { N = candidate.Id.ToString() }
                    }
                };
                var updateRequest = new UpdateItemRequest
                {
                    AttributeUpdates = updates,
                    Key = key
                };
               
                var updateResponse = await _database.UpdateItemAsync(updateRequest);
                if (updateResponse.HttpStatusCode != System.Net.HttpStatusCode.OK) throw new Exception($"Received error code on update request, error {updateResponse.HttpStatusCode}");
                
            } catch (Exception e)
            {
                await RespondAsync($"There was a problem updating your vote!\nError Type: {e.GetType()}");
                return;
            }
            await RespondAsync($"You have re-submitted your vote.");
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
