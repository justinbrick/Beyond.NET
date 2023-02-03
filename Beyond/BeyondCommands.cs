using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using System.Configuration;
using System.Net;

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
            Console.WriteLine("We just registered this vote command");
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
                await RespondAsync(response.Item.Count == 0 ? "You have submit vote." : "You have re-submit vote.");
                return;
            } catch (Exception e)
            {
                await RespondAsync($"There was an error submitting your vote. Error Type: {e.GetType()}");
                return;
            }
        }

        [SlashCommand("setbanner", "Set the banner of the server.")]
        public async Task SetBanner(
            [Summary(description: "banner image")] IAttachment attachment
            )
        {
            await RespondAsync("Setting Guild Banner...");
            try
            {
                var getItemRequest = new GetItemRequest
                {
                    Key =
                    {
                        ["guild"] = new AttributeValue { N = Context.Guild.Id.ToString() },
                        ["tag"] = new AttributeValue { S = $"election/{_database.GetShortDate(DateTime.UtcNow.AddMonths(-1))}"}
                    }
                };
                var getItemResponse = await _database.GetItemAsync(getItemRequest);
                // If there is no item, or they are not the GOTM, then tell them off and end.
                if (!getItemResponse.Item.TryGetValue("gumby", out var gumby) || gumby.N != Context.User.Id.ToString())
                {
                    await FollowupAsync("You must be the GOTM to change the banner of the server!");
                    return;
                }
                if (attachment.Height is null)
                {
                    await FollowupAsync("You must have an image attachment!");
                    return;
                }
                // Just for getting the image stream - we'll dispose of this at the end of the command.
                var client = new HttpClient();
                var response = await client.GetAsync(attachment.Url);
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    await FollowupAsync($"Could not get your image, error code {response.StatusCode}");
                    return;
                }
                await Context.Guild.ModifyAsync(properties =>
                {
                    properties.Banner = new Image(response.Content.ReadAsStream());
                });
                await FollowupAsync("Successfully set the guild banner!");
            }
            catch (Exception e)
            {
                await FollowupAsync($"There was a problem setting the guild banner.\nError Type: {e.GetType()}");
            }
        } 

        [SlashCommand("setname", "Set the name of the server.")]
        public async Task SetName(string name)
        {
            try
            {
                var getItemRequest = new GetItemRequest
                {
                    Key =
                    {
                        ["guild"] = new AttributeValue { N = Context.Guild.Id.ToString() },
                        ["tag"] = new AttributeValue { S = $"election/{_database.GetShortDate(DateTime.UtcNow.AddMonths(-1))}"}
                    }
                };
                var getItemResponse = await _database.GetItemAsync(getItemRequest);
                // If there is no item, or they are not the GOTM, then tell them off and end.
                if (!getItemResponse.Item.TryGetValue("gumby", out var gumby) || gumby.N != Context.User.Id.ToString())
                {
                    await RespondAsync("You must be the GOTM to change the banner of the server!");
                    return;
                }
                await Context.Guild.ModifyAsync(properties =>
                {
                    properties.Name = name;
                });
                await RespondAsync("Guild name has been set!");
            } catch (Exception e)
            {
                await RespondAsync($"There was a problem setting the guild name.\nError Type: {e.GetType()}");
            }
        }

        [SlashCommand("seticon", "Set the icon of the server.")]
        public async Task SetIcon(
            [Summary(description: "icon image")] IAttachment attachment
            )
        {
            await RespondAsync("Setting Guild Icon...");
            try
            {
                var getItemRequest = new GetItemRequest
                {
                    Key =
                    {
                        ["guild"] = new AttributeValue { N = Context.Guild.Id.ToString() },
                        ["tag"] = new AttributeValue { S = $"election/{_database.GetShortDate(DateTime.UtcNow.AddMonths(-1))}"}
                    }
                };
                var getItemResponse = await _database.GetItemAsync(getItemRequest);
                // If there is no item, or they are not the GOTM, then tell them off and end.
                if (!getItemResponse.Item.TryGetValue("gumby", out var gumby) || gumby.N != Context.User.Id.ToString())
                {
                    await FollowupAsync("You must be the GOTM to change the icon of the guild!");
                    return;
                }
                if (attachment.Height is null)
                {
                    await FollowupAsync("You must have an image attachment!");
                    return;
                }
                // Just for getting the image stream - we'll dispose of this at the end of the command.
                var client = new HttpClient();
                var response = await client.GetAsync(attachment.Url);
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    await FollowupAsync($"Could not get your image, error code {response.StatusCode}");
                    return;
                }
                await Context.Guild.ModifyAsync(properties =>
                {
                    properties.Icon = new Image(response.Content.ReadAsStream());
                });
                await FollowupAsync("Successfully set the guild icon!");
            }
            catch (Exception e)
            {
                await FollowupAsync($"There was a problem setting the guild icon.\nError Type: {e.GetType()}");
            }
        }

        [SlashCommand("electionhistory", "Get the history of past elections.")]
        public async Task GetElectionHistory()
        {
        }
    }
}
