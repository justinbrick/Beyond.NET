
using Amazon.DynamoDBv2.Model;
using Discord;
using Discord.WebSocket;
using System.Runtime.CompilerServices;

namespace Beyond
{
    public class BeyondElectionService
    {
        private BeyondDatabase _db;
        private DiscordSocketClient _client;
        public CancellationToken CancellationToken { get; set; } = new();

        public const string ElectionEndpoint = "self/election";
        public const string ElectionSortTag = "v1"; // Perhaps need to add this to app.config so we can change info when version bump?
        private async void UpdateLastCheck()
        {
            var item = new Dictionary<string, AttributeValue>
            {
                ["endpoint"] = new AttributeValue { S = ElectionEndpoint },
                ["tag"] = new AttributeValue { S = ElectionSortTag },
                ["lastVoted"] = new AttributeValue { N = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString() }
            };
            var putRequest = new PutItemRequest
            {
                Item = item
            };
            await _db.PutItemAsync(putRequest);
        }

        private async Task<DateTimeOffset> GetLastCheck()
        {
            var key = new Dictionary<string, AttributeValue>
            {
                ["endpoint"] = new AttributeValue { S = ElectionEndpoint },
                ["tag"] = new AttributeValue { S = ElectionSortTag }
            };
            var request = new GetItemRequest
            {
                Key = key
            };
            var response = await _db.GetItemAsync(request);
            if (!response.Item.ContainsKey("lastVoted")) return DateTimeOffset.UtcNow;
            var unixEpoch = long.Parse(response.Item["lastVoted"].N);
            return DateTimeOffset.FromUnixTimeSeconds(unixEpoch);
        }

        public async Task CreateGumbyElections()
        {
            var lastCheck = await GetLastCheck();
            // If it's the start of a new month, then go through each 
            var request = new QueryRequest
            {
                KeyConditionExpression = "endpoint = :vote and begins_with(tag, :date)",
                ExpressionAttributeValues =
                    {
                        [":date"] = new AttributeValue{S = _db.GetShortDate(lastCheck.DateTime)},
                        [":vote"] = new AttributeValue{S = "vote"}
                    }
            };
            var response = await _db.QueryAsync(request);
            var electionList = new Dictionary<ulong, Dictionary<ulong, ulong>>();
            foreach (var item in response.Items)
            {
                var tag = item["tag"].S;
                var candidate = ulong.Parse(item["candidate"].N);
                var guildId = ulong.Parse(tag.Split("/")[1]);
                if (!electionList.TryGetValue(guildId, out var leaderboard))
                {
                    leaderboard = new Dictionary<ulong, ulong>();
                    electionList[guildId] = leaderboard;
                }
                if (!leaderboard.TryGetValue(candidate, out var votes)) votes = 0;
                leaderboard[candidate] = ++votes;
            }
            foreach (var election in electionList)
            {
                var guildId = election.Key;
                var leaderboard = election.Value;
                var maxVotes = leaderboard.Values.Max();
                var winner = leaderboard.FirstOrDefault(x => x.Value == maxVotes).Key;
                var guild = _client.GetGuild(guildId);
                if (guild is null) continue; // We've been removed from this guild - oh well, just ignore.
                var general = guild.TextChannels.FirstOrDefault(channel => channel.Name == "general");
                if (general is null) continue;
                await general.SendMessageAsync($"<@!{winner}> has won the election for this month!");
            }
        }
            
        public BeyondElectionService(DiscordSocketClient client, BeyondDatabase db)
        {
            _db = db;
            _client = client;
            Task.Run(async () =>
            {
                while (!CancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        var lastCheck = await GetLastCheck();
                        var today = DateTimeOffset.UtcNow;
                        if (lastCheck.Month != today.Month)
                        {
                            await CreateGumbyElections();
                            UpdateLastCheck();
                        }
                        await Task.Delay(TimeSpan.FromDays(1));
                    } catch (Exception e)
                    {
                        Console.Error.WriteLine($"Error while checking election info!");
                    }
                }
            });
        }
    }
}
