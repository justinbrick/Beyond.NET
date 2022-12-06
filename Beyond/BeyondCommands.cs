using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using Amazon.Runtime.CredentialManagement;
using Discord;
using Discord.Interactions;
using System.Configuration;
using System.Globalization;

namespace Beyond
{
    public class BeyondCommandService
    {
        private readonly CultureInfo _culture = new CultureInfo(ConfigurationManager.AppSettings["beyond-culture"] ?? "en-US") ;
        private readonly string _tableName = ConfigurationManager.AppSettings["beyond-table-name"] ?? "beyond";

        public AmazonDynamoDBClient Dynamo;

        // Returns a short date equivalent - based off of the culture types.
        public string GetShortDate(DateTime time) => time.ToString("Y", _culture);

        public Task<GetItemResponse> GetItemAsync(GetItemRequest request)
        {
            request.TableName = _tableName;
            return Dynamo.GetItemAsync(request);
        }

        public Task<PutItemResponse> PutItemAsync(PutItemRequest request)
        {
            request.TableName = _tableName;
            return Dynamo.PutItemAsync(request);
        }

        public Task<UpdateItemResponse> UpdateItemAsync(UpdateItemRequest request)
        {
            request.TableName = _tableName;
            return Dynamo.UpdateItemAsync(request);
        }

        public BeyondCommandService()
        {
            // Setting Amazon DynamoDB integration.
            var profileName = ConfigurationManager.AppSettings["beyond-profile"];
            if (profileName is null) throw new NullReferenceException("Could not get beyond-profile app.config setting!");
            var chain = new CredentialProfileStoreChain();
            AWSCredentials credentials;
            if (!chain.TryGetAWSCredentials(profileName, out credentials)) throw new Exception("Could not get AWS profile from beyond-profile!");
            var config = new AmazonDynamoDBConfig()
            {
                RegionEndpoint = Amazon.RegionEndpoint.USEast1
            };
            Dynamo = new AmazonDynamoDBClient(credentials, config);   
        }
    }

    // TODO: Remove hardcoded data entries and table points.
    public class BeyondCommands : InteractionModuleBase
    {
        private readonly BeyondCommandService _service;
        public BeyondCommands(BeyondCommandService s)
        {
            _service = s;
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

            var endpoint = $"{guildId}/vote";
            var tag = $"{_service.GetShortDate(DateTime.UtcNow)}/{user.Id}";
            // TODO: Move to larger scheme
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
                var response = await _service.GetItemAsync(request);
                var item = response.Item;
                // If item count is 0, then we know this value did not exist.
                if (item.Count == 0)
                {
                    key["candidate"] = new AttributeValue { N = candidate.Id.ToString() };
                    var putRequest = new PutItemRequest
                    {
                        Item = key,
                        TableName = "beyond"
                    };
                    var putResponse = await _service.Dynamo.PutItemAsync(putRequest);
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
               
                var updateResponse = await _service.UpdateItemAsync(updateRequest);
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
            // Stub: Return the past 10 elections from the server.
        }
    }
}
