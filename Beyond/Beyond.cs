using Beyond;
using Discord.Interactions;
using Discord.WebSocket;
using dotenv.net;
using Microsoft.Extensions.DependencyInjection;
using Discord;
using Amazon.DynamoDBv2.Model;
using System.Linq;
using System.Collections.Generic;

class BeyondBot
{
    private readonly IServiceProvider _serviceProvider;
    private IDictionary<string, string> _environment;
    private DiscordSocketClient _client;
    private InteractionService _interactionService;
    private BeyondDatabase _database;

    // The name of the Gumby role
    public const string GumbyRoleName = "gumby";
    // The color of the Gumby role
    public static readonly Color GumbyColor = new Color(191, 24, 226);

    // The permissions that Gumby will have by default. Basically given the capabilities of an admin, without being given the ability to mess with the server itself.
    public static readonly GuildPermissions GumbyGuildPermissions = new(
            manageMessages: true,
            manageChannels: true,
            manageEmojisAndStickers: true,
            manageRoles: true,
            manageNicknames: true,
            moderateMembers: true,
            attachFiles: true,
            kickMembers: true,
            viewAuditLog: true,
            createInstantInvite: true,
            viewGuildInsights: true
        );
    // The name of the category channel which all the template channels are stored in.
    public const string BeyondCategoryName = "beyond";
    // The permissions which the Gumby is not allowed to have. These are for the default channels only, and they will still be able to manage the ones that are not defaults.
    public static readonly OverwritePermissions GumbyDefaultChannelRestrictions = new(manageChannel: PermValue.Deny);
    // A list of database identifiers, as well as the names used as display names for those channels.
    // TODO: Make configurable, if possible.
    public static readonly Dictionary<string, string> GuildMappings = new()
    {
        ["general"] = "general",
        ["development"] = "development",
        ["bot"] = "bot",
        ["rules"] = "rules"
    };
    
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

    // Goes through the guilds listed and confirms that the correct channels are created and in place to prevent modification from GOTM.
    private async Task VerifyGuilds()
    {
        try
        {
            var guildQuery = new QueryRequest
            {
                KeyConditionExpression = "endpoint = :guild",
                ExpressionAttributeValues =
                {
                    [":guild"] = new AttributeValue{S = "guild"}
                }
            };
            var guildResponse = await _database.QueryAsync(guildQuery);
            var guildMap = guildResponse.Items.ToDictionary(guild => ulong.Parse(guild["tag"].N));
            var requests = new List<WriteRequest>();
            foreach (var guild in _client.Guilds)
            {
                bool changed = false;
                var guildResource = guildMap.TryGetValue(guild.Id, out var guildData) ? guildData : new();
                IRole? gumbyRole = guild.GetRole(guildResource.TryGetValue(GumbyRoleName, out var gumbyRoleId) ? ulong.Parse(gumbyRoleId.N) : 0);
                if (gumbyRole is null)
                {
                    gumbyRole = await guild.CreateRoleAsync(GumbyRoleName, permissions: GumbyGuildPermissions, color: GumbyColor);
                    guildResource[GumbyRoleName] = new AttributeValue { N = gumbyRole.Id.ToString() };
                    changed = true;
                }
                // Get the category channel from a guild ID. If it cannot find it, then try and replace it with channel that's already in the guild named the same thing (if there is one) 
                ICategoryChannel? beyondCategoryChannel = guild.GetCategoryChannel(guildResource.TryGetValue(BeyondCategoryName, out var categoryChannelId) ? ulong.Parse(categoryChannelId.N) : 0) ?? guild.CategoryChannels.FirstOrDefault(channel => channel.Name == BeyondCategoryName);
                if (beyondCategoryChannel is null)
                {
                    beyondCategoryChannel = await guild.CreateCategoryChannelAsync(BeyondCategoryName, (channelProperties) =>
                    { 
                        channelProperties.PermissionOverwrites = new List<Overwrite>()
                        {
                            new Overwrite(gumbyRole.Id, PermissionTarget.Role, GumbyDefaultChannelRestrictions)
                        };
                    });
                    guildResource[BeyondCategoryName] = new AttributeValue { N = beyondCategoryChannel.Id.ToString() };
                    changed = false;
                }
                // Iterate through the list of mappings and channel names to see if they exist - if they don't, create / find one.
                foreach (var (resourceName, channelName) in GuildMappings)
                {
                    // Try and get the channel from it's ID, or one from the guild with the same name.
                    ITextChannel? channel = guild.GetTextChannel(guildResource.TryGetValue(resourceName, out var channelId) ? ulong.Parse(channelId.N) : 0) ?? guild.TextChannels.FirstOrDefault(channel => channel.Name == channelName);
                    if (channel is null)
                    {
                        channel = await guild.CreateTextChannelAsync(channelName, (properties) =>
                        {
                            properties.CategoryId = beyondCategoryChannel.Id;
                            properties.PermissionOverwrites = new List<Overwrite>()
                            {
                                new Overwrite(gumbyRole.Id, PermissionTarget.Role, GumbyDefaultChannelRestrictions)
                            };
                        });
                        guildResource[resourceName] = new AttributeValue { N = channel.Id.ToString() };
                        changed = true;
                    }
                    // This is a redundant request in the case that there actually is a channel that fits the conditions. We need to make sure that these channels are current & available.
                    if (channel.CategoryId != beyondCategoryChannel.Id || channel.Name != channelName)
                    {
                        await channel.ModifyAsync(properties =>
                        {
                            properties.CategoryId = beyondCategoryChannel.Id;
                            properties.Name = channelName;
                            properties.PermissionOverwrites = new List<Overwrite>()
                            {
                                new Overwrite(gumbyRole.Id, PermissionTarget.Role, GumbyDefaultChannelRestrictions)
                            };
                        });
                    }
                }
                // If we've changed, we need to put this in the query so that it goes into the database.
                if (changed)
                {
                    var request = new PutRequest
                    {
                        Item = guildResource
                    };
                    requests.Add(new WriteRequest(request));
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
        // Go through and verify guilds.
        await VerifyGuilds();
        // Register slash commands.
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