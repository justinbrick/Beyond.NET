
using Amazon.DynamoDBv2.Model;
using Discord;
using Discord.WebSocket;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Linq;

namespace Beyond
{
    public class BeyondElectionService
    {
        private BeyondDatabase _db;
        private DiscordSocketClient _client;
        private Random _random = new();
        public CancellationToken CancellationToken { get; set; } = new();

        private async Task<Dictionary<string, AttributeValue>> GetElection(IGuild guild, DateTime date)
        {
            var getRequest = new GetItemRequest
            {
                Key =
                {
                    ["guild"] = new AttributeValue { N = guild.Id.ToString() },
                    ["tag"] = new AttributeValue { S = $"election/{_db.GetShortDate(date)}" }
                }
            };
            var getResponse = await _db.GetItemAsync(getRequest);
            return getResponse.Item;
        }

        public async Task CreateGumbyElection(IGuild guild)
        {
            Console.WriteLine("Creating Guild Election");
            Dictionary<string, AttributeValue> guildInformation = await _db.GetGuildInformation(guild);
            var election = await GetElection(guild, DateTime.UtcNow.AddMonths(-1));
            if (election.Count != 0) return; // Void if election finished (maybe check if GOTM has their role?)
            if (!guildInformation.TryGetValue("general", out var generalId))
            {
                Console.Error.WriteLine($"Could not get the general channel from guild information - has this guild been initialized yet? {guild.Id}");
                return;
            }
            var general = (await guild.GetChannelAsync(ulong.Parse(generalId.N))) as ITextChannel;
            if (general is null) throw new Exception("Found null general while getting election results!");
            var lastMonth = DateTime.UtcNow.AddMonths(-1);
            var getQuery = new QueryRequest
            {
                KeyConditionExpression = "guild = :guild and begins_with(tag, :date)",
                ExpressionAttributeValues =
                {
                    [":guild"] = new AttributeValue { N = guild.Id.ToString() },
                    [":date"] = new AttributeValue { S = $"election/{_db.GetShortDate(lastMonth)}" }
                }
            };

            var queryResponse = await _db.QueryAsync(getQuery);
            var leaderboards = new Dictionary<ulong, ulong>();
            await guild.DownloadUsersAsync();
            var users = await guild.GetUsersAsync();
            // Tally up the votes, so that we can query this later.
            foreach (var vote in queryResponse.Items)
            {
                if (!vote.TryGetValue("candidate", out var candidate)) continue;
                var userId = ulong.Parse(candidate.N);
                if (!leaderboards.TryGetValue(userId, out var votes)) votes = 0;
                leaderboards[userId] = ++votes;
            }
            // Get the top person from the leaderboards that is currently in the guild.
            var topList = from candidate in leaderboards join user in users on candidate.Key equals user.Id orderby candidate.Value descending select candidate.Key;
            // If the top list is empty, that means that there were no candidates, and that we need to pick a random person from the group.
            ulong winner = topList.Count() == 0 ? users.ElementAt(_random.Next(users.Count)).Id : topList.First();
            await general.SendMessageAsync($"The winner of the GOTM is <@!{winner}>");
            
            var putRequest = new PutItemRequest
            {
                Item =
                {
                    ["guild"] = new AttributeValue { N = guild.Id.ToString() },
                    ["tag"] = new AttributeValue { S = $"election/{_db.GetShortDate(DateTime.UtcNow.AddMonths(-1))}" },
                    ["gumby"] = new AttributeValue { N = winner.ToString() }
                }
            };
            var putResponse = await _db.PutItemAsync(putRequest);

        }

        // Go through the guilds that the bot is connected to, and make sure that they have the updated GOTM settings.
        public async Task CreateGumbyElections()
        {
            var guilds = _client.Guilds;
            foreach (var guild in guilds)
            {
                try
                {
                    await CreateGumbyElection(guild);
                } catch (Exception e)
                {
                    Console.Error.WriteLine($"Error while updating guild election, {e}");
                }
                await Task.Delay(TimeSpan.FromSeconds(1)); // We cause a built-in delay to prevent overloading the system, we should try and handle in a distributed time.
            }
        }

        private async Task OnClientReady()
        {
            while (!CancellationToken.IsCancellationRequested)
            {
                var startDate = DateTime.UtcNow;
                try
                {
                    await CreateGumbyElections();
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine($"Error while checking election info! {e.ToString()}");
                    break;
                }
                TimeSpan durationTaken = DateTime.UtcNow - startDate;
                var timeWait = TimeSpan.FromDays(1) - durationTaken;
                await Task.Delay(timeWait);
            }
        }

        public BeyondElectionService(DiscordSocketClient client, BeyondDatabase db)
        {
            _db = db;
            _client = client;
            _client.Ready += OnClientReady;
        }
        
    }
}
