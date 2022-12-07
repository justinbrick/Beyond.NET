using Beyond;
using Discord.Interactions;
using Discord.WebSocket;
using dotenv.net;
using Microsoft.Extensions.DependencyInjection;
using Discord;
using Amazon.DynamoDBv2.Model;
using System.Linq;

class BeyondBot
{
    private readonly IServiceProvider _serviceProvider;
    private IDictionary<string, string> _environment;
    private DiscordSocketClient _client;
    private InteractionService _interactionService;
    private BeyondDatabase _database;

    public static readonly string[] DefaultChannels =
    {
        "general",
        ""
    };

    public const string GumbyRole = "gumby";
    public const string BeyondCategory = "beyond";

    public BeyondBot()
    {
        _environment = DotEnv.Read();
        _serviceProvider = CreateServices();
        _client = _serviceProvider.GetService<DiscordSocketClient>()!;
        _interactionService = _serviceProvider.GetService<InteractionService>()!;
        _database = _serviceProvider.GetService<BeyondDatabase>()!;
        _database.AddEndpointString("self", "self", "self");
    }

    private static IServiceProvider CreateServices()
    {
        var db = new BeyondDatabase();
        var commandService = new BeyondCommandService(db);

        var collection = new ServiceCollection()
            //.AddSingleton(config)
            .AddSingleton<DiscordSocketClient>()
            .AddSingleton<InteractionService>()
            .AddSingleton(db)
            .AddSingleton<BeyondElectionService>();

        return collection.BuildServiceProvider();
    }
    private Task LogError(LogMessage msg)
    {
        Console.WriteLine(msg.ToString());
        return Task.CompletedTask;
    }


    private async Task VerifyGuilds()
    {
        try
        {
            var guildQuery = new QueryRequest
            {
                FilterExpression = "endpoint = :guild",
                ExpressionAttributeValues =
                {
                    ["guild"] = new AttributeValue{S = "guild"}
                }
            };
            var guildResponse = await _database.QueryAsync(guildQuery);
            var guildMap = guildResponse.Items.ToDictionary(guild => guild.TryGetValue("tag", out var result) ? ulong.Parse(result.S) : 0);\
            var requests = new List<WriteRequest>();
            foreach (var guild in _client.Guilds)
            {
                
                if (guildMap.ContainsKey(guild.Id))
                {
                    Dictionary<string, ulong> channelChanges = new();
                    var guildChannels = guild.Channels.ToDictionary(guild => guild.Name);
                    var guildData = guildMap[guild.Id];
                    if (!guildData.ContainsKey("beyond"))
                    {
                        ICategoryChannel? category = guild.CategoryChannels.FirstOrDefault(channel => channel.Name == BeyondCategory);
                        if (category is null) category = (await guild.CreateCategoryChannelAsync(BeyondCategory));

                    }
                }
                
            }
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"There was an error attempting to verify the integrity of different servers!\n{e.Message}");
        }
    }

    private async Task OnClientReady()
    {
        var commands = (await _interactionService.AddModuleAsync<BeyondCommands>(_serviceProvider)).SlashCommands.ToArray();
        if (commands is null) throw new Exception("Could not get slash commands from BeyondCommands");

        // Enable - on debug, send these commands to our test server, otherwise put on global.
#if DEBUG
        var guildId = ulong.Parse(_environment["GUILD_ID"]);
        var guild = _client.GetGuild(guildId);
        await _interactionService.AddCommandsToGuildAsync(guild, commands: commands);
#else
        await _interactionService.AddCommandsGloballyAsync(commands: commands);
#endif
    }

    private async Task OnInteractionCreate(SocketInteraction s)
    {
        var ctx = new SocketInteractionContext(_client, s);
        await _interactionService.ExecuteCommandAsync(ctx, _serviceProvider);
    }

    public static Task Main(String[] args) => new BeyondBot().Start(args);

    public async Task Start(String[] args)
    {
#if DEBUG
        var token = _environment["APPLICATION_TOKEN_DEBUG"];
#else
        var token = _environment["APPLICATION_TOKEN"];
#endif
        if (token is null) throw new Exception("Could not find token in environment!");

        // Starting up the bot and adding events.
        _client.Log += LogError;
        _client.Ready += OnClientReady;
        _client.InteractionCreated += OnInteractionCreate;
        _interactionService.Log += LogError;
        await _client.LoginAsync(TokenType.Bot, token);
        await _client.StartAsync();

        await Task.Delay(-1);
    }
}